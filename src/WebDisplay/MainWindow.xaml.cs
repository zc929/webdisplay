using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WebDisplay.Models;
using WebDisplay.Services;
using Forms = System.Windows.Forms;

namespace WebDisplay;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly PowerService _power = new();
    private readonly RecoveryPolicy _recovery = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool _smoke;
    private AppSettings _settings;
    private WebView2? _browser;
    private Forms.NotifyIcon? _tray;
    private bool _closing, _initializing, _isFullscreen, _loading, _settingsOpen, _runtimeMissing, _rebuildRequired;
    private ulong _navigationId;
    private bool _smokeUseHttp;
    private DateTimeOffset? _retryAt, _refreshAt, _navigationStarted;
    private string _failureText = "";
    private Rect _restoreBounds;
    private WindowState _restoreState;
    private const string SmokeHtml = "<!doctype html><html><meta charset='utf-8'><title>WebDisplay smoke test</title><body style='font:28px Segoe UI;background:#edf3ff;color:#173559;padding:64px'><h1>网页展示器</h1><p id='result'>WebView2 rendering works.</p></body></html>";

    public MainWindow(SettingsStore store, bool smoke)
    {
        InitializeComponent();
        _store = store; _smoke = smoke; _settings = store.Load();
        if (smoke) { _settings = new AppSettings { FullScreen = false, PreventSleep = false }; }
        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += (_, _) => UpdatePower();
        IsVisibleChanged += (_, _) => UpdatePower();
        _timer.Tick += OnTick;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateTray();
        if (!_smoke && (!_store.HasSettings || _store.LoadWarning != null))
        {
            if (_store.LoadWarning != null) MessageBox.Show(this, _store.LoadWarning, "设置提示", MessageBoxButton.OK, MessageBoxImage.Information);
            if (!ShowSettings()) { Close(); return; }
        }
        ApplyDisplaySettings();
        _timer.Start();
        await InitializeBrowserAsync();
        if (_smoke) await RunSmokeTestAsync();
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon { Text = "网页展示器", Visible = true };
        try { _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => Dispatcher.BeginInvoke(ShowFromTray));
        menu.Items.Add("设置…", null, (_, _) => Dispatcher.BeginInvoke(() => { ShowFromTray(); ShowSettings(); }));
        menu.Items.Add("立即刷新", null, (_, _) => Dispatcher.BeginInvoke(() => _ = RefreshAsync()));
        menu.Items.Add("切换全屏", null, (_, _) => Dispatcher.BeginInvoke(ToggleFullscreen));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => Dispatcher.BeginInvoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _isFullscreen ? WindowState.Maximized : WindowState.Normal;
        Activate();
    }

    private async Task InitializeBrowserAsync()
    {
        if (_initializing || _closing) return;
        _initializing = true;
        _retryAt = null;
        try
        {
            _browser?.Dispose();
            BrowserHost.Children.Clear();
            var browser = new WebView2();
            _browser = browser;
            BrowserHost.Children.Add(browser);
            StatusText.Text = "正在初始化网页引擎…";
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_store.DataDirectory, "BrowserProfile"));
            await browser.EnsureCoreWebView2Async(environment);
            if (_closing || _browser != browser) return;
            _runtimeMissing = false;
            _rebuildRequired = false;
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (_browser != browser || _closing) return;
                if (!AppSettings.IsValidUrl(args.Uri) && args.Uri != "about:blank") { args.Cancel = true; return; }
                _navigationId = args.NavigationId;
                _loading = true; _navigationStarted = DateTimeOffset.Now;
                StatusText.Text = "正在加载网页…";
            };
            browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (_browser != browser || _closing || args.NavigationId != _navigationId) return;
                _loading = false; _navigationStarted = null;
                if (args.IsSuccess)
                {
                    _retryAt = null; _recovery.Reset();
                    RecoveryBanner.Visibility = Visibility.Collapsed;
                    _refreshAt = DateTimeOffset.Now.AddMinutes(_settings.RefreshIntervalMinutes);
                    SetReadyStatus();
                }
                else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    ScheduleRetry("网页加载失败（" + args.WebErrorStatus + "）");
            };
            browser.CoreWebView2.ProcessFailed += (_, args) =>
            {
                if (_browser != browser || _closing) return;
                string kind = args.ProcessFailedKind.ToString();
                AppLog.Write("WebView2 ProcessFailed: " + kind);
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
                {
                    _loading = false; _navigationStarted = null;
                    // Defer disposal until this native WebView2 callback has returned.
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_closing || _browser != browser) return;
                        _rebuildRequired = true;
                        ScheduleRetry("网页进程异常，将重新创建浏览器");
                    });
                }
            };
            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (AppSettings.IsValidUrl(args.Uri)) browser.CoreWebView2.Navigate(args.Uri);
            };
            browser.CoreWebView2.DownloadStarting += (_, args) => { args.Cancel = true; };
            // WPF WebView2 forwards accelerator keys to the standard WPF
            // PreviewKeyDown route, handled by the owning window below.
            NavigateHome();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _runtimeMissing = true;
            _browser?.Dispose(); _browser = null;
            ScheduleRetry("未检测到 WebView2 Runtime，请安装后点击重试");
            if (!_smoke)
            {
                var result = MessageBox.Show(this, "运行网页展示器需要 Microsoft Edge WebView2 Runtime。\n\n点击“确定”打开微软官方下载页，安装后返回程序点击“立即重试”。", "需要网页运行组件", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (result == MessageBoxResult.OK) Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _browser?.Dispose(); _browser = null;
            AppLog.Write("Browser initialize: " + ex.GetType().Name);
            ScheduleRetry("网页引擎初始化失败，将自动重试");
        }
        finally { _initializing = false; }
    }

    private void NavigateHome()
    {
        if (_browser?.CoreWebView2 == null) return;
        _retryAt = null;
        _loading = true; _navigationStarted = DateTimeOffset.Now;
        if (_smoke && !_smokeUseHttp) _browser.NavigateToString(SmokeHtml);
        else _browser.CoreWebView2.Navigate(_settings.Url);
    }

    private async Task RefreshAsync()
    {
        if (_closing || _initializing) return;
        try
        {
            _runtimeMissing = false;
            if (_rebuildRequired || _browser?.CoreWebView2 == null) await InitializeBrowserAsync();
            else NavigateHome();
        }
        catch (Exception ex)
        {
            AppLog.Write("Refresh failure: " + ex.GetType().Name);
            await InitializeBrowserAsync();
        }
    }

    private void ScheduleRetry(string message)
    {
        _loading = false; _navigationStarted = null;
        _failureText = message;
        _retryAt = DateTimeOffset.Now + _recovery.NextDelay();
        RecoveryBanner.Visibility = Visibility.Visible;
        UpdateRetryText();
        AppLog.Write(message);
    }

    private void UpdateRetryText()
    {
        RecoveryText.Text = _runtimeMissing ? _failureText : $"{_failureText} · {Math.Max(0, Math.Ceiling(((_retryAt ?? DateTimeOffset.Now) - DateTimeOffset.Now).TotalSeconds))} 秒后重试";
        StatusText.Text = "等待恢复";
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_closing || _initializing) return;
        var now = DateTimeOffset.Now;
        if (_loading && _navigationStarted.HasValue && now - _navigationStarted > TimeSpan.FromSeconds(60))
        {
            try { _browser?.CoreWebView2?.Stop(); } catch { }
            ScheduleRetry("网页加载超时");
        }
        if (_retryAt.HasValue)
        {
            UpdateRetryText();
            if (now >= _retryAt.Value && !_runtimeMissing) { _retryAt = null; await RefreshAsync(); }
        }
        else if (!_loading && _settings.AutoRefreshEnabled && _refreshAt.HasValue && now >= _refreshAt)
        {
            _refreshAt = now.AddMinutes(_settings.RefreshIntervalMinutes);
            await RefreshAsync();
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (_closing) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            if (e.IsAvailable && _retryAt.HasValue && !_runtimeMissing) _retryAt = DateTimeOffset.Now.AddSeconds(1);
            else if (!e.IsAvailable) ScheduleRetry("网络连接已断开");
        });
    }

    private bool ShowSettings()
    {
        if (_settingsOpen) return false;
        _settingsOpen = true;
        bool previousTopmost = Topmost;
        Topmost = false;
        try
        {
            var dialog = new SettingsWindow(_settings) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result == null) return false;
            var next = dialog.Result;
            next.Validate();
            var oldStatus = WindowsIntegrationService.ReadStatus();
            bool startupChanged = next.StartAtLogon != oldStatus.StartAtLogon;
            if (startupChanged) WindowsIntegrationService.SetStartAtLogon(next.StartAtLogon);
            try { _store.Save(next); }
            catch
            {
                if (startupChanged) WindowsIntegrationService.SetStartAtLogon(oldStatus.StartAtLogon);
                throw;
            }
            bool changedUrl = next.Url != _settings.Url;
            _settings = next;
            ApplyDisplaySettings();
            if (changedUrl && _browser != null) _ = RefreshAsync();
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, "设置未能保存：" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        finally { _settingsOpen = false; Topmost = _settings.AlwaysOnTop; if (_closing) Topmost = previousTopmost; }
    }

    private void ApplyDisplaySettings()
    {
        AddressText.Text = _settings.Url;
        Topmost = _settings.AlwaysOnTop;
        SetFullscreen(_settings.FullScreen);
        _refreshAt = DateTimeOffset.Now.AddMinutes(_settings.RefreshIntervalMinutes);
        UpdatePower();
        if (!_retryAt.HasValue) SetReadyStatus();
    }

    private void SetReadyStatus() => StatusText.Text = (_settings.AutoRefreshEnabled ? $"每 {_settings.RefreshIntervalMinutes} 分钟刷新" : "自动刷新已关闭") + (_power.IsActive ? "  ·  防休眠已开启" : "  ·  防休眠已关闭");

    private void UpdatePower()
    {
        if (_closing) return;
        try
        {
            _power.SetEnabled(_settings.PreventSleep && IsVisible && WindowState != WindowState.Minimized);
            if (!_loading && !_retryAt.HasValue) SetReadyStatus();
        }
        catch (Exception ex) { AppLog.Write("Power state: " + ex.GetType().Name); StatusText.Text = "无法启用防休眠，请检查系统策略"; }
    }

    private void SetFullscreen(bool enabled)
    {
        if (_isFullscreen == enabled) return;
        if (enabled)
        {
            _restoreState = WindowState;
            _restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            if (!_restoreBounds.IsEmpty && _restoreBounds.Width >= MinWidth) { Left = _restoreBounds.Left; Top = _restoreBounds.Top; Width = _restoreBounds.Width; Height = _restoreBounds.Height; }
            WindowState = _restoreState == WindowState.Minimized ? WindowState.Normal : _restoreState;
        }
        _isFullscreen = enabled;
        Toolbar.Visibility = StatusBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleFullscreen()
    {
        _settings.FullScreen = !_isFullscreen;
        SetFullscreen(_settings.FullScreen);
        try { _store.Save(_settings); } catch (Exception ex) { AppLog.Write("Save fullscreen: " + ex.GetType().Name); }
    }

    private static bool IsShortcut(Key key, bool control) => key == Key.F11 || key == Key.Escape || (control && (key == Key.OemComma || key == Key.R));
    private void HandleShortcut(Key key, bool control)
    {
        if (key == Key.F11) ToggleFullscreen();
        else if (key == Key.Escape && _isFullscreen) ToggleFullscreen();
        else if (control && key == Key.OemComma) ShowSettings();
        else if (control && key == Key.R) _ = RefreshAsync();
    }
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (IsShortcut(e.Key, control)) { e.Handled = true; var key = e.Key; Dispatcher.BeginInvoke(() => HandleShortcut(key, control)); }
    }
    private void RefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();
    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void SettingsClick(object sender, RoutedEventArgs e) => ShowSettings();

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _timer.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _power.Dispose();
        _tray?.Dispose();
        _browser?.Dispose();
        AppLog.Write("Application closed; sleep prevention released.");
    }

    private async Task RunSmokeTestAsync()
    {
        var checks = new System.Collections.Generic.List<string>();
        using var server = new LoopbackTestServer(SmokeHtml);
        try
        {
            server.Start();
            _smokeUseHttp = true;
            _settings.Url = server.Url;
            await RefreshAsync();
            await WaitForSmokePageAsync();
            checks.Add("HTTP page rendering and JavaScript");
            int requests = server.RequestCount;
            _settings.AutoRefreshEnabled = true;
            _refreshAt = DateTimeOffset.Now.AddMilliseconds(200);
            await WaitForConditionAsync(() => server.RequestCount > requests, "automatic refresh", 10);
            await WaitForSmokePageAsync();
            checks.Add("Automatic refresh executes");
            _settings.AutoRefreshEnabled = false;
            requests = server.RequestCount;
            _refreshAt = DateTimeOffset.Now.AddMilliseconds(200);
            await Task.Delay(2200);
            if (server.RequestCount != requests) throw new InvalidOperationException("Disabled refresh still executes");
            checks.Add("Refresh switch disables timer");
            server.Stop();
            await RefreshAsync();
            await WaitForConditionAsync(() => _retryAt.HasValue, "network retry scheduled", 15);
            if (RecoveryBanner.Visibility != Visibility.Visible) throw new InvalidOperationException("Retry banner missing");
            server.Start();
            await WaitForSmokePageAsync();
            checks.Add("Connection failure and automatic recovery");
            var previousBrowser = _browser!;
            int ownedBrowserProcessId = (int)previousBrowser.CoreWebView2.BrowserProcessId;
            using (var process = Process.GetProcessById(ownedBrowserProcessId)) process.Kill();
            await WaitForConditionAsync(() => _browser != previousBrowser, "browser process recreation", 25);
            await WaitForSmokePageAsync();
            checks.Add("Browser process crash and automatic recreation");
            SetFullscreen(true);
            if (WindowStyle != WindowStyle.None || Toolbar.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Fullscreen failed");
            SetFullscreen(false);
            if (WindowStyle != WindowStyle.SingleBorderWindow) throw new InvalidOperationException("Fullscreen restore failed");
            checks.Add("Fullscreen and window restore");
            Topmost = true; if (!Topmost) throw new InvalidOperationException("Topmost failed"); Topmost = false;
            checks.Add("Always-on-top toggle");
            _power.SetEnabled(true); if (!_power.IsActive) throw new InvalidOperationException("Power enable failed");
            _power.SetEnabled(false); if (_power.IsActive) throw new InvalidOperationException("Power release failed");
            checks.Add("Power prevention enable and release");
            using (var preview = File.Create(Path.Combine(_store.DataDirectory, "browser-preview.png")))
                await _browser!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
            var dialog = new SettingsWindow(_settings) { Owner = this };
            dialog.Show();
            await Task.Delay(700);
            if (dialog.ActualWidth < 600 || dialog.ActualHeight < 400) throw new InvalidOperationException("Settings layout failed");
            SaveWindowPreview(dialog, "settings-display.png");
            ((System.Windows.Controls.TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 1;
            dialog.UpdateLayout();
            SaveWindowPreview(dialog, "settings-system.png");
            dialog.Close();
            checks.Add("Both settings tabs render");
            File.WriteAllText(Path.Combine(_store.DataDirectory, "smoke-test-result.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks }));
            Close();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(_store.DataDirectory, "smoke-test-result.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString() }));
            System.Windows.Application.Current.Shutdown(1);
        }
    }

    private async Task WaitForConditionAsync(Func<bool> condition, string description, int seconds)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(seconds);
        while (DateTimeOffset.Now < deadline) { if (condition()) return; await Task.Delay(150); }
        throw new InvalidOperationException("Timed out: " + description);
    }

    private async Task WaitForSmokePageAsync()
    {
        var deadline = DateTimeOffset.Now.AddSeconds(30);
        string last = "";
        while (DateTimeOffset.Now < deadline)
        {
            try
            {
                if (!_loading && !_retryAt.HasValue && !_initializing && _browser?.CoreWebView2 != null)
                {
                    last = await _browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('result')?.textContent");
                    if (System.Text.Json.JsonSerializer.Deserialize<string>(last)?.Contains("rendering works") == true) return;
                }
            }
            catch (Exception ex) { last = ex.GetType().Name; }
            await Task.Delay(200);
        }
        throw new InvalidOperationException("HTTP browser content not ready. Last result: " + last + "; " + _failureText);
    }

    private void SaveWindowPreview(Window window, string name)
    {
        window.UpdateLayout();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(_store.DataDirectory, name));
        encoder.Save(stream);
    }
}
