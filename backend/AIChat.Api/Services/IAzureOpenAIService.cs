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

    /// <summary>
    /// Extracts durable memories (facts/preferences/summaries) from a conversation transcript.
    /// Returns an empty list if nothing is worth remembering — empty is a valid, successful result.
    /// </summary>
    Task<List<ExtractedMemory>> ExtractMemoriesAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);
}
