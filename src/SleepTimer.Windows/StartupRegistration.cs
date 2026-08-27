using Microsoft.Win32;

namespace SleepTimer.Windows;

public interface IStartupValueStore
{
    string? GetValue(string valueName);

    void SetValue(string valueName, string value);

    void DeleteValue(string valueName);
}

public interface IStartupRegistration
{
    bool IsEnabled(string applicationPath);

    void SetEnabled(string applicationPath, bool enabled, bool silent);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "SleepTimer.App";

    private readonly IStartupValueStore _store;

    public WindowsStartupRegistration()
        : this(new RegistryStartupValueStore())
    {
    }

    public WindowsStartupRegistration(IStartupValueStore store)
    {
        _store = store;
    }

    public bool IsEnabled(string applicationPath)
    {
        var command = _store.GetValue(ValueName);
        return string.Equals(command, StartupCommand.Build(applicationPath, silent: false), StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, StartupCommand.Build(applicationPath, silent: true), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(string applicationPath, bool enabled, bool silent)
    {
        if (!enabled)
        {
            _store.DeleteValue(ValueName);
            return;
        }

        _store.SetValue(ValueName, StartupCommand.Build(applicationPath, silent));
    }
}

public static class StartupCommand
{
    public static string Build(string applicationPath, bool silent)
    {
        if (string.IsNullOrWhiteSpace(applicationPath) || applicationPath.Contains('"'))
            throw new ArgumentException("Application path must be a valid non-empty path.", nameof(applicationPath));

        return $"\"{applicationPath}\"{(silent ? " --silent" : string.Empty)}";
    }
}

internal sealed class RegistryStartupValueStore : IStartupValueStore
{
    public string? GetValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistration.RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void SetValue(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(WindowsStartupRegistration.RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current-user startup registry key.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistration.RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
