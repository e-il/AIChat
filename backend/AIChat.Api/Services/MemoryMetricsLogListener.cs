using System.Diagnostics.Metrics;

namespace AIChat.Api.Services;

public sealed class MemoryMetricsLogListener : IHostedService, IDisposable
{
    private readonly MemoryRetrievalMetrics _metrics;
    private readonly ILogger<MemoryMetricsLogListener> _logger;
    private MeterListener? _listener;

    public MemoryMetricsLogListener(
        MemoryRetrievalMetrics metrics,
        ILogger<MemoryMetricsLogListener> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MemoryRetrievalMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == MemoryRetrievalMetrics.RetrievalsName)
            {
                LogRetrievalScoring(tags);
            }
        });

        _listener.Start();
        _logger.LogDebug("Memory metrics log listener started for meter {MeterName}", MemoryRetrievalMetrics.MeterName);
        GC.KeepAlive(_metrics);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }

    private void LogRetrievalScoring(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var scoringMode = GetTagString(tags, "scoring_mode") ?? "unknown";
        var fallbackReason = GetTagString(tags, "fallback_reason") ?? "unknown";
        var dimensions = GetTagInt(tags, "dimensions");

        switch (scoringMode)
        {
            case MemoryRetrievalMetrics.ScoringModeEmbedding:
                _logger.LogDebug(
                    "Memory retrieval metric: embedding scoring active; dimensions={Dimensions}",
                    dimensions);
                break;

            case MemoryRetrievalMetrics.ScoringModeKeyword:
                _logger.LogWarning(
                    "Memory retrieval metric: keyword scoring fallback active; reason={FallbackReason}",
                    fallbackReason);
                break;

            case MemoryRetrievalMetrics.ScoringModePartialFallback:
                _logger.LogWarning(
                    "Memory retrieval metric: partial keyword fallback active; reason={FallbackReason}, dimensions={Dimensions}",
                    fallbackReason, dimensions);
                break;

            default:
                _logger.LogWarning(
                    "Memory retrieval metric: unknown scoring mode={ScoringMode}; reason={FallbackReason}, dimensions={Dimensions}",
                    scoringMode, fallbackReason, dimensions);
                break;
        }
    }

    private static string? GetTagString(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        for (var i = 0; i < tags.Length; i++)
        {
            if (tags[i].Key == key) return tags[i].Value?.ToString();
        }

        return null;
    }

    private static int GetTagInt(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        for (var i = 0; i < tags.Length; i++)
        {
            if (tags[i].Key != key) continue;
            return tags[i].Value switch
            {
                int value => value,
                long value => (int)value,
                short value => value,
                byte value => value,
                string value when int.TryParse(value, out var parsed) => parsed,
                _ => 0,
            };
        }

        return 0;
    }
}
