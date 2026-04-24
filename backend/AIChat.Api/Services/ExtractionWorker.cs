using Microsoft.Extensions.Hosting;

namespace AIChat.Api.Services;

public class ExtractionWorker : BackgroundService
{
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(2);

    private readonly ExtractionQueue _queue;
    private readonly IAzureOpenAIService _openAI;
    private readonly IMemoryService _memory;
    private readonly IExtractionCheckpointService _checkpoint;
    private readonly ILogger<ExtractionWorker> _logger;

    public ExtractionWorker(
        ExtractionQueue queue,
        IAzureOpenAIService openAI,
        IMemoryService memory,
        IExtractionCheckpointService checkpoint,
        ILogger<ExtractionWorker> logger)
    {
        _queue = queue;
        _openAI = openAI;
        _memory = memory;
        _checkpoint = checkpoint;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExtractionWorker started");

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(JobTimeout);
                await ProcessJobAsync(job, cts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw; // shutting down
            }
            catch (Exception ex)
            {
                // Failure does not advance the checkpoint — next chat turn will retry with accumulated messages.
                _logger.LogError(ex, "Extraction job failed for conversation {ConversationId}", job.ConversationId);
            }
            finally
            {
                _queue.Release(job.ConversationId);
            }
        }

        _logger.LogInformation("ExtractionWorker stopped");
    }

    private async Task ProcessJobAsync(ExtractionJob job, CancellationToken ct)
    {
        _logger.LogInformation("Extracting memories: user={UserId}, conversation={ConversationId}, messages={Count}",
            job.UserId, job.ConversationId, job.Messages.Count);

        var extracted = await _openAI.ExtractMemoriesAsync(job.Messages, ct);

        foreach (var item in extracted)
        {
            await _memory.CreateAsync(job.UserId, item.Type, item.Content, job.ConversationId);
        }

        await _checkpoint.SetAsync(job.UserId, job.ConversationId, job.LastMessageId);

        _logger.LogInformation("Extraction complete: user={UserId}, conversation={ConversationId}, created={Count}",
            job.UserId, job.ConversationId, extracted.Count);
    }
}
