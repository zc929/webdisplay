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
        bool oldFullscreenSetting = _settings.FullScreen;
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
            SetFullscreen(true);
            await AssertFullscreenLayoutAsync("No update available");
            SetFullscreen(false);
            await AssertWindowedIndicatorAsync("Exit with no update available");
            _smokeChecks.Add("Fullscreen hides the version and update controls even when no update is available");

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

            async Task AssertFullscreenLayoutAsync(string phase)
            {
                await WaitForConditionAsync(() => _isFullscreen &&
                    Toolbar.Visibility == Visibility.Collapsed && StatusBar.Visibility == Visibility.Collapsed &&
                    VersionBadge.Visibility == Visibility.Collapsed && !RecoveryBanner.IsOpen &&
                    RootGrid.RowDefinitions[3].ActualHeight < 0.5 && BrowserHost.ActualHeight > 0 &&
                    Math.Abs(BrowserHost.ActualHeight - RootGrid.ActualHeight) < 1,
                    phase + ": fullscreen hides all status content without reserving bottom space", 5);
                Require(Grid.GetRow(VersionBadge) == 3 && Grid.GetColumn(VersionBadge) == 1 &&
                    Grid.GetColumnSpan(VersionBadge) == 1, "Fullscreen moved the version indicator over the webpage");
            }

            async Task AssertWindowedIndicatorAsync(string phase)
            {
                await WaitForConditionAsync(() => !_isFullscreen &&
                    Toolbar.Visibility == Visibility.Visible && StatusBar.Visibility == Visibility.Visible &&
                    VersionBadge.Visibility == Visibility.Visible && VersionBadge.ActualWidth > 0 &&
                    RootGrid.RowDefinitions[3].ActualHeight > 0,
                    phase + ": windowed status and version indicator restored", 5);
                Require(Grid.GetRow(VersionBadge) == 3 && Grid.GetColumn(VersionBadge) == 1 &&
                    Grid.GetColumnSpan(VersionBadge) == 1 && CurrentVersionText.Text == AppVersion.Display,
                    "Version indicator did not return to the normal status bar");
            }

            // A check started while windowed must remain hidden if it completes after entering fullscreen.
            await CheckForUpdatesAsync(_ => Result(new(UpdateCheckStatus.UpToDate)));
            var fullscreenPending = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task fullscreenChecking = CheckForUpdatesAsync(_ => fullscreenPending.Task);
            try
            {
                Require(_updateChecking, "Asynchronous fullscreen update check did not start");
                ToggleFullscreen();
                await AssertFullscreenLayoutAsync("Pending update check");
                fullscreenPending.SetResult(new(UpdateCheckStatus.UpdateAvailable, availableVersion, releaseUri));
                await fullscreenChecking;
                Require(!_updateChecking && _availableUpdate?.ReleaseUri == releaseUri,
                    "Fullscreen update callback did not retain the discovered release");
                await AssertFullscreenLayoutAsync("New-release callback completed");
                // Allow the existing blink timer to tick without requiring it to stop in fullscreen.
                await Task.Delay(950);
                await AssertFullscreenLayoutAsync("After update timer tick");
                await SavePreviewAsync(RootGrid, "updates-fullscreen.png");
            }
            finally
            {
                fullscreenPending.TrySetCanceled();
                await fullscreenChecking;
            }
            ToggleFullscreen();
            await AssertWindowedIndicatorAsync("Exit after fullscreen release discovery");
            Require(UpdateButton.IsEnabled && UpdateButtonText.Text == L.Format("发现新版本 {0}", availableVersion),
                "Release found during fullscreen was not available after exit");
            _smokeChecks.Add("An in-flight update callback stays hidden in fullscreen, reserves no bottom space, and restores the release link on exit");

            // Repeat through the saved-display-settings path, now with a release already known.
            _settings.FullScreen = true;
            ApplyDisplaySettings();
            await AssertFullscreenLayoutAsync("ApplyDisplaySettings with known update");
            ToggleFullscreen();
            await AssertWindowedIndicatorAsync("Repeated fullscreen exit");
            Require(_availableUpdate?.ReleaseUri == releaseUri && UpdateButton.IsEnabled,
                "Repeated fullscreen transitions lost the update link");
            _smokeChecks.Add("Applying fullscreen settings also hides a known update, and repeated exits restore the normal status bar");
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
            _settings.FullScreen = oldFullscreenSetting;
            if (!_closing) SetFullscreen(oldFullscreenSetting);
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
