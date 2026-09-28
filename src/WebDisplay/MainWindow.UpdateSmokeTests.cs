using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WebDisplay.Services;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private async Task RunUpdateSmokeChecksAsync()
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        Task<UpdateCheckResult> Result(UpdateCheckResult value) => Task.FromResult(value);
        string language = _settings.Language;
        var opener = _openReleasePage;
        var oldTheme = RootGrid.RequestedTheme;
        string availableVersion = "99.0.0";
        var releaseUri = new Uri("https://github.com/zc929/webdisplay/releases/tag/v99.0.0");
        try
        {
            Require(CurrentVersionText.Text == AppVersion.Display && Grid.GetRow(VersionBadge) == 3,
                "Assembly version was not shown in the status corner");
            await _browser!.CoreWebView2.ExecuteScriptAsync("window.updateTestToken='preserved';");
            await CheckForUpdatesAsync(_ => Result(new(UpdateCheckStatus.UpToDate)));
            Require(UpdateButtonText.Text == L.Text("已是最新版本") && !_updateBlinkTimer.IsEnabled,
                "Up-to-date response should have no flashing notification");
            _smokeChecks.Add("Assembly version and current-release state appear in the lower-right corner");

            var priorSize = AppWindow.Size;
            _settings.Language = "en-US";
            ApplyInterfaceLanguage();
            await CheckForUpdatesAsync(_ => Result(new(UpdateCheckStatus.Unavailable)));
            Require(_nextUpdateCheck > DateTimeOffset.UtcNow.AddMinutes(29), "Failed checks did not schedule a retry");
            _nextManualUpdateCheck = DateTimeOffset.UtcNow.AddSeconds(-1);
            UpdateTimerTick(null, EventArgs.Empty);
            Require(UpdateButton.IsEnabled, "Manual check did not become available after cooldown");
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(600 * RootGrid.XamlRoot.RasterizationScale), (int)(500 * RootGrid.XamlRoot.RasterizationScale)));
            await Task.Delay(350);
            Require(ShortcutHintText.Visibility == Visibility.Collapsed && VersionBadge.ActualWidth < RootGrid.ActualWidth,
                "Narrow window did not preserve room for the version indicator");
            await SavePreviewAsync(RootGrid, "updates-en-US-narrow-failure.png");
            AppWindow.Resize(priorSize);
            _settings.Language = language;
            ApplyInterfaceLanguage();
            _smokeChecks.Add("Failed-check retry and manual cooldown preserve a usable indicator in a narrow English window");
            var pending = new TaskCompletionSource<UpdateCheckResult>();
            var checking = CheckForUpdatesAsync(_ => pending.Task);
            Require(_updateChecking && !UpdateButton.IsEnabled, "Pending update check was not disabled");
            int duplicateRequests = 0;
            await CheckForUpdatesAsync(_ => { duplicateRequests++; return Result(new(UpdateCheckStatus.UpToDate)); });
            pending.SetResult(new(UpdateCheckStatus.UpdateAvailable, availableVersion, releaseUri));
            await checking;
            Require(duplicateRequests == 0 && _availableUpdate?.ReleaseUri == releaseUri && _updateBlinkTimer.IsEnabled,
                "Update state or overlapping-request protection failed");
            // Observe actual DispatcherTimer ticks, allowing reduced-motion mode to remain solid.
            double before = UpdateButton.Opacity;
            if (_updateUiSettings.AnimationsEnabled)
                await WaitForConditionAsync(() => UpdateButton.Opacity != before, "new-version blinking", 3);
            else
            {
                await Task.Delay(950);
                Require(UpdateButton.Opacity == 1, "Reduced-motion preference was ignored");
            }
            _smokeChecks.Add("New releases blink with the Windows animation preference and prevent overlapping checks");

            foreach (string locale in new[] { "en-US", "zh-TW", "ja-JP", "ko-KR", "zh-CN" })
            {
                _settings.Language = locale;
                ApplyInterfaceLanguage();
                Require(UpdateButtonText.Text == L.Format("发现新版本 {0}", availableVersion), "Update notice was not translated");
                foreach (var (theme, name) in new[] { (ElementTheme.Dark, "dark"), (ElementTheme.Light, "light") })
                {
                    RootGrid.RequestedTheme = theme;
                    await Task.Delay(220);
                    UpdateButton.Opacity = 1;
                    await SavePreviewAsync(RootGrid, "updates-" + locale + "-" + name + ".png");
                }
            }
            _smokeChecks.Add("Update indicator supports all five interface languages in dark and light themes");

            SetFullscreen(true);
            await Task.Delay(300);
            Require(StatusBar.Visibility == Visibility.Collapsed && Grid.GetRow(VersionBadge) == 2 &&
                VersionBadge.Visibility == Visibility.Visible && VersionBadge.ActualWidth > 0 &&
                Grid.GetColumnSpan(VersionBadge) == 2, "Fullscreen update overlay missing");
            await SavePreviewAsync(RootGrid, "updates-fullscreen.png");
            SetFullscreen(false);
            Require(Grid.GetRow(VersionBadge) == 3 && Grid.GetColumn(VersionBadge) == 1, "Version badge did not return to status bar");
            _smokeChecks.Add("Fullscreen keeps the small update overlay and restores the normal status corner");

            await CheckForUpdatesAsync(_ => Result(new(UpdateCheckStatus.Unavailable, RetryAfter: TimeSpan.FromHours(2))));
            Require(_availableUpdate?.ReleaseUri == releaseUri && _nextUpdateCheck > DateTimeOffset.UtcNow.AddMinutes(119)
                && _nextManualUpdateCheck > DateTimeOffset.UtcNow.AddMinutes(119), "Network failure lost release or ignored rate-limit wait");
            Uri? launched = null;
            _openReleasePage = uri => { launched = uri; return Task.FromResult(true); };
            await ActivateUpdateIndicatorAsync();
            Require(launched == releaseUri && !_updateBlinkTimer.IsEnabled && UpdateButton.Opacity == 1,
                "Release activation did not open the expected URL and stop blinking");
            Require(await _browser.CoreWebView2.ExecuteScriptAsync("window.updateTestToken") == "\"preserved\"",
                "Update checks or release activation changed the displayed webpage");
            _smokeChecks.Add("Failures preserve a known release, respect rate limits, and release clicks preserve the displayed webpage");

            _openReleasePage = _ => Task.FromResult(false);
            await ActivateUpdateIndicatorAsync();
            Require(_releaseOpenFailed && UpdateButton.IsEnabled, "Failed browser launch should leave the release link usable");
            _smokeChecks.Add("Browser-launch failure leaves the update link available for retry");
        }
        finally
        {
            _settings.Language = language;
            RootGrid.RequestedTheme = oldTheme;
            _availableUpdate = null;
            _lastUpdateStatus = null;
            _nextManualUpdateCheck = DateTimeOffset.MinValue;
            _nextUpdateCheck = DateTimeOffset.MaxValue;
            _acknowledgedUpdateVersion = null;
            _releaseOpenFailed = false;
            _openReleasePage = opener;
            ApplyInterfaceLanguage();
        }
    }
}
