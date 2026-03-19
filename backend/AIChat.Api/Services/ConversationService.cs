using System.Collections.Concurrent;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ConversationService : IConversationService
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();

    public Task<List<ConversationSummary>> GetAllConversationsAsync()
    {
        var summaries = _conversations.Values
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ConversationSummary
            {
                Id = c.Id,
                Title = c.Title,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                MessageCount = c.Messages.Count
            })
            .ToList();

        return Task.FromResult(summaries);
    }

    public Task<Conversation?> GetConversationAsync(string id)
    {
        _conversations.TryGetValue(id, out var conversation);
        return Task.FromResult(conversation);
    }

    public Task<Conversation> CreateConversationAsync()
    {
        var conversation = new Conversation();
        _conversations[conversation.Id] = conversation;
        return Task.FromResult(conversation);
    }

    public Task<bool> DeleteConversationAsync(string id)
    {
        return Task.FromResult(_conversations.TryRemove(id, out _));
    }

    public Task<ChatMessage> AddMessageAsync(string conversationId, string role, string content)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
        {
            throw new KeyNotFoundException($"Conversation {conversationId} not found");
        }

        var message = new ChatMessage
        {
            Role = role,
            Content = content
        };

        conversation.Messages.Add(message);
        conversation.UpdatedAt = DateTime.UtcNow;

        // Auto-generate title from first user message
        if (conversation.Title == "New Chat" && role == "user" && conversation.Messages.Count == 1)
        {
            conversation.Title = content.Length > 50 ? content[..47] + "..." : content;
        }

        return Task.FromResult(message);
    }

    public Task UpdateTitleAsync(string conversationId, string title)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.Title = title;
            conversation.UpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }
}
