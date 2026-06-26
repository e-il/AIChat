namespace AIChat.Api.Services;

public interface IVideoGenerationService
{
    Task<GeneratedVideo> GenerateAsync(
        string deploymentName,
        string prompt,
        int width,
        int height,
        int durationSeconds,
        CancellationToken cancellationToken = default);
}

public sealed record GeneratedVideo(ReadOnlyMemory<byte> Bytes, string MimeType);