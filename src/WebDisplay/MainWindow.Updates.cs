using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WebDisplay.Services;
using Windows.System;
using Windows.UI.ViewManagement;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _updateBlinkTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly UISettings _updateUiSettings = new();
    private Func<Uri, Task<bool>> _openReleasePage = uri => Launcher.LaunchUriAsync(uri).AsTask();
    private UpdateCheckResult? _availableUpdate;
    private UpdateCheckStatus? _lastUpdateStatus;
    private DateTimeOffset _nextUpdateCheck = DateTimeOffset.MaxValue;
    private DateTimeOffset _nextManualUpdateCheck = DateTimeOffset.MinValue;
    private bool _updateChecking, _openingReleasePage, _releaseOpenFailed;
    private string? _acknowledgedUpdateVersion;

    private void InitializeUpdateChecks()
    {
        _updateTimer.Tick += UpdateTimerTick;
        _updateBlinkTimer.Tick += UpdateBlinkTick;
    }

    private void StartUpdateChecks()
    {
        _updateTimer.Start();
        _ = CheckForUpdatesAsync();
    }

    private void UpdateTimerTick(object? sender, object e)
    {
        if (_closing) return;
        if (!_updateChecking && DateTimeOffset.UtcNow >= _nextUpdateCheck)
            _ = CheckForUpdatesAsync();
        else if (!UpdateButton.IsEnabled && !_updateChecking && !_openingReleasePage &&
                 DateTimeOffset.UtcNow >= _nextManualUpdateCheck)
            RenderUpdateIndicator();
    }

    private async Task CheckForUpdatesAsync(Func<CancellationToken, Task<UpdateCheckResult>>? check = null)
    {
        if (_closing || _updateChecking) return;
        _updateChecking = true;
        _releaseOpenFailed = false;
        _nextManualUpdateCheck = DateTimeOffset.UtcNow.AddMinutes(1);
        RenderUpdateIndicator();
        try
        {
            var result = await (check?.Invoke(_updateCancellation.Token) ?? _updates.CheckAsync(_updateCancellation.Token));
            if (_closing) return;
            _lastUpdateStatus = result.Status;
            if (result.HasUpdate) _availableUpdate = result;
            else if (result.Status == UpdateCheckStatus.UpToDate) _availableUpdate = null;
            // Keep a previously discovered release visible during temporary network failures.
            var delay = result.Status == UpdateCheckStatus.Unavailable ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(6);
            if (result.RetryAfter is { } retry && retry > delay) delay = retry;
            _nextUpdateCheck = DateTimeOffset.UtcNow.Add(delay);
            if (result.RetryAfter is { } manualRetry && manualRetry > TimeSpan.Zero)
                _nextManualUpdateCheck = DateTimeOffset.UtcNow.Add(manualRetry > TimeSpan.FromMinutes(1) ? manualRetry : TimeSpan.FromMinutes(1));
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                AppLog.Write("Update check: " + ex.GetType().Name);
                _lastUpdateStatus = UpdateCheckStatus.Unavailable;
                _nextUpdateCheck = DateTimeOffset.UtcNow.AddMinutes(30);
            }
        }
        finally
        {
            _updateChecking = false;
            if (!_closing) RenderUpdateIndicator();
        }
    }

    private void RenderUpdateIndicator()
    {
        CurrentVersionText.Text = AppVersion.Display;
        AutomationProperties.SetName(CurrentVersionText, L.Format("当前版本 {0}", AppVersion.Display));
        string label = _availableUpdate != null ? L.Format("发现新版本 {0}", _availableUpdate.LatestVersion!)
            : _updateChecking ? L.Text("正在检查…")
            : _lastUpdateStatus == UpdateCheckStatus.UpToDate ? L.Text("已是最新版本")
            : _lastUpdateStatus == UpdateCheckStatus.Unavailable ? L.Text("暂时无法检查，点击重试")
            : L.Text("检查更新");
        UpdateButtonText.Text = label;
        AutomationProperties.SetName(UpdateButton, label);
        ToolTipService.SetToolTip(UpdateButton, _releaseOpenFailed ? L.Text("无法打开浏览器，请稍后重试")
            : _availableUpdate != null ? L.Text("点击打开 GitHub 发布页面")
            : L.Text("每 6 小时自动检查更新，点击立即检查"));
        UpdateButton.IsEnabled = !_openingReleasePage && (_availableUpdate != null ||
            (!_updateChecking && DateTimeOffset.UtcNow >= _nextManualUpdateCheck));
        if (_availableUpdate != null && _availableUpdate.LatestVersion != _acknowledgedUpdateVersion)
            _updateBlinkTimer.Start();
        else
        {
            _updateBlinkTimer.Stop();
            UpdateButton.Opacity = 1;
        }
    }

    private void UpdateBlinkTick(object? sender, object e)
    {
        if (_closing) return;
        // Respect Windows' animation preference, including changes made while displaying.
        UpdateButton.Opacity = _updateUiSettings.AnimationsEnabled && UpdateButton.Opacity == 1 ? 0.60 : 1;
    }

    private void UpdateClick(object sender, RoutedEventArgs e) => _ = ActivateUpdateIndicatorAsync();

    private async Task ActivateUpdateIndicatorAsync()
    {
        if (_closing || _openingReleasePage) return;
        var release = _availableUpdate;
        if (release?.ReleaseUri is not { } uri)
        {
            if (DateTimeOffset.UtcNow >= _nextManualUpdateCheck) await CheckForUpdatesAsync();
            return;
        }
        _openingReleasePage = true;
        _releaseOpenFailed = false;
        RenderUpdateIndicator();
        try
        {
            bool opened = await _openReleasePage(uri);
            if (_closing) return;
            if (opened) _acknowledgedUpdateVersion = release.LatestVersion;
            else _releaseOpenFailed = true;
        }
        catch (Exception ex)
        {
            if (!_closing) { _releaseOpenFailed = true; AppLog.Write("Open release page: " + ex.GetType().Name); }
        }
        finally
        {
            _openingReleasePage = false;
            if (!_closing) RenderUpdateIndicator();
        }
    }

    private void UpdateVersionBadgePlacement()
    {
        Grid.SetRow(VersionBadge, _isFullscreen ? 2 : 3);
        Grid.SetColumn(VersionBadge, _isFullscreen ? 0 : 1);
        Grid.SetColumnSpan(VersionBadge, _isFullscreen ? 2 : 1);
        VersionBadge.Margin = _isFullscreen ? new Thickness(12) : new Thickness(0);
        VersionBadge.HorizontalAlignment = _isFullscreen ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        VersionBadge.VerticalAlignment = _isFullscreen ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        if (_isFullscreen) VersionBadge.BorderThickness = new Thickness(0);
        else VersionBadge.ClearValue(Border.BorderThicknessProperty);
    }

    private void StopUpdateChecks()
    {
        _updateTimer.Stop();
        _updateBlinkTimer.Stop();
        _updateTimer.Tick -= UpdateTimerTick;
        _updateBlinkTimer.Tick -= UpdateBlinkTick;
        _updateCancellation.Cancel();
        _updates.Dispose();
        _updateCancellation.Dispose();
    }
}
