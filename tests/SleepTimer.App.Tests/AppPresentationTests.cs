using SleepTimer.App;
using SleepTimer.Core;
using Xunit;
using System.Linq;

namespace SleepTimer.App.Tests;

public sealed class AppPresentationTests
{
    [Theory]
    [InlineData(TimerAction.Shutdown, "关机")]
    [InlineData(TimerAction.ForceShutdown, "强制关机")]
    [InlineData(TimerAction.Sleep, "睡眠")]
    public void ActionPresentation_UsesSafeChineseLabels(TimerAction action, string expected)
    {
        Assert.Equal(expected, AppPresentation.ActionLabel(action));
    }

    [Fact]
    public void DefaultPreferences_KeepThePresetFirstDefaults()
    {
        Assert.Equal("Shutdown", AppPreferences.Default.LastAction);
        Assert.Equal(60, AppPreferences.Default.LastPresetMinutes);
        Assert.True(AppPreferences.Default.RememberSelection);
        Assert.True(AppPreferences.Default.HideToTrayOnClose);
        Assert.False(AppPreferences.Default.LaunchAtStartup);
        Assert.False(AppPreferences.Default.SilentStartup);
    }

    [Fact]
    public void ActionPresentation_RejectsUnknownActions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AppPresentation.ActionLabel((TimerAction)999));
    }

    [Fact]
    public void TimerCommands_UseTheSameThirtyMinuteControlsEverywhere()
    {
        var labels = AppPresentation.TimerCommands
            .Where(command => AppPresentation.RequiresActiveTimer(command))
            .Select(AppPresentation.CommandLabel)
            .ToArray();

        Assert.Equal(new[] { "+30 分钟", "-30 分钟", "取消计划" }, labels);
    }

    [Fact]
    public void TimerCommands_AreDisabledWithoutAnActiveTimer()
    {
        var activeCommands = AppPresentation.TimerCommands
            .Where(command => AppPresentation.RequiresActiveTimer(command))
            .ToArray();

        Assert.NotEmpty(activeCommands);
        Assert.All(activeCommands, command => Assert.False(AppPresentation.IsCommandEnabled(command, hasActiveTimer: false)));
        Assert.All(activeCommands, command => Assert.True(AppPresentation.IsCommandEnabled(command, hasActiveTimer: true)));
    }

    [Fact]
    public void TrayCommands_OfferSettingsAndBothExitPolicies()
    {
        var labels = AppPresentation.TrayCommands
            .Select(AppPresentation.CommandLabel)
            .ToArray();

        Assert.Equal(
            new[] { "显示窗口", "+30 分钟", "-30 分钟", "取消计划", "打开设置", "退出并保留计划", "退出并取消计划" },
            labels);
    }

    [Fact]
    public void QuickSetupOptions_MapEveryPresetToTheThreeActions()
    {
        var options = AppPresentation.GetQuickSetupOptions([30, 60, 90, 120]);

        Assert.Equal(12, options.Count);
        Assert.Equal(new[] { TimerAction.Shutdown, TimerAction.Sleep, TimerAction.ForceShutdown }, options
            .Select(option => option.Action)
            .Distinct()
            .ToArray());
        Assert.All(options, option => Assert.Contains(option.Minutes, new[] { 30, 60, 90, 120 }));
        Assert.Equal(new[] { 30, 60, 90, 120 }, options
            .Where(option => option.Action == TimerAction.Sleep)
            .Select(option => option.Minutes)
            .ToArray());
    }

    [Fact]
    public void Preferences_DefaultToFourEditablePresets()
    {
        Assert.Equal(new[] { 30, 60, 90, 120 }, AppPreferences.Default.PresetMinutes);
        Assert.Equal(new[] { 30, 60, 90, 120 }, AppPresentation.NormalizePresets([30, 0, 90, 99999]));
    }

    [Fact]
    public void SpecificTarget_FormatsTomorrowExplicitly()
    {
        var target = new DateTime(2026, 8, 28, 0, 20, 0);

        Assert.Equal("明天 00:20", AppPresentation.FormatTargetTime(target, new DateTime(2026, 8, 27, 23, 50, 0)));
        Assert.Equal("2026-08-27 23:20", AppPresentation.FormatTargetTime(
            new DateTime(2026, 8, 27, 23, 20, 0), new DateTime(2026, 8, 27, 10, 0, 0)));
    }

    [Fact]
    public void Reminders_AreDueOncePerTaskAndThreshold()
    {
        var task = new ScheduledTaskSummary("SleepTimer.Current", TimerAction.Shutdown, new DateTime(2026, 8, 27, 10, 0, 0));
        var due = AppPresentation.GetDueReminderKeys(task, new DateTime(2026, 8, 27, 9, 50, 0), []);

        Assert.Equal(new[] { "10m" }, due);
        Assert.Empty(AppPresentation.GetDueReminderKeys(task, new DateTime(2026, 8, 27, 9, 49, 59), due));
        Assert.Equal(new[] { "1m", "30s" }, AppPresentation.GetDueReminderKeys(
            task, new DateTime(2026, 8, 27, 9, 59, 30), due));
    }

    [Fact]
    public void ExecutionArgumentsWithoutOneTimeAuthorizationAreRejected()
    {
        Assert.False(App.TryReadExecutionAction(["--execute", "force-shutdown"], out _));
    }

    [Theory]
    [InlineData(true, "--silent")]
    [InlineData(true, "--SILENT")]
    [InlineData(false, "--quiet")]
    public void StartupArguments_RecognizeSilentLaunchOnly(bool expected, string argument)
    {
        Assert.Equal(expected, App.IsSilentLaunch([argument]));
    }

    [Fact]
    public void WpfTheme_UsesLightCanvasAndReadableDarkCards()
    {
        var appXaml = File.ReadAllText(FindWorkspaceFile("src", "SleepTimer.App", "App.xaml"));
        var windowXaml = File.ReadAllText(FindWorkspaceFile("src", "SleepTimer.App", "MainWindow.xaml"));

        Assert.Contains("Color=\"#F3F5F8\"", appXaml);
        Assert.Contains("Color=\"#FFFFFF\"", appXaml);
        Assert.Contains("Color=\"#171B26\"", appXaml);
        Assert.Contains("Color=\"#7657E8\"", appXaml);
        Assert.Contains("x:Name=\"RemainingText\"", windowXaml);
        Assert.Contains("Foreground=\"{StaticResource CountdownTextBrush}\"", windowXaml);
        Assert.Contains("Style=\"{StaticResource CountdownCardStyle}\"", windowXaml);
        Assert.Contains("Background=\"{StaticResource CardBrush}\"", windowXaml);
    }

    [Fact]
    public void MainWindow_UsesFormalMoonClockIconForBrandAndCountdownStatus()
    {
        var windowXaml = File.ReadAllText(FindWorkspaceFile("src", "SleepTimer.App", "MainWindow.xaml"));

        Assert.DoesNotContain("Text=\"Z\"", windowXaml);
        Assert.DoesNotContain("<Image Source=\"Assets/sleep-timer-icon-approved.png\"", windowXaml);
        Assert.Contains("<Image Source=\"Assets/sleep-timer-icon-approved-circle.png\"", windowXaml);
        Assert.Equal(2, windowXaml.Split("Assets/sleep-timer-icon-approved-circle.png", StringSplitOptions.None).Length - 1);
        Assert.Contains("<Image Source=\"Assets/sleep-timer-icon-approved-circle.png\" Width=\"36\" Height=\"36\"", windowXaml);
        Assert.Contains("<Image Source=\"Assets/sleep-timer-icon-approved-circle.png\" Width=\"118\" Height=\"118\"", windowXaml);
        Assert.DoesNotContain("Background=\"{StaticResource AccentBrush}\" CornerRadius=\"10\"", windowXaml);
        Assert.DoesNotContain("Background=\"{StaticResource AccentSoftBrush}\" CornerRadius=\"59\"", windowXaml);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate the workspace file.", Path.Combine(parts));
    }
}
