using System.Text.RegularExpressions;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class MemoryService : IMemoryService
{
    // Total character cap on a retrieved memory block. ~500 tokens at 4 chars/token.
    private const int MaxRetrievedChars = 2000;

    private readonly UserJsonStore<List<Memory>> _store;
    private readonly IAzureOpenAIService _openAI;
    private readonly IMemoryRetrievalMetrics _metrics;

    public MemoryService(
        UserJsonStore<List<Memory>> store,
        IAzureOpenAIService openAI,
        IMemoryRetrievalMetrics metrics)
    {
        _store = store;
        _openAI = openAI;
        _metrics = metrics;
    }

    public async Task<List<Memory>> GetAllAsync(string userId)
    {
        var memories = await _store.ReadAsync(userId);
        return memories.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public async Task<Memory?> GetAsync(string userId, string id)
    {
        var memories = await _store.ReadAsync(userId);
        return memories.FirstOrDefault(m => m.Id == id);
    }

    public async Task<List<Memory>> GetByIdsAsync(string userId, IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet();
        if (idSet.Count == 0) return new List<Memory>();

        var memories = await _store.ReadAsync(userId);
        return memories.Where(m => idSet.Contains(m.Id)).ToList();
    }

    public async Task<Memory> CreateAsync(string userId, MemoryType type, string content, string? sourceConversationId)
    {
        // Generate embedding outside the per-user lock to avoid holding it during an LLM call.
        var embedding = await _openAI.TryGenerateEmbeddingAsync(content);

        return await _store.MutateAsync(userId, memories =>
        {
            var memory = new Memory
            {
                UserId = userId,
                Type = type,
                Content = content,
                SourceConversationId = sourceConversationId,
                Embedding = embedding,
            };
            memories.Add(memory);
            return memory;
        });
    }

    public async Task<Memory?> UpdateAsync(string userId, string id, MemoryType? type, string? content)
    {
        // Regenerate embedding when content changes (done outside the lock).
        float[]? newEmbedding = null;
        var contentChanged = content is not null;
        if (contentChanged)
        {
            newEmbedding = await _openAI.TryGenerateEmbeddingAsync(content!);
        }

        return await _store.MutateAsync(userId, memories =>
        {
            var memory = memories.FirstOrDefault(m => m.Id == id);
            if (memory is null) return (Memory?)null;

            if (type.HasValue) memory.Type = type.Value;
            if (contentChanged)
            {
                memory.Content = content!;
                memory.Embedding = newEmbedding;
            }

            return memory;
        });
    }

    public Task<bool> DeleteAsync(string userId, string id)
    {
        return _store.MutateAsync(userId, memories => memories.RemoveAll(m => m.Id == id) > 0);
    }

    public async Task<List<Memory>> RetrieveAsync(string userId, string query, int limit = 5)
    {
        var all = await _store.ReadAsync(userId);
        if (all.Count == 0) return new List<Memory>();

        // Prefer embedding-based similarity; fall back to keyword overlap when either
        // the query or the memory has no embedding.
        var queryEmbedding = await _openAI.TryGenerateEmbeddingAsync(query);
        var queryTokens = Tokenize(query);

        var preferences = all.Where(m => m.Type == MemoryType.Preference).ToList();
        var scorableMemories = all.Where(m => m.Type != MemoryType.Preference).ToList();
        EmitEmbeddingScoringMetric(queryEmbedding, scorableMemories);

        var scorable = scorableMemories
            .Select(m => new { Memory = m, Score = ScoreMemory(m, queryEmbedding, queryTokens) })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Memory)
            .ToList();

        var candidates = preferences.Concat(scorable);

        var total = 0;
        var result = new List<Memory>();
        foreach (var memory in candidates)
        {
            if (total + memory.Content.Length > MaxRetrievedChars) continue;
            result.Add(memory);
            total += memory.Content.Length;
        }

        return result;
    }

    public Task MarkUsedAsync(string userId, IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet();
        if (idSet.Count == 0) return Task.CompletedTask;

        return _store.MutateAsync(userId, memories =>
        {
            var now = DateTime.UtcNow;
            foreach (var memory in memories)
            {
                if (!idSet.Contains(memory.Id)) continue;
                memory.UseCount++;
                memory.LastUsedAt = now;
            }
        });
    }

    // ---------- scoring ----------

    private void EmitEmbeddingScoringMetric(float[]? queryEmbedding, List<Memory> scorableMemories)
    {
        if (scorableMemories.Count == 0) return;

        if (queryEmbedding is null)
        {
            _metrics.RecordRetrievalScoring(
                MemoryRetrievalMetrics.ScoringModeKeyword,
                MemoryRetrievalMetrics.FallbackReasonQueryEmbeddingMissing,
                scorableMemories.Count,
                embeddingScored: 0,
                missingEmbedding: scorableMemories.Count(m => m.Embedding is null),
                dimensionMismatch: 0,
                dimensions: 0);
            return;
        }

        var usable = 0;
        var missing = 0;
        var dimensionMismatch = 0;

        foreach (var memory in scorableMemories)
        {
            if (memory.Embedding is null)
            {
                missing++;
            }
            else if (memory.Embedding.Length != queryEmbedding.Length)
            {
                dimensionMismatch++;
            }
            else
            {
                usable++;
            }
        }

        if (missing == 0 && dimensionMismatch == 0)
        {
            _metrics.RecordRetrievalScoring(
                MemoryRetrievalMetrics.ScoringModeEmbedding,
                MemoryRetrievalMetrics.FallbackReasonNone,
                scorableMemories.Count,
                usable,
                missingEmbedding: 0,
                dimensionMismatch: 0,
                queryEmbedding.Length);
            return;
        }

        _metrics.RecordRetrievalScoring(
            MemoryRetrievalMetrics.ScoringModePartialFallback,
            GetEmbeddingFallbackReason(missing, dimensionMismatch),
            scorableMemories.Count,
            usable,
            missing,
            dimensionMismatch,
            queryEmbedding.Length);
    }

    private static string GetEmbeddingFallbackReason(int missing, int dimensionMismatch)
    {
        return (missing, dimensionMismatch) switch
        {
            (> 0, > 0) => MemoryRetrievalMetrics.FallbackReasonMixed,
            (> 0, _) => MemoryRetrievalMetrics.FallbackReasonMemoryEmbeddingMissing,
            (_, > 0) => MemoryRetrievalMetrics.FallbackReasonDimensionMismatch,
            _ => MemoryRetrievalMetrics.FallbackReasonNone,
        };
    }

    private static double ScoreMemory(Memory memory, float[]? queryEmbedding, HashSet<string> queryTokens)
    {
        // Relevance term: cosine similarity when both sides have embeddings, else keyword overlap.
        double relevance;
        if (queryEmbedding is not null && memory.Embedding is not null && memory.Embedding.Length == queryEmbedding.Length)
        {
            // Cosine similarity is in [-1, 1] (effectively [0, 1] for text). Scale to match keyword overlap magnitude.
            relevance = CosineSimilarity(memory.Embedding, queryEmbedding) * 5.0;
        }
        else
        {
            var memoryTokens = Tokenize(memory.Content);
            relevance = memoryTokens.Intersect(queryTokens).Count();
        }

        var daysOld = (DateTime.UtcNow - memory.CreatedAt).TotalDays;
        var recency = Math.Exp(-daysOld / 30.0);
        var usage = Math.Log(memory.UseCount + 1) / Math.Log(10);
        var typeWeight = memory.Type switch
        {
            MemoryType.Fact => 1.0,
            MemoryType.Summary => 0.7,
            _ => 0.0,
        };
        return relevance * 3.0 + recency * 1.0 + usage * 0.5 + typeWeight;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom == 0 ? 0 : dot / denom;
    }

    private static readonly Regex TokenSplit = new(@"\W+", RegexOptions.Compiled);
    private static HashSet<string> Tokenize(string text)
    {
        return new HashSet<string>(
            TokenSplit.Split(text.ToLowerInvariant())
                .Where(t => t.Length >= 3),
            StringComparer.Ordinal);
    }
}
