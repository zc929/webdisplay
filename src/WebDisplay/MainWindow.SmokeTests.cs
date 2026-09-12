using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using WebDisplay.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private readonly List<string> _smokeChecks = new();
    private const string SmokeHtml = "<!doctype html><html><meta charset='utf-8'><title>WebDisplay WinUI 3 test</title><style>:root{color-scheme:light dark}body{font:24px system-ui;padding:60px}h1{font-size:36px}</style><body><h1>网页展示器 · WinUI 3</h1><p id='result'>WebView2 rendering works.</p><p>系统主题 · 自动恢复 · 持续展示</p></body></html>";

    private async Task RunSmokeTestAsync()
    {
        using var server = new LoopbackTestServer(SmokeHtml);
        try
        {
            server.Start();
            _settings.Url = server.Url;
            AddressText.Text = server.Url;
            await InitializeBrowserAsync();
            await WaitForSmokePageAsync();
            _smokeChecks.Add("WinUI WebView2 HTTP rendering and JavaScript");
            int requests = server.RequestCount;
            _settings.AutoRefreshEnabled = true;
            _refreshAt = DateTimeOffset.Now.AddMilliseconds(200);
            await WaitForConditionAsync(() => server.RequestCount > requests, "automatic refresh", 10);
            await WaitForSmokePageAsync();
            _smokeChecks.Add("Automatic refresh executes");
            _settings.AutoRefreshEnabled = false;
            requests = server.RequestCount;
            _refreshAt = DateTimeOffset.Now.AddMilliseconds(200);
            await Task.Delay(2200);
            if (server.RequestCount != requests) throw new InvalidOperationException("Disabled refresh still executes");
            _smokeChecks.Add("Refresh switch disables timer");
            server.Stop();
            await RefreshAsync();
            await WaitForConditionAsync(() => _retryAt.HasValue, "network retry scheduled", 15);
            if (!RecoveryBanner.IsOpen) throw new InvalidOperationException("Retry banner missing");
            server.Start();
            await WaitForSmokePageAsync();
            _smokeChecks.Add("Connection failure and automatic recovery");
            var previousBrowser = _browser!;
            using (var process = Process.GetProcessById((int)previousBrowser.CoreWebView2.BrowserProcessId)) process.Kill();
            await WaitForConditionAsync(() => _browser != previousBrowser, "browser process recreation", 25);
            await WaitForSmokePageAsync();
            _smokeChecks.Add("Browser process crash and automatic recreation");
            SetFullscreen(true);
            if (AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen || Toolbar.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Fullscreen failed");
            SetFullscreen(false);
            if (AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) throw new InvalidOperationException("Window restore failed");
            _smokeChecks.Add("AppWindow fullscreen and restore");
            SetTopmost(true);
            if (!IsTopmostForSmoke()) throw new InvalidOperationException("Topmost style was not applied");
            SetTopmost(false);
            if (IsTopmostForSmoke()) throw new InvalidOperationException("Topmost style was not removed");
            _smokeChecks.Add("Always-on-top toggles");
            _power.SetEnabled(true);
            if (!_power.IsActive) throw new InvalidOperationException("Power enable failed");
            _power.SetEnabled(false);
            if (_power.IsActive) throw new InvalidOperationException("Power release failed");
            _smokeChecks.Add("Sleep prevention enable and release");
            _settings.PreventSleep = true;
            UpdatePower();
            if (!_power.IsActive) throw new InvalidOperationException("Visible display did not prevent sleep");
            ((OverlappedPresenter)AppWindow.Presenter).Minimize();
            await WaitForConditionAsync(() => !_power.IsActive, "minimizing releases sleep prevention", 5);
            ((OverlappedPresenter)AppWindow.Presenter).Restore();
            Activate();
            await WaitForConditionAsync(() => _power.IsActive, "restoring resumes sleep prevention", 5);
            _settings.PreventSleep = false;
            UpdatePower();
            _smokeChecks.Add("Minimizing releases and restoring resumes sleep prevention");

            var dialog = new SettingsWindow(_settings, this);
            dialog.ThemePreviewChanged += _theme.SetPreference;
            var closed = dialog.ShowAsync();
            await Task.Delay(500);
            var root = (FrameworkElement)dialog.Content;
            var themeBox = (ComboBox)root.FindName("ThemePreferenceBox");
            var pivot = (Pivot)root.FindName("SettingsPivot");
            foreach (var (theme, index, name) in new[] { (ElementTheme.Dark, 2, "dark"), (ElementTheme.Light, 1, "light") })
            {
                themeBox.SelectedIndex = index;
                await WaitForConditionAsync(() => root.ActualTheme == theme && RootGrid.ActualTheme == theme, "theme preview " + name, 5);
                await Task.Delay(800);
                pivot.SelectedIndex = 0;
                await Task.Delay(900);
                await SavePreviewAsync(root, "settings-display-" + name + ".png");
                pivot.SelectedIndex = 1;
                await Task.Delay(900);
                await SavePreviewAsync(root, "settings-system-" + name + ".png");
                string prefersDark = await _browser!.CoreWebView2.ExecuteScriptAsync("matchMedia('(prefers-color-scheme: dark)').matches");
                if (prefersDark != (theme == ElementTheme.Dark ? "true" : "false")) throw new InvalidOperationException("Web theme did not follow " + name);
                _smokeChecks.Add("Live " + name + " theme on both windows and web color preference");
            }
            themeBox.SelectedIndex = 0;
            await Task.Delay(250);
            if (root.RequestedTheme != ElementTheme.Default || RootGrid.RequestedTheme != ElementTheme.Default) throw new InvalidOperationException("System theme preference failed");
            _smokeChecks.Add("Follow-system theme selected");
            themeBox.SelectedIndex = 2;
            await Task.Delay(100);
            dialog.Close();
            if (await closed != null || RootGrid.RequestedTheme != ElementTheme.Default) throw new InvalidOperationException("Theme cancel did not restore saved preference");
            _smokeChecks.Add("Settings cancellation restores theme");
            File.WriteAllText(Path.Combine(_store.DataDirectory, "smoke-test-result.json"), JsonSerializer.Serialize(new { passed = true, framework = "WinUI 3", checks = _smokeChecks }));
            Close();
        }
        catch (Exception ex) { WriteSmokeFailure(ex); Close(); }
    }

    private void WriteSmokeFailure(Exception ex)
    {
        Environment.ExitCode = 1;
        File.WriteAllText(Path.Combine(_store.DataDirectory, "smoke-test-result.json"), JsonSerializer.Serialize(new { passed = false, framework = "WinUI 3", checks = _smokeChecks, error = ex.ToString() }));
    }

    private bool IsTopmostForSmoke() => (SmokeGetWindowLongPtr(WinRT.Interop.WindowNative.GetWindowHandle(this), -20).ToInt64() & 0x00000008) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr SmokeGetWindowLongPtr(IntPtr hwnd, int index);

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
                    if (JsonSerializer.Deserialize<string>(last)?.Contains("rendering works") == true) return;
                }
            }
            catch (Exception ex) { last = ex.GetType().Name; }
            await Task.Delay(200);
        }
        throw new InvalidOperationException("HTTP browser content not ready. Last result: " + last + "; " + _failureText);
    }

    private async Task SavePreviewAsync(FrameworkElement root, string fileName)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using var reader = DataReader.FromBuffer(pixels);
        byte[] bytes = new byte[pixels.Length];
        reader.ReadBytes(bytes);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var output = File.Create(Path.Combine(_store.DataDirectory, fileName));
        await stream.AsStreamForRead().CopyToAsync(output);
    }
}
