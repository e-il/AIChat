using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IAzureOpenAIService
{
    List<ModelInfo> GetAvailableModels();
    string GetDefaultModel();
    IAsyncEnumerable<string> StreamChatCompletionAsync(List<ChatMessage> messages, string modelId, CancellationToken cancellationToken = default);
}
