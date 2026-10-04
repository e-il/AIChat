using Microsoft.AspNetCore.Mvc;
using AIChat.Api.Middleware;
using AIChat.Api.Models;
using AIChat.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace AIChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MemoryController : ControllerBase
{
    private readonly IMemoryService _memory;
    private readonly IdleExtractionScheduler _scheduler;

    public MemoryController(IMemoryService memory, IdleExtractionScheduler scheduler)
    {
        _memory = memory;
        _scheduler = scheduler;
    }

    private string UserId =>
        HttpContext.Items[AuthCodeMiddleware.UserIdItemKey] as string
        ?? throw new InvalidOperationException("User not resolved");

    [HttpGet]
    public async Task<ActionResult<List<Memory>>> GetAll()
    {
        return Ok(await _memory.GetAllAsync(UserId));
    }

    [HttpPut("conversations/{conversationId:guid}/mode")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetConversationMode(Guid conversationId, [FromBody] UpdateConversationMemoryRequest request, CancellationToken cancellationToken)
    {
        await _scheduler.SetMemoryModeAsync(UserId, conversationId.ToString(), request.Enabled, request.LastMessageId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Memory>> Get(string id)
    {
        var memory = await _memory.GetAsync(UserId, id);
        return memory is null ? NotFound() : Ok(memory);
    }

    [HttpPost]
    public async Task<ActionResult<Memory>> Create([FromBody] CreateMemoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "content is required" });
        }

        var memory = await _memory.CreateAsync(UserId, request.Type, request.Content, request.SourceConversationId);
        return CreatedAtAction(nameof(Get), new { id = memory.Id }, memory);
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<Memory>> Update(string id, [FromBody] UpdateMemoryRequest request)
    {
        var memory = await _memory.UpdateAsync(UserId, id, request.Type, request.Content);
        return memory is null ? NotFound() : Ok(memory);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _memory.DeleteAsync(UserId, id);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("retrieve")]
    public async Task<ActionResult<List<Memory>>> Retrieve([FromBody] RetrieveMemoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "query is required" });
        }

        var limit = request.Limit is > 0 ? request.Limit.Value : 5;
        return Ok(await _memory.RetrieveAsync(UserId, request.Query, limit));
    }
}

public class CreateMemoryRequest
{
    public MemoryType Type { get; set; } = MemoryType.Fact;
    public string Content { get; set; } = "";
    public string? SourceConversationId { get; set; }
}

public class UpdateMemoryRequest
{
    public MemoryType? Type { get; set; }
    public string? Content { get; set; }
}

public class RetrieveMemoryRequest
{
    public string Query { get; set; } = "";
    public int? Limit { get; set; }
}

/// <summary>Controls memory use and extraction for one conversation without deleting existing memories.</summary>
public sealed record UpdateConversationMemoryRequest
{
    public required bool Enabled { get; init; }
    [MaxLength(128)]
    public string? LastMessageId { get; init; }
}
