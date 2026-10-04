using System.Threading.Channels;
using AIChat.Api.Models;

namespace AIChat.Api.Services;

public class ExtractionJob
{
    public required string UserId { get; init; }
    public required string ConversationId { get; init; }
    public required List<ChatMessage> Messages { get; init; }
    public required string LastMessageId { get; init; }
    internal CancellationTokenSource Cancellation { get; } = new();
}

// Producer-only surface. Injected into ChatHub so callers can enqueue but not
// accidentally reach into the channel / dedup state.
public interface IExtractionQueue
{
    /// <summary>
    /// Enqueue a job. Returns false if the conversation is already queued or in progress.
    /// </summary>
    bool TryEnqueue(ExtractionJob job);
    void Cancel(string userId, string conversationId);
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
    private readonly object _gate = new();
    private readonly Dictionary<(string UserId, string ConversationId), (ExtractionJob Job, DateTime AcquiredAt)> _pending = new();

    public bool TryEnqueue(ExtractionJob job)
    {
        lock (_gate)
        {
            var key = (job.UserId, job.ConversationId);
            if (_pending.TryGetValue(key, out var existing))
            {
                if (DateTime.UtcNow - existing.AcquiredAt < StaleLeaseTimeout)
                {
                    job.Cancellation.Dispose();
                    return false;
                }
                existing.Job.Cancellation.Cancel();
            }
            _pending[key] = (job, DateTime.UtcNow);
            if (_channel.Writer.TryWrite(job)) return true;
            _pending.Remove(key);
            job.Cancellation.Dispose();
            return false;
        }
    }

    public void Cancel(string userId, string conversationId)
    {
        lock (_gate)
        {
            if (_pending.Remove((userId, conversationId), out var lease)) lease.Job.Cancellation.Cancel();
        }
    }

    internal ChannelReader<ExtractionJob> Reader => _channel.Reader;
    internal void Release(ExtractionJob job)
    {
        lock (_gate)
        {
            var key = (job.UserId, job.ConversationId);
            if (_pending.TryGetValue(key, out var lease) && ReferenceEquals(lease.Job, job)) _pending.Remove(key);
            job.Cancellation.Dispose();
        }
    }
}
