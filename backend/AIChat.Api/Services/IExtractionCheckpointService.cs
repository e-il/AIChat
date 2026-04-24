using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IExtractionCheckpointService
{
    Task<ExtractionCheckpoint?> GetAsync(string userId, string conversationId);
    Task SetAsync(string userId, string conversationId, string lastExtractedMessageId);
}
