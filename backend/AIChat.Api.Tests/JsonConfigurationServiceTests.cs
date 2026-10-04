using System.Text.Json;
using AIChat.Api.Models;
using AIChat.Api.Services;
using AIChat.Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AIChat.Api.Tests;

public sealed class JsonConfigurationServiceTests : IDisposable
{
    private readonly string _originalDirectory = Directory.GetCurrentDirectory();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aichat-config-tests", Guid.NewGuid().ToString("N"));
    private readonly string? _originalKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    private readonly string? _originalEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
    private readonly string? _originalStandardKey = Environment.GetEnvironmentVariable("AzureOpenAI__ApiKey");
    private readonly string? _originalStandardEndpoint = Environment.GetEnvironmentVariable("AzureOpenAI__Endpoint");

    public JsonConfigurationServiceTests()
    {
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", "test-only-api-key");
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://test-only.invalid");
        Environment.SetEnvironmentVariable("AzureOpenAI__ApiKey", null);
        Environment.SetEnvironmentVariable("AzureOpenAI__Endpoint", null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelChangesPersistOnlyCatalogSettings(bool delete)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureOpenAI:ApiKey"] = "configuration-test-key",
            ["AzureOpenAI:DefaultModel"] = "original",
            ["AzureOpenAI:Models:0:Id"] = "original",
            ["AzureOpenAI:Models:0:Name"] = "Original",
            ["AzureOpenAI:Models:0:DeploymentName"] = "original-deployment",
            ["AzureOpenAI:DefaultContextSize"] = "25000",
            ["AzureOpenAI:ContextSizeOptions:0"] = "25000",
            ["AzureOpenAI:MaxMessagesOptions:0"] = "10",
            ["AzureOpenAI:EnableVideoGeneration"] = "false",
        }).Build();
        var path = Path.Combine(_directory, "config", "models.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { AzureOpenAI = configuration.GetSection("AzureOpenAI").Get<AzureOpenAISettings>() }));
        using var fileConfiguration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonFile(path).Build();
        var services = new ServiceCollection();
        services.AddAzureOpenAI(fileConfiguration);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IOptionsMonitor<AzureOpenAISettings>>();
        Assert.Single(runtime.CurrentValue.Models);
        var service = new JsonConfigurationService(fileConfiguration, new TestEnvironment(_directory));

        if (delete) Assert.True(await service.DeleteModelAsync("original"));
        else await service.AddOrUpdateModelAsync(new ModelInfo { Id = "new", Name = "New", DeploymentName = "new-deployment" });

        var saved = await File.ReadAllTextAsync(Path.Combine(_directory, "config", "models.json"));
        Assert.DoesNotContain("test-only-api-key", saved);
        Assert.DoesNotContain("configuration-test-key", saved);
        Assert.DoesNotContain("test-only.invalid", saved);
        using var document = JsonDocument.Parse(saved);
        var catalog = document.RootElement.GetProperty("azureOpenAI");
        Assert.Equal(new[]
        {
            "contextSizeOptions", "defaultContextSize", "defaultMaxMessages", "defaultModel",
            "enableImageGeneration", "enableVideoGeneration", "imageGenerationModelId",
            "maxMessagesOptions", "models", "videoGenerationModelId",
        }, catalog.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(delete ? 0 : 2, catalog.GetProperty("models").GetArrayLength());
        Assert.Equal(25000, catalog.GetProperty("defaultContextSize").GetInt32());
        Assert.False(catalog.GetProperty("enableVideoGeneration").GetBoolean());
        Assert.Equal("test-only-api-key", runtime.CurrentValue.ApiKey);
        Assert.Equal("https://test-only.invalid", runtime.CurrentValue.Endpoint);
        Assert.Equal(delete ? 0 : 2, runtime.CurrentValue.Models.Count);
        Assert.False(File.Exists(Path.Combine(_directory, "config", "models.json.tmp")));
    }

    [Fact]
    public async Task CancelledEditLeavesFileUnchanged()
    {
        var path = Path.Combine(_directory, "config", "models.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original = "{\"AzureOpenAI\":{\"Models\":[]}}";
        await File.WriteAllTextAsync(path, original);
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonFile(path).Build();
        var service = new JsonConfigurationService(configuration, new TestEnvironment(_directory));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AddOrUpdateModelAsync(new ModelInfo { Id = "new", Name = "New", DeploymentName = "new" }, cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void CredentialsNeverFallBackToJson()
    {
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", null);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", null);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureOpenAI:ApiKey"] = "file-key-must-not-be-used",
            ["AzureOpenAI:Endpoint"] = "https://file-endpoint.invalid",
        }).Build();
        var services = new ServiceCollection();
        services.AddAzureOpenAI(configuration);
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<AzureOpenAISettings>>().Value;
        Assert.Empty(settings.ApiKey);
        Assert.Empty(settings.Endpoint);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", _originalKey);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", _originalEndpoint);
        Environment.SetEnvironmentVariable("AzureOpenAI__ApiKey", _originalStandardKey);
        Environment.SetEnvironmentVariable("AzureOpenAI__Endpoint", _originalStandardEndpoint);
        Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class TestEnvironment(string root) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "AIChat.Api.Tests";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}