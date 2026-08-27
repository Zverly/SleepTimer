using SleepTimer.Core;
using SleepTimer.Windows;

namespace SleepTimer.App;

public static class AppComposition
{
    public static TimerService CreateTimerService(
        string applicationPath,
        string portableDirectory,
        IScheduledTaskStore? taskStore = null,
        Func<DateTime>? now = null)
    {
        var stateStore = new StateStore(portableDirectory);
        var historyStore = new HistoryStore(stateStore);
        var logger = new FileAppLogger(portableDirectory);
        return new TimerService(
            taskStore ?? new WindowsScheduledTaskStore(applicationPath),
            now,
            historyStore,
            logger);
    }
}
