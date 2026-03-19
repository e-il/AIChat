using Microsoft.AspNetCore.Mvc;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;

    public ConversationsController(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ConversationSummary>>> GetAll()
    {
        var conversations = await _conversationService.GetAllConversationsAsync();
        return Ok(conversations);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Conversation>> Get(string id)
    {
        var conversation = await _conversationService.GetConversationAsync(id);
        if (conversation == null)
        {
            return NotFound();
        }
        return Ok(conversation);
    }

    [HttpPost]
    public async Task<ActionResult<Conversation>> Create()
    {
        var conversation = await _conversationService.CreateConversationAsync();
        return CreatedAtAction(nameof(Get), new { id = conversation.Id }, conversation);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _conversationService.DeleteConversationAsync(id);
        if (!deleted)
        {
            return NotFound();
        }
        return NoContent();
    }

    [HttpPatch("{id}/title")]
    public async Task<IActionResult> UpdateTitle(string id, [FromBody] UpdateTitleRequest request)
    {
        var conversation = await _conversationService.GetConversationAsync(id);
        if (conversation == null)
        {
            return NotFound();
        }
        
        await _conversationService.UpdateTitleAsync(id, request.Title);
        return NoContent();
    }
}

public class UpdateTitleRequest
{
    public string Title { get; set; } = "";
}
