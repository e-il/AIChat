namespace AIChat.Api.Models;

public class MemorySettings
{
    public int ExtractionThreshold { get; set; } = 10;
    public string? EmbeddingDeploymentName { get; set; }
}
