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
}
