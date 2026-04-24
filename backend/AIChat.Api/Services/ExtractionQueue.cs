using System.Collections.Concurrent;
using System.Threading.Channels;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ExtractionJob
{
    public required string UserId { get; init; }
    public required string ConversationId { get; init; }
    public required List<ChatMessage> Messages { get; init; }
    public required string LastMessageId { get; init; }
}

// Producer-only surface. Injected into ChatHub so callers can enqueue but not
// accidentally reach into the channel / dedup state.
public interface IExtractionQueue
{
    /// <summary>
    /// Enqueue a job. Returns false if the conversation is already queued or in progress.
    /// </summary>
    bool TryEnqueue(ExtractionJob job);
}

// Consumer members (Reader / Release) are internal so producers don't see them.
// ExtractionWorker injects the concrete type (not the interface) to reach them.
// See Program.cs for the paired registration that keeps both surfaces pointing
// at the same singleton instance.
public class ExtractionQueue : IExtractionQueue
{
    // A lease older than this is treated as abandoned (worker crashed / hung).
    // Next enqueue for the same conversation replaces it instead of being dropped.
    private static readonly TimeSpan StaleLeaseTimeout = TimeSpan.FromMinutes(5);

    private readonly Channel<ExtractionJob> _channel = Channel.CreateUnbounded<ExtractionJob>();
    private readonly ConcurrentDictionary<string, DateTime> _pending = new();

    public bool TryEnqueue(ExtractionJob job)
    {
        var now = DateTime.UtcNow;

        // Fast path: no existing lease.
        if (_pending.TryAdd(job.ConversationId, now))
        {
            return TryWriteOrRollback(job, now);
        }

        // Existing lease: take over only if it's stale.
        if (_pending.TryGetValue(job.ConversationId, out var acquiredAt)
            && now - acquiredAt >= StaleLeaseTimeout
            && _pending.TryUpdate(job.ConversationId, now, acquiredAt))
        {
            return TryWriteOrRollback(job, now);
        }

        return false;
    }

    private bool TryWriteOrRollback(ExtractionJob job, DateTime now)
    {
        if (_channel.Writer.TryWrite(job)) return true;

        // Only clear our own lease (don't stomp a newer one from a concurrent caller).
        ((ICollection<KeyValuePair<string, DateTime>>)_pending)
            .Remove(new KeyValuePair<string, DateTime>(job.ConversationId, now));
        return false;
    }

    internal ChannelReader<ExtractionJob> Reader => _channel.Reader;
    internal void Release(string conversationId) => _pending.TryRemove(conversationId, out _);
}
