using Microsoft.AspNetCore.Mvc;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly IAzureOpenAIService _openAIService;

    public ModelsController(IAzureOpenAIService openAIService)
    {
        _openAIService = openAIService;
    }

    [HttpGet]
    public ActionResult<ModelsResponse> GetModels()
    {
        return Ok(new ModelsResponse
        {
            Models = _openAIService.GetAvailableModels(),
            DefaultModel = _openAIService.GetDefaultModel(),
            DefaultContextSize = _openAIService.GetDefaultContextSize(),
            ContextSizeOptions = _openAIService.GetContextSizeOptions(),
            DefaultMaxMessages = _openAIService.GetDefaultMaxMessages(),
            MaxMessagesOptions = _openAIService.GetMaxMessagesOptions()
        });
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
}
