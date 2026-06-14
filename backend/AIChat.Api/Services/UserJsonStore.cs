using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AIChat.Api.Services;

/// <summary>
/// Per-user JSON document storage at data/&lt;subdirectory&gt;/&lt;userId&gt;.json.
///
/// Each user's file is the unit of storage and concurrency: reads and read-modify-write
/// operations for the same user are serialized by a per-user lock, while different users
/// proceed in parallel. Writes are atomic (temp file + move); a missing or corrupt file
/// reads as an empty <typeparamref name="T"/>.
///
/// Callers never touch locks or IO directly — use <see cref="ReadAsync"/> for reads and
/// <see cref="MutateAsync(string, Action{T})"/> / <see cref="MutateAsync{TResult}"/> to
/// load-mutate-persist within a single lock.
/// </summary>
public sealed class UserJsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Restricts userId to a safe filename so it can't escape the storage directory.
    private static readonly Regex SafeUserId = new(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

    private readonly string _dir;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public UserJsonStore(string subdirectory, ILogger<UserJsonStore<T>> logger)
    {
        _dir = Path.Combine("data", subdirectory);
        Directory.CreateDirectory(_dir);
        _logger = logger;
    }

    /// <summary>Load the user's document (empty if absent/corrupt).</summary>
    public Task<T> ReadAsync(string userId) =>
        WithLock(userId, () => LoadAsync(userId));

    /// <summary>Load, apply <paramref name="mutate"/>, then persist — under a single per-user lock.</summary>
    public Task MutateAsync(string userId, Action<T> mutate) =>
        MutateAsync(userId, data => { mutate(data); return true; });

    /// <summary>
    /// Load, apply <paramref name="mutate"/>, then persist — under a single per-user lock.
    /// Returns whatever <paramref name="mutate"/> produced (e.g. the created or removed entity).
    /// </summary>
    public Task<TResult> MutateAsync<TResult>(string userId, Func<T, TResult> mutate) =>
        WithLock(userId, async () =>
        {
            var data = await LoadAsync(userId);
            var result = mutate(data);
            await SaveAsync(userId, data);
            return result;
        });

    /// <summary>Enumerate userIds that have a stored file (for startup scans).</summary>
    public IEnumerable<string> EnumerateUserIds()
    {
        if (!Directory.Exists(_dir)) yield break;

        foreach (var path in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var userId = Path.GetFileNameWithoutExtension(path);
            if (SafeUserId.IsMatch(userId)) yield return userId;
        }
    }

    // ---------- internals ----------

    private async Task<TResult> WithLock<TResult>(string userId, Func<Task<TResult>> action)
    {
        if (!SafeUserId.IsMatch(userId))
        {
            throw new InvalidOperationException($"Invalid userId for file storage: {userId}");
        }

        var sem = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try { return await action(); }
        finally { sem.Release(); }
    }

    private async Task<T> LoadAsync(string userId)
    {
        var path = PathFor(userId);
        if (!File.Exists(path)) return new T();

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions) ?? new T();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse {Path}, returning empty", path);
            return new T();
        }
    }

    private async Task SaveAsync(string userId, T data)
    {
        var path = PathFor(userId);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string PathFor(string userId) => Path.Combine(_dir, $"{userId}.json");
}
