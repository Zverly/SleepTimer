using SleepTimer.Core;
using Xunit;

namespace SleepTimer.Core.Tests;

public sealed class HistoryAndLoggingTests : IDisposable
{
    private readonly string _portableDirectory = Path.Combine(
        @"E:\codex_project",
        ".tmp",
        "history-logging-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HistoryStore_AppendsAndLoadsEventsFromPortableData()
    {
        var store = new HistoryStore(new StateStore(_portableDirectory));
        var entry = new TimerHistoryEntry(
            DateTimeOffset.Parse("2026-08-27T22:00:00+08:00"),
            TimerEventType.Created,
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));

        await store.AppendAsync(entry);

        var loaded = await store.LoadAsync();

        Assert.Equal(new[] { entry }, loaded);
        Assert.True(File.Exists(Path.Combine(_portableDirectory, "data", "history.json")));
    }

    [Fact]
    public async Task HistoryStore_WhenHistoryIsCorrupt_ReturnsEmptyAndBacksUpFile()
    {
        var dataDirectory = Path.Combine(_portableDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "history.json");
        await File.WriteAllTextAsync(path, "not-json");

        var loaded = await new HistoryStore(new StateStore(_portableDirectory)).LoadAsync();

        Assert.Empty(loaded);
        Assert.False(File.Exists(path));
        Assert.Equal("not-json", await File.ReadAllTextAsync(path + ".bak"));
    }

    [Fact]
    public async Task HistoryStore_DropsOldestEntriesBeyondConfiguredLimit()
    {
        var store = new HistoryStore(new StateStore(_portableDirectory), maxEntries: 2);
        var first = CreateEntry(TimerEventType.Created);
        var second = CreateEntry(TimerEventType.Delayed);
        var third = CreateEntry(TimerEventType.Cancelled);

        await store.AppendAsync(first);
        await store.AppendAsync(second);
        await store.AppendAsync(third);

        Assert.Equal(new[] { second, third }, await store.LoadAsync());
    }

    [Fact]
    public async Task FileAppLogger_RotatesBySizeAndRetainsOnlyConfiguredFiles()
    {
        var logger = new FileAppLogger(
            _portableDirectory,
            new AppLoggerOptions
            {
                MaxFileBytes = 1,
                RetainedFileCount = 2,
                Clock = () => new DateTimeOffset(2026, 8, 27, 22, 0, 0, TimeSpan.FromHours(8))
            });

        await logger.LogAsync(AppLogLevel.Information, "timer.created");
        await logger.LogAsync(AppLogLevel.Error, "timer.execution-failed", "power-command-failed");
        await logger.LogAsync(AppLogLevel.Warning, "timer.cancelled");

        var files = Directory.GetFiles(Path.Combine(_portableDirectory, "data", "logs"));

        Assert.Equal(2, files.Length);
        Assert.All(files, file => Assert.DoesNotContain("secret", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => Path.GetFileName(file).StartsWith("app-20260827", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("window title")]
    [InlineData("C:\\Users\\person\\secret.txt")]
    [InlineData("error\nforged-entry")]
    public async Task FileAppLogger_RejectsUnboundedOrPrivateCodes(string code)
    {
        var logger = new FileAppLogger(_portableDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            logger.LogAsync(AppLogLevel.Error, code));
    }

    [Fact]
    public async Task TimerService_LoadsHistoryAndRecordsSuccessfulLifecycleEvents()
    {
        var historyStore = new InMemoryHistoryStore();
        var scheduler = new InMemoryScheduledTaskStore();
        var service = new TimerService(scheduler, () => new DateTime(2026, 8, 27, 22, 0, 0), historyStore);

        await service.LoadHistoryAsync();
        await service.StartAsync(TimerAction.Shutdown, new DateTime(2026, 8, 27, 23, 0, 0));
        await service.ExtendAsync(TimeSpan.FromMinutes(30));
        await service.CancelAsync();

        Assert.Equal(
            new[] { TimerEventType.Created, TimerEventType.Delayed, TimerEventType.Cancelled },
            historyStore.Entries.Select(entry => entry.EventType));
        Assert.Equal(historyStore.Entries, service.History);
    }

    [Fact]
    public async Task TimerService_RecordsSchedulerAndExecutionFailures()
    {
        var historyStore = new InMemoryHistoryStore();
        var scheduler = new InMemoryScheduledTaskStore { FailNextOperation = true };
        var service = new TimerService(scheduler, () => new DateTime(2026, 8, 27, 22, 0, 0), historyStore);
        var task = new ScheduledTaskSummary(
            TimerService.AppTaskName,
            TimerAction.Sleep,
            new DateTime(2026, 8, 27, 23, 0, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAsync(task.Action, task.TargetTime));
        await service.RecordExecutionFailureAsync(task, "power-command-failed");

        Assert.Equal(
            new[] { TimerEventType.CreationFailed, TimerEventType.ExecutionFailed },
            historyStore.Entries.Select(entry => entry.EventType));
        Assert.Equal("power-command-failed", historyStore.Entries[^1].ErrorCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_portableDirectory))
            Directory.Delete(_portableDirectory, recursive: true);
    }

    private static TimerHistoryEntry CreateEntry(TimerEventType eventType) =>
        new(
            DateTimeOffset.Parse("2026-08-27T22:00:00+08:00"),
            eventType,
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));

    private sealed class InMemoryHistoryStore : IHistoryStore
    {
        public List<TimerHistoryEntry> Entries { get; } = [];

        public Task<IReadOnlyList<TimerHistoryEntry>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TimerHistoryEntry>>(Entries.ToArray());

        public Task AppendAsync(TimerHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryScheduledTaskStore : IScheduledTaskStore
    {
        public bool FailNextOperation { get; set; }
        private ScheduledTaskSummary? _currentTask;

        public Task<ScheduledTaskSummary?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_currentTask);

        public Task ReplaceAsync(ScheduledTaskSummary task, CancellationToken cancellationToken = default)
        {
            if (FailNextOperation)
            {
                FailNextOperation = false;
                throw new InvalidOperationException("scheduler failure");
            }

            _currentTask = task;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string taskName, CancellationToken cancellationToken = default)
        {
            _currentTask = null;
            return Task.CompletedTask;
        }
    }
}
