namespace AIChat.Api.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "user"; // "user" | "assistant" | "system" | "tool"
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<MessageAttachment>? Attachments { get; set; }
    // For assistant messages that triggered tools (e.g. image generation).
    // Persisted so the next turn's history reproduces the tool_call/tool_result pair.
    public List<MessageToolCall>? ToolCalls { get; set; }
    // Set on role="tool" messages to bind back to the assistant's tool_call.
    public string? ToolCallId { get; set; }
}

public class MessageAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = "image"; // "image" | "video" (future: "file", "audio")
    public string MimeType { get; set; } = "image/png";
    public string Url { get; set; } = ""; // server-relative, e.g. /api/images/{file} or /api/videos/{file}
    public string? Prompt { get; set; }
    public string? RevisedPrompt { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }
}

public class MessageToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ArgumentsJson { get; set; } = "";
}
