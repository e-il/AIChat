namespace AIChat.Api.Models;

public class ModelInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeploymentName { get; set; } = "";
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
    // Deployment name for the embedding model (e.g. "text-embedding-3-small").
    // When empty, memory retrieval falls back to keyword overlap scoring.
    public string? EmbeddingDeploymentName { get; set; }
}
