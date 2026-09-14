using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using WebDisplay.Services;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private static bool IsCertificateError(CoreWebView2WebErrorStatus status) => status is
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
        CoreWebView2WebErrorStatus.CertificateExpired or
        CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or
        CoreWebView2WebErrorStatus.CertificateRevoked or
        CoreWebView2WebErrorStatus.CertificateIsInvalid;

    private async Task RecreateBrowserForCertificatePolicyAsync()
    {
        _certificatePolicyChanging = true;
        _retryAt = null;
        _refreshAt = null;
        bool hadBrowser = false;
        await _browserLifecycleLock.WaitAsync();
        try
        {
            if (_closing) return;
            StatusText.Text = L.Text("正在重新应用 HTTPS 证书设置…");
            var browser = _browser;
            var environment = _browserEnvironment;
            hadBrowser = browser != null;
            if (browser?.CoreWebView2 != null && environment != null)
            {
                uint processId = browser.CoreWebView2.BrowserProcessId;
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void BrowserExited(CoreWebView2Environment sender, CoreWebView2BrowserProcessExitedEventArgs args)
                {
                    if (args.BrowserProcessId != processId) return;
                    environment.BrowserProcessExited -= BrowserExited;
                    exited.TrySetResult();
                }
                environment.BrowserProcessExited += BrowserExited;
                _certificateSessionRetired = exited.Task;
                try
                {
                    browser.CoreWebView2.Stop();
                    await browser.CoreWebView2.ClearServerCertificateErrorActionsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    // Stop alone does not stop page scripts. Close the control
                    // even when clearing fails; never leave an allowed page live.
                    DisposeBrowser();
                }
                // Preserve cookies/profile, but retire the old connection session.
                // A timeout leaves this task pending, so initialization cannot
                // reuse the old process on a later automatic/manual retry.
                await _certificateSessionRetired.WaitAsync(TimeSpan.FromSeconds(15));
            }
            else DisposeBrowser();
        }
        catch (Exception ex)
        {
            DisposeBrowser();
            _rebuildRequired = true;
            AppLog.Write("Certificate policy reset: " + ex.GetType().Name);
            ScheduleRetry("正在关闭旧网页会话，证书设置应用后将重新加载");
            return;
        }
        finally
        {
            _certificatePolicyChanging = false;
            _browserLifecycleLock.Release();
        }
        // Once the display has started, saving must also recover a missing
        // control after an earlier initialization failure. First-run settings
        // are initialized by OnLoaded after the timer starts.
        if ((hadBrowser || _timer.IsEnabled) && !_closing) await InitializeBrowserAsync();
    }
}
