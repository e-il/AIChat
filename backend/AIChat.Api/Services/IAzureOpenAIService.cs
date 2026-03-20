using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IAzureOpenAIService
{
    List<ModelInfo> GetAvailableModels();
    string GetDefaultModel();
    int GetDefaultContextSize();
    List<int> GetContextSizeOptions();
    int GetDefaultMaxMessages();
    List<int> GetMaxMessagesOptions();
    IAsyncEnumerable<string> StreamChatCompletionAsync(List<ChatMessage> messages, string modelId, int maxContextSize, int maxMessages, CancellationToken cancellationToken = default);
}
