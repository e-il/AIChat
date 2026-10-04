namespace AIChat.Api.Models;

public class ExtractionCheckpoint
{
    public string LastExtractedMessageId { get; set; } = "";
    public DateTime LastExtractedAt { get; set; }
    public bool MemoryDisabled { get; set; }
    public string? SuppressedThroughMessageId { get; set; }
}
