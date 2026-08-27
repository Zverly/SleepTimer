using Xunit;
using SleepTimer.Core;

namespace SleepTimer.Windows.Tests;

public sealed class WindowsScheduledTaskStoreTests
{
    [Fact]
    public async Task ReplaceAsync_CreatesOnlyTheFixedApplicationTask()
    {
        var runner = new FakeProcessRunner();
        var target = new DateTime(2026, 8, 27, 23, 45, 0);
        runner.Results.Enqueue(new ProcessResult(1, string.Empty, MissingTaskError));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.ResultFactory = (startInfo, fakeRunner) => new ProcessResult(0, TaskXml(@"E:\Sleep Timer\SleepTimer.App.exe", "sleep", target, ExtractAuthorization(fakeRunner)), string.Empty);
        var store = new WindowsScheduledTaskStore(@"E:\Sleep Timer\SleepTimer.App.exe", runner);

        await store.ReplaceAsync(
            new ScheduledTaskSummary(WindowsScheduledTaskStore.AppTaskName, TimerAction.Sleep, target),
            CancellationToken.None);

        Assert.Equal(3, runner.Calls.Count);
        var createCall = runner.Calls.Single(item => item.ArgumentList.Contains("/Create"));
        Assert.Equal("schtasks.exe", createCall.FileName);
        Assert.Equal(["/Create", "/F", "/TN", "SleepTimer.Current", "/XML"], createCall.ArgumentList.Take(5));
        Assert.EndsWith(".xml", createCall.ArgumentList[^1], StringComparison.OrdinalIgnoreCase);
        var xml = runner.CapturedFiles[createCall.ArgumentList[^1]];
        Assert.Contains("<StartBoundary>2026-08-27T23:45:00+08:00</StartBoundary>", xml);
        Assert.Contains("<URI>SleepTimer://SleepTimer.Current</URI>", xml);
        Assert.Contains("<Command>E:\\Sleep Timer\\SleepTimer.App.exe</Command>", xml);
        Assert.Matches("<Arguments>--execute sleep --authorization [0-9A-F]{32}</Arguments>", xml);
        Assert.Contains("<WakeToRun>false</WakeToRun>", xml);
    }

