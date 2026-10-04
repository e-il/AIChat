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
            if (!all.TryGetValue(conversationId, out var checkpoint))
                all[conversationId] = checkpoint = new ExtractionCheckpoint();
            checkpoint.LastExtractedMessageId = lastExtractedMessageId;
            checkpoint.LastExtractedAt = DateTime.UtcNow;
        });
    }

    public Task SetMemoryModeAsync(string userId, string conversationId, bool enabled, string? lastMessageId)
    {
        return _store.MutateAsync(userId, all =>
        {
            if (!all.TryGetValue(conversationId, out var checkpoint))
                all[conversationId] = checkpoint = new ExtractionCheckpoint();
            checkpoint.MemoryDisabled = !enabled;
            if (!enabled && !string.IsNullOrWhiteSpace(lastMessageId))
                checkpoint.SuppressedThroughMessageId = lastMessageId;
        });
    }
}
