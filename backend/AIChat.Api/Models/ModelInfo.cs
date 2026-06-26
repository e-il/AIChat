namespace AIChat.Api.Models;

public class ModelInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeploymentName { get; set; } = "";
    // "chat" (default) | "image" | "video". Media models are not selectable from the chat dropdown;
    // they are invoked by the chat model via generation tools.
    public string Kind { get; set; } = "chat";
}

public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<ModelInfo> Models { get; set; } = new();
    public string DefaultModel { get; set; } = "gpt-4o";
    public int DefaultContextSize { get; set; } = 100000;
    // Empty defaults: .NET config binds List<T> via Add(), so pre-populated defaults would be appended to, not replaced. Values come from models.json.
    public List<int> ContextSizeOptions { get; set; } = new();
    public int DefaultMaxMessages { get; set; } = 50;
    public List<int> MaxMessagesOptions { get; set; } = new();
    // Id (matches Models[].Id) of the image-generation deployment. Empty disables image generation.
    public string? ImageGenerationModelId { get; set; }
    // Master switch. When false, the generate_image tool is not exposed even if a model is configured.
    public bool EnableImageGeneration { get; set; } = true;
    // Id (matches Models[].Id) of the video-generation deployment. Empty disables video generation.
    public string? VideoGenerationModelId { get; set; }
    // Master switch. When false, the generate_video tool is not exposed even if a model is configured.
    public bool EnableVideoGeneration { get; set; } = true;
}
