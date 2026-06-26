using System.Text.RegularExpressions;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class MediaStorageService : IMediaStorageService
{
    private static readonly Regex SafeUserId = new(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeFilename = new(@"^[A-Za-z0-9_\-]+\.[A-Za-z0-9]+$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> MimeToExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/jpg"] = "jpg",
        ["image/webp"] = "webp",
        ["image/gif"] = "gif",
        ["video/mp4"] = "mp4",
    };

    private readonly string _root;
    private readonly ILogger<MediaStorageService> _logger;

    public MediaStorageService(ILogger<MediaStorageService> logger)
    {
        _logger = logger;
        _root = Path.Combine("data", "images");
        Directory.CreateDirectory(_root);
    }

    public async Task<MessageAttachment> SaveAsync(
        string userId,
        ReadOnlyMemory<byte> bytes,
        string mimeType,
        string? prompt = null,
        string? revisedPrompt = null,
        int? width = null,
        int? height = null,
        int? durationSeconds = null,
        string? providerMediaId = null,
        string attachmentType = "image",
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (!MimeToExt.TryGetValue(mimeType, out var ext))
        {
            throw new InvalidOperationException($"Unsupported media mime type: {mimeType}");
        }

        var userDir = Path.Combine(_root, userId);
        Directory.CreateDirectory(userDir);

        var id = Guid.NewGuid().ToString("N");
        var filename = $"{id}.{ext}";
        var path = Path.Combine(userDir, filename);

        await File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken);
        _logger.LogInformation("Stored {AttachmentType} {Filename} for user {UserId} ({Bytes} bytes)", attachmentType, filename, userId, bytes.Length);

        return new MessageAttachment
        {
            Id = id,
            Type = attachmentType,
            MimeType = mimeType,
            Url = $"{GetRoutePrefix(attachmentType)}/{filename}",
            Prompt = prompt,
            RevisedPrompt = revisedPrompt,
            Width = width,
            Height = height,
            DurationSeconds = durationSeconds,
            ProviderMediaId = providerMediaId,
        };
    }

    public async Task SaveDescriptionAsync(
        string userId, string mediaId, string description, CancellationToken cancellationToken = default)
    {
        ValidateUserId(mediaId);
        ValidateUserId(userId);

        var userDir = Path.Combine(_root, userId);
        Directory.CreateDirectory(userDir);
        var path = Path.Combine(userDir, $"{mediaId}.description.txt");
        await File.WriteAllTextAsync(path, description, cancellationToken);
    }

    public async Task<string?> TryReadDescriptionAsync(
        string userId, string mediaId, CancellationToken cancellationToken = default)
    {
        if (!SafeUserId.IsMatch(userId) || !SafeUserId.IsMatch(mediaId)) return null;
        var path = Path.Combine(_root, userId, $"{mediaId}.description.txt");
        var fullUserDir = Path.GetFullPath(Path.Combine(_root, userId));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullUserDir, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, cancellationToken) : null;
    }

    public string? TryGetPath(string userId, string filename)
    {
        if (!SafeUserId.IsMatch(userId) || !SafeFilename.IsMatch(filename)) return null;

        var userDir = Path.Combine(_root, userId);
        var path = Path.Combine(userDir, filename);

        // Defence in depth: ensure resolved path is still inside the user's directory.
        var fullUserDir = Path.GetFullPath(userDir);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullUserDir, StringComparison.OrdinalIgnoreCase)) return null;

        return File.Exists(fullPath) ? fullPath : null;
    }

    public async Task<(ReadOnlyMemory<byte> Bytes, string MimeType)?> TryReadAsync(
        string userId, string filename, CancellationToken cancellationToken = default)
    {
        var path = TryGetPath(userId, filename);
        if (path is null) return null;
        return (await File.ReadAllBytesAsync(path, cancellationToken), MimeForFile(filename));
    }

    public async Task<(ReadOnlyMemory<byte> Bytes, string MimeType)?> TryReadByFilenameAsync(
        string filename, CancellationToken cancellationToken = default)
    {
        // Filenames are globally-unique GUIDs, so the owning user directory is irrelevant
        // for lookup. SafeFilename blocks path traversal; we still join + verify each
        // candidate path stays within its user directory as defence in depth.
        if (!SafeFilename.IsMatch(filename) || !Directory.Exists(_root)) return null;

        foreach (var userDir in Directory.EnumerateDirectories(_root))
        {
            var path = Path.Combine(userDir, filename);
            var fullUserDir = Path.GetFullPath(userDir);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullUserDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(fullPath)) continue;

            return (await File.ReadAllBytesAsync(fullPath, cancellationToken), MimeForFile(filename));
        }

        return null;
    }

    private static string MimeForFile(string filename) =>
        Path.GetExtension(filename).TrimStart('.').ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "mp4" => "video/mp4",
            _ => "application/octet-stream",
        };

    private static string GetRoutePrefix(string attachmentType) =>
        string.Equals(attachmentType, "video", StringComparison.OrdinalIgnoreCase)
            ? "/api/videos"
            : "/api/images";

    private static void ValidateUserId(string userId)
    {
        if (!SafeUserId.IsMatch(userId))
        {
            throw new InvalidOperationException($"Invalid userId for media storage: {userId}");
        }
    }
}
