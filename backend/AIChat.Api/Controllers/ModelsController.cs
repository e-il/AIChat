using Microsoft.AspNetCore.Mvc;
using AIChat.Api.Models;
using AIChat.Api.Services;
using AIChat.Api.Middleware;

namespace AIChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly IAzureOpenAIService _openAIService;
    private readonly IJsonConfigurationService _configuration;
    private readonly IUserIdentityService _identity;

    public ModelsController(IAzureOpenAIService openAIService, IJsonConfigurationService configuration, IUserIdentityService identity)
    {
        _openAIService = openAIService;
        _configuration = configuration;
        _identity = identity;
    }

    [HttpGet]
    public ActionResult<ModelsResponse> GetModels()
    {
        // Only Kind="chat" entries are user-selectable. Media and embedding models are invoked internally.
        var chatModels = _openAIService.GetAvailableModels()
            .Where(m => string.Equals(m.Kind, "chat", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Ok(new ModelsResponse
        {
            Models = chatModels,
            AllModels = _identity.IsAdmin(HttpContext.Items[AuthCodeMiddleware.UserIdItemKey] as string) ? _openAIService.GetAvailableModels() : new(),
            DefaultModel = _openAIService.GetDefaultModel(),
            DefaultContextSize = _openAIService.GetDefaultContextSize(),
            ContextSizeOptions = _openAIService.GetContextSizeOptions(),
            DefaultMaxMessages = _openAIService.GetDefaultMaxMessages(),
            MaxMessagesOptions = _openAIService.GetMaxMessagesOptions(),
            IsAdmin = _identity.IsAdmin(HttpContext.Items[AuthCodeMiddleware.UserIdItemKey] as string)
        });
    }

    [HttpPut]
    public async Task<ActionResult<List<ModelInfo>>> Upsert([FromBody] ModelInfo model, CancellationToken ct)
    {
        var userId = HttpContext.Items[AuthCodeMiddleware.UserIdItemKey] as string;
        if (!_identity.IsAdmin(userId)) return StatusCode(StatusCodes.Status403Forbidden);
        try { return Ok(await _configuration.AddOrUpdateModelAsync(model, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var userId = HttpContext.Items[AuthCodeMiddleware.UserIdItemKey] as string;
        if (!_identity.IsAdmin(userId)) return StatusCode(StatusCodes.Status403Forbidden);
        return await _configuration.DeleteModelAsync(id, ct) ? NoContent() : NotFound();
    }
}

public class ModelsResponse
{
    public List<ModelInfo> Models { get; set; } = new();
    public string DefaultModel { get; set; } = "";
    public int DefaultContextSize { get; set; }
    public List<int> ContextSizeOptions { get; set; } = new();
    public int DefaultMaxMessages { get; set; }
    public List<int> MaxMessagesOptions { get; set; } = new();
    public bool IsAdmin { get; set; }
    public List<ModelInfo> AllModels { get; set; } = new();
}
