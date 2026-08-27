using SleepTimer.Core;
using Xunit;

namespace SleepTimer.Core.Tests;

public class TimerServiceTests
{
    [Fact]
    public async Task Start_CreatesOneAppOwnedTask()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        var target = new DateTime(2026, 8, 27, 23, 0, 0);

        var summary = await service.StartAsync(TimerAction.Shutdown, target);

        Assert.Single(store.Tasks);
        Assert.Equal(TimerService.AppTaskName, summary.TaskName);
        Assert.Equal(summary, store.Tasks[TimerService.AppTaskName]);
        Assert.Equal(summary, service.CurrentTask);
    }

    [Fact]
    public async Task Start_RoundsTargetUpToTheNextWholeMinute()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));

        var summary = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 1));

        Assert.Equal(new DateTime(2026, 8, 27, 23, 1, 0), summary.TargetTime);
        Assert.Equal(summary, store.Tasks[TimerService.AppTaskName]);
    }

    [Fact]
    public async Task Extend_ReplacesAppTaskTarget()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        var originalTarget = new DateTime(2026, 8, 27, 23, 0, 0);
        await service.StartAsync(TimerAction.Sleep, originalTarget);

        var extended = await service.ExtendAsync(TimeSpan.FromMinutes(30));

        Assert.Single(store.Tasks);
        Assert.Equal(originalTarget.AddMinutes(30), extended.TargetTime);
        Assert.Equal(extended, store.Tasks[TimerService.AppTaskName]);
        Assert.Equal(2, store.ReplaceCallCount);
    }

    [Fact]
    public async Task Adjust_UsesTheCurrentRealTaskAndConfirmsTheReplacement()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        var realTask = new ScheduledTaskSummary(
            TimerService.AppTaskName,
            TimerAction.Sleep,
            new DateTime(2026, 8, 27, 23, 30, 0));
        store.Tasks[TimerService.AppTaskName] = realTask;

        var adjusted = await service.AdjustAsync(TimeSpan.FromMinutes(10));

        Assert.Equal(realTask with { TargetTime = new DateTime(2026, 8, 27, 23, 40, 0) }, adjusted);
        Assert.Equal(adjusted, service.CurrentTask);
        Assert.Equal(adjusted, store.Tasks[TimerService.AppTaskName]);
        Assert.Equal(2, store.GetCurrentCallCount);
    }

    [Fact]
    public async Task Adjust_WhenAdvancingPastNow_RejectsTargetInsideTwoMinuteSafetyMargin()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        store.Tasks[TimerService.AppTaskName] = new ScheduledTaskSummary(
            TimerService.AppTaskName,
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 22, 30, 0));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AdjustAsync(TimeSpan.FromMinutes(-60)));

        var original = new ScheduledTaskSummary(
            TimerService.AppTaskName,
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 22, 30, 0));
        Assert.Equal(original, store.Tasks[TimerService.AppTaskName]);
        Assert.Null(service.CurrentTask);
    }

    [Fact]
    public async Task AdjustWithResult_ReturnsRequestedAndActualTargets()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));

        var result = await service.AdjustWithResultAsync(
            TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));

        Assert.Equal(new DateTime(2026, 8, 27, 23, 30, 1), result.RequestedTargetTime);
        Assert.Equal(new DateTime(2026, 8, 27, 23, 31, 0), result.ActualTargetTime);
        Assert.Equal(result.ActualTask, service.CurrentTask);
    }

    [Fact]
    public async Task Adjust_WhenRequestedTargetIsTooSoon_PreservesOldState()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 30));
        var original = new ScheduledTaskSummary(
            TimerService.AppTaskName,
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 22, 3, 0));
        store.Tasks[TimerService.AppTaskName] = original;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AdjustWithResultAsync(TimeSpan.FromMinutes(-1)));

        Assert.Equal(original, store.Tasks[TimerService.AppTaskName]);
        Assert.Null(service.CurrentTask);
    }

    [Fact]
    public async Task Adjust_WhenReplacementFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.FailNextReplace = true;

        await Assert.ThrowsAsync<SchedulerException>(() =>
            service.AdjustAsync(TimeSpan.FromMinutes(30)));

        Assert.Equal(original, service.CurrentTask);
        Assert.Equal(original, store.Tasks[TimerService.AppTaskName]);
    }

    [Fact]
    public async Task Adjust_WhenConfirmationFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.ReturnNullOnGetCall = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdjustAsync(TimeSpan.FromMinutes(30)));

        Assert.Equal(original, service.CurrentTask);
    }

    [Fact]
    public async Task Cancel_RemovesOnlyAppOwnedTask()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        await service.StartAsync(TimerAction.Shutdown, new DateTime(2026, 8, 27, 23, 0, 0));
        store.Tasks["OtherApplication.Task"] = new ScheduledTaskSummary(
            "OtherApplication.Task",
            TimerAction.Sleep,
            new DateTime(2026, 8, 28, 1, 0, 0));

        await service.CancelAsync();

        Assert.False(store.Tasks.ContainsKey(TimerService.AppTaskName));
        Assert.True(store.Tasks.ContainsKey("OtherApplication.Task"));
        Assert.Null(service.CurrentTask);
        Assert.Equal(2, store.GetCurrentCallCount);
    }

    [Fact]
    public async Task Cancel_WhenTaskIsMissing_IsIdempotentlySuccessful()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);

        await service.CancelAsync();
        await service.CancelAsync();

        Assert.Null(service.CurrentTask);
        Assert.Equal(2, store.GetCurrentCallCount);
        Assert.Equal(0, store.RemoveCallCount);
    }

    [Fact]
    public async Task Cancel_WhenRemovalConfirmationFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.KeepTaskOnRemove = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync());

        Assert.Equal(original, service.CurrentTask);
    }

    [Fact]
    public async Task Start_WhenSchedulerFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore { FailNextOperation = true };
        var service = new TimerService(store);

        await Assert.ThrowsAsync<SchedulerException>(() =>
            service.StartAsync(TimerAction.Shutdown, new DateTime(2026, 8, 27, 23, 0, 0)));

        Assert.Null(service.CurrentTask);
    }

    [Fact]
    public async Task Start_WhenReplacingTaskFails_LeavesExistingStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.FailNextOperation = true;

        await Assert.ThrowsAsync<SchedulerException>(() =>
            service.StartAsync(
                TimerAction.Sleep,
                new DateTime(2026, 8, 28, 1, 0, 0)));

        Assert.Equal(original, service.CurrentTask);
        Assert.Equal(original, store.Tasks[TimerService.AppTaskName]);
    }

    [Fact]
    public async Task Extend_WhenSchedulerFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.FailNextOperation = true;

        await Assert.ThrowsAsync<SchedulerException>(() =>
            service.ExtendAsync(TimeSpan.FromMinutes(30)));

        Assert.Equal(original, service.CurrentTask);
        Assert.Equal(original, store.Tasks[TimerService.AppTaskName]);
    }

    [Fact]
    public async Task Cancel_WhenSchedulerFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store);
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.FailNextOperation = true;

        await Assert.ThrowsAsync<SchedulerException>(() => service.CancelAsync());

        Assert.Equal(original, service.CurrentTask);
        Assert.Equal(original, store.Tasks[TimerService.AppTaskName]);
    }

    [Fact]
    public async Task Start_InvalidAction_RejectsWithoutScheduling()
    {
        var store = new InMemoryScheduledTaskStore();
        var now = new DateTime(2026, 8, 27, 22, 0, 0);
        var service = new TimerService(store, () => now);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.StartAsync((TimerAction)999, now.AddHours(1)));

        Assert.Empty(store.Tasks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Start_TargetNotInFuture_RejectsWithoutScheduling(int offsetMinutes)
    {
        var store = new InMemoryScheduledTaskStore();
        var now = new DateTime(2026, 8, 27, 22, 0, 0);
        var service = new TimerService(store, () => now);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.StartAsync(TimerAction.Shutdown, now.AddMinutes(offsetMinutes)));

        Assert.Empty(store.Tasks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Extend_NonPositiveDuration_RejectsWithoutReplacing(int minutes)
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(store, () => DateTime.MinValue);
        var original = await service.StartAsync(TimerAction.Shutdown, new DateTime(2026, 8, 27, 23, 0, 0));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ExtendAsync(TimeSpan.FromMinutes(minutes)));

        Assert.Equal(1, store.ReplaceCallCount);
        Assert.Equal(original, service.CurrentTask);
    }

    [Fact]
    public async Task RestoreAsync_ReadsTheAppOwnedTaskFromTheScheduler()
    {
        var store = new InMemoryScheduledTaskStore();
        var restored = new ScheduledTaskSummary(
            TimerService.AppTaskName,
            TimerAction.Sleep,
            new DateTime(2026, 8, 28, 1, 0, 0));
        store.Tasks[TimerService.AppTaskName] = restored;
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));

        var result = await service.RestoreAsync();

        Assert.Equal(restored, result);
        Assert.Equal(restored, service.CurrentTask);
        Assert.Equal(1, store.GetCurrentCallCount);
    }

    [Fact]
    public async Task RestoreAsync_WhenTaskIsMissing_ClearsThePreviousState()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.Tasks.Clear();

        var result = await service.RestoreAsync();

        Assert.Null(result);
        Assert.Null(service.CurrentTask);
    }

    [Fact]
    public async Task RestoreAsync_WhenSchedulerFails_LeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.FailNextOperation = true;

        await Assert.ThrowsAsync<SchedulerException>(() => service.RestoreAsync());

        Assert.Equal(original, service.CurrentTask);
    }

    [Fact]
    public async Task RestoreAsync_WhenTaskIsNotAppOwned_RejectsAndLeavesCurrentStateUnchanged()
    {
        var store = new InMemoryScheduledTaskStore();
        var service = new TimerService(
            store,
            () => new DateTime(2026, 8, 27, 22, 0, 0));
        var original = await service.StartAsync(
            TimerAction.Shutdown,
            new DateTime(2026, 8, 27, 23, 0, 0));
        store.Tasks[TimerService.AppTaskName] = new ScheduledTaskSummary(
            "OtherApplication.Task",
            TimerAction.Sleep,
            new DateTime(2026, 8, 28, 1, 0, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync());

        Assert.Equal(original, service.CurrentTask);
    }
    private sealed class InMemoryScheduledTaskStore : IScheduledTaskStore
    {
        public Dictionary<string, ScheduledTaskSummary> Tasks { get; } = [];
        public int ReplaceCallCount { get; private set; }
        public int GetCurrentCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }
        public bool FailNextOperation { get; set; }
        public bool FailNextReplace { get; set; }
        public int? ReturnNullOnGetCall { get; set; }
        public bool KeepTaskOnRemove { get; set; }

        public Task<ScheduledTaskSummary?> GetCurrentAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfRequested();
            GetCurrentCallCount++;
            if (GetCurrentCallCount == ReturnNullOnGetCall)
                return Task.FromResult<ScheduledTaskSummary?>(null);
            Tasks.TryGetValue(TimerService.AppTaskName, out var task);
            return Task.FromResult(task);
        }

        public Task ReplaceAsync(
            ScheduledTaskSummary task,
            CancellationToken cancellationToken = default)
        {
            if (FailNextReplace)
            {
                FailNextReplace = false;
                throw new SchedulerException();
            }

            ThrowIfRequested();
            Tasks[task.TaskName] = task;
            ReplaceCallCount++;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string taskName, CancellationToken cancellationToken = default)
        {
            ThrowIfRequested();
            RemoveCallCount++;
            if (!KeepTaskOnRemove)
                Tasks.Remove(taskName);
            return Task.CompletedTask;
        }

        private void ThrowIfRequested()
        {
            if (!FailNextOperation)
                return;

            FailNextOperation = false;
            throw new SchedulerException();
        }
    }

    private sealed class SchedulerException : Exception;
}
