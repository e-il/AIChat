using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ExtractionCheckpointService : IExtractionCheckpointService
{
    private readonly UserJsonStore<Dictionary<string, ExtractionCheckpoint>> _store;

    public ExtractionCheckpointService(UserJsonStore<Dictionary<string, ExtractionCheckpoint>> store)
    {
        _store = store;
    }

    public async Task<ExtractionCheckpoint?> GetAsync(string userId, string conversationId)
    {
        var all = await _store.ReadAsync(userId);
        return all.TryGetValue(conversationId, out var cp) ? cp : null;
    }

    public Task SetAsync(string userId, string conversationId, string lastExtractedMessageId)
    {
        return _store.MutateAsync(userId, all =>
        {
            all[conversationId] = new ExtractionCheckpoint
            {
                LastExtractedMessageId = lastExtractedMessageId,
                LastExtractedAt = DateTime.UtcNow,
            };
        });
    }
}
