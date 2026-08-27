using System.Globalization;
using SleepTimer.Core;

namespace SleepTimer.App;

public sealed record AppPreferences(
    string LastAction,
    int LastPresetMinutes,
    bool RememberSelection,
    bool HideToTrayOnClose,
    bool LaunchAtStartup,
    bool SilentStartup)
{
    public static AppPreferences Default { get; } = new("Shutdown", 60, true, true, false, false);

    public IReadOnlyList<int> PresetMinutes { get; init; } = AppPresentation.DefaultPresets;
}

public sealed record QuickSetupOption(TimerAction Action, int Minutes);

public enum TimerCommand
{
    AddThirtyMinutes,
    SubtractThirtyMinutes,
    Cancel,
    HideToTray,
    ShowWindow,
    OpenSettings,
    ExitKeepingPlan,
    ExitAndCancelPlan
}

public static class AppPresentation
{
    public static IReadOnlyList<int> DefaultPresets { get; } = [30, 60, 90, 120];
    public static IReadOnlyList<TimeSpan> ReminderThresholds { get; } =
        [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30)];

    public static IReadOnlyList<QuickSetupOption> GetQuickSetupOptions(IEnumerable<int>? presets)
    {
        var normalized = NormalizePresets(presets);
        return new[] { TimerAction.Shutdown, TimerAction.Sleep, TimerAction.ForceShutdown }
            .SelectMany(action => normalized.Select(minutes => new QuickSetupOption(action, minutes)))
            .ToArray();
    }

    public static IReadOnlyList<int> NormalizePresets(IEnumerable<int>? values)
    {
        var normalized = values?.Take(4).ToArray() ?? [];
        if (normalized.Length != 4 || normalized.Any(value => value is < 1 or > 1440) || normalized.Distinct().Count() != 4)
            return DefaultPresets;
        return normalized;
    }

    public static string FormatTargetTime(DateTime target, DateTime now) =>
        target.Date == now.Date ? target.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) :
        target.Date == now.Date.AddDays(1) ? $"明天 {target:HH:mm}" : target.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> GetDueReminderKeys(
        ScheduledTaskSummary task,
        DateTime now,
        IEnumerable<string> notifiedKeys)
    {
        var known = notifiedKeys.ToHashSet(StringComparer.Ordinal);
        var remaining = task.TargetTime - now;
        var result = new List<string>();
        foreach (var (threshold, key) in new[]
        {
            (TimeSpan.FromMinutes(10), "10m"),
            (TimeSpan.FromMinutes(1), "1m"),
            (TimeSpan.FromSeconds(30), "30s")
        })
        {
            if (remaining <= threshold && remaining > TimeSpan.Zero && known.Add(key))
                result.Add(key);
        }
        return result;
    }

    public static string AdjustmentSuccessMessage(TimerAdjustmentResult result, DateTime now) =>
        $"计划已调整，新的目标时间：{FormatTargetTime(result.ActualTargetTime, now)}";

    public static IReadOnlyList<TimerCommand> TimerCommands { get; } =
    [
        TimerCommand.AddThirtyMinutes,
        TimerCommand.SubtractThirtyMinutes,
        TimerCommand.Cancel,
        TimerCommand.HideToTray
    ];

    public static IReadOnlyList<TimerCommand> TrayCommands { get; } =
    [
        TimerCommand.ShowWindow,
        TimerCommand.AddThirtyMinutes,
        TimerCommand.SubtractThirtyMinutes,
        TimerCommand.Cancel,
        TimerCommand.OpenSettings,
        TimerCommand.ExitKeepingPlan,
        TimerCommand.ExitAndCancelPlan
    ];

    public static string CommandLabel(TimerCommand command) => command switch
    {
        TimerCommand.AddThirtyMinutes => "+30 分钟",
        TimerCommand.SubtractThirtyMinutes => "-30 分钟",
        TimerCommand.Cancel => "取消计划",
        TimerCommand.HideToTray => "隐藏到托盘",
        TimerCommand.ShowWindow => "显示窗口",
        TimerCommand.OpenSettings => "打开设置",
        TimerCommand.ExitKeepingPlan => "退出并保留计划",
        TimerCommand.ExitAndCancelPlan => "退出并取消计划",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    public static bool RequiresActiveTimer(TimerCommand command) => command switch
    {
        TimerCommand.AddThirtyMinutes or TimerCommand.SubtractThirtyMinutes or TimerCommand.Cancel => true,
        TimerCommand.HideToTray or TimerCommand.ShowWindow or TimerCommand.OpenSettings or
            TimerCommand.ExitKeepingPlan or TimerCommand.ExitAndCancelPlan => false,
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    public static bool IsCommandEnabled(TimerCommand command, bool hasActiveTimer) =>
        !RequiresActiveTimer(command) || hasActiveTimer;

    public static string ActionLabel(TimerAction action) => action switch
    {
        TimerAction.Shutdown => "关机",
        TimerAction.ForceShutdown => "强制关机",
        TimerAction.Sleep => "睡眠",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static string ActionDescription(TimerAction action) => action switch
    {
        TimerAction.Shutdown => "让未保存的工作有机会阻止关机",
        TimerAction.ForceShutdown => "关闭应用并立即关机，可能丢失未保存内容",
        TimerAction.Sleep => "让电脑进入睡眠，之后可快速恢复工作",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public static TimerAction ParseAction(string? value) => value switch
    {
        "Shutdown" => TimerAction.Shutdown,
        "ForceShutdown" => TimerAction.ForceShutdown,
        "Sleep" => TimerAction.Sleep,
        _ => TimerAction.Shutdown
    };
}
