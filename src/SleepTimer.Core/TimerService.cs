namespace SleepTimer.Core;

public sealed record TimerAdjustmentResult(
    ScheduledTaskSummary ActualTask,
    DateTime RequestedTargetTime,
    DateTime ActualTargetTime);

public sealed class TimerService
{
    public const string AppTaskName = "SleepTimer.Current";

    private readonly IScheduledTaskStore _taskStore;
    private readonly Func<DateTime> _now;
    private readonly IHistoryStore? _historyStore;
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public TimerService(
        IScheduledTaskStore taskStore,
        Func<DateTime>? now = null,
        IHistoryStore? historyStore = null,
        IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(taskStore);
        _taskStore = taskStore;
        _now = now ?? (() => DateTime.Now);
        _historyStore = historyStore;
        _logger = logger;
    }

    public ScheduledTaskSummary? CurrentTask { get; private set; }
    public IReadOnlyList<TimerHistoryEntry> History { get; private set; } = [];

    public async Task<IReadOnlyList<TimerHistoryEntry>> LoadHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        History = _historyStore is null
            ? []
            : await _historyStore.LoadAsync(cancellationToken);
        return History;
    }

    public async Task<ScheduledTaskSummary?> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var task = await _taskStore.GetCurrentAsync(cancellationToken);
            if (task is null)
            {
                CurrentTask = null;
                return null;
            }

            ValidateTask(task);
            if (task.TargetTime <= _now())
            {
                CurrentTask = null;
                return null;
            }

            CurrentTask = task;
            return task;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<ScheduledTaskSummary> StartAsync(
        TimerAction action,
        DateTime targetTime,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));
        var actualTargetTime = TimerCalculator.NormalizeTargetToMinute(targetTime);
        if (actualTargetTime <= _now())
            throw new ArgumentOutOfRangeException(nameof(targetTime), "Target time must be in the future.");

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var task = new ScheduledTaskSummary(AppTaskName, action, actualTargetTime);
            try
            {
                await _taskStore.ReplaceAsync(task, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await RecordAsync(
                    new TimerHistoryEntry(ToTimestamp(), TimerEventType.CreationFailed, action, actualTargetTime, ErrorCode: "scheduler-error"),
                    AppLogLevel.Error,
                    "timer.creation-failed",
                    "scheduler-error");
                throw;
            }

            CurrentTask = task;
            await RecordAsync(
                new TimerHistoryEntry(ToTimestamp(), TimerEventType.Created, action, targetTime),
                AppLogLevel.Information,
                "timer.created");
            return task;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<ScheduledTaskSummary> ExtendAsync(
        TimeSpan extension,
        CancellationToken cancellationToken = default)
    {
        if (extension <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(extension), "Extension must be positive.");

        return await AdjustAsync(extension, cancellationToken);
    }

    public async Task<ScheduledTaskSummary> AdjustAsync(
        TimeSpan delta,
        CancellationToken cancellationToken = default)
    {
        var result = await AdjustWithResultAsync(delta, cancellationToken);
        return result.ActualTask;
    }

    public async Task<TimerAdjustmentResult> AdjustWithResultAsync(
        TimeSpan delta,
        CancellationToken cancellationToken = default)
    {
        if (delta == TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta), "Adjustment must not be zero.");

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var currentTask = await _taskStore.GetCurrentAsync(cancellationToken);
            if (currentTask is null)
                throw new InvalidOperationException("There is no active timer to adjust.");

            ValidateTask(currentTask);

            DateTime targetTime;
            try
            {
                targetTime = currentTask.TargetTime.Add(delta);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), exception.Message);
            }

            var requestedTargetTime = targetTime;
            var actualTargetTime = TimerCalculator.NormalizeTargetToMinute(requestedTargetTime);
            var minimumTargetTime = TimerCalculator.NormalizeTargetToMinute(_now().AddMinutes(2));
            if (actualTargetTime < minimumTargetTime)
                throw new ArgumentOutOfRangeException(
                    nameof(delta),
                    "The adjusted target must remain at least two minutes in the future.");

            var replacement = currentTask with { TargetTime = actualTargetTime };
            try
            {
                await _taskStore.ReplaceAsync(replacement, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await RecordAsync(
                    new TimerHistoryEntry(
                        ToTimestamp(),
                        TimerEventType.DelayFailed,
                        currentTask.Action,
                        currentTask.TargetTime,
                        ErrorCode: "scheduler-error"),
                    AppLogLevel.Error,
                    "timer.delay-failed",
                    "scheduler-error");
                throw;
            }

            ScheduledTaskSummary? confirmedTask;
            try
            {
                confirmedTask = await _taskStore.GetCurrentAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await RecordAsync(
                    new TimerHistoryEntry(
                        ToTimestamp(),
                        TimerEventType.DelayFailed,
                        currentTask.Action,
                        currentTask.TargetTime,
                        ErrorCode: "confirmation-failed"),
                    AppLogLevel.Error,
                    "timer.delay-failed",
                    "confirmation-failed");
                throw;
            }

            if (confirmedTask is null || confirmedTask != replacement)
            {
                var exception = new InvalidOperationException("The adjusted task could not be confirmed.");
                await RecordAsync(
                    new TimerHistoryEntry(
                        ToTimestamp(),
                        TimerEventType.DelayFailed,
                        currentTask.Action,
                        currentTask.TargetTime,
                        ErrorCode: "confirmation-failed"),
                    AppLogLevel.Error,
                    "timer.delay-failed",
                    "confirmation-failed");
                throw exception;
            }

            CurrentTask = replacement;
            await RecordAsync(
                new TimerHistoryEntry(
                    ToTimestamp(),
                    TimerEventType.Delayed,
                    replacement.Action,
                    replacement.TargetTime,
                    currentTask.TargetTime,
                    delta),
                AppLogLevel.Information,
                "timer.delayed");
            return new TimerAdjustmentResult(
                replacement,
                requestedTargetTime,
                actualTargetTime);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var currentTask = await _taskStore.GetCurrentAsync(cancellationToken);
            if (currentTask is null)
            {
                CurrentTask = null;
                return;
            }

            ValidateTask(currentTask);
            try
            {
                await _taskStore.RemoveAsync(AppTaskName, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (currentTask is not null)
                {
                    await RecordAsync(
                        new TimerHistoryEntry(
                            ToTimestamp(),
                            TimerEventType.CancellationFailed,
                            currentTask.Action,
                            currentTask.TargetTime,
                            ErrorCode: "scheduler-error"),
                        AppLogLevel.Error,
                        "timer.cancellation-failed",
                        "scheduler-error");
                }

                throw;
            }

            ScheduledTaskSummary? confirmedTask;
            try
            {
                confirmedTask = await _taskStore.GetCurrentAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await RecordAsync(
                    new TimerHistoryEntry(
                        ToTimestamp(),
                        TimerEventType.CancellationFailed,
                        currentTask.Action,
                        currentTask.TargetTime,
                        ErrorCode: "confirmation-failed"),
                    AppLogLevel.Error,
                    "timer.cancellation-failed",
                    "confirmation-failed");
                throw;
            }

            if (confirmedTask is not null)
            {
                await RecordAsync(
                    new TimerHistoryEntry(
                        ToTimestamp(),
                        TimerEventType.CancellationFailed,
                        currentTask.Action,
                        currentTask.TargetTime,
                        ErrorCode: "confirmation-failed"),
                    AppLogLevel.Error,
                    "timer.cancellation-failed",
                    "confirmation-failed");
                throw new InvalidOperationException("The current task could not be cancelled.");
            }

            CurrentTask = null;
            if (currentTask is not null)
            {
                await RecordAsync(
                    new TimerHistoryEntry(
                        ToTimestamp(),
                        TimerEventType.Cancelled,
                        currentTask.Action,
                        currentTask.TargetTime),
                    AppLogLevel.Information,
                    "timer.cancelled");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task RecordExecutionFailureAsync(
        ScheduledTaskSummary task,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("An error code is required.", nameof(errorCode));

        return RecordAsync(
            new TimerHistoryEntry(
                ToTimestamp(),
                TimerEventType.ExecutionFailed,
                task.Action,
                task.TargetTime,
                ErrorCode: errorCode),
            AppLogLevel.Error,
            "timer.execution-failed",
            errorCode,
            cancellationToken);
    }

    private async Task RecordAsync(
        TimerHistoryEntry entry,
        AppLogLevel level,
        string eventCode,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        History = History.Append(entry).ToArray();
        if (_historyStore is not null)
        {
            try
            {
                await _historyStore.AppendAsync(entry, CancellationToken.None);
            }
            catch
            {
                await TryLogAsync(AppLogLevel.Error, "history.write-failed", "history-store-error");
            }
        }

        await TryLogAsync(level, eventCode, errorCode);
    }

    private async Task TryLogAsync(AppLogLevel level, string eventCode, string? errorCode)
    {
        if (_logger is null)
            return;

        try
        {
            await _logger.LogAsync(level, eventCode, errorCode, CancellationToken.None);
        }
        catch
        {
        }
    }

    private DateTimeOffset ToTimestamp()
    {
        var now = _now();
        try
        {
            return new DateTimeOffset(now);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        }
    }

    private static void ValidateTask(ScheduledTaskSummary task)
    {
        if (!string.Equals(task.TaskName, AppTaskName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Only the {AppTaskName} task may be restored.");
        if (!Enum.IsDefined(task.Action))
            throw new InvalidOperationException("The current task contains an unknown action.");
    }
}
