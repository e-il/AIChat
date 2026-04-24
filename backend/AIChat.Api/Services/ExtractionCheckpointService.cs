using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ExtractionCheckpointService : IExtractionCheckpointService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex SafeUserId = new(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

    private readonly string _dir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ILogger<ExtractionCheckpointService> _logger;

    public ExtractionCheckpointService(ILogger<ExtractionCheckpointService> logger)
    {
        _logger = logger;
        _dir = Path.Combine("data", "extraction");
        Directory.CreateDirectory(_dir);
    }

    public async Task<ExtractionCheckpoint?> GetAsync(string userId, string conversationId)
    {
        var all = await WithLock(userId, () => LoadAsync(userId));
        return all.TryGetValue(conversationId, out var cp) ? cp : null;
    }

    public Task SetAsync(string userId, string conversationId, string lastExtractedMessageId)
    {
        return WithLock(userId, async () =>
        {
            var all = await LoadAsync(userId);
            all[conversationId] = new ExtractionCheckpoint
            {
                LastExtractedMessageId = lastExtractedMessageId,
                LastExtractedAt = DateTime.UtcNow,
            };
            await SaveAsync(userId, all);
        });
    }

    // ---------- IO + locking ----------

    private SemaphoreSlim LockFor(string userId) =>
        _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    private async Task<T> WithLock<T>(string userId, Func<Task<T>> action)
    {
        ValidateUserId(userId);
        var sem = LockFor(userId);
        await sem.WaitAsync();
        try { return await action(); }
        finally { sem.Release(); }
    }

    private async Task WithLock(string userId, Func<Task> action)
    {
        ValidateUserId(userId);
        var sem = LockFor(userId);
        await sem.WaitAsync();
        try { await action(); }
        finally { sem.Release(); }
    }

    private static void ValidateUserId(string userId)
    {
        if (!SafeUserId.IsMatch(userId))
        {
            throw new InvalidOperationException($"Invalid userId for file storage: {userId}");
        }
    }

    private string PathFor(string userId) => Path.Combine(_dir, $"{userId}.json");

    private async Task<Dictionary<string, ExtractionCheckpoint>> LoadAsync(string userId)
    {
        var path = PathFor(userId);
        if (!File.Exists(path)) return new Dictionary<string, ExtractionCheckpoint>();

        try
        {
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<Dictionary<string, ExtractionCheckpoint>>(stream, JsonOptions);
            return data ?? new Dictionary<string, ExtractionCheckpoint>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse extraction checkpoint file for user {UserId}, returning empty", userId);
            return new Dictionary<string, ExtractionCheckpoint>();
        }
    }

    private async Task SaveAsync(string userId, Dictionary<string, ExtractionCheckpoint> data)
    {
        var path = PathFor(userId);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
