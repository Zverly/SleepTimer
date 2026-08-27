using SleepTimer.Core;
using Xunit;

namespace SleepTimer.App.Tests;

public sealed class AppCompositionTests : IDisposable
{
    private readonly string _portableDirectory = Path.Combine(
        @"E:\codex_project",
        ".tmp",
        "app-composition-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateTimerService_UsesPortableHistoryAndLoggerForLifecycleEvents()
    {
        var service = AppComposition.CreateTimerService(
            @"E:\SleepTimer.App.exe",
            _portableDirectory,
            new InMemoryScheduledTaskStore(),
            () => new DateTime(2026, 8, 27, 22, 0, 0));

        await service.StartAsync(TimerAction.Shutdown, new DateTime(2026, 8, 27, 23, 0, 0));
        await service.CancelAsync();

        Assert.True(File.Exists(Path.Combine(_portableDirectory, "data", "history.json")));
        var logFiles = Directory.GetFiles(Path.Combine(_portableDirectory, "data", "logs"), "app-*.log");
        Assert.NotEmpty(logFiles);
        var log = string.Join(Environment.NewLine, logFiles.Select(File.ReadAllText));
        Assert.Contains("timer.created", log);
        Assert.Contains("timer.cancelled", log);
    }

    public void Dispose()
    {
        if (Directory.Exists(_portableDirectory))
            Directory.Delete(_portableDirectory, recursive: true);
    }

    private sealed class InMemoryScheduledTaskStore : IScheduledTaskStore
    {
        private ScheduledTaskSummary? _current;

        public Task<ScheduledTaskSummary?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_current);

        public Task ReplaceAsync(ScheduledTaskSummary task, CancellationToken cancellationToken = default)
        {
            _current = task;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string taskName, CancellationToken cancellationToken = default)
        {
            _current = null;
            return Task.CompletedTask;
        }
    }
}
