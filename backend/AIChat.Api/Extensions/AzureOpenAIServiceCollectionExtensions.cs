using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Extensions;

public static class AzureOpenAIServiceCollectionExtensions
{
    public static IServiceCollection AddAzureOpenAI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureOpenAISettings>(configuration.GetSection("AzureOpenAI"));
        services.PostConfigure<AzureOpenAISettings>(settings =>
        {
            var envEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var envApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

            if (!string.IsNullOrEmpty(envEndpoint)) settings.Endpoint = envEndpoint;
            if (!string.IsNullOrEmpty(envApiKey)) settings.ApiKey = envApiKey;
        });

        // Used by AzureOpenAIService for image URL fallback fetches and long-running
        // image edit REST calls.
        services.AddHttpClient();
        services.AddHttpClient("azure-openai-image-fetch", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddSingleton<IMediaStorageService, MediaStorageService>();
        services.AddSingleton<IVideoGenerationService, AzureOpenAIVideoGenerationService>();
        services.AddSingleton<IAzureOpenAIService, AzureOpenAIService>();
        return services;
    }
}
