using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Extensions;

public static class PromptProfileServiceCollectionExtensions
{
    public static IServiceCollection AddPromptProfiles(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PromptProfileSettings>(configuration.GetSection("PromptProfiles"));
        services.AddSingleton<IPromptProfileRegistry, PromptProfileRegistry>();
        return services;
    }
}
