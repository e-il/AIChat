using Microsoft.AspNetCore.Mvc;
using AIChat.Api.Middleware;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private static readonly string[] AllowedMimeTypes =
    {
        "image/png", "image/jpeg", "image/jpg", "image/webp", "image/gif",
    };
    private const long MaxUploadBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IImageStorageService _storage;
    private readonly IAzureOpenAIService _openAI;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(
        IImageStorageService storage,
        IAzureOpenAIService openAI,
        ILogger<ImagesController> logger)
    {
        _storage = storage;
        _openAI = openAI;
        _logger = logger;
    }

    private string? CurrentUserId => HttpContext.Items[AuthCodeMiddleware.UserIdItemKey] as string;

    /// <summary>
    /// Multipart upload of an image to attach to a user message (vision input).
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<MessageAttachment>> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (file is null || file.Length == 0) return BadRequest(new { error = "No file uploaded" });
        if (file.Length > MaxUploadBytes) return BadRequest(new { error = "File too large" });
        if (!AllowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = $"Unsupported content type: {file.ContentType}" });
        }

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var attachment = await _storage.SaveAsync(
            userId,
            bytes,
            file.ContentType,
            cancellationToken: ct);

        return Ok(attachment);
    }

    /// <summary>
    /// Serves a stored image. Unauthenticated: filenames are unguessable 128-bit GUIDs,
    /// so the image is located by filename alone. The endpoint is reachable via plain
    /// <img src> without any token in the URL.
    /// </summary>
    [HttpGet("{filename}")]
    public async Task<IActionResult> Get(string filename, CancellationToken ct)
    {
        var read = await _storage.TryReadByFilenameAsync(filename, ct);
        if (read is null) return NotFound();

        return File(read.Value.Bytes.ToArray(), read.Value.MimeType);
    }

    /// <summary>
    /// Dev-only: directly invokes image generation. Used to verify the gpt-image deployment
    /// before the chat-tool path lands. Safe to remove after P2.
    /// </summary>
    public class GenerateImageRequest
    {
        public string Prompt { get; set; } = "";
        public string? Size { get; set; }
    }

    [HttpPost("generate")]
    public async Task<ActionResult<MessageAttachment>> Generate(
        [FromBody] GenerateImageRequest req, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Prompt))
        {
            return BadRequest(new { error = "Prompt is required" });
        }
        if (!_openAI.IsImageGenerationAvailable)
        {
            return StatusCode(503, new { error = "Image generation is not available" });
        }

        try
        {
            var attachment = await _openAI.GenerateImageAsync(userId, req.Prompt, req.Size, ct);
            return Ok(attachment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image generation failed for user {UserId}", userId);
            return StatusCode(500, new { error = "Image generation failed" });
        }
    }
}
