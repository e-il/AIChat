using AIChat.Api.Models;

namespace AIChat.Api.Services;

/// <summary>
/// Typed events yielded by IAzureOpenAIService.StreamChatCompletionAsync.
/// ChatHub maps each variant to a SignalR client event.
/// </summary>
public abstract record StreamEvent;

/// <summary>Incremental text from the chat model. Concatenated across both passes.</summary>
public sealed record TextDelta(string Text) : StreamEvent;

/// <summary>The model invoked a tool. Frontend uses this to show "Generating image…".</summary>
public sealed record ToolCallStart(string ToolName, string ToolCallId) : StreamEvent;

/// <summary>An attachment is ready to display (e.g. a generated image saved to storage).</summary>
public sealed record AttachmentReady(MessageAttachment Attachment, string ToolCallId) : StreamEvent;
