using AIChat.Api.Controllers;
using AIChat.Api.Middleware;
using AIChat.Api.Models;
using AIChat.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIChat.Api.Tests;

public sealed class MemoryModeTests : IDisposable
{
    private readonly string _originalDirectory = Directory.GetCurrentDirectory();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aichat-memory-tests", Guid.NewGuid().ToString("N"));
    private readonly PendingExtractionStore _pending;
    private readonly ExtractionCheckpointService _checkpoint;
    private readonly ExtractionQueue _queue = new();
    private readonly IdleExtractionScheduler _scheduler;

    public MemoryModeTests()
    {
        Directory.CreateDirectory(_directory);
        Directory.SetCurrentDirectory(_directory);
        _pending = new(new UserJsonStore<Dictionary<string, PendingExtraction>>("pending", NullLogger<UserJsonStore<Dictionary<string, PendingExtraction>>>.Instance));
        _checkpoint = new(new UserJsonStore<Dictionary<string, ExtractionCheckpoint>>("extraction", NullLogger<UserJsonStore<Dictionary<string, ExtractionCheckpoint>>>.Instance));
        _scheduler = CreateScheduler();
    }

    private IdleExtractionScheduler CreateScheduler() => new(_pending, _checkpoint, _queue,
        Options.Create(new MemorySettings { IdleExtractionSeconds = 3600, MinMessagesToExtract = 1 }), NullLogger<IdleExtractionScheduler>.Instance);

    private static ChatMessage Message(string id, string role = "user") => new() { Id = id, Role = role, Content = $"body-{id}" };
    private static ExtractionJob Job(string user = "alice") => new() { UserId = user, ConversationId = "shared", Messages = [Message("old")], LastMessageId = "old" };

    [Fact]
    public async Task DisableCancelsPendingAndQueuedWorkOnlyForCurrentUser()
    {
        await _scheduler.ScheduleAsync("alice", "shared", [Message("old")]);
        await _scheduler.ScheduleAsync("bob", "shared", [Message("old")]);
        var alice = Job();
        var bob = Job("bob");
        Assert.True(_queue.TryEnqueue(alice));
        Assert.True(_queue.TryEnqueue(bob));
        await _scheduler.SetMemoryModeAsync("alice", "shared", false, "private");
        Assert.Null(await _pending.GetAsync("alice", "shared"));
        Assert.NotNull(await _pending.GetAsync("bob", "shared"));
        Assert.True(alice.Cancellation.IsCancellationRequested);
        Assert.False(bob.Cancellation.IsCancellationRequested);
        await _scheduler.ScheduleAsync("alice", "shared", [Message("private")]);
        Assert.Null(await _pending.GetAsync("alice", "shared"));
    }

    [Fact]
    public async Task ReenableAfterRestartNeverStagesPrivateMessagesOrTheirAnswer()
    {
        await _scheduler.SetMemoryModeAsync("alice", "shared", false, "private");
        _scheduler.CancelAll();
        var restarted = CreateScheduler();
        try
        {
            await restarted.SetMemoryModeAsync("alice", "shared", true);
            await restarted.ScheduleAsync("alice", "shared", [Message("old"), Message("private"), Message("private-answer", "assistant"), Message("public")]);
            var pending = await _pending.GetAsync("alice", "shared");
            Assert.Equal("public", Assert.Single(pending!.Messages).Id);
            var saved = await File.ReadAllTextAsync(Path.Combine("data", "pending", "alice.json"));
            Assert.DoesNotContain("body-private", saved);
            await _checkpoint.SetAsync("alice", "shared", "public");
            Assert.Equal("private", (await _checkpoint.GetAsync("alice", "shared"))!.SuppressedThroughMessageId);
        }
        finally { restarted.CancelAll(); }
    }

    [Fact]
    public async Task MissingSuppressionBoundaryFailsClosed()
    {
        await _scheduler.SetMemoryModeAsync("alice", "shared", false, "missing-private");
        await _scheduler.SetMemoryModeAsync("alice", "shared", true);
        await _scheduler.ScheduleAsync("alice", "shared", [Message("untrusted-history")]);
        Assert.Null(await _pending.GetAsync("alice", "shared"));
    }

    [Fact]
    public async Task CancelledJobCannotCommitAfterReenableOrReleaseNewLease()
    {
        var oldJob = Job();
        Assert.True(_queue.TryEnqueue(oldJob));
        await _scheduler.SetMemoryModeAsync("alice", "shared", false, "private");
        await _scheduler.SetMemoryModeAsync("alice", "shared", true);
        var committed = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scheduler.CommitAsync(oldJob, () => { committed = true; return Task.CompletedTask; }, CancellationToken.None));
        Assert.False(committed);
        var newJob = Job();
        Assert.True(_queue.TryEnqueue(newJob));
        _queue.Release(oldJob);
        Assert.False(_queue.TryEnqueue(Job()));
        _queue.Release(newJob);
    }

    [Fact]
    public async Task ApiUsesAuthenticatedUserAndConfirmsPersistedState()
    {
        var conversationId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Items[AuthCodeMiddleware.UserIdItemKey] = "alice";
        var controller = new MemoryController(null!, _scheduler) { ControllerContext = new ControllerContext { HttpContext = context } };
        var result = await controller.SetConversationMode(conversationId, new UpdateConversationMemoryRequest { Enabled = false, LastMessageId = "private" }, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
        Assert.True((await _checkpoint.GetAsync("alice", conversationId.ToString()))!.MemoryDisabled);
        Assert.Null(await _checkpoint.GetAsync("bob", conversationId.ToString()));
    }

    public void Dispose()
    {
        _scheduler.CancelAll();
        Directory.SetCurrentDirectory(_originalDirectory);
        Directory.Delete(_directory, recursive: true);
    }
}