using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Hubs;

public class ChatHub : Hub
{
    public const string UserIdItemKey = "userId";

    private readonly IAzureOpenAIService _openAIService;
    private readonly IUserIdentityService _identity;
    private readonly IMemoryService _memory;
    private readonly IdleExtractionScheduler _extractionScheduler;
    private readonly IMemorySuppressionPolicy _memorySuppression;
    private readonly IPromptProfileRegistry _promptProfiles;
    private readonly AzureOpenAISettings _azureOpenAISettings;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IAzureOpenAIService openAIService,
        IUserIdentityService identity,
        IMemoryService memory,
        IdleExtractionScheduler extractionScheduler,
        IMemorySuppressionPolicy memorySuppression,
        IPromptProfileRegistry promptProfiles,
        IOptions<AzureOpenAISettings> azureOpenAISettings,
        ILogger<ChatHub> logger)
    {
        _openAIService = openAIService;
        _identity = identity;
        _memory = memory;
        _extractionScheduler = extractionScheduler;
        _memorySuppression = memorySuppression;
        _promptProfiles = promptProfiles;
        _azureOpenAISettings = azureOpenAISettings.Value;
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

    public async Task SendMessage(SendMessageRequest? request)
    {
        if (request is null)
        {
            await Clients.Caller.SendAsync("Error", "", "Request cannot be empty");
            return;
        }

        var conversationId = request.ConversationId;
        var messages = request.Messages;
        var modelId = request.ModelId;
        var maxContextSize = request.MaxContextSize;
        var maxMessages = request.MaxMessages;
        var memoryMode = request.MemoryMode;
        var explicitMemoryIds = request.ExplicitMemoryIds;

        var userId = GetUserId();
        if (userId is null)
        {
            _logger.LogWarning("SendMessage rejected - connection not authenticated");
            await Clients.Caller.SendAsync("Error", conversationId, "Invalid authentication code");
            Context.Abort();
            return;
        }

        _logger.LogInformation("SendMessage: user={UserId}, conversationId={ConversationId}, message count={Count}, modelId={ModelId}, memoryMode={MemoryMode}, promptProfileId={PromptProfileId}",
            userId, conversationId, messages?.Count ?? 0, modelId, memoryMode, request.PromptProfileId);

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
            if (!_promptProfiles.TryResolveSystemPrompt(
                    request.PromptProfileId,
                    request.CustomSystemPrompt,
                    out var selectedSystemPrompt,
                    out var isDefaultGeneralPrompt,
                    out var promptError))
            {
                await Clients.Caller.SendAsync("Error", conversationId, promptError);
                return;
            }

            var suppressMemory = _memorySuppression.ShouldSuppress(request.PromptProfileId, messages);
            if (suppressMemory)
            {
                _logger.LogInformation(
                    "Memory disabled for conversation {ConversationId}: promptProfileId={PromptProfileId}",
                    conversationId,
                    request.PromptProfileId);
            }

            // Resolve memory based on mode and inject into system prompt.
            var memories = suppressMemory
                ? new List<Memory>()
                : await ResolveMemoriesAsync(userId, lastMessage.Content, memoryMode, explicitMemoryIds);
            var messagesToSend = messages;
            if (memories.Count > 0 || !isDefaultGeneralPrompt)
            {
                var systemPrompt = BuildSystemPrompt(selectedSystemPrompt, memories, _promptProfiles.GeneralSystemPrompt);
                messagesToSend = PrependSystemMessage(messages, systemPrompt);
                if (memories.Count > 0)
                {
                    _logger.LogInformation("Injected {Count} memories into system prompt", memories.Count);
                    // Send full Memory objects so the client can surface content in the UI.
                    await Clients.Caller.SendAsync("MemoryUsed", conversationId, memories);
                }
            }

            var allowImageGen = _azureOpenAISettings.EnableImageGeneration;
            var allowVideoGen = _azureOpenAISettings.EnableVideoGeneration;
            var generatedMediaThisTurn = false;
            var toolCalls = new List<MessageToolCall>();
            await foreach (var ev in _openAIService.StreamChatCompletionAsync(
                userId, messagesToSend, modelId, maxContextSize, maxMessages, allowImageGen, allowVideoGen))
            {
                switch (ev)
                {
                    case TextDelta td:
                        await Clients.Caller.SendAsync("ReceiveMessageChunk", conversationId, td.Text);
                        break;
                    case ToolCallStart tcs:
                        toolCalls.Add(new MessageToolCall
                        {
                            Id = tcs.ToolCallId,
                            Name = tcs.ToolName,
                            ArgumentsJson = tcs.ArgumentsJson,
                        });
                        await Clients.Caller.SendAsync("ToolCallStart", conversationId, tcs.ToolName, tcs.ToolCallId);
                        break;
                    case AttachmentReady ar:
                        generatedMediaThisTurn |= IsMediaAttachment(ar.Attachment);
                        await Clients.Caller.SendAsync("AttachmentReady", conversationId, ar.Attachment, ar.ToolCallId);
                        break;
                }
            }

            if (memories.Count > 0)
            {
                await _memory.MarkUsedAsync(userId, memories.Select(m => m.Id));
            }

            _logger.LogInformation("Stream complete for conversation {ConversationId}", conversationId);
            await Clients.Caller.SendAsync("StreamComplete", conversationId, toolCalls);

            // Stage the latest snapshot and (re)start the idle timer. A new turn within the
            // idle window cancels it; otherwise extraction runs once the conversation goes quiet.
            // messages is the client-sent list (without our injected system prompt).
            if (!suppressMemory && !generatedMediaThisTurn)
            {
                await _extractionScheduler.ScheduleAsync(userId, conversationId, messages);
            }
            else
            {
                await _extractionScheduler.CancelAsync(userId, conversationId, messages[^1].Id);
                _logger.LogInformation(
                    "Idle extraction skipped for conversation {ConversationId}: suppressMemory={SuppressMemory}, generatedMediaThisTurn={GeneratedMediaThisTurn}",
                    conversationId,
                        suppressMemory,
                    generatedMediaThisTurn);
            }
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

    private static string BuildSystemPrompt(string baseSystemPrompt, List<Memory> memories, string fallbackSystemPrompt)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(baseSystemPrompt)
            ? fallbackSystemPrompt
            : baseSystemPrompt.Trim());

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

    private static bool IsMediaAttachment(MessageAttachment attachment) =>
        string.Equals(attachment.Type, "image", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attachment.Type, "video", StringComparison.OrdinalIgnoreCase);
}
