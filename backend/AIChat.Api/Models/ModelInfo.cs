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
    /// <summary>
    /// Default context size in characters for new conversations.
    /// </summary>
    public int DefaultContextSize { get; set; } = 100000;
    /// <summary>
    /// Available context size options for users to choose from.
    /// </summary>
    public List<int> ContextSizeOptions { get; set; } = new() { 20000, 50000, 100000, 200000 };
    /// <summary>
    /// Default max message count for new conversations.
    /// </summary>
    public int DefaultMaxMessages { get; set; } = 50;
    /// <summary>
    /// Available max message count options for users to choose from.
    /// </summary>
    public List<int> MaxMessagesOptions { get; set; } = new() { 10, 20, 50, 100, 200 };
}
