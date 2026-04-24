namespace AIChat.Api.Models;

public enum MemoryType
{
    Fact,
    Preference,
    Summary
}

public class Memory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public MemoryType Type { get; set; } = MemoryType.Fact;
    public string Content { get; set; } = "";
    public string? SourceConversationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    public int UseCount { get; set; }
}
