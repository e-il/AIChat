using System.ClientModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting stream for model {ModelId} with {MessageCount} messages", modelId, messages.Count);
        
        var chatClient = GetChatClient(modelId);
        
        var chatMessages = messages.Select(m => m.Role switch
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
}
