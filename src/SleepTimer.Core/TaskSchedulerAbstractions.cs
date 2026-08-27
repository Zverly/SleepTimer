namespace SleepTimer.Core;

public sealed record ScheduledTaskSummary(
    string TaskName,
    TimerAction Action,
    DateTime TargetTime);

public interface IScheduledTaskStore
{
    Task<ScheduledTaskSummary?> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        ScheduledTaskSummary task,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string taskName,
        CancellationToken cancellationToken = default);
}