    [Fact]
    public async Task RemoveAsync_RejectsAnyOtherTaskNameWithoutStartingAProcess()
    {
        var runner = new FakeProcessRunner();
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RemoveAsync("Some.Other.Task", CancellationToken.None));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheFixedApplicationTask()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, TaskXml(@"E:\SleepTimer.App.exe", "shutdown"), string.Empty));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.Results.Enqueue(new ProcessResult(1, string.Empty, MissingTaskError));
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await store.RemoveAsync(WindowsScheduledTaskStore.AppTaskName, CancellationToken.None);

        Assert.Equal(["/Query", "/TN", "SleepTimer.Current", "/XML", "ONE"], runner.Calls[0].ArgumentList);
        Assert.Equal(["/Delete", "/F", "/TN", "SleepTimer.Current"], runner.Calls[1].ArgumentList);
    }

    [Fact]
    public async Task RemoveAsync_WhenTaskIsMissing_IsSuccessful()
    {
        var runner = new FakeProcessRunner
        {
            ExitCode = 1,
            StandardError = "ERROR: The system cannot find the file specified."
        };
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await store.RemoveAsync(WindowsScheduledTaskStore.AppTaskName, CancellationToken.None);

        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task GetCurrentAsync_QueriesAndParsesAnAppOwnedTask()
    {
        var runner = new FakeProcessRunner
        {
            StandardOutput = TaskXml(@"""E:\Sleep Timer\SleepTimer.App.exe""", "sleep")
        };
        var store = new WindowsScheduledTaskStore(@"E:\Sleep Timer\SleepTimer.App.exe", runner);

        var task = await store.GetCurrentAsync();

        Assert.Equal(
            new ScheduledTaskSummary(
                WindowsScheduledTaskStore.AppTaskName,
                TimerAction.Sleep,
                new DateTime(2026, 8, 27, 23, 45, 0)),
            task);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(["/Query", "/TN", "SleepTimer.Current", "/XML", "ONE"], call.ArgumentList);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenTaskIsMissing_ReturnsNull()
    {
        var runner = new FakeProcessRunner
        {
            ExitCode = 1,
            StandardError = "ERROR: The system cannot find the file specified."
        };
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        var task = await store.GetCurrentAsync();

        Assert.Null(task);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenQueryFails_ThrowsInsteadOfReportingMissing()
    {
        var runner = new FakeProcessRunner
        {
            ExitCode = 1,
            StandardError = "ERROR: Access is denied."
        };
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCurrentAsync());
    }

    [Fact]
    public async Task GetCurrentAsync_RejectsTaskPointingToAnotherExecutable()
    {
        var runner = new FakeProcessRunner
        {
            StandardOutput = TaskXml(@"E:\Other\Other.exe", "shutdown")
        };
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCurrentAsync());
    }

    [Fact]
    public async Task GetCurrentAsync_AcceptsWindowsNormalizedTaskXml()
    {
        var runner = new FakeProcessRunner
        {
            StandardOutput = WindowsNormalizedTaskXml(@"E:\SleepTimer.App.exe", "shutdown")
        };
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        var task = await store.GetCurrentAsync();

        Assert.Equal(TimerAction.Shutdown, task?.Action);
        Assert.Equal(new DateTime(2026, 8, 27, 15, 50, 0), task?.TargetTime);
    }

    [Fact]
    public async Task ReplaceAsync_AcceptsWindowsNormalizedFinalDefinition()
    {
        var target = new DateTime(2026, 8, 27, 15, 50, 0);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(1, string.Empty, MissingTaskError));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.ResultFactory = (startInfo, fakeRunner) => new ProcessResult(0, WindowsNormalizedTaskXml(@"E:\SleepTimer.App.exe", "shutdown", ExtractAuthorization(fakeRunner)), string.Empty);
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await store.ReplaceAsync(new ScheduledTaskSummary(WindowsScheduledTaskStore.AppTaskName, TimerAction.Shutdown, target), CancellationToken.None);

        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task ReplaceAsync_RejectsForeignExistingTaskBeforeCreate()
    {
        var runner = new FakeProcessRunner { StandardOutput = TaskXml(@"E:\Other\Other.exe", "shutdown") };
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceAsync(
            new ScheduledTaskSummary(WindowsScheduledTaskStore.AppTaskName, TimerAction.Shutdown, DateTime.Now.AddHours(1)),
            CancellationToken.None));

        Assert.Single(runner.Calls);
        Assert.DoesNotContain(runner.Calls, call => call.ArgumentList.Contains("/Create"));
    }

    [Fact]
    public async Task ReplaceAsync_UsesWakeToRunOnlyForShutdown()
    {
        var target = new DateTime(2027, 1, 2, 12, 34, 0);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(1, string.Empty, MissingTaskError));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.ResultFactory = (startInfo, fakeRunner) => new ProcessResult(0, TaskXml(@"E:\SleepTimer.App.exe", "shutdown", target, ExtractAuthorization(fakeRunner)), string.Empty);
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await store.ReplaceAsync(new ScheduledTaskSummary(WindowsScheduledTaskStore.AppTaskName, TimerAction.Shutdown, target), CancellationToken.None);

        var createPath = runner.Calls.Single(call => call.ArgumentList.Contains("/Create")).ArgumentList[^1];
        var xml = runner.CapturedFiles[createPath];
        Assert.Contains("<WakeToRun>true</WakeToRun>", xml);
    }

    [Fact]
    public async Task ReplaceAsync_RejectsWhenCreatedTaskDoesNotMatchRequest()
    {
        var target = new DateTime(2026, 8, 27, 23, 45, 0);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(1, string.Empty, MissingTaskError));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.ResultFactory = (startInfo, fakeRunner) => new ProcessResult(0, TaskXml(@"E:\SleepTimer.App.exe", "sleep", target, ExtractAuthorization(fakeRunner)), string.Empty);
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceAsync(new ScheduledTaskSummary(WindowsScheduledTaskStore.AppTaskName, TimerAction.Shutdown, target), CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceAsync_RejectsWhenCreatedTaskContainsUnexpectedSettings()
    {
        var target = new DateTime(2026, 8, 27, 23, 45, 0);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(1, string.Empty, MissingTaskError));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.ResultFactory = (startInfo, fakeRunner) => new ProcessResult(0, TaskXml(@"E:\SleepTimer.App.exe", "shutdown", target, ExtractAuthorization(fakeRunner)).Replace(
            "<ExecutionTimeLimit>PT1H</ExecutionTimeLimit>",
            "<ExecutionTimeLimit>PT1H</ExecutionTimeLimit><AllowHardTerminate>false</AllowHardTerminate>"), string.Empty);
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceAsync(
            new ScheduledTaskSummary(WindowsScheduledTaskStore.AppTaskName, TimerAction.Shutdown, target),
            CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_RejectsWhenTaskRemainsWithForeignDefinition()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new ProcessResult(0, TaskXml(@"E:\SleepTimer.App.exe", "shutdown"), string.Empty));
        runner.Results.Enqueue(new ProcessResult(0, string.Empty, string.Empty));
        runner.Results.Enqueue(new ProcessResult(0, TaskXml(@"E:\Other\Other.exe", "shutdown"), string.Empty));
        var store = new WindowsScheduledTaskStore(@"E:\SleepTimer.App.exe", runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RemoveAsync(
            WindowsScheduledTaskStore.AppTaskName, CancellationToken.None));

        Assert.Contains("after deletion", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string MissingTaskError = "ERROR: The system cannot find the file specified.";

    private static string TaskXml(string command, string action, DateTime? target = null, string? authorization = null) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><URI>SleepTimer://SleepTimer.Current</URI></RegistrationInfo>
          <Triggers>
            <TimeTrigger>
            <StartBoundary>{FormatBoundary(target ?? new DateTime(2026, 8, 27, 23, 45, 0))}</StartBoundary>
            <Enabled>true</Enabled>
            </TimeTrigger>
          </Triggers>
          <Principals><Principal id="Author"><UserId>{CurrentUserId}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
          <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><WakeToRun>{(action == "sleep" ? "false" : "true")}</WakeToRun><ExecutionTimeLimit>PT1H</ExecutionTimeLimit></Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{command}</Command>
              <Arguments>--execute {action}{(authorization is null ? string.Empty : $" --authorization {authorization}")}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    private static string WindowsNormalizedTaskXml(string command, string action, string authorization = "0123456789ABCDEF0123456789ABCDEF") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><URI>\SleepTimer.Current</URI></RegistrationInfo>
          <Triggers><TimeTrigger><StartBoundary>2026-08-27T15:50:00+08:00</StartBoundary></TimeTrigger></Triggers>
          <Principals><Principal><UserId>{CurrentUserId}</UserId><LogonType>InteractiveToken</LogonType></Principal></Principals>
          <Settings>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <WakeToRun>true</WakeToRun>
            <IdleSettings />
            <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
          </Settings>
          <Actions Context="Author"><Exec><Command>{command}</Command><Arguments>--execute {action} --authorization {authorization}</Arguments></Exec></Actions>
        </Task>
        """;

    private static string ExtractAuthorization(FakeProcessRunner runner)
    {
        var xml = runner.CapturedFiles.Values.Single();
        var start = xml.IndexOf("--authorization ", StringComparison.Ordinal) + "--authorization ".Length;
        return xml.Substring(start, 32);
    }

    private static string CurrentUserId => System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "S-1-5-18";
    private static string FormatBoundary(DateTime target) => new DateTimeOffset(target, TimeZoneInfo.Local.GetUtcOffset(target)).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
}
