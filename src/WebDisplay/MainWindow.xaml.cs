using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WebDisplay.Models;
using WebDisplay.Services;
using Windows.Graphics;

namespace WebDisplay;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly PowerService _power = new();
    private readonly RecoveryPolicy _recovery = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool _smoke;
    private readonly ThemeService _theme;
    private readonly WindowShortcutService _shortcuts;
    private AppSettings _settings;
    private WebView2? _browser;
    private CoreWebView2Environment? _browserEnvironment;
    private readonly SemaphoreSlim _browserLifecycleLock = new(1, 1);
    private Task _certificateSessionRetired = Task.CompletedTask;
    private bool _certificatePolicyChanging;
    private string? _zoomScriptId;
    private readonly SemaphoreSlim _pageSettingsLock = new(1, 1);
    private string? _scrollbarWarning;
    private TrayService? _tray;
    private bool _closing, _initializing, _started, _isFullscreen, _loading, _settingsOpen, _runtimeMissing, _rebuildRequired;
    private ulong _navigationId;
    private DateTimeOffset? _retryAt, _refreshAt, _navigationStarted;
    private string _failureText = "";
    private string _failureFormat = "";
    private object[] _failureArguments = Array.Empty<object>();
    private RectInt32 _restoreBounds;
    private bool _restoreMaximized;
    private OverlappedPresenter? _normalPresenter;
    private SettingsWindow? _settingsWindow;

    public MainWindow(SettingsStore store, bool smoke)
    {
        _store = store; _smoke = smoke; _settings = store.Load();
        if (smoke) _settings = new AppSettings { FullScreen = false, PreventSleep = false };
        L.SetLanguage(_settings.Language);
        InitializeComponent();
        ApplyInterfaceLanguage();
        WindowInteropService.Initialize(this, 1200, 800);
        _theme = new ThemeService(this, RootGrid, _settings.ThemePreference);
        _theme.ThemeChanged += (_, _) => ApplyBrowserTheme();
        RootGrid.Loaded += OnLoaded;
        Closed += OnClosed;
        AppWindow.Changed += (_, _) => UpdatePower();
        AppWindow.Closing += (_, args) =>
        {
            if (_settingsOpen) { args.Cancel = true; ShowFromTray(); }
        };
        _timer.Tick += OnTick;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _shortcuts = new WindowShortcutService(this, () => !_closing && !_settingsOpen, shortcut =>
        {
            switch (shortcut)
            {
                case WindowShortcut.ToggleFullscreen: ToggleFullscreen(); break;
                case WindowShortcut.ExitFullscreen: if (_isFullscreen) ToggleFullscreen(); break;
                case WindowShortcut.OpenSettings: _ = ShowSettingsAsync(); break;
                case WindowShortcut.Refresh: _ = RefreshAsync(); break;
            }
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        try
        {
            CreateTray();
            if (!_smoke && (!_store.HasSettings || _store.LoadWarning != null))
            {
                if (_store.LoadWarning != null) await ShowNoticeAsync(L.Text("设置提示"), _store.LoadWarning);
                if (!await ShowSettingsAsync()) { Close(); return; }
            }
            ApplyDisplaySettings();
            _timer.Start();
            if (_smoke) await RunSmokeTestAsync();
            else await InitializeBrowserAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("Initialization failed: " + ex);
            if (_smoke) { WriteSmokeFailure(ex); Close(); }
            else await ShowNoticeAsync(L.Text("启动未完成"), ex.Message);
        }
    }

    private void ShowFromTray()
    {
        WindowInteropService.BringToFront((Window?)_settingsWindow ?? this);
    }

    private void RequestClose()
    {
        if (_settingsOpen) ShowFromTray();
        else Close();
    }

    private async Task InitializeBrowserAsync()
    {
        if (_initializing || _closing || _certificatePolicyChanging) return;
        _initializing = true;
        _retryAt = null;
        await _browserLifecycleLock.WaitAsync();
        try
        {
            if (_certificatePolicyChanging || _closing) return;
            await _certificateSessionRetired.WaitAsync(TimeSpan.FromSeconds(15));
            DisposeBrowser();
            var browser = new WebView2();
            _browser = browser;
            ApplyBrowserTheme();
            BrowserHost.Children.Add(browser);
            StatusText.Text = L.Text("正在初始化网页引擎…");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(_store.DataDirectory, "BrowserProfile"), null)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            if (_closing || _browser != browser) return;
            _browserEnvironment = environment;
            await browser.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(TimeSpan.FromSeconds(45));
            if (_closing || _browser != browser || _certificatePolicyChanging) return;
            _runtimeMissing = false;
            _rebuildRequired = false;
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            // Mute the whole WebView before the first page can produce audio.
            ApplyBrowserAudio();
            browser.CoreWebView2.ServerCertificateErrorDetected += (_, args) =>
            {
                args.Action = !_closing && !_certificatePolicyChanging && _browser == browser && _settings.IgnoreCertificateErrors
                    ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow
                    : CoreWebView2ServerCertificateErrorAction.Cancel;
                if (_smoke)
                {
                    AppLog.Write("Smoke certificate decision: " + args.ErrorStatus + "; " + args.Action);
                    if (args.Action == CoreWebView2ServerCertificateErrorAction.Cancel) _smokeRejectedCertificate = true;
                }
            };
            // AlwaysAllow is cached per browser session. Start with clean
            // decisions even if WebView2 reused a process for this profile.
            await browser.CoreWebView2.ClearServerCertificateErrorActionsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (_browser != browser || _closing) return;
                if (_certificatePolicyChanging) { args.Cancel = true; return; }
                if (!AppSettings.IsValidUrl(args.Uri) && args.Uri != "about:blank") { args.Cancel = true; return; }
                _navigationId = args.NavigationId;
                _loading = true; _navigationStarted = DateTimeOffset.Now;
                StatusText.Text = L.Text("正在加载网页…");
            };
            browser.CoreWebView2.NavigationCompleted += async (_, args) =>
            {
                if (_browser != browser || _closing || args.NavigationId != _navigationId) return;
                // WebView2 reports certificate failures before asking the host
                // whether to allow them. Let the permitted load finish first.
                if (!args.IsSuccess && _settings.IgnoreCertificateErrors && IsCertificateError(args.WebErrorStatus))
                {
                    _loading = true;
                    _navigationStarted ??= DateTimeOffset.Now;
                    return;
                }
                _loading = false; _navigationStarted = null;
                if (args.IsSuccess)
                {
                    try { await browser.CoreWebView2.ExecuteScriptAsync(PageZoomScript.Create(_settings.ZoomPercent)); }
                    catch (Exception ex) { AppLog.Write("Page zoom on navigation: " + ex.GetType().Name); }
                    await ApplyBrowserScrollbarsAsync();
                    if (_browser != browser || _closing || args.NavigationId != _navigationId) return;
                    _retryAt = null; _recovery.Reset();
                    RecoveryBanner.IsOpen = false;
                    _refreshAt = DateTimeOffset.Now.AddMinutes(_settings.RefreshIntervalMinutes);
                    SetReadyStatus();
                }
                else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    ScheduleRetry("网页加载失败（{0}）", args.WebErrorStatus);
            };
            browser.CoreWebView2.ProcessFailed += (_, args) =>
            {
                if (_browser != browser || _closing) return;
                AppLog.Write("WebView2 ProcessFailed: " + args.ProcessFailedKind);
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
                {
                    DispatcherQueue.TryEnqueue(() =>
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
            browser.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            ApplyBrowserTheme();
            await ApplyBrowserZoomAsync();
            await ApplyBrowserScrollbarsAsync();
            if (_closing || _browser != browser) return;
            NavigateHome();
        }
        catch (Exception ex)
        {
            DisposeBrowser();
            AppLog.Write("Browser initialize: " + ex.GetType().Name + " HRESULT=" + ex.HResult.ToString("X"));
            _runtimeMissing = unchecked((uint)ex.HResult) == 0x80070002;
            ScheduleRetry(_runtimeMissing ? "未检测到 WebView2 Runtime，请安装后点击重试" : "网页引擎初始化失败，将自动重试");
            if (_runtimeMissing && !_smoke)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot, Title = L.Text("需要网页运行组件"),
                    Content = L.Text("运行网页展示器需要 Microsoft Edge WebView2 Runtime。安装后返回程序点击“立即重试”。"),
                    PrimaryButtonText = L.Text("打开微软下载页"), CloseButtonText = L.Text("稍后"),
                    RequestedTheme = RootGrid.ActualTheme
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            }
        }
        finally { _initializing = false; _browserLifecycleLock.Release(); }
    }

    private void DisposeBrowser()
    {
        var previous = _browser;
        _browser = null;
        _browserEnvironment = null;
        _zoomScriptId = null;
        try { previous?.Close(); } catch { }
        BrowserHost.Children.Clear();
    }

    private void ApplyBrowserTheme()
    {
        if (_closing) return;
        try
        {
            if (_browser != null)
                _browser.DefaultBackgroundColor = _theme.EffectiveTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 255, 255, 255);
            if (_browser?.CoreWebView2 != null)
                _browser.CoreWebView2.Profile.PreferredColorScheme = _theme.EffectiveTheme == ElementTheme.Dark
                    ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        }
        catch (Exception ex) { AppLog.Write("Browser theme: " + ex.GetType().Name); }
    }

    private void NavigateHome()
    {
        if (_certificatePolicyChanging || _browser?.CoreWebView2 == null) return;
        _retryAt = null;
        _loading = true; _navigationStarted = DateTimeOffset.Now;
        _browser.CoreWebView2.Navigate(_settings.Url);
    }

    private async Task ApplyBrowserZoomAsync()
    {
        await _pageSettingsLock.WaitAsync();
        try
        {
            var browser = _browser;
            if (_closing || browser?.CoreWebView2 == null) return;
            var core = browser.CoreWebView2;
            var script = PageZoomScript.Create(_settings.ZoomPercent);
            var previousScriptId = _zoomScriptId;
            // Register before navigating; this also covers refreshes, retries
            // and website links. Serialize updates with browser initialization.
            var scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (_closing || _browser != browser) return;
            _zoomScriptId = scriptId;
            if (previousScriptId != null) core.RemoveScriptToExecuteOnDocumentCreated(previousScriptId);
            await core.ExecuteScriptAsync(script);
        }
        finally { _pageSettingsLock.Release(); }
    }

    private async Task<bool> ApplyBrowserScrollbarsAsync()
    {
        await _pageSettingsLock.WaitAsync();
        var browser = _browser;
        try
        {
            if (_closing || browser?.CoreWebView2 == null) return true;
            // Suppress native scrollbar painting without disabling scrolling or
            // altering the website's overflow styles. Keep CDP calls ordered.
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setScrollbarsHidden",
                _settings.ShowScrollbars ? "{\"hidden\":false}" : "{\"hidden\":true}")
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            if (!_closing && _browser == browser) _scrollbarWarning = null;
            return true;
        }
        catch (Exception ex)
        {
            if (_closing || _browser != browser) return true;
            AppLog.Write("Apply scrollbars: " + ex.GetType().Name + " HRESULT=" + ex.HResult.ToString("X"));
            _scrollbarWarning = "滚动条设置暂未生效，请更新 WebView2 Runtime 后重试";
            return false;
        }
        finally { _pageSettingsLock.Release(); }
    }

    private async Task RefreshAsync()
    {
        if (_closing || _initializing || _certificatePolicyChanging) return;
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

    private void ScheduleRetry(string message, params object[] arguments)
    {
        _loading = false; _navigationStarted = null;
        _failureFormat = message;
        _failureArguments = arguments;
        _failureText = L.Format(message, arguments);
        _retryAt = DateTimeOffset.Now + _recovery.NextDelay();
        RecoveryBanner.IsOpen = true;
        UpdateRetryText();
        AppLog.Write(_failureText);
    }

    private void UpdateRetryText()
    {
        RecoveryBanner.Message = _runtimeMissing ? _failureText : L.Format("{0} · {1} 秒后重试", _failureText, Math.Max(0, Math.Ceiling(((_retryAt ?? DateTimeOffset.Now) - DateTimeOffset.Now).TotalSeconds)));
        StatusText.Text = L.Text("等待恢复");
    }

    private async void OnTick(object? sender, object e)
    {
        if (_closing || _initializing || _certificatePolicyChanging) return;
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
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closing || _certificatePolicyChanging) return;
            if (e.IsAvailable && _retryAt.HasValue && !_runtimeMissing) _retryAt = DateTimeOffset.Now.AddSeconds(1);
            else if (!e.IsAvailable) ScheduleRetry("网络连接已断开");
        });
    }

    private async Task<bool> ShowSettingsAsync()
    {
        if (_settingsOpen || _closing) return false;
        _settingsOpen = true;
        SetTopmost(false);
        try
        {
            var dialog = new SettingsWindow(_settings, this);
            _settingsWindow = dialog;
            dialog.ThemePreviewChanged += _theme.SetPreference;
            var next = await dialog.ShowAsync();
            if (_closing || next == null) return false;
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
            bool changedScrollbars = next.ShowScrollbars != _settings.ShowScrollbars;
            bool changedCertificatePolicy = next.IgnoreCertificateErrors != _settings.IgnoreCertificateErrors;
            _settings = next;
            ApplyDisplaySettings();
            try { ApplyBrowserAudio(); }
            catch (Exception ex)
            {
                AppLog.Write("Apply saved audio: " + ex.GetType().Name);
                _rebuildRequired = true;
                ScheduleRetry("网页声音设置应用失败，将重新创建浏览器");
            }
            if (changedCertificatePolicy)
            {
                await RecreateBrowserForCertificatePolicyAsync();
                return true;
            }
            try { await ApplyBrowserZoomAsync(); }
            catch (Exception ex)
            {
                AppLog.Write("Apply saved zoom: " + ex.GetType().Name);
                _rebuildRequired = true;
                ScheduleRetry("网页缩放应用失败，将重新创建浏览器");
            }
            if (!await ApplyBrowserScrollbarsAsync())
                await ShowNoticeAsync(L.Text("滚动条设置暂未生效"), L.Text("设置已保存，但网页引擎暂时无法应用滚动条选项。请更新 WebView2 Runtime 后重试。网页仍可正常显示。"));
            if (!_loading && !_retryAt.HasValue) SetReadyStatus();
            // The native scrollbar policy fully takes effect on the next
            // document layout. Reload only when this option or the URL changes.
            if ((changedUrl || changedScrollbars) && _browser != null) await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            await ShowNoticeAsync(L.Text("保存失败"), L.Format("设置未能保存：{0}", ex.Message));
            return false;
        }
        finally
        {
            _settingsOpen = false; _settingsWindow = null;
            if (!_closing) { _theme.SetPreference(_settings.ThemePreference); SetTopmost(_settings.AlwaysOnTop); }
        }
    }

    private async Task ShowNoticeAsync(string title, string message)
    {
        if (_closing) return;
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = title, Content = message, CloseButtonText = L.Text("确定"), RequestedTheme = RootGrid.ActualTheme };
        await dialog.ShowAsync();
    }

    private void ApplyDisplaySettings()
    {
        ApplyInterfaceLanguage();
        AddressText.Text = _settings.Url;
        _theme.SetPreference(_settings.ThemePreference);
        SetFullscreen(_settings.FullScreen);
        SetTopmost(_settings.AlwaysOnTop);
        _refreshAt = DateTimeOffset.Now.AddMinutes(_settings.RefreshIntervalMinutes);
        UpdatePower();
        if (!_retryAt.HasValue && !_loading && !_initializing && !_certificatePolicyChanging) SetReadyStatus();
    }

    private void SetReadyStatus() => StatusText.Text = (_settings.AutoRefreshEnabled ? L.Format("每 {0} 分钟刷新", _settings.RefreshIntervalMinutes) : L.Text("自动刷新已关闭")) + "  ·  " + L.Text(_power.IsActive ? "防休眠已开启" : "防休眠已关闭") + (_settings.IgnoreCertificateErrors ? "  ·  " + L.Text("已忽略证书错误") : "") + (_settings.MutePage ? "  ·  " + L.Text("网页已静音") : "") + (_scrollbarWarning == null ? "" : "  ·  " + L.Text(_scrollbarWarning));

    private void UpdatePower()
    {
        if (_closing || !_started) return;
        try
        {
            _power.SetEnabled(_settings.PreventSleep && AppWindow.IsVisible && !WindowInteropService.IsMinimized(this));
            if (!_loading && !_retryAt.HasValue) SetReadyStatus();
        }
        catch (Exception ex) { AppLog.Write("Power state: " + ex.GetType().Name); StatusText.Text = L.Text("无法启用防休眠，请检查系统策略"); }
    }

    private void SetFullscreen(bool enabled)
    {
        if (_isFullscreen == enabled) return;
        if (enabled)
        {
            _normalPresenter = AppWindow.Presenter as OverlappedPresenter;
            _restoreMaximized = _normalPresenter?.State == OverlappedPresenterState.Maximized;
            _restoreBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            if (_normalPresenter != null) AppWindow.SetPresenter(_normalPresenter);
            else AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                if (_restoreMaximized) presenter.Maximize();
                else { presenter.Restore(); AppWindow.MoveAndResize(_restoreBounds); }
            }
        }
        _isFullscreen = enabled;
        Toolbar.Visibility = StatusBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        SetTopmost(_settings.AlwaysOnTop);
    }

    private void SetTopmost(bool enabled)
    {
        if (_closing) return;
        MainWindowNative.SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(this), enabled ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    private void ToggleFullscreen()
    {
        if (_settingsOpen || _closing) return;
        _settings.FullScreen = !_isFullscreen;
        SetFullscreen(_settings.FullScreen);
        try { _store.Save(_settings); } catch (Exception ex) { AppLog.Write("Save fullscreen: " + ex.GetType().Name); }
    }
    private void RefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();
    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void SettingsClick(object sender, RoutedEventArgs e) => _ = ShowSettingsAsync();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _timer.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _power.Dispose();
        _shortcuts.Dispose();
        _tray?.Dispose();
        _theme.Dispose();
        DisposeBrowser();
        AppLog.Write("WinUI application closed; sleep prevention released.");
    }
}

internal static class MainWindowNative
{
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}

