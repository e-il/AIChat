using System.Collections.Concurrent;
using AIChat.Api.Models;
using Microsoft.Extensions.Options;

namespace AIChat.Api.Services;

/// <summary>
/// Debounced, idle-based memory extraction. Each completed turn (re)starts a per-conversation
/// timer; a new turn within the idle window cancels and reschedules it, so extraction only
/// runs once a conversation has been quiet for <see cref="MemorySettings.IdleExtractionSeconds"/>.
/// This replaces fixed turn-count thresholds and avoids any background polling. Pending snapshots
/// are persisted via <see cref="PendingExtractionStore"/> and reloaded/rescheduled on startup.
/// Lifecycle (reload on start, cancel on stop) is driven by <see cref="ExtractionWorker"/>, so
/// this type does not need to be a hosted service itself.
/// </summary>
public class IdleExtractionScheduler
{
    // On startup, never fire sooner than this after reloading a persisted entry.
    private static readonly TimeSpan MinRescheduleDelay = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PendingExtractionStore _store;
    private readonly IExtractionCheckpointService _checkpoint;
    private readonly IExtractionQueue _queue;
    private readonly IOptions<MemorySettings> _configuration;
    private readonly ILogger<IdleExtractionScheduler> _logger;

    public IdleExtractionScheduler(
        PendingExtractionStore store,
        IExtractionCheckpointService checkpoint,
        IExtractionQueue queue,
        IOptions<MemorySettings> configuration,
        ILogger<IdleExtractionScheduler> logger)
    {
        _store = store;
        _checkpoint = checkpoint;
        _queue = queue;
        _configuration = configuration;
        _logger = logger;
    }

    private TimeSpan IdleDelay => TimeSpan.FromSeconds(Math.Max(1, _configuration.Value.IdleExtractionSeconds));

    private static string Key(string userId, string conversationId) => $"{userId}|{conversationId}";

