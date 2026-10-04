using System.Text.Json;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public interface IJsonConfigurationService
{
    Task<List<ModelInfo>> AddOrUpdateModelAsync(ModelInfo model, CancellationToken cancellationToken = default);
    Task<bool> DeleteModelAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists edits to the model catalog; runtime configuration is owned by .NET options.
/// </summary>
public sealed class JsonConfigurationService : IJsonConfigurationService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _modelsPath;
    private readonly IConfiguration _configuration;

    public JsonConfigurationService(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _modelsPath = Path.Combine(environment.ContentRootPath, "config", "models.json");
    }

    public async Task<List<ModelInfo>> AddOrUpdateModelAsync(ModelInfo model, CancellationToken cancellationToken = default)
    {
        Validate(model);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadAsync(cancellationToken);
            var models = file.AzureOpenAI.Models;
            var index = models.FindIndex(item => string.Equals(item.Id, model.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) models[index] = model;
            else models.Add(model);
            await SaveAsync(file, cancellationToken);
            return models;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteModelAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadAsync(cancellationToken);
            var settings = file.AzureOpenAI;
            if (settings.Models.RemoveAll(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) == 0) return false;
            if (string.Equals(settings.DefaultModel, id, StringComparison.OrdinalIgnoreCase))
            {
                settings.DefaultModel = settings.Models.FirstOrDefault(model => string.Equals(model.Kind, "chat", StringComparison.OrdinalIgnoreCase))?.Id ?? "";
            }
            await SaveAsync(file, cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<ModelCatalogFile> ReadAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_modelsPath);
        var file = await JsonSerializer.DeserializeAsync<ModelCatalogFile>(stream, JsonOptions, cancellationToken);
        return file?.AzureOpenAI is not null ? file : throw new InvalidDataException("Model catalog is empty");
    }

    private async Task SaveAsync(ModelCatalogFile file, CancellationToken cancellationToken)
    {
        var tempPath = _modelsPath + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, _modelsPath, overwrite: true);
            if (_configuration is IConfigurationRoot root) root.Reload();
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    private static void Validate(ModelInfo model)
    {
        if (string.IsNullOrWhiteSpace(model.Id)
            || string.IsNullOrWhiteSpace(model.Name)
            || string.IsNullOrWhiteSpace(model.DeploymentName))
        {
            throw new ArgumentException("id, name and deploymentName are required");
        }

        model.Id = model.Id.Trim();
        model.Name = model.Name.Trim();
        model.DeploymentName = model.DeploymentName.Trim();
        model.Kind = string.IsNullOrWhiteSpace(model.Kind) ? "chat" : model.Kind.Trim().ToLowerInvariant();
    }

    private sealed record ModelCatalogFile(ModelCatalogSettings AzureOpenAI);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
