namespace AIChat.Api.Models;

public class PromptProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string InputPlaceholder { get; set; } = "";
    public bool IsBuiltIn { get; set; } = true;
}

public class PromptProfilesResponse
{
    public List<PromptProfile> Profiles { get; set; } = new();
    public int MaxCustomSystemPromptLength { get; set; }
}

public class PromptProfileSettings
{
    public int MaxCustomSystemPromptLength { get; set; } = 8000;
    public List<PromptProfile> Profiles { get; set; } = new();
}

public class SendMessageRequest
{
    public string ConversationId { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();
    public string ModelId { get; set; } = "";
    public int MaxContextSize { get; set; } = 100000;
    public int MaxMessages { get; set; } = 50;
    public string MemoryMode { get; set; } = "auto";
    public List<string>? ExplicitMemoryIds { get; set; }
    public string PromptProfileId { get; set; } = "general";
    public string? CustomSystemPrompt { get; set; }
}
