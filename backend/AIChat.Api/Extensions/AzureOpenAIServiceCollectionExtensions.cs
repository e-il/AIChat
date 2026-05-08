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

        // Used by AzureOpenAIService to fetch image URLs as a defensive fallback when
        // a deployment ignores ResponseFormat=Bytes.
        services.AddHttpClient();
        services.AddSingleton<IImageStorageService, ImageStorageService>();
        services.AddSingleton<IAzureOpenAIService, AzureOpenAIService>();
        return services;
    }
}
