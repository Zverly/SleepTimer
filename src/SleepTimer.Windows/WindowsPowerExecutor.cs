using System.Diagnostics;
using SleepTimer.Core;

namespace SleepTimer.Windows;

public sealed class WindowsPowerExecutor : IPowerExecutor
{
    private readonly IProcessRunner _processRunner;

    public WindowsPowerExecutor(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public async Task ExecuteAsync(TimerAction action, CancellationToken cancellationToken)
    {
        var startInfo = action switch
        {
            TimerAction.Shutdown => CreateStartInfo("shutdown.exe", "/s", "/t", "0"),
            TimerAction.ForceShutdown => CreateStartInfo("shutdown.exe", "/s", "/f", "/t", "0"),
            TimerAction.Sleep => CreateStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState", "0,1,0"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        var result = await _processRunner.RunAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Power command exited with code {result.ExitCode}.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
