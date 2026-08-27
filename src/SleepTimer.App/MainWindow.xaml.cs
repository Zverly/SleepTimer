using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Forms = System.Windows.Forms;
using SleepTimer.Core;
using SleepTimer.Windows;

namespace SleepTimer.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly TimerService _timerService;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _taskSyncTimer;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly StateStore _stateStore;
    private readonly IStartupRegistration _startupManager;
    private readonly string _applicationPath;
    private readonly Func<Task<ScheduledTaskSummary?>>? _existingTaskReader;
    private readonly Forms.ToolStripMenuItem _trayStatusItem;
    private readonly Forms.ToolStripMenuItem _trayAddThirtyItem;
    private readonly Forms.ToolStripMenuItem _traySubtractThirtyItem;
    private readonly Forms.ToolStripMenuItem _trayCancelItem;
    private readonly Forms.ToolStripMenuItem _trayQuickSetupMenu;
    private readonly Forms.ToolStripMenuItem _trayQuickShutdownMenu;
    private readonly Forms.ToolStripMenuItem _trayQuickSleepMenu;
    private readonly Forms.ToolStripMenuItem _trayQuickForceShutdownMenu;
    private DateTime? _presetTarget;
    private bool _allowClose;
    private bool _hideToTrayOnClose = true;
    private AppPreferences _preferences = AppPreferences.Default;
    private ScheduledTaskSummary? _restoredTaskForDisplay;
    private readonly HashSet<string> _shownReminderKeys = new(StringComparer.Ordinal);
    private string? _reminderTaskKey;
    private Controls.Button[] _presetButtons = [];

    public MainWindow(
        TimerService timerService,
        StateStore? stateStore = null,
        Func<Task<ScheduledTaskSummary?>>? existingTaskReader = null,
        IStartupRegistration? startupManager = null,
        string? applicationPath = null)
    {
        InitializeComponent();
        _timerService = timerService;
        _stateStore = stateStore ?? new StateStore(AppContext.BaseDirectory);
        _startupManager = startupManager ?? new WindowsStartupRegistration();
        _applicationPath = applicationPath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the application path.");
        _existingTaskReader = existingTaskReader;
        TargetTimeText.Text = DateTime.Now.AddHours(1).ToString("HH:mm", CultureInfo.CurrentCulture);
        TargetTimeText.TextChanged += TargetTimeText_TextChanged;
        Closing += MainWindow_Closing;

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => RefreshCountdown();
        _taskSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _taskSyncTimer.Tick += async (_, _) => await SynchronizeTaskAsync(showError: true);

        _trayStatusItem = new Forms.ToolStripMenuItem("当前未安排任务") { Enabled = false };
        _trayAddThirtyItem = new Forms.ToolStripMenuItem(AppPresentation.CommandLabel(TimerCommand.AddThirtyMinutes)) { Enabled = false };
        _traySubtractThirtyItem = new Forms.ToolStripMenuItem(AppPresentation.CommandLabel(TimerCommand.SubtractThirtyMinutes)) { Enabled = false };
        _trayCancelItem = new Forms.ToolStripMenuItem(AppPresentation.CommandLabel(TimerCommand.Cancel)) { Enabled = false };
        _trayQuickSetupMenu = new Forms.ToolStripMenuItem("快速设置");
        _trayQuickShutdownMenu = new Forms.ToolStripMenuItem("关机");
        _trayQuickSleepMenu = new Forms.ToolStripMenuItem("睡眠");
        _trayQuickForceShutdownMenu = new Forms.ToolStripMenuItem("强制关机（需确认）");
        _trayQuickSetupMenu.DropDownItems.Add(_trayQuickShutdownMenu);
        _trayQuickSetupMenu.DropDownItems.Add(_trayQuickSleepMenu);
        _trayQuickSetupMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        _trayQuickSetupMenu.DropDownItems.Add(_trayQuickForceShutdownMenu);
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "睡眠关机定时器",
            Visible = true,
            ContextMenuStrip = CreateTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    public void Dispose()
    {
        _countdownTimer.Stop();
        _taskSyncTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("/SleepTimer.App;component/Assets/sleep-timer-icon.ico", UriKind.Relative));
            if (resource is not null)
            {
                using (resource.Stream)
                    return new System.Drawing.Icon(resource.Stream);
            }
        }
        catch
        {
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void ActivateFromExternalInstance() => ShowFromTray();

    public void RestoreExistingTask(ScheduledTaskSummary task)
    {
        _restoredTaskForDisplay = task;
        Dispatcher.BeginInvoke(() =>
        {
            ShowCountdown();
            CountdownStatusText.Text = "已恢复任务摘要；系统任务仍是最终执行依据。";
        });
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_trayStatusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(AppPresentation.CommandLabel(TimerCommand.ShowWindow), null, (_, _) => Dispatcher.BeginInvoke(ShowFromTray));
        _trayAddThirtyItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await AdjustFromTrayAsync(TimeSpan.FromMinutes(30), "已增加 30 分钟")));
        _traySubtractThirtyItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await AdjustFromTrayAsync(TimeSpan.FromMinutes(-30), "已减少 30 分钟")));
        _trayCancelItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await CancelFromTrayAsync()));
        menu.Items.Add(_trayAddThirtyItem);
        menu.Items.Add(_traySubtractThirtyItem);
        menu.Items.Add(_trayCancelItem);
        menu.Items.Add(_trayQuickSetupMenu);
        menu.Items.Add(AppPresentation.CommandLabel(TimerCommand.OpenSettings), null, (_, _) => Dispatcher.BeginInvoke(OpenSettingsFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(AppPresentation.CommandLabel(TimerCommand.ExitKeepingPlan), null, (_, _) => Dispatcher.BeginInvoke(new Action(async () => await ExitFromTrayAsync(cancelPlan: false))));
        menu.Items.Add(AppPresentation.CommandLabel(TimerCommand.ExitAndCancelPlan), null, (_, _) => Dispatcher.BeginInvoke(new Action(async () => await ExitFromTrayAsync(cancelPlan: true))));
        return menu;
    }

    private void UpdateQuickSetupMenu()
    {
        var options = AppPresentation.GetQuickSetupOptions(_preferences.PresetMinutes);
        ConfigureQuickSetupGroup(_trayQuickShutdownMenu, options, TimerAction.Shutdown);
        ConfigureQuickSetupGroup(_trayQuickSleepMenu, options, TimerAction.Sleep);
        ConfigureQuickSetupGroup(_trayQuickForceShutdownMenu, options, TimerAction.ForceShutdown);
    }

    private void ConfigureQuickSetupGroup(
        Forms.ToolStripMenuItem group,
        IReadOnlyList<QuickSetupOption> options,
        TimerAction action)
    {
        group.DropDownItems.Clear();
        foreach (var option in options.Where(option => option.Action == action))
        {
            var item = new Forms.ToolStripMenuItem($"{option.Minutes} 分钟");
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await StartQuickSetupAsync(option)));
            group.DropDownItems.Add(item);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            _preferences = await _stateStore.LoadPreferencesAsync(AppPreferences.Default);
            try
            {
                var startupEnabled = _startupManager.IsEnabled(_applicationPath);
                _preferences = _preferences with
                {
                    LaunchAtStartup = startupEnabled,
                    SilentStartup = startupEnabled && _preferences.SilentStartup
                };
            }
            catch
            {
            }
            ApplyPreferences();
            await SynchronizeTaskAsync(showError: true);
        }
        catch (Exception exception)
        {
            SetHomeStatus($"偏好读取失败，已使用默认设置：{exception.Message}", isError: true);
        }
    }

    private void ApplyPreferences()
    {
        _preferences = _preferences with { PresetMinutes = AppPresentation.NormalizePresets(_preferences.PresetMinutes) };
        UpdateQuickSetupMenu();
        var action = _preferences.RememberSelection
            ? AppPresentation.ParseAction(_preferences.LastAction)
            : TimerAction.Shutdown;
        ShutdownRadio.IsChecked = action != TimerAction.Sleep;
        SleepRadio.IsChecked = action == TimerAction.Sleep;
        ForceShutdownCheck.IsChecked = _preferences.RememberSelection && action == TimerAction.ForceShutdown;
        _hideToTrayOnClose = _preferences.HideToTrayOnClose;
        var preset = _preferences.RememberSelection && _preferences.LastPresetMinutes > 0 ? _preferences.LastPresetMinutes : 60;
        var rawTarget = DateTime.Now.AddMinutes(preset);
        _presetTarget = new DateTime(rawTarget.Year, rawTarget.Month, rawTarget.Day, rawTarget.Hour, rawTarget.Minute, 0);
        if (rawTarget.Second != 0 || rawTarget.Millisecond != 0)
            _presetTarget = _presetTarget.Value.AddMinutes(1);
        TargetTimeText.Text = _presetTarget.Value.ToString("HH:mm", CultureInfo.CurrentCulture);
        _presetTarget = new DateTime(rawTarget.Year, rawTarget.Month, rawTarget.Day, rawTarget.Hour, rawTarget.Minute, 0);
        if (rawTarget.Second != 0 || rawTarget.Millisecond != 0)
            _presetTarget = _presetTarget.Value.AddMinutes(1);
        _presetButtons = [Preset30, Preset60, Preset90, Preset120];
        for (var index = 0; index < _presetButtons.Length; index++)
        {
            var minutes = _preferences.PresetMinutes[index];
            _presetButtons[index].Tag = minutes.ToString(CultureInfo.InvariantCulture);
            _presetButtons[index].Content = $"{minutes} 分钟";
        }
        UpdatePresetSelection(preset);
        HomeStatusText.Text = $"已准备好，默认安排在约 {preset} 分钟后。";
    }

    private async Task SavePreferencesAsync()
    {
        var action = SleepRadio.IsChecked == true
            ? "Sleep"
            : ForceShutdownCheck.IsChecked == true ? "ForceShutdown" : "Shutdown";
        _preferences = _preferences with { HideToTrayOnClose = _hideToTrayOnClose };
        if (_preferences.RememberSelection)
            _preferences = _preferences with
            {
                LastAction = action,
                LastPresetMinutes = _presetTarget is not null ? Math.Max(1, (int)Math.Round((_presetTarget.Value - DateTime.Now).TotalMinutes)) : _preferences.LastPresetMinutes
            };
        await _stateStore.SavePreferencesAsync(_preferences);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async Task AdjustFromTrayAsync(TimeSpan delta, string successMessage)
    {
        if (!await SynchronizeTaskAsync(showError: true) || !HasActiveTask)
        {
            ShowTrayNotice("当前没有安排中的任务。", "没有可调整的计划。");
            UpdateTrayStatus();
            return;
        }

        try
        {
            var result = await _timerService.AdjustWithResultAsync(delta);
            SetCountdownStatus(AppPresentation.AdjustmentSuccessMessage(result, DateTime.Now), isError: false);
            RefreshCountdown();
            UpdateTrayStatus();
        }
        catch (Exception exception)
        {
            ShowTrayError("调整失败", exception.Message);
        }
    }

    private async Task CancelFromTrayAsync()
    {
        if (!await SynchronizeTaskAsync(showError: true) || !HasActiveTask)
        {
            ShowTrayNotice("当前没有安排中的任务。", "没有可取消的计划。");
            UpdateTrayStatus();
            return;
        }

        try
        {
            await _timerService.CancelAsync();
            ReturnToHome("计划已取消。", isError: false);
        }
        catch (Exception exception)
        {
            ShowTrayError("取消失败", exception.Message);
        }
    }

    private async Task ExitFromTrayAsync(bool cancelPlan)
    {
        if (cancelPlan)
        {
            try
            {
                await _timerService.CancelAsync();
            }
            catch (Exception exception)
            {
                ShowTrayError("取消并退出失败", exception.Message);
                return;
            }
        }

        _allowClose = true;
        Close();
        System.Windows.Application.Current?.Shutdown();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowClose)
            return;

        if (!_hideToTrayOnClose)
        {
            eventArgs.Cancel = true;
            _allowClose = true;
            System.Windows.Application.Current?.Shutdown();
            return;
        }

        eventArgs.Cancel = true;
        Hide();
        _notifyIcon.ShowBalloonTip(1500, "计划仍在运行", "窗口已隐藏，系统任务不会取消。", Forms.ToolTipIcon.Info);
    }

    private void ActionRadio_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (ForceShutdownCheck is null || ShutdownRadio is null)
            return;

        ForceShutdownCheck.IsEnabled = ShutdownRadio.IsChecked == true;
        if (!ForceShutdownCheck.IsEnabled)
            ForceShutdownCheck.IsChecked = false;
    }

    private void ForceShutdownCheck_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (ForceShutdownCheck.IsChecked == true && IsLoaded)
            SetHomeStatus("已启用强制关机，请在开始前确认风险。", isError: true);
    }

    private void Preset_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string minutesText } ||
            !int.TryParse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            return;

        var rawTarget = DateTime.Now.AddMinutes(minutes);
        var target = new DateTime(rawTarget.Year, rawTarget.Month, rawTarget.Day, rawTarget.Hour, rawTarget.Minute, 0);
        if (rawTarget.Second != 0 || rawTarget.Millisecond != 0)
            target = target.AddMinutes(1);
        TargetTimeText.Text = target.ToString("HH:mm", CultureInfo.CurrentCulture);
        _presetTarget = target;
        UpdatePresetSelection(minutes);
        SetHomeStatus($"将在约 {minutes} 分钟后执行。", isError: false);
    }

    private void TargetTimeText_TextChanged(object sender, Controls.TextChangedEventArgs eventArgs) => _presetTarget = null;

    private async void Start_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!await SynchronizeTaskAsync(showError: true))
            return;

        if (!TryGetTargetTime(out var targetTime))
        {
            SetHomeStatus("请输入有效时间，例如 23:30。", isError: true);
            return;
        }

        if (targetTime.Date > DateTime.Today)
        {
            SetHomeStatus($"目标时间：明天 {targetTime:HH:mm}", isError: false);
            if (System.Windows.MessageBox.Show($"目标时间为明天 {targetTime:HH:mm}，确定安排到明天吗？", "确认次日计划", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                SetHomeStatus("已取消次日计划。", isError: false);
                return;
            }
        }

        var action = SleepRadio.IsChecked == true
            ? TimerAction.Sleep
            : ForceShutdownCheck.IsChecked == true ? TimerAction.ForceShutdown : TimerAction.Shutdown;
        await StartTimerAsync(action, targetTime);
    }

    private async Task StartQuickSetupAsync(QuickSetupOption option)
    {
        if (!await SynchronizeTaskAsync(showError: true))
            return;

        if (HasActiveTask)
        {
            ShowTrayNotice("已有计划", "当前已有计划，请先取消后再创建新的计划。");
            return;
        }

        var rawTarget = DateTime.Now.AddMinutes(option.Minutes);
        var targetTime = TimerCalculator.NormalizeTargetToMinute(rawTarget);
        await StartTimerAsync(option.Action, targetTime, fromTray: true);
    }

    private async Task StartTimerAsync(TimerAction action, DateTime targetTime, bool fromTray = false)
    {
        if (action == TimerAction.ForceShutdown &&
            System.Windows.MessageBox.Show("强制关机会关闭阻止关机的应用，可能导致未保存内容丢失。\n\n确定要继续吗？", "确认强制关机", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            if (fromTray)
                ShowTrayNotice("已取消", "未创建强制关机计划。");
            else
                SetHomeStatus("已取消强制关机。", isError: false);
            return;
        }

        try
        {
            var message = "正在创建 Windows 计划任务…";
            if (fromTray)
                ShowTrayNotice("正在创建计划", $"正在安排{AppPresentation.ActionLabel(action)}，{targetTime:HH:mm} 执行。");
            else
                SetHomeStatus(message, isError: false);
            await _timerService.StartAsync(action, targetTime);
            if (_preferences.RememberSelection)
            {
                _preferences = _preferences with
                {
                    LastAction = action.ToString(),
                    LastPresetMinutes = Math.Max(1, (int)Math.Round((targetTime - DateTime.Now).TotalMinutes))
                };
            }
            await SavePreferencesAsync();
            ShowCountdown();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("active", StringComparison.OrdinalIgnoreCase))
        {
            ShowTrayNotice("已有计划", "当前已有计划，请先取消后再创建新的计划。");
        }
        catch (Exception exception)
        {
            if (fromTray)
                ShowTrayError("创建失败", exception.Message);
            else
                SetHomeStatus($"创建失败：{exception.Message} 请检查权限后重试。", isError: true);
        }
    }

    private async void Extend_Click(object sender, RoutedEventArgs eventArgs)
    {
        await AdjustTimerAsync(TimeSpan.FromMinutes(30), "已增加 30 分钟");
    }

    private async void Reduce_Click(object sender, RoutedEventArgs eventArgs)
    {
        await AdjustTimerAsync(TimeSpan.FromMinutes(-30), "已减少 30 分钟");
    }

    private async Task AdjustTimerAsync(TimeSpan delta, string successMessage)
    {
        if (!await SynchronizeTaskAsync(showError: true) || !HasActiveTask)
        {
            SetCountdownStatus("当前没有安排中的任务。", isError: true);
            return;
        }

        try
        {
            var result = await _timerService.AdjustWithResultAsync(delta);
            SetCountdownStatus(AppPresentation.AdjustmentSuccessMessage(result, DateTime.Now), isError: false);
            RefreshCountdown();
            UpdateTrayStatus();
        }
        catch (Exception exception)
        {
            SetCountdownStatus($"调整失败：{exception.Message}", isError: true);
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!await SynchronizeTaskAsync(showError: true) || !HasActiveTask)
        {
            SetCountdownStatus("当前没有安排中的任务。", isError: true);
            return;
        }

        try
        {
            await _timerService.CancelAsync();
            ReturnToHome("计划已取消。", isError: false);
        }
        catch (Exception exception)
        {
            SetCountdownStatus($"取消失败：{exception.Message}", isError: true);
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs eventArgs) => Hide();

    private void ShowCountdown()
    {
        var task = CurrentDisplayTask;
        if (task is null)
            return;

        ActionText.Text = AppPresentation.ActionLabel(task.Action);
        HomePanel.Visibility = Visibility.Collapsed;
        CountdownPanel.Visibility = Visibility.Visible;
        RefreshCountdown();
        UpdateTrayStatus();
        _countdownTimer.Start();
        _taskSyncTimer.Start();
    }

    private ScheduledTaskSummary? CurrentDisplayTask => _timerService.CurrentTask ?? _restoredTaskForDisplay;

    private bool HasActiveTask => CurrentDisplayTask is not null;

    private void RefreshCountdown()
    {
        var task = CurrentDisplayTask;
        if (task is null)
            return;

        var remaining = task.TargetTime - DateTime.Now;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        RemainingText.Text = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}.{remaining:hh\\:mm\\:ss}"
            : remaining.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        TargetText.Text = $"目标时间：{task.TargetTime:yyyy-MM-dd HH:mm}";
        foreach (var key in AppPresentation.GetDueReminderKeys(task, DateTime.Now, _shownReminderKeys))
        {
            _shownReminderKeys.Add(key);
            var label = key switch { "10m" => "10 分钟", "1m" => "1 分钟", _ => "30 秒" };
            _notifyIcon.ShowBalloonTip(2500, "计划提醒", $"距离{AppPresentation.ActionLabel(task.Action)}还有 {label}。", Forms.ToolTipIcon.Info);
            if (key == "30s")
            {
                ShowFromTray();
                Topmost = true;
                Topmost = false;
            }
        }
        if (remaining == TimeSpan.Zero)
            SetCountdownStatus("系统任务即将执行。", isError: false);
    }

    private void UpdateTrayStatus()
    {
        var task = CurrentDisplayTask;
        if (task is null)
        {
            ReduceButton.IsEnabled = false;
            _trayStatusItem.Text = "当前未安排任务";
            _trayAddThirtyItem.Enabled = AppPresentation.IsCommandEnabled(TimerCommand.AddThirtyMinutes, hasActiveTimer: false);
            _traySubtractThirtyItem.Enabled = AppPresentation.IsCommandEnabled(TimerCommand.SubtractThirtyMinutes, hasActiveTimer: false);
            _trayCancelItem.Enabled = AppPresentation.IsCommandEnabled(TimerCommand.Cancel, hasActiveTimer: false);
            return;
        }
        _trayStatusItem.Text = $"{AppPresentation.ActionLabel(task.Action)} · {task.TargetTime:MM-dd HH:mm}";
        _trayAddThirtyItem.Enabled = AppPresentation.IsCommandEnabled(TimerCommand.AddThirtyMinutes, hasActiveTimer: true);
        _traySubtractThirtyItem.Enabled = AppPresentation.IsCommandEnabled(TimerCommand.SubtractThirtyMinutes, hasActiveTimer: true)
            && task.TargetTime - DateTime.Now >= TimeSpan.FromMinutes(32);
        ReduceButton.IsEnabled = task.TargetTime - DateTime.Now >= TimeSpan.FromMinutes(32);
        _trayCancelItem.Enabled = AppPresentation.IsCommandEnabled(TimerCommand.Cancel, hasActiveTimer: true);
    }

    private void TryReturnToHome()
    {
        _countdownTimer.Stop();
        CountdownPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
        _restoredTaskForDisplay = null;
        _taskSyncTimer.Stop();
        UpdateTrayStatus();
    }

    private void ReturnToHome(string message, bool isError)
    {
        TryReturnToHome();
        SetHomeStatus(message, isError);
    }

    private bool TryGetTargetTime(out DateTime targetTime)
    {
        if (_presetTarget is { } presetTarget && presetTarget > DateTime.Now)
        {
            targetTime = presetTarget;
            return true;
        }

        var formats = new[] { "H:mm", "HH:mm" };
        if (!DateTime.TryParseExact(TargetTimeText.Text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            targetTime = default;
            return false;
        }
        targetTime = DateTime.Today.Add(parsed.TimeOfDay);
        if (targetTime <= DateTime.Now)
            targetTime = targetTime.AddDays(1);
        return true;
    }

    private async Task<bool> SynchronizeTaskAsync(bool showError)
    {
        try
        {
            ScheduledTaskSummary? task;
            if (_existingTaskReader is not null)
                task = await _existingTaskReader();
            else
                task = await _timerService.RestoreAsync();

            var taskKey = task is null ? null : $"{task.TaskName}|{task.Action}|{task.TargetTime:O}";
            if (!string.Equals(_reminderTaskKey, taskKey, StringComparison.Ordinal))
            {
                _shownReminderKeys.Clear();
                _reminderTaskKey = taskKey;
            }
            _restoredTaskForDisplay = _existingTaskReader is not null ? task : null;
            if (task is null)
            {
                if (CountdownPanel.Visibility == Visibility.Visible)
                    TryReturnToHome();
                UpdateTrayStatus();
            }
            else
            {
                ShowCountdown();
            }
            return true;
        }
        catch (Exception exception)
        {
            if (showError)
            {
                var message = $"无法读取 Windows 计划任务：{exception.Message}";
                if (CountdownPanel.Visibility == Visibility.Visible)
                    SetCountdownStatus(message, isError: true);
                else
                    SetHomeStatus(message, isError: true);
                _notifyIcon.ShowBalloonTip(2500, "计划状态读取失败", message, Forms.ToolTipIcon.Error);
            }
            return false;
        }
    }

    private void UpdatePresetSelection(int selectedMinutes)
    {
        foreach (var button in _presetButtons)
            button.FontWeight = string.Equals(button.Tag?.ToString(), selectedMinutes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                ? FontWeights.Bold : FontWeights.Normal;
    }

    private void SetHomeStatus(string message, bool isError)
    {
        HomeStatusText.Text = message;
        HomeStatusText.Foreground = (Media.Brush)FindResource(isError ? "DangerBrush" : "WarningBrush");
    }

    private void SetCountdownStatus(string message, bool isError)
    {
        CountdownStatusText.Text = message;
        CountdownStatusText.Foreground = (Media.Brush)FindResource(isError ? "DangerBrush" : "SuccessBrush");
    }

    private void OpenSettingsFromTray()
    {
        ShowFromTray();
        Settings_Click(this, new RoutedEventArgs());
    }

    private void Settings_Click(object sender, RoutedEventArgs eventArgs)
    {
        OverlayTitle.Text = "设置";
        OverlayContent.Children.Clear();
        var remember = new Controls.CheckBox { Content = "记住上次动作和预设", IsChecked = _preferences.RememberSelection, Margin = new Thickness(0, 0, 0, 16) };
        var hide = new Controls.CheckBox { Content = "关闭窗口时隐藏到托盘", IsChecked = _hideToTrayOnClose, Margin = new Thickness(0, 0, 0, 16) };
        var launchAtStartup = new Controls.CheckBox { Content = "开机自启动", IsChecked = _preferences.LaunchAtStartup, Margin = new Thickness(0, 0, 0, 8) };
        var silentStartup = new Controls.CheckBox { Content = "静默启动（启动后隐藏到托盘）", IsChecked = _preferences.SilentStartup, IsEnabled = launchAtStartup.IsChecked == true, Margin = new Thickness(0, 0, 0, 4) };
        launchAtStartup.Checked += (_, _) => silentStartup.IsEnabled = true;
        launchAtStartup.Unchecked += (_, _) =>
        {
            silentStartup.IsChecked = false;
            silentStartup.IsEnabled = false;
        };
        OverlayContent.Children.Add(remember);
        OverlayContent.Children.Add(hide);
        OverlayContent.Children.Add(launchAtStartup);
        OverlayContent.Children.Add(silentStartup);
        AddOverlayText("静默启动仅对开机自启动生效，手动打开程序仍会显示窗口。", false);
        AddOverlayText("快速预设（分钟，1–1440）", false);
        var presetEditors = _preferences.PresetMinutes.Select(minutes => new Controls.TextBox
        {
            Text = minutes.ToString(CultureInfo.InvariantCulture),
            Width = 90,
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        }).ToArray();
        foreach (var editor in presetEditors)
            OverlayContent.Children.Add(editor);
        AddOverlayText("设置会保存在程序目录的 data 文件夹中，不使用用户配置目录。", false);
        OverlayPrimaryButton.Content = "保存设置";
        OverlayPrimaryButton.Click -= SaveSettings_Click;
        OverlayPrimaryButton.Click -= CloseOverlay_Click;
        OverlayPrimaryButton.Click += SaveSettings_Click;
        OverlayPrimaryButton.Tag = (remember, hide, launchAtStartup, silentStartup, presetEditors);
        OverlayPanel.Visibility = Visibility.Visible;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (OverlayPrimaryButton.Tag is ValueTuple<Controls.CheckBox, Controls.CheckBox, Controls.CheckBox, Controls.CheckBox, Controls.TextBox[]> controls)
        {
            var values = controls.Item5.Select(editor => int.TryParse(editor.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0).ToArray();
            var normalized = AppPresentation.NormalizePresets(values);
            if (!values.SequenceEqual(normalized))
            {
                AddOverlayText("预设必须是 4 个不重复的整数，范围为 1–1440 分钟。", true);
                return;
            }
            var previousPreferences = _preferences;
            var previousHideToTray = _hideToTrayOnClose;
            var launchAtStartup = controls.Item3.IsChecked == true;
            var silentStartup = launchAtStartup && controls.Item4.IsChecked == true;
            try
            {
                _startupManager.SetEnabled(_applicationPath, launchAtStartup, silentStartup);
                _preferences = _preferences with
                {
                    RememberSelection = controls.Item1.IsChecked == true,
                    PresetMinutes = normalized,
                    LaunchAtStartup = launchAtStartup,
                    SilentStartup = silentStartup
                };
                _hideToTrayOnClose = controls.Item2.IsChecked == true;
                ApplyPreferences();
                await SavePreferencesAsync();
            }
            catch (Exception exception)
            {
                try
                {
                    _startupManager.SetEnabled(_applicationPath, previousPreferences.LaunchAtStartup, previousPreferences.SilentStartup);
                }
                catch
                {
                }
                _preferences = previousPreferences;
                _hideToTrayOnClose = previousHideToTray;
                ApplyPreferences();
                AddOverlayText($"启动设置保存失败：{exception.Message}", true);
                return;
            }
        }
        CloseOverlay_Click(sender, eventArgs);
    }

    private void About_Click(object sender, RoutedEventArgs eventArgs)
    {
        ShowInfoOverlay("关于", "睡眠关机定时器", "版本 1.0 · Windows 10 / 11 · x64\n\n一个离线、便携的睡前计时工具。任务交给 Windows 计划任务执行，关闭窗口不会取消已创建的计划。\n\n不联网、不收集数据。", "知道了");
    }

    private void Help_Click(object sender, RoutedEventArgs eventArgs)
    {
        ShowInfoOverlay("使用帮助", "三步开始", "1  选择关机或睡眠\n2  点击一个常用时长，或输入 HH:mm\n3  点击开始倒计时\n\n计时中可以增加或减少 30 分钟、取消计划，或隐藏到托盘。正常关机不会自动变成强制关机。\n\n如果创建失败，请检查 Windows 计划任务权限；真实关机和睡眠请先保存所有文件。", "知道了");
    }

    private void ShowInfoOverlay(string title, string heading, string body, string action)
    {
        OverlayTitle.Text = title;
        OverlayContent.Children.Clear();
        OverlayContent.Children.Add(new Controls.TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        AddOverlayText(body, false);
        OverlayPrimaryButton.Content = action;
        OverlayPrimaryButton.Tag = null;
        OverlayPrimaryButton.Click -= SaveSettings_Click;
        OverlayPrimaryButton.Click -= CloseOverlay_Click;
        OverlayPrimaryButton.Click += CloseOverlay_Click;
        OverlayPanel.Visibility = Visibility.Visible;
    }

    private void AddOverlayText(string text, bool isError)
    {
        OverlayContent.Children.Add(new Controls.TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
            Foreground = (Media.Brush)FindResource(isError ? "DangerBrush" : "MutedBrush")
        });
    }

    private async void BackToHome_Click(object sender, RoutedEventArgs eventArgs)
    {
        await SynchronizeTaskAsync(showError: true);
        TryReturnToHome();
        if (HasActiveTask)
            SetHomeStatus("计划仍在运行；返回设置不会取消系统任务。", isError: false);
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs eventArgs) => OverlayPanel.Visibility = Visibility.Collapsed;

    private void ShowTrayError(string title, string message)
    {
        SetCountdownStatus($"{title}：{message}", isError: true);
        _notifyIcon.ShowBalloonTip(2500, title, message, Forms.ToolTipIcon.Error);
    }

    private void ShowTrayNotice(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(2000, title, message, Forms.ToolTipIcon.Info);
    }
}
