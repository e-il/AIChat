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
    /// <summary>
    /// Streams a chat completion as a series of typed events (text deltas, tool calls,
    /// attachments). Handles two-pass tool calling internally: when the model invokes
    /// generate_image, the service executes the tool and feeds the result back for a
    /// natural-language wrap-up — all surfaced as additional TextDelta events.
    /// </summary>
    IAsyncEnumerable<StreamEvent> StreamChatCompletionAsync(
        string userId,
        List<ChatMessage> messages,
        string modelId,
        int maxContextSize,
        int maxMessages,
        bool allowImageGeneration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts durable memories (facts/preferences/summaries) from a conversation transcript.
    /// Returns an empty list if nothing is worth remembering — empty is a valid, successful result.
    /// </summary>
    Task<List<ExtractedMemory>> ExtractMemoriesAsync(
        List<ChatMessage> messages,
        List<Memory> existingMemories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an embedding vector for the given text. Returns null if embeddings
    /// aren't configured or the call fails (best effort; callers should fall back).
    /// </summary>
    Task<float[]?> TryGenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an image for the given prompt using the configured image deployment,
    /// persists the bytes via IImageStorageService for the given user, and returns the
    /// resulting attachment (with a server-relative URL). Throws if image generation
    /// is disabled or unconfigured.
    /// </summary>
    Task<MessageAttachment> GenerateImageAsync(
        string userId,
        string prompt,
        string? size = null,
        CancellationToken cancellationToken = default);

    /// <summary>True if a usable image-generation deployment is configured and enabled.</summary>
    bool IsImageGenerationAvailable { get; }
}
