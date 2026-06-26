using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class AzureOpenAIVideoGenerationService : IVideoGenerationService
{
    private const string HttpClientName = "azure-openai-video-generation";

    private readonly AzureOpenAISettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIVideoGenerationService> _logger;

    public AzureOpenAIVideoGenerationService(
        IOptions<AzureOpenAISettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIVideoGenerationService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GeneratedVideo> GenerateAsync(
        string deploymentName,
        string prompt,
        int width,
        int height,
        int durationSeconds,
        CancellationToken cancellationToken = default)
    {
        using var videoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        videoCts.CancelAfter(TimeSpan.FromMinutes(10));

        var http = _httpClientFactory.CreateClient(HttpClientName);
        var createUrl = BuildVideoApiUrl("videos");
        var body = new
        {
            model = deploymentName,
            prompt,
            size = $"{width}x{height}",
            seconds = durationSeconds.ToString(),
        };

        using var createRequest = CreateVideoRequest(HttpMethod.Post, createUrl);
        createRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var createResponse = await http.SendAsync(createRequest, videoCts.Token);
        var createJson = await ReadSuccessfulResponseAsync(createResponse, "create video generation job", videoCts.Token);
        using var createDoc = JsonDocument.Parse(createJson);
        var videoId = createDoc.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new InvalidOperationException("Video generation did not return a video id");
        }

        await WaitForVideoGenerationAsync(http, videoId, videoCts.Token);
        var videoUrl = BuildVideoApiUrl($"videos/{Uri.EscapeDataString(videoId)}/content", "variant=video");
        using var downloadRequest = CreateVideoRequest(HttpMethod.Get, videoUrl);
        using var downloadResponse = await http.SendAsync(downloadRequest, videoCts.Token);
        if (!downloadResponse.IsSuccessStatusCode)
        {
            var error = await downloadResponse.Content.ReadAsStringAsync(videoCts.Token);
            throw new InvalidOperationException($"Failed to download generated video: {(int)downloadResponse.StatusCode} {error}");
        }

        var bytes = await downloadResponse.Content.ReadAsByteArrayAsync(videoCts.Token);
        return new GeneratedVideo(bytes, "video/mp4");
    }

    private async Task WaitForVideoGenerationAsync(HttpClient http, string videoId, CancellationToken ct)
    {
        var statusUrl = BuildVideoApiUrl($"videos/{Uri.EscapeDataString(videoId)}");

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            using var statusRequest = CreateVideoRequest(HttpMethod.Get, statusUrl);
            using var statusResponse = await http.SendAsync(statusRequest, ct);
            var statusJson = await ReadSuccessfulResponseAsync(statusResponse, "poll video generation status", ct);
            using var statusDoc = JsonDocument.Parse(statusJson);
            var root = statusDoc.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            _logger.LogInformation("Video generation {VideoId} status: {Status}", videoId, status ?? "unknown");

            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Video generation {status}: {statusJson}");
            }
        }
    }

    private HttpRequestMessage CreateVideoRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("api-key", _settings.ApiKey);
        return request;
    }

    private string BuildVideoApiUrl(string path, string? query = null)
    {
        var url = $"{_settings.Endpoint.TrimEnd('/')}/openai/v1/{path.TrimStart('/')}";
        return string.IsNullOrWhiteSpace(query) ? url : $"{url}?{query}";
    }

    private static async Task<string> ReadSuccessfulResponseAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to {operation}: {(int)response.StatusCode} {text}");
        }
        return text;
    }
}