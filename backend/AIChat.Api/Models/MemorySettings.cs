namespace AIChat.Api.Models;

public class MemorySettings
{
    // Memories are extracted after a conversation has been idle (no new messages)
    // for this many seconds, rather than after a fixed number of turns. This captures
    // the full context of a finished exchange and avoids extracting mid-thought.
    public int IdleExtractionSeconds { get; set; } = 600;

    // An idle conversation is only sent for extraction when it has at least this many
    // new (unextracted) text messages. Prevents extracting from trivial one-line exchanges.
    public int MinMessagesToExtract { get; set; } = 2;

    public string? EmbeddingDeploymentName { get; set; }
}
