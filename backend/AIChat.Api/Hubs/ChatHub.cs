using System.Text;
using Microsoft.AspNetCore.SignalR;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Hubs;

public class ChatHub : Hub
{
    public const string UserIdItemKey = "userId";

    private readonly IAzureOpenAIService _openAIService;
    private readonly IUserIdentityService _identity;
    private readonly IMemoryService _memory;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IAzureOpenAIService openAIService,
        IUserIdentityService identity,
        IMemoryService memory,
        ILogger<ChatHub> logger)
    {
        _openAIService = openAIService;
        _identity = identity;
        _memory = memory;
        _logger = logger;
    }

    private string? GetAuthCodeFromQuery()
    {
        var httpContext = Context.GetHttpContext();
        return httpContext?.Request.Query["access_token"].FirstOrDefault();
    }

    private string? GetUserId() => Context.Items[UserIdItemKey] as string;

    public override async Task OnConnectedAsync()
    {
        var authCode = GetAuthCodeFromQuery();
        var userId = _identity.ResolveUserId(authCode);

        if (userId is null)
        {
            _logger.LogWarning("SignalR auth failed on connect, aborting");
            Context.Abort();
            return;
        }

        Context.Items[UserIdItemKey] = userId;
        _logger.LogInformation("SignalR auth succeeded for user {UserId} on connection {ConnectionId}", userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(
        string conversationId,
        List<ChatMessage> messages,
        string modelId,
        int maxContextSize = 100000,
        int maxMessages = 50,
        string memoryMode = "auto",
        List<string>? explicitMemoryIds = null)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            _logger.LogWarning("SendMessage rejected - connection not authenticated");
            await Clients.Caller.SendAsync("Error", conversationId, "Invalid authentication code");
            Context.Abort();
            return;
        }

        _logger.LogInformation("SendMessage: user={UserId}, conversationId={ConversationId}, message count={Count}, modelId={ModelId}, memoryMode={MemoryMode}",
            userId, conversationId, messages?.Count ?? 0, modelId, memoryMode);

        if (messages is null || messages.Count == 0)
        {
            await Clients.Caller.SendAsync("Error", conversationId, "Messages cannot be empty");
            return;
        }

        var lastMessage = messages[^1];
        if (lastMessage.Role != "user" || string.IsNullOrWhiteSpace(lastMessage.Content))
        {
            await Clients.Caller.SendAsync("Error", conversationId, "Last message must be a non-empty user message");
            return;
        }

        try
        {
            // Resolve memory based on mode
            var memories = await ResolveMemoriesAsync(userId, lastMessage.Content, memoryMode, explicitMemoryIds);
            if (memories.Count > 0)
            {
                var systemPrompt = BuildSystemPrompt(memories);
                messages = PrependSystemMessage(messages, systemPrompt);
                _logger.LogInformation("Injected {Count} memories into system prompt", memories.Count);
                await Clients.Caller.SendAsync("MemoryUsed", conversationId, memories.Select(m => m.Id).ToList());
            }

            await foreach (var chunk in _openAIService.StreamChatCompletionAsync(messages, modelId, maxContextSize, maxMessages))
            {
                await Clients.Caller.SendAsync("ReceiveMessageChunk", conversationId, chunk);
            }

            // Mark memories as used after successful stream
            if (memories.Count > 0)
            {
                await _memory.MarkUsedAsync(userId, memories.Select(m => m.Id));
            }

            _logger.LogInformation("Stream complete for conversation {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("StreamComplete", conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for conversation {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("Error", conversationId, "An error occurred while processing your message");
        }
    }

    private async Task<List<Memory>> ResolveMemoriesAsync(
        string userId,
        string query,
        string memoryMode,
        List<string>? explicitMemoryIds)
    {
        return memoryMode switch
        {
            "off" => new List<Memory>(),
            "explicit" => explicitMemoryIds is null or { Count: 0 }
                ? new List<Memory>()
                : await _memory.GetByIdsAsync(userId, explicitMemoryIds),
            _ => await _memory.RetrieveAsync(userId, query),
        };
    }

    private static string BuildSystemPrompt(List<Memory> memories)
    {
        var sb = new StringBuilder();
        sb.Append("You are a helpful AI assistant. Be concise and helpful in your responses.");

        if (memories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Here are some things you remember about the user:");
            foreach (var memory in memories)
            {
                sb.Append("- ");
                sb.AppendLine(memory.Content);
            }
            sb.AppendLine();
            sb.Append("Use this context naturally when relevant. Don't explicitly mention that you remembered something unless the user asks.");
        }

        return sb.ToString();
    }

    private static List<ChatMessage> PrependSystemMessage(List<ChatMessage> messages, string systemContent)
    {
        var prepended = new List<ChatMessage>(messages.Count + 1)
        {
            new ChatMessage { Role = "system", Content = systemContent },
        };
        prepended.AddRange(messages);
        return prepended;
    }
}
