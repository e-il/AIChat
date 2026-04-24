using System.ClientModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using AIChat.Api.Models;
using AppChatMessage = AIChat.Api.Models.ChatMessage;

namespace AIChat.Api.Services;

public class AzureOpenAIService : IAzureOpenAIService
{
    private readonly AzureOpenAIClient _client;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<AzureOpenAIService> _logger;

    public AzureOpenAIService(IConfiguration configuration, ILogger<AzureOpenAIService> logger)
    {
        _logger = logger;
        _settings = configuration.GetSection("AzureOpenAI").Get<AzureOpenAISettings>()
            ?? throw new InvalidOperationException("AzureOpenAI settings are not configured");

        // Override with environment variables if set
        var envEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var envApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        if (!string.IsNullOrEmpty(envEndpoint)) _settings.Endpoint = envEndpoint;
        if (!string.IsNullOrEmpty(envApiKey)) _settings.ApiKey = envApiKey;

        if (string.IsNullOrEmpty(_settings.Endpoint) || string.IsNullOrEmpty(_settings.ApiKey))
        {
            throw new InvalidOperationException("AzureOpenAI:Endpoint and ApiKey must be configured (via appsettings.json or environment variables)");
        }

        _client = new AzureOpenAIClient(new Uri(_settings.Endpoint), new ApiKeyCredential(_settings.ApiKey));
    }

    public List<ModelInfo> GetAvailableModels() => _settings.Models;

    public string GetDefaultModel() => _settings.DefaultModel;

    public int GetDefaultContextSize() => _settings.DefaultContextSize;

    public List<int> GetContextSizeOptions() => _settings.ContextSizeOptions;

    public int GetDefaultMaxMessages() => _settings.DefaultMaxMessages;

    public List<int> GetMaxMessagesOptions() => _settings.MaxMessagesOptions;

    private ChatClient GetChatClient(string modelId)
    {
        return _chatClients.GetOrAdd(modelId, id =>
        {
            var model = _settings.Models.FirstOrDefault(m => m.Id == id)
                ?? _settings.Models.FirstOrDefault(m => m.Id == _settings.DefaultModel)
                ?? _settings.Models.First();

            return _client.GetChatClient(model.DeploymentName);
        });
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        List<AppChatMessage> messages,
        string modelId,
        int maxContextSize,
        int maxMessages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Truncate context by message count first, then by size
        var truncatedMessages = TruncateContext(messages, maxContextSize, maxMessages);
        _logger.LogInformation("Starting stream for model {ModelId} with {MessageCount} messages (truncated from {OriginalCount})",
            modelId, truncatedMessages.Count, messages.Count);

        var chatClient = GetChatClient(modelId);

        var chatMessages = truncatedMessages.Select(m => m.Role switch
        {
            "system" => new SystemChatMessage(m.Content) as OpenAI.Chat.ChatMessage,
            "assistant" => new AssistantChatMessage(m.Content),
            _ => new UserChatMessage(m.Content)
        }).ToList();

        // Add system message if not present
        if (!chatMessages.Any(m => m is SystemChatMessage))
        {
            chatMessages.Insert(0, new SystemChatMessage("You are a helpful AI assistant. Be concise and helpful in your responses."));
        }

        _logger.LogInformation("Calling Azure OpenAI with {Count} messages", chatMessages.Count);

        AsyncCollectionResult<StreamingChatCompletionUpdate> updates;

        try
        {
            updates = chatClient.CompleteChatStreamingAsync(chatMessages, cancellationToken: cancellationToken);
            _logger.LogInformation("Stream initiated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting chat completion stream with model {ModelId}", modelId);
            throw;
        }

        var chunkCount = 0;
        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    chunkCount++;
                    yield return part.Text;
                }
            }
        }
        _logger.LogInformation("Stream completed with {ChunkCount} chunks", chunkCount);
    }

    public async Task<List<ExtractedMemory>> ExtractMemoriesAsync(
        List<AppChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var chatClient = GetChatClient(_settings.DefaultModel);
        var promptMessages = BuildExtractionPrompt(messages);

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
        };

        ClientResult<ChatCompletion> result;
        try
        {
            result = await chatClient.CompleteChatAsync(promptMessages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction LLM call failed");
            throw;
        }

        var text = result.Value.Content.FirstOrDefault()?.Text ?? "{}";
        _logger.LogDebug("Extraction raw response: {Text}", text);

        try
        {
            var parsed = JsonSerializer.Deserialize<ExtractionResponse>(text, ExtractionJsonOptions);
            return parsed?.Memories ?? new List<ExtractedMemory>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response as JSON, returning empty. Raw: {Text}", text);
            return new List<ExtractedMemory>();
        }
    }

    private static readonly JsonSerializerOptions ExtractionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private class ExtractionResponse
    {
        public List<ExtractedMemory> Memories { get; set; } = new();
    }

    private static List<OpenAI.Chat.ChatMessage> BuildExtractionPrompt(List<AppChatMessage> messages)
    {
        var transcript = new StringBuilder();
        foreach (var msg in messages)
        {
            var speaker = msg.Role switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                "system" => "System",
                _ => msg.Role,
            };
            transcript.Append(speaker).Append(": ");
            transcript.AppendLine(msg.Content);
            transcript.AppendLine();
        }

        var system = """
You analyze chat transcripts and extract durable user information worth remembering across future conversations.

Output a JSON object with this exact shape:
{
  "memories": [
    { "type": "fact" | "preference" | "summary", "content": "<= 300 chars" }
  ]
}

Extract ONLY:
- facts: user's name, role, situation, skills, enduring context
- preferences: coding style, response style, tools they favor
- summaries: takeaways from a long conversation worth keeping (rare)

Do NOT extract:
- Transient state (the current question, what they are doing right now)
- PII the user did not explicitly share (emails/phones/addresses)
- Passwords, API keys, tokens, sensitive credentials
- Temporary context (weather, file paths, session-specific details)

If nothing is worth remembering, return {"memories": []}. Empty is a valid, correct answer.
Return ONLY the JSON object, no commentary or code fences.
""";

        return new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(system),
            new UserChatMessage("Transcript:\n\n" + transcript.ToString()),
        };
    }

    /// <summary>
    /// Truncates conversation context by message count and size.
    /// Keeps the most recent messages, removing older ones first.
    /// </summary>
    private List<AppChatMessage> TruncateContext(List<AppChatMessage> messages, int maxContextSize, int maxMessages)
    {
        // First, limit by message count
        var limitedMessages = messages.Count > maxMessages
            ? messages.Skip(messages.Count - maxMessages).ToList()
            : messages;

        var totalSize = limitedMessages.Sum(m => m.Content?.Length ?? 0);

        if (totalSize <= maxContextSize)
        {
            if (limitedMessages.Count < messages.Count)
            {
                _logger.LogInformation("Context truncated by message count: {OriginalCount} -> {NewCount}",
                    messages.Count, limitedMessages.Count);
            }
            return limitedMessages;
        }

        // Then, limit by size
        var result = new List<AppChatMessage>();
        var currentSize = 0;

        for (var i = limitedMessages.Count - 1; i >= 0; i--)
        {
            var msgSize = limitedMessages[i].Content?.Length ?? 0;
            if (currentSize + msgSize > maxContextSize)
                break;

            result.Insert(0, limitedMessages[i]);
            currentSize += msgSize;
        }

        _logger.LogInformation("Context truncated: {OriginalCount} messages -> {NewCount} messages, {OriginalSize} -> {NewSize} chars",
            messages.Count, result.Count, totalSize, currentSize);

        return result;
    }
}
