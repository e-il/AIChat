using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Extensions;

public static class AzureOpenAIServiceCollectionExtensions
{
    public static IServiceCollection AddAzureOpenAI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureOpenAISettings>().Bind(configuration.GetSection("AzureOpenAI"))
            .PostConfigure(settings =>
            {
                settings.Endpoint = Environment.GetEnvironmentVariable("AzureOpenAI__Endpoint")
                    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";
                settings.ApiKey = Environment.GetEnvironmentVariable("AzureOpenAI__ApiKey")
                    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? "";
            });
        // Used by AzureOpenAIService for image URL fallback fetches and long-running
        // image edit REST calls.
        services.AddHttpClient();
        services.AddHttpClient("azure-openai-image-fetch", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddSingleton<IMediaStorageService, MediaStorageService>();
        services.AddSingleton<IJsonConfigurationService, JsonConfigurationService>();
        services.AddSingleton<IVideoGenerationService, AzureOpenAIVideoGenerationService>();
        services.AddSingleton<IAzureOpenAIService, AzureOpenAIService>();
        return services;
    }
}
