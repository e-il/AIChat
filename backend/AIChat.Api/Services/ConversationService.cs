using Microsoft.EntityFrameworkCore;
using AIChat.Api.Data;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ConversationService : IConversationService
{
    private readonly AIChatDbContext _db;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ConversationService(AIChatDbContext db)
    {
        _db = db;
    }

    public async Task<List<ConversationSummary>> GetAllConversationsAsync()
    {
        return await _db.Conversations
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ConversationSummary
            {
                Id = c.Id,
                Title = c.Title,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                MessageCount = c.Messages.Count
            })
            .ToListAsync();
    }

    public async Task<Conversation?> GetConversationAsync(string id)
    {
        return await _db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.Timestamp))
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Conversation> CreateConversationAsync()
    {
        var conversation = new Conversation();
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync();
        return conversation;
    }

    public async Task<bool> DeleteConversationAsync(string id)
    {
        var conversation = await _db.Conversations.FindAsync(id);
        if (conversation == null) return false;
        
        _db.Conversations.Remove(conversation);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ChatMessage> AddMessageAsync(string conversationId, string role, string content)
    {
        // Use lock to prevent race conditions when adding messages
        await _lock.WaitAsync();
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId)
                ?? throw new KeyNotFoundException($"Conversation {conversationId} not found");

            var message = new ChatMessage
            {
                ConversationId = conversationId,
                Role = role,
                Content = content
            };

            _db.Messages.Add(message);
            conversation.UpdatedAt = DateTime.UtcNow;

            // Auto-generate title from first user message
            if (conversation.Title == "New Chat" && role == "user")
            {
                var messageCount = await _db.Messages.CountAsync(m => m.ConversationId == conversationId);
                if (messageCount == 0)
                {
                    conversation.Title = content.Length > 50 ? content[..47] + "..." : content;
                }
            }

            await _db.SaveChangesAsync();
            return message;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateTitleAsync(string conversationId, string title)
    {
        var conversation = await _db.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.Title = title;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
