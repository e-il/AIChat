using System.Diagnostics.Metrics;

namespace AIChat.Api.Services;

public interface IMemoryRetrievalMetrics
{
    void RecordRetrievalScoring(
        string scoringMode,
        string fallbackReason,
        int memoryCount,
        int embeddingScored,
        int missingEmbedding,
        int dimensionMismatch,
        int dimensions = 0);
}

public sealed class MemoryRetrievalMetrics : IMemoryRetrievalMetrics, IDisposable
{
    public const string MeterName = "AIChat.Memory";
    public const string RetrievalsName = "aichat.memory.retrievals";
    public const string MemoryCountName = "aichat.memory.retrieval.memory_count";
    public const string EmbeddingScoredCountName = "aichat.memory.retrieval.embedding_scored_count";
    public const string MissingEmbeddingCountName = "aichat.memory.retrieval.missing_embedding_count";
    public const string DimensionMismatchCountName = "aichat.memory.retrieval.dimension_mismatch_count";

    public const string ScoringModeEmbedding = "embedding";
    public const string ScoringModeKeyword = "keyword";
    public const string ScoringModePartialFallback = "partial_fallback";

    public const string FallbackReasonNone = "none";
    public const string FallbackReasonQueryEmbeddingMissing = "query_embedding_missing";
    public const string FallbackReasonMemoryEmbeddingMissing = "memory_embedding_missing";
    public const string FallbackReasonDimensionMismatch = "dimension_mismatch";
    public const string FallbackReasonMixed = "mixed";

    private readonly Meter _meter;
    private readonly Counter<long> _retrievals;
    private readonly Histogram<int> _memoryCount;
    private readonly Histogram<int> _embeddingScoredCount;
    private readonly Histogram<int> _missingEmbeddingCount;
    private readonly Histogram<int> _dimensionMismatchCount;

    public MemoryRetrievalMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName, "1.0.0");
        _retrievals = _meter.CreateCounter<long>(
            RetrievalsName,
            unit: "retrieval",
            description: "Memory retrieval requests by scoring mode.");
        _memoryCount = _meter.CreateHistogram<int>(
            MemoryCountName,
            unit: "memory",
            description: "Number of scorable memories considered during retrieval.");
        _embeddingScoredCount = _meter.CreateHistogram<int>(
            EmbeddingScoredCountName,
            unit: "memory",
            description: "Number of memories scored with embedding similarity during retrieval.");
        _missingEmbeddingCount = _meter.CreateHistogram<int>(
            MissingEmbeddingCountName,
            unit: "memory",
            description: "Number of memories that could not use embedding scoring because they have no embedding.");
        _dimensionMismatchCount = _meter.CreateHistogram<int>(
            DimensionMismatchCountName,
            unit: "memory",
            description: "Number of memories that could not use embedding scoring because embedding dimensions did not match.");
    }

    public void RecordRetrievalScoring(
        string scoringMode,
        string fallbackReason,
        int memoryCount,
        int embeddingScored,
        int missingEmbedding,
        int dimensionMismatch,
        int dimensions = 0)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("scoring_mode", scoringMode),
            new("fallback_reason", fallbackReason),
            new("dimensions", dimensions),
        };

        _retrievals.Add(
            1,
            tags);
        _memoryCount.Record(memoryCount, tags);
        _embeddingScoredCount.Record(embeddingScored, tags);
        _missingEmbeddingCount.Record(missingEmbedding, tags);
        _dimensionMismatchCount.Record(dimensionMismatch, tags);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
