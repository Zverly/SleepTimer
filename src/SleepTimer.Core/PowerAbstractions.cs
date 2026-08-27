namespace SleepTimer.Core;

public enum TimerAction
{
    Shutdown,
    ForceShutdown,
    Sleep
}

public interface IPowerExecutor
{
    Task ExecuteAsync(
        TimerAction action,
        CancellationToken cancellationToken = default);
}
