using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IMemoryService
{
    Task<List<Memory>> GetAllAsync(string userId);
    Task<Memory?> GetAsync(string userId, string id);
    Task<List<Memory>> GetByIdsAsync(string userId, IEnumerable<string> ids);
    Task<Memory> CreateAsync(string userId, MemoryType type, string content, string? sourceConversationId);
    Task<Memory?> UpdateAsync(string userId, string id, MemoryType? type, string? content);
    Task<bool> DeleteAsync(string userId, string id);

    /// <summary>
    /// Retrieve relevant memories for a given query. All "preference" memories are
    /// always included; "fact"/"summary" memories go through scoring and the top
    /// results are returned. Results are capped by total character count.
    /// </summary>
    Task<List<Memory>> RetrieveAsync(string userId, string query, int limit = 5);

    /// <summary>
    /// Increment useCount and update lastUsedAt for the given memories.
    /// </summary>
    Task MarkUsedAsync(string userId, IEnumerable<string> ids);
}
