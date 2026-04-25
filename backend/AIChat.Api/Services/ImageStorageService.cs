using System.Text.RegularExpressions;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ImageStorageService : IImageStorageService
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
    };

    private readonly string _root;
    private readonly ILogger<ImageStorageService> _logger;

    public ImageStorageService(ILogger<ImageStorageService> logger)
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
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (!MimeToExt.TryGetValue(mimeType, out var ext))
        {
            throw new InvalidOperationException($"Unsupported image mime type: {mimeType}");
        }

        var userDir = Path.Combine(_root, userId);
        Directory.CreateDirectory(userDir);

        var id = Guid.NewGuid().ToString("N");
        var filename = $"{id}.{ext}";
        var path = Path.Combine(userDir, filename);

        await File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken);
        _logger.LogInformation("Stored image {Filename} for user {UserId} ({Bytes} bytes)", filename, userId, bytes.Length);

        return new MessageAttachment
        {
            Id = id,
            Type = "image",
            MimeType = mimeType,
            Url = $"/api/images/{filename}",
            Prompt = prompt,
            RevisedPrompt = revisedPrompt,
            Width = width,
            Height = height,
        };
    }

    public async Task SaveDescriptionAsync(
        string userId, string imageId, string description, CancellationToken cancellationToken = default)
    {
        ValidateUserId(imageId); // imageId comes from us (Guid.NewGuid().ToString("N")) — same charset rule
        ValidateUserId(userId);

        var userDir = Path.Combine(_root, userId);
        Directory.CreateDirectory(userDir);
        var path = Path.Combine(userDir, $"{imageId}.description.txt");
        await File.WriteAllTextAsync(path, description, cancellationToken);
    }

    public async Task<string?> TryReadDescriptionAsync(
        string userId, string imageId, CancellationToken cancellationToken = default)
    {
        if (!SafeUserId.IsMatch(userId) || !SafeUserId.IsMatch(imageId)) return null;
        var path = Path.Combine(_root, userId, $"{imageId}.description.txt");
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

        var ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        var mime = ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            _ => "application/octet-stream",
        };

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return (bytes, mime);
    }

    private static void ValidateUserId(string userId)
    {
        if (!SafeUserId.IsMatch(userId))
        {
            throw new InvalidOperationException($"Invalid userId for image storage: {userId}");
        }
    }
}
