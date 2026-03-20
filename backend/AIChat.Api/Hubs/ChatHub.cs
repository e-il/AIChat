using Microsoft.AspNetCore.SignalR;
using AIChat.Api.Services;

namespace AIChat.Api.Hubs;

public class ChatHub : Hub
{
    private readonly IAzureOpenAIService _openAIService;
    private readonly IConversationService _conversationService;
    private readonly ILogger<ChatHub> _logger;
    private readonly HashSet<string> _validCodes;
    
    // Track authenticated connections
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _authenticatedConnections = new();

    public ChatHub(
        IAzureOpenAIService openAIService,
        IConversationService conversationService,
        ILogger<ChatHub> logger,
        IConfiguration configuration)
    {
        _openAIService = openAIService;
        _conversationService = conversationService;
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
        
        // Store the connection as authenticated
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

    public async Task SendMessage(string conversationId, string message, string modelId)
    {
        _logger.LogInformation("SendMessage called: conversationId={ConversationId}, message length={Length}, modelId={ModelId}", 
            conversationId, message?.Length ?? 0, modelId);
            
        if (!IsConnectionAuthenticated())
        {
            _logger.LogWarning("SendMessage rejected - connection not authenticated");
            await Clients.Caller.SendAsync("Error", conversationId, "Invalid authentication code");
            Context.Abort();
            return;
        }

        try
        {
            // Get or create conversation
            var conversation = await _conversationService.GetConversationAsync(conversationId);
            if (conversation == null)
            {
                _logger.LogWarning("Conversation not found: {ConversationId}", conversationId);
                await Clients.Caller.SendAsync("Error", conversationId, "Conversation not found");
                return;
            }

            _logger.LogInformation("Conversation found, adding user message");
            
            // Add user message
            var userMessage = await _conversationService.AddMessageAsync(conversationId, "user", message);
            await Clients.Caller.SendAsync("MessageAdded", conversationId, userMessage);

            _logger.LogInformation("Streaming AI response with model {ModelId}", modelId);
            
            // Stream AI response
            var fullResponse = new System.Text.StringBuilder();
            
            await foreach (var chunk in _openAIService.StreamChatCompletionAsync(conversation.Messages, modelId))
            {
                fullResponse.Append(chunk);
                await Clients.Caller.SendAsync("ReceiveMessageChunk", conversationId, chunk);
            }

            _logger.LogInformation("Stream complete, saving assistant message");
            
            // Save complete assistant message
            var assistantMessage = await _conversationService.AddMessageAsync(
                conversationId, "assistant", fullResponse.ToString());
            
            // Send completion signal - client should disconnect after this
            await Clients.Caller.SendAsync("MessageComplete", conversationId, assistantMessage);
            _logger.LogInformation("MessageComplete sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for conversation {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("Error", conversationId, "An error occurred while processing your message");
        }
    }
}
