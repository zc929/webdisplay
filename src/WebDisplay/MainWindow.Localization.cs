using Microsoft.UI.Xaml.Controls;
using WebDisplay.Services;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private void CreateTray()
    {
        _tray = new TrayService(this, ShowFromTray, () => _ = ShowSettingsAsync(),
            () => _ = RefreshAsync(), ToggleFullscreen, RequestClose, () => _theme.EffectiveTheme == Microsoft.UI.Xaml.ElementTheme.Dark);
    }

    private void ApplyInterfaceLanguage()
    {
        bool changed = L.Language != _settings.Language;
        L.SetLanguage(_settings.Language);
        RootGrid.Language = L.Language;
        Title = AppTitleText.Text = L.Text("网页展示器");
        RefreshButtonText.Text = L.Text("刷新");
        FullscreenButtonText.Text = L.Text("全屏");
        SettingsButtonText.Text = L.Text("设置");
        ToolTipService.SetToolTip(RefreshButton, L.Text("刷新 · Ctrl+R"));
        ToolTipService.SetToolTip(FullscreenButton, L.Text("全屏 · F11"));
        ToolTipService.SetToolTip(SettingsButton, L.Text("设置 · Ctrl+,"));
        ShortcutHintText.Text = L.Text("F11 全屏  ·  Esc 返回  ·  Ctrl+, 设置");
        RecoveryBanner.Title = L.Text("等待恢复");
        RetryButton.Content = L.Text("立即重试");
        if (changed && _tray != null)
        {
            _tray.Dispose();
            CreateTray();
        }
        if (_failureFormat.Length > 0) _failureText = L.Format(_failureFormat, _failureArguments);
        if (RecoveryBanner.IsOpen && !_retryAt.HasValue) RecoveryBanner.Message = _failureText;
        if (_retryAt.HasValue) UpdateRetryText();
        else if (_certificatePolicyChanging) StatusText.Text = L.Text("正在重新应用 HTTPS 证书设置…");
        else if (_initializing) StatusText.Text = L.Text("正在初始化网页引擎…");
        else if (_loading) StatusText.Text = L.Text("正在加载网页…");
        else SetReadyStatus();
    }

    private void ApplyBrowserAudio()
    {
        if (!_closing && _browser?.CoreWebView2 != null)
            _browser.CoreWebView2.IsMuted = _settings.MutePage;
    }
}