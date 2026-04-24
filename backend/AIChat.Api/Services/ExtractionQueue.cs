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
    private readonly Channel<ExtractionJob> _channel = Channel.CreateUnbounded<ExtractionJob>();
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    public bool TryEnqueue(ExtractionJob job)
    {
        // Dedup by conversationId: if a job is already pending/running for this conversation, drop.
        if (!_pending.TryAdd(job.ConversationId, 0)) return false;

        if (!_channel.Writer.TryWrite(job))
        {
            _pending.TryRemove(job.ConversationId, out _);
            return false;
        }
        return true;
    }

    internal ChannelReader<ExtractionJob> Reader => _channel.Reader;
    internal void Release(string conversationId) => _pending.TryRemove(conversationId, out _);
}
