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

        // Per-user JSON stores. Each owns a subdirectory under data/ and its own per-user locks.
        services.AddUserJsonStore<List<Memory>>("memory");
        services.AddUserJsonStore<Dictionary<string, ExtractionCheckpoint>>("extraction");
        services.AddUserJsonStore<Dictionary<string, PendingExtraction>>("pending");

        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IMemorySuppressionPolicy, MemorySuppressionPolicy>();
        services.AddSingleton<IExtractionCheckpointService, ExtractionCheckpointService>();
        // ExtractionQueue has two surfaces pointing at the same singleton: IExtractionQueue
        // (enqueue-only, injected into ChatHub) and the concrete type (full access to
        // internal Reader/Release, injected into ExtractionWorker).
        services.AddSingleton<ExtractionQueue>();
        services.AddSingleton<IExtractionQueue>(sp => sp.GetRequiredService<ExtractionQueue>());
        // Idle-based extraction: ChatHub stages the latest snapshot via the scheduler, which
        // debounces a per-conversation timer and flushes to the queue once the chat goes quiet.
        // Snapshots are persisted by the store and reloaded on startup. The scheduler's lifecycle
        // (reload on start, cancel on stop) is driven by ExtractionWorker, so it isn't a hosted service.
        services.AddSingleton<PendingExtractionStore>();
        services.AddSingleton<IdleExtractionScheduler>();
        services.AddHostedService<MemoryMetricsLogListener>();
        services.AddHostedService<ExtractionWorker>();
        return services;
    }

    private static IServiceCollection AddUserJsonStore<T>(this IServiceCollection services, string subdirectory)
        where T : new() =>
        services.AddSingleton(sp => new UserJsonStore<T>(
            subdirectory, sp.GetRequiredService<ILogger<UserJsonStore<T>>>()));
}
