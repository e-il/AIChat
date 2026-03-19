using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IConversationService
{
    Task<List<ConversationSummary>> GetAllConversationsAsync();
    Task<Conversation?> GetConversationAsync(string id);
    Task<Conversation> CreateConversationAsync();
    Task<bool> DeleteConversationAsync(string id);
    Task<ChatMessage> AddMessageAsync(string conversationId, string role, string content);
    Task UpdateTitleAsync(string conversationId, string title);
}
