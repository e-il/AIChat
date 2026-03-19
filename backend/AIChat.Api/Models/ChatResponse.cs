namespace AIChat.Api.Models;

public class ChatResponse
{
    public string ConversationId { get; set; } = "";
    public ChatMessage Message { get; set; } = new();
}
