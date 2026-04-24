using Microsoft.AspNetCore.SignalR;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Hubs;

public class ChatHub : Hub
{
    public const string UserIdItemKey = "userId";

    private readonly IAzureOpenAIService _openAIService;
    private readonly IUserIdentityService _identity;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IAzureOpenAIService openAIService,
        IUserIdentityService identity,
        ILogger<ChatHub> logger)
    {
        _openAIService = openAIService;
        _identity = identity;
        _logger = logger;
    }

    private string? GetAuthCodeFromQuery()
    {
        var httpContext = Context.GetHttpContext();
        // SignalR accessTokenFactory sends token as 'access_token' query param for WebSocket
        // (browsers can't set headers on WebSocket connections)
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
        int maxMessages = 50)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            _logger.LogWarning("SendMessage rejected - connection not authenticated");
            await Clients.Caller.SendAsync("Error", conversationId, "Invalid authentication code");
            Context.Abort();
            return;
        }

        _logger.LogInformation("SendMessage: user={UserId}, conversationId={ConversationId}, message count={Count}, modelId={ModelId}, maxContextSize={MaxContextSize}, maxMessages={MaxMessages}",
            userId, conversationId, messages?.Count ?? 0, modelId, maxContextSize, maxMessages);

        if (messages is null || messages.Count == 0)
        {
            _logger.LogWarning("Empty messages array rejected for conversation: {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("Error", conversationId, "Messages cannot be empty");
            return;
        }

        var lastMessage = messages[^1];
        if (lastMessage.Role != "user" || string.IsNullOrWhiteSpace(lastMessage.Content))
        {
            _logger.LogWarning("Last message must be a non-empty user message for conversation: {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("Error", conversationId, "Last message must be a non-empty user message");
            return;
        }

        try
        {
            _logger.LogInformation("Streaming AI response with model {ModelId}", modelId);

            await foreach (var chunk in _openAIService.StreamChatCompletionAsync(messages, modelId, maxContextSize, maxMessages))
            {
                await Clients.Caller.SendAsync("ReceiveMessageChunk", conversationId, chunk);
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
}
