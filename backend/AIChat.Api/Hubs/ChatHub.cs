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
    private readonly IExtractionCheckpointService _checkpoint;
    private readonly IExtractionQueue _extractionQueue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IAzureOpenAIService openAIService,
        IUserIdentityService identity,
        IMemoryService memory,
        IExtractionCheckpointService checkpoint,
        IExtractionQueue extractionQueue,
        IConfiguration configuration,
        ILogger<ChatHub> logger)
    {
        _openAIService = openAIService;
        _identity = identity;
        _memory = memory;
        _checkpoint = checkpoint;
        _extractionQueue = extractionQueue;
        _configuration = configuration;
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
        var hasAttachments = lastMessage.Attachments is { Count: > 0 };
        if (lastMessage.Role != "user" || (string.IsNullOrWhiteSpace(lastMessage.Content) && !hasAttachments))
        {
            await Clients.Caller.SendAsync("Error", conversationId, "Last message must be a user message with content or an attachment");
            return;
        }

        try
        {
            // Resolve memory based on mode and inject into system prompt
            var memories = await ResolveMemoriesAsync(userId, lastMessage.Content, memoryMode, explicitMemoryIds);
            var messagesToSend = messages;
            if (memories.Count > 0)
            {
                var systemPrompt = BuildSystemPrompt(memories);
                messagesToSend = PrependSystemMessage(messages, systemPrompt);
                _logger.LogInformation("Injected {Count} memories into system prompt", memories.Count);
                // Send full Memory objects so the client can surface content in the UI.
                await Clients.Caller.SendAsync("MemoryUsed", conversationId, memories);
            }

            var allowImageGen = _configuration.GetValue("AzureOpenAI:EnableImageGeneration", true);
            await foreach (var ev in _openAIService.StreamChatCompletionAsync(
                userId, messagesToSend, modelId, maxContextSize, maxMessages, allowImageGen))
            {
                switch (ev)
                {
                    case TextDelta td:
                        await Clients.Caller.SendAsync("ReceiveMessageChunk", conversationId, td.Text);
                        break;
                    case ToolCallStart tcs:
                        await Clients.Caller.SendAsync("ToolCallStart", conversationId, tcs.ToolName, tcs.ToolCallId);
                        break;
                    case AttachmentReady ar:
                        await Clients.Caller.SendAsync("AttachmentReady", conversationId, ar.Attachment, ar.ToolCallId);
                        break;
                }
            }

            if (memories.Count > 0)
            {
                await _memory.MarkUsedAsync(userId, memories.Select(m => m.Id));
            }

            _logger.LogInformation("Stream complete for conversation {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("StreamComplete", conversationId);

            // After successful stream, check whether to queue extraction.
            // messages is the client-sent list (without our injected system prompt).
            await TryTriggerExtractionAsync(userId, conversationId, messages);
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

    private async Task TryTriggerExtractionAsync(string userId, string conversationId, List<ChatMessage> messages)
    {
        var threshold = _configuration.GetValue<int>("Memory:ExtractionThreshold", 10);

        var checkpoint = await _checkpoint.GetAsync(userId, conversationId);
        var unextracted = GetUnextractedMessages(messages, checkpoint?.LastExtractedMessageId);

        // Threshold gating still uses raw count (so users with image-heavy turns aren't
        // permanently below threshold), but the extraction batch itself drops messages
        // with attachments — we don't extract memory from picture-related conversations.
        if (unextracted.Count < threshold)
        {
            _logger.LogDebug("Extraction not triggered: {Count} unextracted < threshold {Threshold}",
                unextracted.Count, threshold);
            return;
        }

        var textOnly = unextracted
            .Where(m => m.Attachments is null || m.Attachments.Count == 0)
            .ToList();

        var lastMessageId = unextracted[^1].Id;

        if (textOnly.Count == 0)
        {
            _logger.LogInformation(
                "Extraction skipped: all {Count} unextracted messages have image attachments. Advancing checkpoint to {LastId}.",
                unextracted.Count, lastMessageId);
            await _checkpoint.SetAsync(userId, conversationId, lastMessageId);
            return;
        }

        var enqueued = _extractionQueue.TryEnqueue(new ExtractionJob
        {
            UserId = userId,
            ConversationId = conversationId,
            Messages = textOnly,
            LastMessageId = lastMessageId,
        });

        if (enqueued)
        {
            _logger.LogInformation("Queued extraction: user={UserId}, conversation={ConversationId}, messages={Count}",
                userId, conversationId, unextracted.Count);
        }
        else
        {
            _logger.LogDebug("Extraction already pending for conversation {ConversationId}, skipped", conversationId);
        }
    }

    private static List<ChatMessage> GetUnextractedMessages(List<ChatMessage> messages, string? lastExtractedMessageId)
    {
        if (string.IsNullOrEmpty(lastExtractedMessageId)) return messages;

        var idx = messages.FindIndex(m => m.Id == lastExtractedMessageId);
        // Checkpoint id not found in the current history -- treat all as unextracted.
        if (idx < 0) return messages;

        return messages.Skip(idx + 1).ToList();
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
