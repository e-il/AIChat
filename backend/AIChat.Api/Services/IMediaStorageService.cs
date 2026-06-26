using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IMediaStorageService
{
    /// <summary>
    /// Persists raw media bytes for a user and returns a MessageAttachment whose Url
    /// is a server-relative path (/api/images/{filename}) or (/api/videos/{filename}). The bytes live on
    /// disk under data/images/{userId}/.
    /// </summary>
    Task<MessageAttachment> SaveAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a stored media file's absolute path. Returns null if the file does not exist
    /// or if the requested filename escapes the user's directory.
    /// </summary>
    string? TryGetPath(string userId, string filename);

    /// <summary>
    /// Reads stored bytes (used by the chat path to inline a vision-input image as base64
    /// when calling Azure OpenAI, since Azure cannot fetch our internal URLs).
    /// </summary>
    Task<(ReadOnlyMemory<byte> Bytes, string MimeType)?> TryReadAsync(
        string userId, string filename, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads stored media by filename alone, locating it across user directories.
    /// Filenames are globally-unique 128-bit GUIDs, so this is unambiguous. Used by the
    /// public (unauthenticated) image-serving endpoint. Returns null if not found or if
    /// the filename is unsafe.
    /// </summary>
    Task<(ReadOnlyMemory<byte> Bytes, string MimeType)?> TryReadByFilenameAsync(
        string filename, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the model's description of generated media as `{mediaId}.description.txt`
    /// next to the media bytes. Called after the second tool-calling pass produces text.
    /// </summary>
    Task SaveDescriptionAsync(
        string userId, string mediaId, string description, CancellationToken cancellationToken = default);

    /// <summary>Reads the saved description sidecar for generated media, if any.</summary>
    Task<string?> TryReadDescriptionAsync(
        string userId, string mediaId, CancellationToken cancellationToken = default);
}
