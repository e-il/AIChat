using AIChat.Api.Models;
using Microsoft.Extensions.Options;

namespace AIChat.Api.Services;

public interface IPromptProfileRegistry
{
    int MaxCustomSystemPromptLength { get; }
    string GeneralSystemPrompt { get; }
    IReadOnlyList<PromptProfile> GetBuiltIns();
    bool TryResolveSystemPrompt(
        string? profileId,
        string? customSystemPrompt,
        out string systemPrompt,
        out bool isDefaultGeneral,
        out string? error);
}

public class PromptProfileRegistry : IPromptProfileRegistry
{
    public const string GeneralId = "general";

    private const int DefaultMaxCustomSystemPromptLength = 8000;
    private const string DefaultGeneralSystemPrompt =
        "You are a helpful AI assistant. Be concise and helpful in your responses.";

    private readonly IOptionsMonitor<PromptProfileSettings> _settings;

    public PromptProfileRegistry(IOptionsMonitor<PromptProfileSettings> settings)
    {
        _settings = settings;
    }

    public int MaxCustomSystemPromptLength =>
        _settings.CurrentValue.MaxCustomSystemPromptLength > 0
            ? _settings.CurrentValue.MaxCustomSystemPromptLength
            : DefaultMaxCustomSystemPromptLength;

    public string GeneralSystemPrompt =>
        GetProfiles()
            .FirstOrDefault(p => string.Equals(p.Id, GeneralId, StringComparison.OrdinalIgnoreCase))
            ?.SystemPrompt
            ?.Trim()
        ?? DefaultGeneralSystemPrompt;

    public IReadOnlyList<PromptProfile> GetBuiltIns() => GetProfiles();

    public bool TryResolveSystemPrompt(
        string? profileId,
        string? customSystemPrompt,
        out string systemPrompt,
        out bool isDefaultGeneral,
        out string? error)
    {
        var builtInProfilesById = GetProfiles().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profileId)
            && builtInProfilesById.TryGetValue(profileId, out var builtInProfile))
        {
            systemPrompt = builtInProfile.SystemPrompt.Trim();
            isDefaultGeneral = string.Equals(builtInProfile.Id, GeneralId, StringComparison.OrdinalIgnoreCase);
            error = null;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(customSystemPrompt))
        {
            var trimmed = customSystemPrompt.Trim();
            if (trimmed.Length > MaxCustomSystemPromptLength)
            {
                systemPrompt = GeneralSystemPrompt;
                isDefaultGeneral = true;
                error = $"Custom system prompt cannot exceed {MaxCustomSystemPromptLength} characters";
                return false;
            }

            systemPrompt = trimmed;
            isDefaultGeneral = false;
            error = null;
            return true;
        }

        systemPrompt = GeneralSystemPrompt;
        isDefaultGeneral = true;
        error = null;
        return true;
    }

    private List<PromptProfile> GetProfiles()
    {
        return _settings.CurrentValue.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.SystemPrompt))
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => NormalizeBuiltIn(g.First()))
            .ToList();
    }

    private static PromptProfile NormalizeBuiltIn(PromptProfile profile)
    {
        return new PromptProfile
        {
            Id = profile.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id.Trim() : profile.Name.Trim(),
            Description = profile.Description?.Trim() ?? "",
            SystemPrompt = profile.SystemPrompt.Trim(),
            InputPlaceholder = string.IsNullOrWhiteSpace(profile.InputPlaceholder)
                ? "Message AIChat..."
                : profile.InputPlaceholder.Trim(),
            IsBuiltIn = true,
        };
    }
}