    /// <summary>
    /// Stage the latest conversation snapshot and (re)start its idle timer. Called after each
    /// completed turn; a fresh call within the idle window cancels the previous pending extraction.
    /// </summary>
    public async Task ScheduleAsync(string userId, string conversationId, List<ChatMessage> messages)
    {
        await _gate.WaitAsync();
        try
        {
            var checkpoint = await _checkpoint.GetAsync(userId, conversationId);
            var eligible = FilterEligibleMessages(messages, checkpoint);
            if (eligible.Count == 0) return;
            await _store.SaveAsync(userId, conversationId, eligible);
            StartTimer(userId, conversationId, IdleDelay);
        }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync(string userId, string conversationId, string? advanceCheckpointToMessageId = null)
    {
        await SetMemoryModeAsync(userId, conversationId, false, advanceCheckpointToMessageId);
    }

    public async Task SetMemoryModeAsync(string userId, string conversationId, bool enabled, string? lastMessageId = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!enabled)
            {
                _queue.Cancel(userId, conversationId);
                await CancelPendingAsync(userId, conversationId);
            }
            await _checkpoint.SetMemoryModeAsync(userId, conversationId, enabled, lastMessageId);
        }
        finally { _gate.Release(); }
    }

    public async Task CommitAsync(ExtractionJob job, Func<Task> commit, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            job.Cancellation.Token.ThrowIfCancellationRequested();
            var checkpoint = await _checkpoint.GetAsync(job.UserId, job.ConversationId);
            if (checkpoint?.MemoryDisabled == true) return;
            await commit();
        }
        finally { _gate.Release(); }
    }

    private async Task CancelPendingAsync(string userId, string conversationId)
    {
        var key = Key(userId, conversationId);
        if (_timers.TryRemove(key, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        await _store.RemoveAsync(userId, conversationId);
        _logger.LogInformation("Idle extraction cancelled for conversation {ConversationId}", conversationId);
    }

    /// <summary>
    /// Reload persisted pending conversations and reschedule their idle timers. Called once by
    /// <see cref="ExtractionWorker"/> on startup so a restart during an idle window isn't lost.
    /// </summary>
    public async Task ReloadPendingAsync()
    {
        var all = await _store.LoadAllAsync();
        foreach (var (userId, entry) in all)
        {
            var remaining = IdleDelay - (DateTime.UtcNow - entry.LastActivityUtc);
            if (remaining < MinRescheduleDelay) remaining = MinRescheduleDelay;
            StartTimer(userId, entry.ConversationId, remaining);
        }

        _logger.LogInformation(
            "IdleExtractionScheduler reloaded {Count} pending conversation(s) (idle={IdleSeconds}s)",
            all.Count, _configuration.Value.IdleExtractionSeconds);
    }

    /// <summary>
    /// Cancel all outstanding idle timers. Called by <see cref="ExtractionWorker"/> on shutdown;
    /// persisted snapshots remain on disk for the next startup.
    /// </summary>
    public void CancelAll()
    {
        foreach (var kvp in _timers)
        {
            try { kvp.Value.Cancel(); } catch (ObjectDisposedException) { }
            kvp.Value.Dispose();
        }
        _timers.Clear();
        _logger.LogInformation("IdleExtractionScheduler timers cancelled");
    }

    private void StartTimer(string userId, string conversationId, TimeSpan delay)
    {
        var key = Key(userId, conversationId);
        var cts = new CancellationTokenSource();

        _timers.AddOrUpdate(
            key,
            cts,
            (_, existing) =>
            {
                try { existing.Cancel(); } catch (ObjectDisposedException) { }
                existing.Dispose();
                return cts;
            });

        _ = RunAfterDelayAsync(userId, conversationId, key, delay, cts);
    }

    private async Task RunAfterDelayAsync(
        string userId, string conversationId, string key, TimeSpan delay, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // rescheduled by a newer turn, or shutting down
        }

        // Only remove our own entry (a newer turn may have replaced it between the delay
        // completing and this line).
        ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_timers)
            .Remove(new KeyValuePair<string, CancellationTokenSource>(key, cts));
        cts.Dispose();

        try
        {
            await FireAsync(userId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Idle extraction failed for conversation {ConversationId}", conversationId);
        }
    }

    private async Task FireAsync(string userId, string conversationId)
    {
        await _gate.WaitAsync();
        try
        {
            var pending = await _store.GetAsync(userId, conversationId);
            if (pending is null) return;

            try
            {
                await TryQueueAsync(userId, conversationId, pending.Messages);
            }
            finally
            {
                // The snapshot has been consumed; the client re-supplies the full list on any
                // future turn, and the persisted checkpoint prevents re-extracting old messages.
                await _store.RemoveAsync(userId, conversationId);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task TryQueueAsync(string userId, string conversationId, List<ChatMessage> messages)
    {
        var checkpoint = await _checkpoint.GetAsync(userId, conversationId);
        if (checkpoint?.MemoryDisabled == true) return;
        var unextracted = GetUnextractedMessages(messages, checkpoint?.LastExtractedMessageId);

        if (unextracted.Count == 0)
        {
            return;
        }

        // Extract only text turns; conversations about images don't yield durable memory.
        var textOnly = unextracted
            .Where(m => m.Attachments is null || m.Attachments.Count == 0)
            .ToList();

        var lastMessageId = unextracted[^1].Id;

        if (textOnly.Count == 0)
        {
            _logger.LogInformation(
                "Idle extraction skipped: all {Count} unextracted messages have image attachments. Advancing checkpoint to {LastId}.",
                unextracted.Count, lastMessageId);
            await _checkpoint.SetAsync(userId, conversationId, lastMessageId);
            return;
        }

        if (textOnly.Count < _configuration.Value.MinMessagesToExtract)
        {
            // Too little new content to bother. Leave the checkpoint unadvanced so these
            // messages are reconsidered (together with future turns) on the next idle flush.
            _logger.LogDebug(
                "Idle extraction skipped: {Count} new text messages < minimum {Min} for conversation {ConversationId}",
                textOnly.Count, _configuration.Value.MinMessagesToExtract, conversationId);
            return;
        }

        var enqueued = _queue.TryEnqueue(new ExtractionJob
        {
            UserId = userId,
            ConversationId = conversationId,
            Messages = textOnly,
            LastMessageId = lastMessageId,
        });

        if (enqueued)
        {
            _logger.LogInformation("Queued idle extraction: user={UserId}, conversation={ConversationId}, messages={Count}",
                userId, conversationId, textOnly.Count);
        }
        else
        {
            _logger.LogDebug("Extraction already pending for conversation {ConversationId}, skipped", conversationId);
        }
    }

    private static List<ChatMessage> GetUnextractedMessages(List<ChatMessage> messages, string? lastExtractedMessageId)
    {
        if (string.IsNullOrEmpty(lastExtractedMessageId)) return messages;

        var idx = messages.FindIndex(m => m.Id == lastExtractedMessageId);
        // Checkpoint id not found in the current history -- treat all as unextracted.
        if (idx < 0) return messages;

        return messages.Skip(idx + 1).ToList();
    }

    private static List<ChatMessage> FilterEligibleMessages(List<ChatMessage> messages, ExtractionCheckpoint? checkpoint)
    {
        if (checkpoint?.MemoryDisabled == true) return new();
        if (string.IsNullOrEmpty(checkpoint?.SuppressedThroughMessageId)) return messages;
        var boundary = messages.FindIndex(message => message.Id == checkpoint.SuppressedThroughMessageId);
        if (boundary < 0) return new();
        return messages.Skip(boundary + 1).SkipWhile(message => message.Role != "user").ToList();
    }
}
