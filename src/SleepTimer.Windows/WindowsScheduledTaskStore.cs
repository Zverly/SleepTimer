using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using SleepTimer.Core;

namespace SleepTimer.Windows;

public sealed class WindowsScheduledTaskStore : IScheduledTaskStore
{
    public const string AppTaskName = "SleepTimer.Current";
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly SemaphoreSlim TaskOperationGate = new(1, 1);
    private readonly string _applicationPath;
    private readonly IProcessRunner _processRunner;

    public WindowsScheduledTaskStore(string applicationPath, IProcessRunner? processRunner = null)
    {
        if (string.IsNullOrWhiteSpace(applicationPath)) throw new ArgumentException("An application path is required.", nameof(applicationPath));
        _applicationPath = Path.GetFullPath(applicationPath);
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public async Task ReplaceAsync(ScheduledTaskSummary task, CancellationToken cancellationToken)
    {
        EnsureApplicationTask(task.TaskName);
        await TaskOperationGate.WaitAsync(cancellationToken);
        try
        {
            await GetCurrentDefinitionAsync(cancellationToken);
            var authorization = Convert.ToHexString(Guid.NewGuid().ToByteArray());
            WriteExecutionAuthorization(authorization);
            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{AppTaskName}.{Guid.NewGuid():N}.xml");
            try
            {
                await File.WriteAllTextAsync(xmlPath, CreateTaskXml(task, authorization), cancellationToken);
                var startInfo = CreateSchtasksStartInfo();
                startInfo.ArgumentList.Add("/Create"); startInfo.ArgumentList.Add("/F"); startInfo.ArgumentList.Add("/TN"); startInfo.ArgumentList.Add(AppTaskName); startInfo.ArgumentList.Add("/XML"); startInfo.ArgumentList.Add(xmlPath);
                await EnsureSuccessAsync(startInfo, cancellationToken);
            }
            finally
            {
                try { File.Delete(xmlPath); }
                catch (Exception exception) { Trace.TraceWarning($"Unable to remove temporary task XML '{xmlPath}': {exception.Message}"); }
            }

            TaskDefinition? confirmed;
            try
            {
                confirmed = await GetCurrentDefinitionAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception.InnerException is null)
            {
                throw new InvalidOperationException($"The scheduled task was created, but final application definition confirmation failed: {exception.Message}", exception);
            }
            var expected = CreateExpectedDefinition(task, authorization);
            if (confirmed is null || !confirmed.Matches(expected))
                throw new InvalidOperationException($"The scheduled task was created, but its final application definition does not match the request (requested action '{task.Action}' at '{task.TargetTime:O}').");
        }
        finally
        {
            TaskOperationGate.Release();
        }
    }

    public async Task<ScheduledTaskSummary?> GetCurrentAsync(CancellationToken cancellationToken = default)
        => (await GetCurrentDefinitionAsync(cancellationToken))?.Summary;

    private async Task<TaskDefinition?> GetCurrentDefinitionAsync(CancellationToken cancellationToken)
    {
        var startInfo = CreateSchtasksStartInfo();
        startInfo.ArgumentList.Add("/Query"); startInfo.ArgumentList.Add("/TN"); startInfo.ArgumentList.Add(AppTaskName); startInfo.ArgumentList.Add("/XML"); startInfo.ArgumentList.Add("ONE");
        var result = await _processRunner.RunAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsMissingTaskResult(result)) return null;
            throw new InvalidOperationException($"Unable to query {AppTaskName}; schtasks.exe exited with code {result.ExitCode}.");
        }
        return ParseTaskXml(result.StandardOutput);
    }

    public async Task RemoveAsync(string taskName, CancellationToken cancellationToken)
    {
        EnsureApplicationTask(taskName);
        await TaskOperationGate.WaitAsync(cancellationToken);
        try
        {
            if (await GetCurrentDefinitionAsync(cancellationToken) is null) return;
            var startInfo = CreateSchtasksStartInfo();
            startInfo.ArgumentList.Add("/Delete"); startInfo.ArgumentList.Add("/F"); startInfo.ArgumentList.Add("/TN"); startInfo.ArgumentList.Add(AppTaskName);
            var result = await _processRunner.RunAsync(startInfo, cancellationToken);
            if (result.ExitCode == 0 || IsMissingTaskResult(result))
            {
                try
                {
                    if (await GetCurrentDefinitionAsync(cancellationToken) is not null)
                        throw new InvalidOperationException("The scheduled task deletion completed, but a task still exists after deletion; its definition was not removed.");
                }
                catch (Exception exception) when (exception is InvalidOperationException && exception.InnerException is null)
                {
                    throw new InvalidOperationException($"The scheduled task deletion completed, but confirmation failed after deletion: {exception.Message}", exception);
                }
                return;
            }
            throw new InvalidOperationException($"schtasks.exe exited with code {result.ExitCode} while deleting {AppTaskName}.");
        }
        finally
        {
            TaskOperationGate.Release();
        }
    }

    private async Task EnsureSuccessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException($"schtasks.exe exited with code {result.ExitCode}.");
    }

    private string CreateTaskXml(ScheduledTaskSummary task, string authorization)
    {
        XNamespace ns = TaskNamespace;
        var offset = TimeZoneInfo.Local.GetUtcOffset(task.TargetTime);
        var boundary = new DateTimeOffset(task.TargetTime, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var action = ToCommandValue(task.Action);
        var wake = task.Action != TimerAction.Sleep;
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(ns + "Task", new XAttribute("version", "1.4"),
            new XElement(ns + "RegistrationInfo", new XElement(ns + "URI", $"SleepTimer://{AppTaskName}")),
            new XElement(ns + "Triggers", new XElement(ns + "TimeTrigger", new XElement(ns + "StartBoundary", boundary), new XElement(ns + "Enabled", "true"))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"), new XElement(ns + "UserId", CurrentUserId), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"), new XElement(ns + "DisallowStartIfOnBatteries", "false"), new XElement(ns + "StopIfGoingOnBatteries", "false"), new XElement(ns + "WakeToRun", wake.ToString().ToLowerInvariant()), new XElement(ns + "ExecutionTimeLimit", "PT1H")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"), new XElement(ns + "Exec", new XElement(ns + "Command", _applicationPath), new XElement(ns + "Arguments", $"--execute {action} --authorization {authorization}")))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private TaskDefinition ParseTaskXml(string output)
    {
        try
        {
            XNamespace ns = TaskNamespace;
            var root = XDocument.Parse(output, LoadOptions.PreserveWhitespace).Root ?? throw new InvalidOperationException("The application task XML is incomplete.");
            var registration = root.Element(ns + "RegistrationInfo");
            var uriElements = registration?.Elements(ns + "URI").ToList() ?? [];
            var uri = uriElements.SingleOrDefault()?.Value.Trim();
            var triggerContainer = root.Element(ns + "Triggers");
            var actionContainer = root.Element(ns + "Actions");
            var triggers = triggerContainer?.Elements(ns + "TimeTrigger").ToList() ?? [];
            var actions = actionContainer?.Elements(ns + "Exec").ToList() ?? [];
            var principals = root.Element(ns + "Principals")?.Elements(ns + "Principal").ToList() ?? [];
            var principal = principals.SingleOrDefault();
            var exec = actions.SingleOrDefault();
            var command = exec?.Element(ns + "Command")?.Value.Trim().Trim('"');
            var arguments = exec?.Element(ns + "Arguments")?.Value.Trim();
            var startBoundary = triggers.SingleOrDefault()?.Element(ns + "StartBoundary")?.Value.Trim();
            var logonType = principal?.Element(ns + "LogonType")?.Value.Trim();
            var runLevel = principal?.Element(ns + "RunLevel")?.Value.Trim() ?? "";
            var principalUserId = principal?.Element(ns + "UserId")?.Value.Trim();
            var triggerEnabled = triggers.SingleOrDefault()?.Element(ns + "Enabled")?.Value.Trim() ?? "";
            var settings = root.Element(ns + "Settings");
            var settingsElements = settings?.Elements().ToList() ?? [];
            var version = root.Attribute("version")?.Value;
            var principalId = principal?.Attribute("id")?.Value.Trim();
            var normalizedUri = uri is $"\\{AppTaskName}" or $"SleepTimer://{AppTaskName}";
            var normalizedTrigger = triggerContainer?.Elements().Count() == 1 && triggers.Count == 1 && triggers[0].Elements().Count() is 1 or 2 && (triggerEnabled is "" or "true");
            var normalizedPrincipal = principals.Count == 1 && principal?.Elements().Count() is 2 or 3 && (principalId is null or "Author");
            var requiredSettings = settings is not null && settings.Element(ns + "MultipleInstancesPolicy")?.Value.Trim() == "IgnoreNew" && settings.Element(ns + "DisallowStartIfOnBatteries")?.Value.Trim() == "false" && settings.Element(ns + "StopIfGoingOnBatteries")?.Value.Trim() == "false" && settings.Element(ns + "ExecutionTimeLimit")?.Value.Trim() == "PT1H";
            var allowedSettings = new HashSet<string>(StringComparer.Ordinal) { "MultipleInstancesPolicy", "DisallowStartIfOnBatteries", "StopIfGoingOnBatteries", "ExecutionTimeLimit", "WakeToRun", "IdleSettings", "UseUnifiedSchedulingEngine" };
            var settingsAreKnown = settingsElements.Count >= 5 && settingsElements.Select(element => element.Name.LocalName).Distinct(StringComparer.Ordinal).Count() == settingsElements.Count && settingsElements.All(element => allowedSettings.Contains(element.Name.LocalName));
            if ((version is not null && version != "1.4") || root.Elements().Count() != 5 || registration?.Elements().Count() != 1 || !normalizedUri || !normalizedTrigger || !normalizedPrincipal || actionContainer?.Elements().Count() != 1 || actions.Count != 1 || exec?.Elements().Count() != 2 || !string.Equals(actionContainer?.Attribute("Context")?.Value, "Author", StringComparison.Ordinal) || principalUserId != CurrentUserId || logonType != "InteractiveToken" || (runLevel is not ("" or "LeastPrivilege")) || !requiredSettings || !settingsAreKnown || string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(arguments) || string.IsNullOrWhiteSpace(startBoundary)) throw new InvalidOperationException("The current task does not belong to this application.");
            if (!string.Equals(Path.GetFullPath(command), _applicationPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The current task does not belong to this application.");
            var argumentParts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (argumentParts.Length is not (2 or 4) || argumentParts[0] != "--execute" || (argumentParts.Length == 4 && (argumentParts[2] != "--authorization" || !IsExecutionAuthorization(argumentParts[3])))) throw new InvalidOperationException("The current task contains an unknown command.");
            var action = argumentParts[1] switch { "shutdown" => TimerAction.Shutdown, "force-shutdown" => TimerAction.ForceShutdown, "sleep" => TimerAction.Sleep, _ => throw new InvalidOperationException("The current task contains an unknown command.") };
            var wakeToRun = settings!.Element(ns + "WakeToRun")?.Value.Trim() ?? "";
            if (wakeToRun != (action == TimerAction.Sleep ? "false" : "true")) throw new InvalidOperationException("The current task does not belong to this application.");
            var targetTime = DateTimeOffset.ParseExact(startBoundary, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None).ToLocalTime().DateTime;
            return new TaskDefinition(new ScheduledTaskSummary(AppTaskName, action, targetTime), version ?? "", uri!, principalUserId!, logonType!, runLevel, triggerEnabled, actionContainer!.Attribute("Context")!.Value, command!, arguments!, wakeToRun, settingsElements.Select(element => $"{element.Name.LocalName}={element.Value}").ToArray());
        }
        catch (FormatException exception) { throw new InvalidOperationException("The application task contains an invalid start time.", exception); }
        catch (XmlException exception) { throw new InvalidOperationException("The application task XML is invalid.", exception); }
    }

    private static ProcessStartInfo CreateSchtasksStartInfo() => new("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true };
    private static string CurrentUserId => WindowsIdentity.GetCurrent().User?.Value ?? "S-1-5-18";
    private static void WriteExecutionAuthorization(string authorization)
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "execution-authorization.token"), authorization);
    }
    private TaskDefinition CreateExpectedDefinition(ScheduledTaskSummary task, string authorization)
    {
        var action = ToCommandValue(task.Action);
        return new TaskDefinition(task, "1.4", $"SleepTimer://{AppTaskName}", CurrentUserId, "InteractiveToken", "LeastPrivilege", "true", "Author", _applicationPath, $"--execute {action} --authorization {authorization}", task.Action == TimerAction.Sleep ? "false" : "true", ["MultipleInstancesPolicy=IgnoreNew", "DisallowStartIfOnBatteries=false", "StopIfGoingOnBatteries=false", $"WakeToRun={(task.Action == TimerAction.Sleep ? "false" : "true")}", "ExecutionTimeLimit=PT1H"]);
    }
    private static string ToCommandValue(TimerAction action) => action switch { TimerAction.Shutdown => "shutdown", TimerAction.ForceShutdown => "force-shutdown", TimerAction.Sleep => "sleep", _ => throw new ArgumentOutOfRangeException(nameof(action), action, null) };
    private static void EnsureApplicationTask(string taskName) { if (!string.Equals(taskName, AppTaskName, StringComparison.Ordinal)) throw new InvalidOperationException($"Only the {AppTaskName} task may be changed."); }
    private static bool IsMissingTaskResult(ProcessResult result) { if (result.ExitCode != 1) return false; var error = result.StandardError + " " + result.StandardOutput; return error.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || error.Contains("not found", StringComparison.OrdinalIgnoreCase) || error.Contains("cannot find", StringComparison.OrdinalIgnoreCase) || error.Contains("找不到", StringComparison.Ordinal) || error.Contains("不存在", StringComparison.Ordinal); }

    private sealed record TaskDefinition(ScheduledTaskSummary Summary, string Version, string Uri, string UserId, string LogonType, string RunLevel, string TriggerEnabled, string ActionsContext, string Command, string Arguments, string WakeToRun, string[] Settings)
    {
        public bool Matches(TaskDefinition expected) => Summary == expected.Summary && (Version == expected.Version || Version.Length == 0) && UserId == expected.UserId && LogonType == expected.LogonType && ActionsContext == expected.ActionsContext && string.Equals(Command, expected.Command, StringComparison.OrdinalIgnoreCase) && WakeToRun == expected.WakeToRun && Arguments == expected.Arguments;
    }

    private static bool IsExecutionAuthorization(string token) => token.Length == 32 && token.All(Uri.IsHexDigit);
}
