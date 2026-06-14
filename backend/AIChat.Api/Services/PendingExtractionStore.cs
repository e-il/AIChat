using AIChat.Api.Models;

namespace AIChat.Api.Services;

/// <summary>
/// A pending conversation snapshot awaiting idle-based memory extraction.
/// Persisted per user so a restart during the idle window doesn't lose it.
/// </summary>
public class PendingExtraction
{
    public string ConversationId { get; set; } = "";
    // The latest full message snapshot sent by the client. Replaced on every new turn.
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime LastActivityUtc { get; set; }
}

/// <summary>
/// File-backed staging area for conversations whose messages may need extracting once
/// they go idle. The backend does not persist chat history (the client owns it and sends
/// the full list each turn), so the most recent snapshot is kept here until a conversation
/// has been quiet long enough to flush. Stored as data/pending/{userId}.json keyed by
/// conversationId. Survives restarts; <see cref="IdleExtractionScheduler"/> reloads and
/// reschedules pending entries on startup.
/// </summary>
public class PendingExtractionStore
{
    private readonly UserJsonStore<Dictionary<string, PendingExtraction>> _store;

    public PendingExtractionStore(UserJsonStore<Dictionary<string, PendingExtraction>> store)
    {
        _store = store;
    }

    /// <summary>Record (or refresh) the latest snapshot for a conversation.</summary>
    public Task SaveAsync(string userId, string conversationId, List<ChatMessage> messages)
    {
        return _store.MutateAsync(userId, all =>
        {
            all[conversationId] = new PendingExtraction
            {
                ConversationId = conversationId,
                Messages = messages,
                LastActivityUtc = DateTime.UtcNow,
            };
        });
    }

    public async Task<PendingExtraction?> GetAsync(string userId, string conversationId)
    {
        var all = await _store.ReadAsync(userId);
        return all.TryGetValue(conversationId, out var entry) ? entry : null;
    }

    public Task RemoveAsync(string userId, string conversationId)
    {
        return _store.MutateAsync(userId, all => all.Remove(conversationId));
    }

    /// <summary>Load every persisted pending conversation across all users (startup reload).</summary>
    public async Task<List<(string UserId, PendingExtraction Entry)>> LoadAllAsync()
    {
        var result = new List<(string, PendingExtraction)>();

        foreach (var userId in _store.EnumerateUserIds())
        {
            var all = await _store.ReadAsync(userId);
            foreach (var entry in all.Values)
            {
                result.Add((userId, entry));
            }
        }

        return result;
    }
}
