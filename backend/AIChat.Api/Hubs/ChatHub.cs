using Microsoft.AspNetCore.SignalR;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Hubs;

public class ChatHub : Hub
{
    private readonly IAzureOpenAIService _openAIService;
    private readonly ILogger<ChatHub> _logger;
    private readonly HashSet<string> _validCodes;

    // Track authenticated connections
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _authenticatedConnections = new();

    public ChatHub(
        IAzureOpenAIService openAIService,
        ILogger<ChatHub> logger,
        IConfiguration configuration)
    {
        _openAIService = openAIService;
        _logger = logger;
        var codes = configuration.GetSection("AuthCodes").Get<string[]>() ?? [];
        _validCodes = new HashSet<string>(codes, StringComparer.Ordinal);
    }

    private string? GetAuthCodeFromQuery()
    {
        var httpContext = Context.GetHttpContext();
        // SignalR accessTokenFactory sends token as 'access_token' query param for WebSocket
        // (browsers can't set headers on WebSocket connections)
        return httpContext?.Request.Query["access_token"].FirstOrDefault();
    }

    public override async Task OnConnectedAsync()
    {
        var authCode = GetAuthCodeFromQuery();
        _logger.LogInformation("SignalR connect - authCode: {AuthCode}, valid codes count: {Count}",
            authCode ?? "null", _validCodes.Count);

        if (string.IsNullOrEmpty(authCode) || !_validCodes.Contains(authCode))
        {
            _logger.LogWarning("SignalR auth failed on connect, aborting");
            Context.Abort();
            return;
        }

        _authenticatedConnections[Context.ConnectionId] = authCode;
        _logger.LogInformation("SignalR auth succeeded for connection {ConnectionId}", Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _authenticatedConnections.TryRemove(Context.ConnectionId, out _);
        _logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private bool IsConnectionAuthenticated()
    {
        return _authenticatedConnections.ContainsKey(Context.ConnectionId);
    }

    public async Task SendMessage(
        string conversationId,
        List<ChatMessage> messages,
        string modelId,
        int maxContextSize = 100000,
        int maxMessages = 50)
    {
        _logger.LogInformation("SendMessage called: conversationId={ConversationId}, message count={Count}, modelId={ModelId}, maxContextSize={MaxContextSize}, maxMessages={MaxMessages}",
            conversationId, messages?.Count ?? 0, modelId, maxContextSize, maxMessages);

        if (!IsConnectionAuthenticated())
        {
            _logger.LogWarning("SendMessage rejected - connection not authenticated");
            await Clients.Caller.SendAsync("Error", conversationId, "Invalid authentication code");
            Context.Abort();
            return;
        }

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
