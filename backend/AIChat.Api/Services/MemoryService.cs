using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class MemoryService : IMemoryService
{
    // Total character cap on a retrieved memory block. ~500 tokens at 4 chars/token.
    private const int MaxRetrievedChars = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex SafeUserId = new(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

    private readonly string _memoryDir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly IAzureOpenAIService _openAI;
    private readonly ILogger<MemoryService> _logger;

    public MemoryService(IAzureOpenAIService openAI, ILogger<MemoryService> logger)
    {
        _openAI = openAI;
        _logger = logger;
        _memoryDir = Path.Combine("data", "memory");
        Directory.CreateDirectory(_memoryDir);
    }

    public async Task<List<Memory>> GetAllAsync(string userId)
    {
        var memories = await WithLock(userId, () => LoadAsync(userId));
        return memories.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public async Task<Memory?> GetAsync(string userId, string id)
    {
        var memories = await WithLock(userId, () => LoadAsync(userId));
        return memories.FirstOrDefault(m => m.Id == id);
    }

    public async Task<List<Memory>> GetByIdsAsync(string userId, IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet();
        if (idSet.Count == 0) return new List<Memory>();

        var memories = await WithLock(userId, () => LoadAsync(userId));
        return memories.Where(m => idSet.Contains(m.Id)).ToList();
    }

    public async Task<Memory> CreateAsync(string userId, MemoryType type, string content, string? sourceConversationId)
    {
        // Generate embedding outside the per-user lock to avoid holding it during an LLM call.
        var embedding = await _openAI.TryGenerateEmbeddingAsync(content);

        return await WithLock(userId, async () =>
        {
            var memories = await LoadAsync(userId);
            var memory = new Memory
            {
                UserId = userId,
                Type = type,
                Content = content,
                SourceConversationId = sourceConversationId,
                Embedding = embedding,
            };
            memories.Add(memory);
            await SaveAsync(userId, memories);
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

        return await WithLock(userId, async () =>
        {
            var memories = await LoadAsync(userId);
            var memory = memories.FirstOrDefault(m => m.Id == id);
            if (memory is null) return (Memory?)null;

            if (type.HasValue) memory.Type = type.Value;
            if (contentChanged)
            {
                memory.Content = content!;
                memory.Embedding = newEmbedding;
            }

            await SaveAsync(userId, memories);
            return memory;
        });
    }

    public Task<bool> DeleteAsync(string userId, string id)
    {
        return WithLock(userId, async () =>
        {
            var memories = await LoadAsync(userId);
            var removed = memories.RemoveAll(m => m.Id == id);
            if (removed == 0) return false;

            await SaveAsync(userId, memories);
            return true;
        });
    }

    public async Task<List<Memory>> RetrieveAsync(string userId, string query, int limit = 5)
    {
        var all = await WithLock(userId, () => LoadAsync(userId));
        if (all.Count == 0) return new List<Memory>();

        // Prefer embedding-based similarity; fall back to keyword overlap when either
        // the query or the memory has no embedding.
        var queryEmbedding = await _openAI.TryGenerateEmbeddingAsync(query);
        var queryTokens = Tokenize(query);

        var preferences = all.Where(m => m.Type == MemoryType.Preference).ToList();
        var scorable = all
            .Where(m => m.Type != MemoryType.Preference)
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

        return WithLock(userId, async () =>
        {
            var memories = await LoadAsync(userId);
            var now = DateTime.UtcNow;
            var touched = 0;

            foreach (var memory in memories)
            {
                if (!idSet.Contains(memory.Id)) continue;
                memory.UseCount++;
                memory.LastUsedAt = now;
                touched++;
            }

            if (touched > 0)
            {
                await SaveAsync(userId, memories);
            }
        });
    }

    // ---------- IO + locking ----------

    private SemaphoreSlim LockFor(string userId) =>
        _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    private async Task<T> WithLock<T>(string userId, Func<Task<T>> action)
    {
        ValidateUserId(userId);
        var sem = LockFor(userId);
        await sem.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task WithLock(string userId, Func<Task> action)
    {
        ValidateUserId(userId);
        var sem = LockFor(userId);
        await sem.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            sem.Release();
        }
    }

    private static void ValidateUserId(string userId)
    {
        if (!SafeUserId.IsMatch(userId))
        {
            throw new InvalidOperationException($"Invalid userId for file storage: {userId}");
        }
    }

    private string PathFor(string userId) => Path.Combine(_memoryDir, $"{userId}.json");

    private async Task<List<Memory>> LoadAsync(string userId)
    {
        var path = PathFor(userId);
        if (!File.Exists(path)) return new List<Memory>();

        try
        {
            await using var stream = File.OpenRead(path);
            var memories = await JsonSerializer.DeserializeAsync<List<Memory>>(stream, JsonOptions);
            return memories ?? new List<Memory>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse memory file for user {UserId}, returning empty", userId);
            return new List<Memory>();
        }
    }

    private async Task SaveAsync(string userId, List<Memory> memories)
    {
        var path = PathFor(userId);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, memories, JsonOptions);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    // ---------- scoring ----------

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
