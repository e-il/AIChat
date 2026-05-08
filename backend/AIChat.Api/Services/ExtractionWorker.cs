using Microsoft.Extensions.Hosting;
using AIChat.Api.Models;

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

        var existingMemories = await _memory.GetAllAsync(job.UserId);
        var existingById = existingMemories.ToDictionary(m => m.Id);
        var seen = existingMemories
            .Select(m => NormalizeMemoryContent(m.Content))
            .Where(normalized => normalized.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var extracted = await _openAI.ExtractMemoriesAsync(job.Messages, existingMemories, ct);
        var created = 0;
        var updated = 0;
        var skipped = 0;
        foreach (var item in extracted)
        {
            var content = item.Content.Trim();
            var normalized = NormalizeMemoryContent(content);
            if (normalized.Length == 0 || seen.Contains(normalized))
            {
                skipped++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.ExistingMemoryId)
                && existingById.TryGetValue(item.ExistingMemoryId, out var existing))
            {
                var updatedMemory = await _memory.UpdateAsync(job.UserId, existing.Id, item.Type, content);
                if (updatedMemory is null)
                {
                    skipped++;
                    continue;
                }

                seen.Add(normalized);
                updated++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.ExistingMemoryId))
            {
                _logger.LogWarning("Extraction returned unknown existingMemoryId={MemoryId}; creating as new if not duplicate",
                    item.ExistingMemoryId);
            }

            await _memory.CreateAsync(job.UserId, item.Type, content, job.ConversationId);
            seen.Add(normalized);
            created++;
        }

        await _checkpoint.SetAsync(job.UserId, job.ConversationId, job.LastMessageId);

        _logger.LogInformation(
            "Extraction complete: user={UserId}, conversation={ConversationId}, created={Created}, updated={Updated}, skipped={Skipped}",
            job.UserId, job.ConversationId, created, updated, skipped);
    }

    private static string NormalizeMemoryContent(string content)
    {
        var normalizedChars = content
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return string.Join(
            ' ',
            new string(normalizedChars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
