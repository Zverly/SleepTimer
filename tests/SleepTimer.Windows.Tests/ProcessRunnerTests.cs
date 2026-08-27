using System.Diagnostics;
using Xunit;

namespace SleepTimer.Windows.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CancellationTerminatesTheChildProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var runner = new ProcessRunner(TimeSpan.FromSeconds(5));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(startInfo, cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_EnforcesBoundedTimeout()
    {
        var runner = new ProcessRunner(TimeSpan.FromMilliseconds(150));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" }
        };

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(startInfo, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_CancellationStillReportsCancellationWhenKillRacesWithExit()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var runner = new ProcessRunner(TimeSpan.FromSeconds(5));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Milliseconds 500" }
        };

        var exception = await Record.ExceptionAsync(() => runner.RunAsync(startInfo, cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }
}
