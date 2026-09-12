using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WebDisplay.Models;
using WebDisplay.Services;

namespace WebDisplay;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _current;
    private bool _applying;
    private bool _appliedRestartEnabled;
    private string _appliedRestartTime;
    private string _appliedRestartDays;
    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        _current = current.Clone();
        _appliedRestartEnabled = current.RestartEnabled;
        _appliedRestartTime = current.RestartTime;
        _appliedRestartDays = current.RestartDays;
        InitializeComponent();
        SourceInitialized += ClampToWorkingArea;
        UrlBox.Text = current.Url;
        RefreshCheck.IsChecked = current.AutoRefreshEnabled;
        RefreshMinutesBox.Text = current.RefreshIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        TopmostCheck.IsChecked = current.AlwaysOnTop;
        FullScreenCheck.IsChecked = current.FullScreen;
        PreventSleepCheck.IsChecked = current.PreventSleep;
        StartAtLogonCheck.IsChecked = current.StartAtLogon;
        UserNameBox.Text = Environment.UserName;
        DomainBox.Text = Environment.MachineName;
        SetRestartControls(current.RestartEnabled, current.RestartTime, current.RestartDays);
        ReadSystemStatus();
        UpdateControlAvailability();
    }

    private CheckBox[] WeekdayChecks => new[]
    {
        MondayCheck, TuesdayCheck, WednesdayCheck, ThursdayCheck,
        FridayCheck, SaturdayCheck, SundayCheck
    };

    private void ReadSystemStatus(bool populateInputs = true)
    {
        try
        {
            var status = WindowsIntegrationService.ReadStatus();
            if (populateInputs) StartAtLogonCheck.IsChecked = status.StartAtLogon;
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

    private void ClampToWorkingArea(object? sender, EventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        nint handle = new WindowInteropHelper(this).Handle;
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitorInfo))
        {
            // Monitor rectangles use physical pixels; WPF dimensions use logical units.
            uint dpi = GetDpiForWindow(handle);
            double scale = dpi > 0 ? 96d / dpi : 1d;
            workArea = new Rect(monitorInfo.Work.Left * scale, monitorInfo.Work.Top * scale,
                (monitorInfo.Work.Right - monitorInfo.Work.Left) * scale,
                (monitorInfo.Work.Bottom - monitorInfo.Work.Top) * scale);
        }
        double availableWidth = Math.Max(1, workArea.Width - 24);
        double availableHeight = Math.Max(1, workArea.Height - 24);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private void SetRestartControls(bool enabled, string time, string days)
    {
        RestartCheck.IsChecked = enabled;
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
        // Checked events can fire while InitializeComponent is still constructing controls.
        if (RefreshMinutesBox is not null) RefreshMinutesBox.IsEnabled = RefreshCheck.IsChecked == true;
        if (RestartOptions is not null) RestartOptions.IsEnabled = RestartCheck.IsChecked == true;
        if (WeekdayPanel is not null) WeekdayPanel.IsEnabled = WeeklyRadio.IsChecked == true;
    }

    private void RefreshCheck_Changed(object sender, RoutedEventArgs e) => UpdateControlAvailability();
    private void RestartCheck_Changed(object sender, RoutedEventArgs e) => UpdateControlAvailability();
    private void Frequency_Changed(object sender, RoutedEventArgs e) => UpdateControlAvailability();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var result = _current.Clone();
        result.Url = UrlBox.Text.Trim();
        if (!AppSettings.IsValidUrl(result.Url))
        {
            ShowValidation("请输入完整的 http:// 或 https:// 网页地址，网址中不要包含账号密码。", UrlBox);
            return;
        }
        if (!int.TryParse(RefreshMinutesBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes < 1 || minutes > 10080)
        {
            ShowValidation("刷新间隔必须是 1 到 10080 之间的整数分钟。", RefreshMinutesBox);
            return;
        }
        result.AutoRefreshEnabled = RefreshCheck.IsChecked == true;
        result.RefreshIntervalMinutes = minutes;
        result.AlwaysOnTop = TopmostCheck.IsChecked == true;
        result.FullScreen = FullScreenCheck.IsChecked == true;
        result.PreventSleep = PreventSleepCheck.IsChecked == true;
        result.StartAtLogon = StartAtLogonCheck.IsChecked == true;
        // System settings take effect only through their explicit administrator actions.
        result.RestartEnabled = _appliedRestartEnabled;
        result.RestartTime = _appliedRestartTime;
        result.RestartDays = _appliedRestartDays;
        Result = result;
        AccountPasswordBox.Clear();
        DialogResult = true;
    }

    private void ShowValidation(string message, Control field)
    {
        ValidationText.Text = message;
        ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(174, 48, 48));
        SettingsTabs.SelectedIndex = 0;
        field.BringIntoView();
        field.Focus();
    }

    private async void ApplyRestart_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = RestartCheck.IsChecked == true;
        string time = RestartTimeBox.Text.Trim();
        string days = DailyRadio.IsChecked == true ? "Daily" : string.Join(",", WeekdayChecks.Where(check => check.IsChecked == true).Select(check => (string)check.Tag));
        if (enabled && !DateTime.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            RestartStatusText.Text = "请输入有效的 24 小时制时间，例如 03:00 或 18:30。";
            RestartTimeBox.Focus();
            return;
        }
        if (enabled && string.IsNullOrWhiteSpace(days))
        {
            RestartStatusText.Text = "每周计划请至少选择一个日期。";
            MondayCheck.Focus();
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
        string user = UserNameBox.Text.Trim();
        string domain = DomainBox.Text.Trim();
        string password = enabled ? AccountPasswordBox.Password : string.Empty;
        AccountPasswordBox.Clear();
        if (enabled && string.IsNullOrWhiteSpace(user))
        {
            AutoLogonActionText.Text = "请填写要自动登录的 Windows 账号。";
            UserNameBox.Focus();
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
            AccountPasswordBox.Clear();
            SetApplying(false);
        }
    }

    private void SetApplying(bool applying)
    {
        _applying = applying;
        SettingsTabs.IsEnabled = !applying;
        SaveButton.IsEnabled = !applying;
        CancelButton.IsEnabled = !applying;
        if (applying) ValidationText.Text = "正在应用 Windows 设置，请完成管理员授权。";
        else ValidationText.Text = "保存将应用展示设置和登录后启动选项。";
        ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(102, 117, 139));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_applying)
        {
            e.Cancel = true;
            ValidationText.Text = "请等待当前系统设置操作结束后再关闭窗口。";
        }
    }

    private void Window_Closed(object? sender, EventArgs e) => AccountPasswordBox.Clear();
}
