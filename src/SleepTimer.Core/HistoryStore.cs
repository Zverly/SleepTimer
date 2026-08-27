namespace SleepTimer.Core;

public enum TimerEventType
{
    Created,
    Delayed,
    Cancelled,
    CreationFailed,
    DelayFailed,
    CancellationFailed,
    ExecutionFailed
}

public sealed record TimerHistoryEntry(
    DateTimeOffset OccurredAt,
    TimerEventType EventType,
    TimerAction Action,
    DateTime TargetTime,
    DateTime? PreviousTargetTime = null,
    TimeSpan? Extension = null,
    string? ErrorCode = null);

public interface IHistoryStore
{
    Task<IReadOnlyList<TimerHistoryEntry>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task AppendAsync(
        TimerHistoryEntry entry,
        CancellationToken cancellationToken = default);
}

public sealed class HistoryStore : IHistoryStore
{
    private readonly SemaphoreSlim _appendLock = new(1, 1);
    private readonly StateStore _stateStore;
    private readonly int _maxEntries;

    public HistoryStore(string portableDirectory, int maxEntries = 200)
        : this(new StateStore(portableDirectory), maxEntries)
    {
    }

    public HistoryStore(StateStore stateStore, int maxEntries = 200)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        _stateStore = stateStore;
        _maxEntries = maxEntries;
    }

    public Task<IReadOnlyList<TimerHistoryEntry>> LoadAsync(
        CancellationToken cancellationToken = default) =>
        _stateStore.LoadHistoryAsync(cancellationToken);

    public async Task AppendAsync(
        TimerHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        await _appendLock.WaitAsync(cancellationToken);
        try
        {
            var history = (await _stateStore.LoadHistoryAsync(cancellationToken)).ToList();
            history.Add(entry);
            if (history.Count > _maxEntries)
                history.RemoveRange(0, history.Count - _maxEntries);

            await _stateStore.SaveHistoryAsync(history, cancellationToken);
        }
        finally
        {
            _appendLock.Release();
        }
    }
}
