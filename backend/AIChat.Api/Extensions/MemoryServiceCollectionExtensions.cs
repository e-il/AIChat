using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Extensions;

public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MemorySettings>(configuration.GetSection("Memory"));
        services.AddSingleton<MemoryRetrievalMetrics>();
        services.AddSingleton<IMemoryRetrievalMetrics>(sp => sp.GetRequiredService<MemoryRetrievalMetrics>());
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IExtractionCheckpointService, ExtractionCheckpointService>();
        // ExtractionQueue has two surfaces pointing at the same singleton: IExtractionQueue
        // (enqueue-only, injected into ChatHub) and the concrete type (full access to
        // internal Reader/Release, injected into ExtractionWorker).
        services.AddSingleton<ExtractionQueue>();
        services.AddSingleton<IExtractionQueue>(sp => sp.GetRequiredService<ExtractionQueue>());
        services.AddHostedService<MemoryMetricsLogListener>();
        services.AddHostedService<ExtractionWorker>();
        return services;
    }
}
