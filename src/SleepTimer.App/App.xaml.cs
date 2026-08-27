using System.Threading;
using System.IO;
using SleepTimer.Core;
using SleepTimer.Windows;

namespace SleepTimer.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(System.Windows.StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteStartupError(args.Exception);
            args.Handled = true;
            System.Windows.MessageBox.Show(
                $"程序启动失败，诊断日志已保存到程序目录 data\\startup-error.log。\n\n{args.Exception.Message}",
                "睡眠关机定时器",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        };

        try
        {
            if (TryReadExecutionAction(eventArgs.Args, out var action))
            {
                try { await new WindowsPowerExecutor().ExecuteAsync(action, CancellationToken.None); }
                finally { Shutdown(); }
                return;
            }

            var startsSilently = IsSilentLaunch(eventArgs.Args);

            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SleepTimer.App.Activate");
            _singleInstanceMutex = new Mutex(true, @"Local\SleepTimer.App", out var isFirstInstance);
            if (!isFirstInstance)
            {
                _activationEvent.Set();
                Shutdown();
                return;
            }

            var applicationPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to determine the application path.");
            var timerService = AppComposition.CreateTimerService(applicationPath, AppContext.BaseDirectory);
            _mainWindow = new MainWindow(
                timerService,
                startupManager: new WindowsStartupRegistration(),
                applicationPath: applicationPath);
            MainWindow = _mainWindow;
            if (startsSilently)
            {
                _mainWindow.Opacity = 0;
                _mainWindow.Show();
                _mainWindow.Hide();
                _mainWindow.Opacity = 1;
            }
            else
            {
                _mainWindow.Show();
            }
            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, _) => Dispatcher.BeginInvoke(_mainWindow.ActivateFromExternalInstance),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception exception)
        {
            WriteStartupError(exception);
            System.Windows.MessageBox.Show(
                $"程序启动失败，诊断日志已保存到程序目录 data\\startup-error.log。\n\n{exception.Message}",
                "睡眠关机定时器",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs eventArgs)
    {
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        _mainWindow?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(eventArgs);
    }

    public static bool TryReadExecutionAction(string[] args, out TimerAction action)
    {
        action = default;
        if (args.Length != 4 || !string.Equals(args[0], "--execute", StringComparison.OrdinalIgnoreCase) || !string.Equals(args[2], "--authorization", StringComparison.OrdinalIgnoreCase))
            return false;

        var validAction = args[1].ToLowerInvariant() switch
        {
            "shutdown" => SetAction(TimerAction.Shutdown, out action),
            "force-shutdown" => SetAction(TimerAction.ForceShutdown, out action),
            "sleep" => SetAction(TimerAction.Sleep, out action),
            _ => false
        };
        return validAction && TryConsumeExecutionAuthorization(args[3]);
    }

    public static bool IsSilentLaunch(IEnumerable<string> args) =>
        args.Any(argument => string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase));

    private static bool SetAction(TimerAction value, out TimerAction action)
    {
        action = value;
        return true;
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDirectory);
            File.AppendAllText(
                Path.Combine(dataDirectory, "startup-error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\r\n");
        }
        catch
        {
        }
    }

    private static bool TryConsumeExecutionAuthorization(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32 || token.Any(character => !Uri.IsHexDigit(character))) return false;
        using var gate = new Mutex(false, @"Local\SleepTimer.ExecuteAuthorization");
        try
        {
            gate.WaitOne(TimeSpan.FromSeconds(2));
            var path = Path.Combine(AppContext.BaseDirectory, "data", "execution-authorization.token");
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path).Trim(), token, StringComparison.OrdinalIgnoreCase)) return false;
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { gate.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }
}
