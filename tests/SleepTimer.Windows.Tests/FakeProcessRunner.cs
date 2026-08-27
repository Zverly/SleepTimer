using Xunit;
using System.Diagnostics;
using SleepTimer.Windows;

namespace SleepTimer.Windows.Tests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessStartInfo> Calls { get; } = [];
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public Dictionary<string, string> CapturedFiles { get; } = [];
    public Queue<ProcessResult> Results { get; } = [];
    public Func<ProcessStartInfo, FakeProcessRunner, ProcessResult>? ResultFactory { get; set; }

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        Calls.Add(startInfo);
        if (startInfo.ArgumentList.Contains("/XML") && startInfo.ArgumentList.Count > 0)
        {
            var path = startInfo.ArgumentList[^1];
            if (File.Exists(path)) CapturedFiles[path] = File.ReadAllText(path);
        }
        if (Results.Count > 0) return Task.FromResult(Results.Dequeue());
        return Task.FromResult(ResultFactory?.Invoke(startInfo, this) ?? new ProcessResult(ExitCode, StandardOutput, StandardError));
    }
}
