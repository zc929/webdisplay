using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WebDisplay.Models;
using WebDisplay.Services;
using Windows.System;

namespace WebDisplay;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettings _current;
    private readonly Window _owner;
    private readonly ThemeService _themeService;
    private readonly TaskCompletionSource<AppSettings?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _initializing = true;
    private bool _applying;
    private bool _shown;
    private bool _closed;
    private bool _appliedRestartEnabled;
    private string _appliedRestartTime;
    private string _appliedRestartDays;

    public AppSettings? Result { get; private set; }
    public event Action<string>? ThemePreviewChanged;

    public SettingsWindow(AppSettings current, Window owner)
    {
        _current = current.Clone();
        _current.ThemePreference = NormalizeTheme(current.ThemePreference);
        _owner = owner;
        _appliedRestartEnabled = current.RestartEnabled;
        _appliedRestartTime = current.RestartTime;
        _appliedRestartDays = current.RestartDays;
        InitializeComponent();
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), true);
        WindowInteropService.Initialize(this, 940, 800, owner);
        _themeService = new ThemeService(this, RootGrid, _current.ThemePreference);
        AppWindow.Closing += Window_Closing;
        Closed += Window_Closed;

        UrlBox.Text = current.Url;
        ZoomPercentBox.Text = current.ZoomPercent.ToString(CultureInfo.InvariantCulture);
        ShowScrollbarsToggle.IsOn = current.ShowScrollbars;
        RefreshToggle.IsOn = current.AutoRefreshEnabled;
        RefreshMinutesBox.Text = current.RefreshIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        TopmostToggle.IsOn = current.AlwaysOnTop;
        FullScreenToggle.IsOn = current.FullScreen;
        PreventSleepToggle.IsOn = current.PreventSleep;
        StartAtLogonToggle.IsOn = current.StartAtLogon;
        ThemePreferenceBox.SelectedIndex = _current.ThemePreference switch { "Light" => 1, "Dark" => 2, _ => 0 };
        UserNameBox.Text = Environment.UserName;
        DomainBox.Text = Environment.MachineName;
        SetRestartControls(current.RestartEnabled, current.RestartTime, current.RestartDays);
        ReadSystemStatus();
        UpdateControlAvailability();
        _initializing = false;
    }

    public Task<AppSettings?> ShowAsync()
    {
        if (_closed) return _completion.Task;
        if (!_shown)
        {
            _shown = true;
            WindowInteropService.SetEnabled(_owner, false);
            try
            {
                Activate();
                WindowInteropService.BringToFront(this);
            }
            catch
            {
                WindowInteropService.SetEnabled(_owner, true);
                _shown = false;
                throw;
            }
        }
        else
        {
            WindowInteropService.BringToFront(this);
        }
        return _completion.Task;
    }

    private CheckBox[] WeekdayChecks => new[]
    {
        MondayCheck, TuesdayCheck, WednesdayCheck, ThursdayCheck,
        FridayCheck, SaturdayCheck, SundayCheck
    };

    private static string NormalizeTheme(string? value) => value switch
    {
        "Light" => "Light", "Dark" => "Dark", _ => "System"
    };

    private string SelectedTheme => ThemePreferenceBox.SelectedItem is ComboBoxItem item
        ? NormalizeTheme(item.Tag as string) : "System";

    private void ThemePreference_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _closed) return;
        string preference = SelectedTheme;
        _themeService.SetPreference(preference);
        ThemePreviewChanged?.Invoke(preference);
    }

    private void ReadSystemStatus(bool populateInputs = true)
    {
        try
        {
            var status = WindowsIntegrationService.ReadStatus();
            if (populateInputs) StartAtLogonToggle.IsOn = status.StartAtLogon;
            _appliedRestartEnabled = status.RestartEnabled;
            _appliedRestartTime = status.RestartTime;
            _appliedRestartDays = status.RestartDays;
            if (populateInputs) SetRestartControls(status.RestartEnabled, status.RestartTime, status.RestartDays);
            RestartStatusText.Text = status.RestartEnabled
                ? $"当前计划：{DescribeDays(status.RestartDays)} {status.RestartTime} 重启。"
                : "当前未启用定时重启。";
            AutoLogonStatusText.Text = status.AutoLogonEnabled
                ? $"已启用 · {FormatAccount(status.AutoLogonDomain, status.AutoLogonUser)}"
                : "当前未启用 Windows 自动登录。";
            if (populateInputs && !string.IsNullOrWhiteSpace(status.AutoLogonUser)) UserNameBox.Text = status.AutoLogonUser;
            if (populateInputs && !string.IsNullOrWhiteSpace(status.AutoLogonDomain)) DomainBox.Text = status.AutoLogonDomain;
        }
        catch (Exception ex)
        {
            RestartStatusText.Text = "暂时无法读取系统设置：" + ex.Message;
            AutoLogonStatusText.Text = "自动登录状态读取失败。";
        }
    }

    private void SetRestartControls(bool enabled, string time, string days)
    {
        RestartToggle.IsOn = enabled;
        RestartTimeBox.Text = string.IsNullOrWhiteSpace(time) ? "03:00" : time;
        bool daily = string.IsNullOrWhiteSpace(days) || days.Equals("Daily", StringComparison.OrdinalIgnoreCase);
        DailyRadio.IsChecked = daily;
        WeeklyRadio.IsChecked = !daily;
        var selected = new HashSet<string>((days ?? "Daily").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        foreach (var check in WeekdayChecks) check.IsChecked = selected.Contains((string)check.Tag);
        UpdateControlAvailability();
    }

    private static string FormatAccount(string domain, string user) => string.IsNullOrWhiteSpace(domain) ? user : domain + "\\" + user;

    private static string DescribeDays(string days)
    {
        if (string.IsNullOrWhiteSpace(days) || days.Equals("Daily", StringComparison.OrdinalIgnoreCase)) return "每天";
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MON"] = "周一", ["TUE"] = "周二", ["WED"] = "周三", ["THU"] = "周四",
            ["FRI"] = "周五", ["SAT"] = "周六", ["SUN"] = "周日"
        };
        return string.Join("、", days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(day => names.TryGetValue(day, out var label) ? label : day));
    }

    private void UpdateControlAvailability()
    {
        // XAML can raise toggled/checked events before all named elements exist.
        if (RefreshMinutesBox is not null && RefreshToggle is not null) RefreshMinutesBox.IsEnabled = RefreshToggle.IsOn;
        bool restartEnabled = RestartToggle?.IsOn == true;
        if (RestartTimeBox is not null) RestartTimeBox.IsEnabled = restartEnabled;
        if (DailyRadio is not null) DailyRadio.IsEnabled = restartEnabled;
        if (WeeklyRadio is not null) WeeklyRadio.IsEnabled = restartEnabled;
        bool weekdaysEnabled = restartEnabled && WeeklyRadio?.IsChecked == true;
        foreach (var check in WeekdayChecks)
            if (check is not null) check.IsEnabled = weekdaysEnabled;
    }

    private void RefreshToggle_Toggled(object sender, RoutedEventArgs e) => UpdateControlAvailability();
    private void RestartToggle_Toggled(object sender, RoutedEventArgs e) => UpdateControlAvailability();
    private void Frequency_Changed(object sender, RoutedEventArgs e) => UpdateControlAvailability();

    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ResetZoomInput();

    internal string ZoomInputText
    {
        get => ZoomPercentBox.Text;
        set => ZoomPercentBox.Text = value;
    }

    internal void ResetZoomInput() => ZoomPercentBox.Text = "100";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_applying || _closed) return;
        var result = _current.Clone();
        result.Url = UrlBox.Text.Trim();
        if (!AppSettings.IsValidUrl(result.Url))
        {
            ShowValidation("请输入完整的 http:// 或 https:// 网页地址，网址中不要包含账号密码。", UrlBox);
            return;
        }
        if (!int.TryParse(ZoomPercentBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int zoomPercent) || zoomPercent < 25 || zoomPercent > 500)
        {
            ShowValidation("网页缩放必须是 25 到 500 之间的整数百分比。", ZoomPercentBox);
            return;
        }
        if (!int.TryParse(RefreshMinutesBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes < 1 || minutes > 10080)
        {
            ShowValidation("刷新间隔必须是 1 到 10080 之间的整数分钟。", RefreshMinutesBox);
            return;
        }
        result.AutoRefreshEnabled = RefreshToggle.IsOn;
        result.ZoomPercent = zoomPercent;
        result.ShowScrollbars = ShowScrollbarsToggle.IsOn;
        result.RefreshIntervalMinutes = minutes;
        result.AlwaysOnTop = TopmostToggle.IsOn;
        result.FullScreen = FullScreenToggle.IsOn;
        result.PreventSleep = PreventSleepToggle.IsOn;
        result.StartAtLogon = StartAtLogonToggle.IsOn;
        result.ThemePreference = SelectedTheme;
        // System settings take effect only through their explicit administrator actions.
        result.RestartEnabled = _appliedRestartEnabled;
        result.RestartTime = _appliedRestartTime;
        result.RestartDays = _appliedRestartDays;
        Result = result;
        AccountPasswordBox.Password = string.Empty;
        Close();
    }

    private void ShowValidation(string message, Control field)
    {
        ValidationText.Text = message;
        SettingsPivot.SelectedIndex = 0;
        field.StartBringIntoView();
        field.Focus(FocusState.Programmatic);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_applying) Close();
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        if (!_applying) Close();
        else ValidationText.Text = "请等待当前系统设置操作结束后再关闭窗口。";
    }

    private async void ApplyRestart_Click(object sender, RoutedEventArgs e)
    {
        if (_applying || _closed) return;
        bool enabled = RestartToggle.IsOn;
        string time = RestartTimeBox.Text.Trim();
        string days = DailyRadio.IsChecked == true ? "Daily" : string.Join(",", WeekdayChecks.Where(check => check.IsChecked == true).Select(check => (string)check.Tag));
        if (enabled && !DateTime.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            RestartStatusText.Text = "请输入有效的 24 小时制时间，例如 03:00 或 18:30。";
            RestartTimeBox.Focus(FocusState.Programmatic);
            return;
        }
        if (enabled && string.IsNullOrWhiteSpace(days))
        {
            RestartStatusText.Text = "每周计划请至少选择一个日期。";
            MondayCheck.Focus(FocusState.Programmatic);
            return;
        }
        if (!enabled)
        {
            time = _appliedRestartTime;
            days = _appliedRestartDays;
        }
        SetApplying(true);
        RestartStatusText.Text = "正在请求管理员授权并应用计划…";
        try
        {
            string message = await WindowsIntegrationService.ConfigureRestartAsync(enabled, time, days);
            _appliedRestartEnabled = enabled;
            _appliedRestartTime = time;
            _appliedRestartDays = days;
            RestartStatusText.Text = string.IsNullOrWhiteSpace(message)
                ? (enabled ? $"已应用：{DescribeDays(days)} {time} 重启。" : "已关闭定时重启。")
                : message;
        }
        catch (Exception ex)
        {
            ReadSystemStatus(populateInputs: false);
            RestartStatusText.Text = "操作未完成：" + ex.Message + "\n" + RestartStatusText.Text;
        }
        finally
        {
            SetApplying(false);
        }
    }

    private async void EnableAutoLogon_Click(object sender, RoutedEventArgs e) => await ApplyAutoLogonAsync(true);
    private async void DisableAutoLogon_Click(object sender, RoutedEventArgs e) => await ApplyAutoLogonAsync(false);

    private async Task ApplyAutoLogonAsync(bool enabled)
    {
        if (_applying || _closed) return;
        string user = UserNameBox.Text.Trim();
        string domain = DomainBox.Text.Trim();
        string password = enabled ? AccountPasswordBox.Password : string.Empty;
        AccountPasswordBox.Password = string.Empty;
        if (enabled && string.IsNullOrWhiteSpace(user))
        {
            AutoLogonActionText.Text = "请填写要自动登录的 Windows 账号。";
            UserNameBox.Focus(FocusState.Programmatic);
            return;
        }
        SetApplying(true);
        AutoLogonActionText.Text = "正在请求管理员授权并配置自动登录…";
        try
        {
            string message = await WindowsIntegrationService.ConfigureAutoLogonAsync(enabled, user, domain, password);
            AutoLogonStatusText.Text = enabled ? $"已启用 · {FormatAccount(domain, user)}" : "当前未启用 Windows 自动登录。";
            AutoLogonActionText.Text = string.IsNullOrWhiteSpace(message)
                ? (enabled ? "已应用，将在下一次 Windows 登录时生效。" : "已关闭自动登录。")
                : message;
        }
        catch (Exception ex)
        {
            // A failed secure write can disable a previous automatic-login setting.
            ReadSystemStatus(populateInputs: false);
            AutoLogonActionText.Text = "操作未完成：" + ex.Message;
        }
        finally
        {
            password = string.Empty;
            AccountPasswordBox.Password = string.Empty;
            SetApplying(false);
        }
    }

    private void SetApplying(bool applying)
    {
        _applying = applying;
        SettingsPivot.IsEnabled = !applying;
        SaveButton.IsEnabled = !applying;
        CancelButton.IsEnabled = !applying;
        ValidationText.Text = applying
            ? "正在应用 Windows 设置，请完成管理员授权。"
            : "保存将应用展示设置和登录后启动选项。";
    }

    private void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_applying) return;
        args.Cancel = true;
        ValidationText.Text = "请等待当前系统设置操作结束后再关闭窗口。";
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        AccountPasswordBox.Password = string.Empty;
        AppWindow.Closing -= Window_Closing;
        _themeService.Dispose();
        try
        {
            if (Result is null) ThemePreviewChanged?.Invoke(_current.ThemePreference);
        }
        finally
        {
            try
            {
                if (_shown)
                {
                    WindowInteropService.SetEnabled(_owner, true);
                    WindowInteropService.BringToFront(_owner);
                }
            }
            finally
            {
                _completion.TrySetResult(Result);
            }
        }
    }
}
