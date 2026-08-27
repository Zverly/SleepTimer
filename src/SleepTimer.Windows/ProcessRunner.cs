using System.Diagnostics;

namespace SleepTimer.Windows;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly TimeSpan _timeout;

    public ProcessRunner(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Unable to start {startInfo.FileName}.");
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(linked.Token);
            var standardError = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Terminate(process);
            throw new TimeoutException($"Process '{startInfo.FileName}' exceeded the {_timeout.TotalSeconds:0.#}-second timeout.");
        }
        catch (OperationCanceledException)
        {
            Terminate(process);
            throw;
        }
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
