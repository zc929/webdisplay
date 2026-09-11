using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using WebDisplay.Services;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private bool _smokeRejectedCertificate;

    private async Task RunCertificateSmokeChecksAsync()
    {
        string originalUrl = _settings.Url;
        using var server = new LoopbackHttpsTestServer(SmokeHtml);
        server.Start();
        _settings.Url = server.Url;
        AddressText.Text = server.Url;
        if (_settings.IgnoreCertificateErrors) throw new InvalidOperationException("Certificate bypass is enabled by default");
        await RefreshAsync();
        await WaitForRejectedCertificateAsync();
        if (server.RequestCount != 0) throw new InvalidOperationException("An untrusted HTTPS request was accepted by default");
        _smokeChecks.Add("Untrusted HTTPS certificates are rejected by default");

        await SaveCertificateChoiceForSmokeAsync(true);
        await RecreateBrowserForCertificatePolicyAsync();
        await WaitForSmokePageAsync();
        if (server.RequestCount == 0 || _retryAt.HasValue) throw new InvalidOperationException("Allowed HTTPS page did not finish loading");
        _smokeChecks.Add("Certificate bypass saves through the settings UI and loads a self-signed HTTPS page");
        int requests = server.RequestCount;
        await RefreshAsync();
        await WaitForConditionAsync(() => server.RequestCount > requests, "allowed HTTPS refresh", 10);
        await WaitForSmokePageAsync();
        _smokeChecks.Add("Certificate bypass remains effective on subsequent HTTPS loads");

        var previousBrowser = _browser!;
        using (var process = Process.GetProcessById((int)previousBrowser.CoreWebView2.BrowserProcessId)) process.Kill();
        await WaitForConditionAsync(() => _browser != previousBrowser, "HTTPS browser process recreation", 25);
        await WaitForSmokePageAsync();
        _smokeChecks.Add("Certificate preference reapplies after a browser process crash");

        await _browser!.CoreWebView2.ExecuteScriptAsync("document.cookie='webdisplay-smoke=retained;Secure;SameSite=Strict;path=/;max-age=120'");
        uint previousProcessId = _browser!.CoreWebView2.BrowserProcessId;
        requests = server.RequestCount;
        await SaveCertificateChoiceForSmokeAsync(false);
        await RecreateBrowserForCertificatePolicyAsync();
        await WaitForRejectedCertificateAsync();
        if (!_certificateSessionRetired.IsCompletedSuccessfully || _browser!.CoreWebView2.BrowserProcessId == previousProcessId)
            throw new InvalidOperationException("The previously allowed browser session was reused");
        if (server.RequestCount != requests) throw new InvalidOperationException("Disabling certificate bypass still allowed an untrusted HTTPS request");
        _smokeChecks.Add("Disabling bypass retires the allowed session and rejects the same cached certificate again");

        // Simulate an earlier initialization failure with no control remaining.
        // Saving a new policy must restart the display rather than lose its retry.
        DisposeBrowser();
        _rebuildRequired = true;
        _retryAt = DateTimeOffset.Now.AddMinutes(1);
        await SaveCertificateChoiceForSmokeAsync(true);
        await RecreateBrowserForCertificatePolicyAsync();
        await WaitForSmokePageAsync();
        string cookies = await _browser!.CoreWebView2.ExecuteScriptAsync("document.cookie");
        if (!cookies.Contains("webdisplay-smoke=retained", StringComparison.Ordinal))
            throw new InvalidOperationException("Certificate policy changes discarded the browser's saved cookies");
        _smokeChecks.Add("Certificate policy changes recover a missing control and retain profile cookies");
        await SaveCertificateChoiceForSmokeAsync(false);
        await RecreateBrowserForCertificatePolicyAsync();
        await WaitForRejectedCertificateAsync();

        _settings.Url = originalUrl;
        AddressText.Text = originalUrl;
        _store.Save(_settings);
        await RefreshAsync();
        await WaitForSmokePageAsync();
    }

    private async Task WaitForRejectedCertificateAsync()
    {
        // Cancel can map the final navigation error to ConnectionAborted.
        // Assert the actual certificate decision, not the navigation wording.
        await WaitForConditionAsync(() => _smokeRejectedCertificate && _retryAt.HasValue && !_loading, "untrusted HTTPS rejection", 15);
        _smokeRejectedCertificate = false;
    }

    private async Task SaveCertificateChoiceForSmokeAsync(bool enabled)
    {
        var dialog = new SettingsWindow(_settings, this);
        var closed = dialog.ShowAsync();
        await Task.Delay(300);
        var root = (FrameworkElement)dialog.Content;
        var toggle = (ToggleSwitch)root.FindName("IgnoreCertificateErrorsToggle");
        if (toggle.IsOn != _settings.IgnoreCertificateErrors) throw new InvalidOperationException("Certificate setting did not load its current value");
        toggle.IsOn = enabled;
        if (_settings.IgnoreCertificateErrors == enabled) throw new InvalidOperationException("Certificate toggle changed the current policy before saving");
        var invoke = (IInvokeProvider)new ButtonAutomationPeer((Button)root.FindName("SaveButton")).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        var saved = await closed.WaitAsync(TimeSpan.FromSeconds(5)) ?? throw new InvalidOperationException("Certificate setting save was cancelled");
        _store.Save(saved);
        _settings = _store.Load();
        if (_settings.IgnoreCertificateErrors != enabled) throw new InvalidOperationException("Certificate setting did not persist");
    }
}
