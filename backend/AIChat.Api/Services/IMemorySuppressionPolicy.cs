using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IMemorySuppressionPolicy
{
    bool ShouldSuppress(string? promptProfileId, IReadOnlyList<ChatMessage> messages);
}

public class MemorySuppressionPolicy : IMemorySuppressionPolicy
{
    private const string GenerateImageToolName = "generate_image";
    private const string GenerateVideoToolName = "generate_video";

    public bool ShouldSuppress(string? promptProfileId, IReadOnlyList<ChatMessage> messages) =>
        IsMemorySuppressedProfile(promptProfileId) || IsMediaGenerationConversation(messages);

    private static bool IsMemorySuppressedProfile(string? promptProfileId) =>
        string.Equals(promptProfileId, "rewrite", StringComparison.OrdinalIgnoreCase)
        || (promptProfileId?.StartsWith("translate", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsMediaAttachment(MessageAttachment attachment) =>
        string.Equals(attachment.Type, "image", StringComparison.OrdinalIgnoreCase)
        || string.Equals(attachment.Type, "video", StringComparison.OrdinalIgnoreCase);

    private bool IsMediaGenerationConversation(IReadOnlyList<ChatMessage> messages) =>
        messages.Any(IsGeneratedMediaAssistantMessage) || LooksLikeMediaGenerationRequest(messages.LastOrDefault()?.Content);

    private bool IsGeneratedMediaAssistantMessage(ChatMessage message) =>
        string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
        && ((message.ToolCalls?.Any(IsMediaToolCall) ?? false)
            || (message.Attachments?.Any(IsMediaAttachment) ?? false));

    private static bool IsMediaToolCall(MessageToolCall toolCall) =>
        string.Equals(toolCall.Name, GenerateImageToolName, StringComparison.Ordinal)
        || string.Equals(toolCall.Name, GenerateVideoToolName, StringComparison.Ordinal);

    private static bool LooksLikeMediaGenerationRequest(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var text = content.ToLowerInvariant();
        return text.Contains("generate image")
            || text.Contains("create image")
            || text.Contains("draw")
            || text.Contains("picture")
            || text.Contains("photo")
            || text.Contains("illustration")
            || text.Contains("generate video")
            || text.Contains("create video")
            || text.Contains("video")
            || text.Contains("clip")
            || text.Contains("animation")
            || text.Contains("animate")
            || text.Contains("生成图片")
            || text.Contains("生成图")
            || text.Contains("画图")
            || text.Contains("图片")
            || text.Contains("照片")
            || text.Contains("插图")
            || text.Contains("生成视频")
            || text.Contains("视频")
            || text.Contains("短片")
            || text.Contains("动画");
    }
}
