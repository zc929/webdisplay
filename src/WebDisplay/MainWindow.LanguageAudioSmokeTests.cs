using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using WebDisplay.Services;

namespace WebDisplay;

public sealed partial class MainWindow
{
    private void AssertPageMutedForSmoke(bool expected)
    {
        if (_browser?.CoreWebView2 == null || _browser.CoreWebView2.IsMuted != expected)
            throw new InvalidOperationException("Unexpected native WebView page mute state: " + expected);
    }

    private async Task RunLanguageAudioSmokeChecksAsync()
    {
        AssertPageMutedForSmoke(false);
        await _browser!.CoreWebView2.ExecuteScriptAsync("window.webDisplayUiToken='kept';");
        foreach (string language in new[] { "en-US", "zh-TW", "zh-CN" })
        {
            await SaveLanguageAndAudioFromUiForSmokeAsync(language, true);
            AssertPageMutedForSmoke(true);
            if (_settings.Language != language || _store.Load().Language != language ||
                !_store.Load().MutePage || RootGrid.Language != language ||
                Title != L.Text("网页展示器") || SettingsButtonText.Text != L.Text("设置"))
                throw new InvalidOperationException("Saved language did not reach the window or persisted settings: " + language);
            if (await _browser.CoreWebView2.ExecuteScriptAsync("window.webDisplayUiToken") != "\"kept\"")
                throw new InvalidOperationException("Language/audio changes unexpectedly reloaded the website");
            _smokeChecks.Add(language + " saves, updates the main window immediately, and preserves the current page");
            await CaptureLocalizedSettingsForSmokeAsync(language);
        }
        await SaveLanguageAndAudioFromUiForSmokeAsync("zh-CN", false);
        AssertPageMutedForSmoke(false);
        if (_store.Load().MutePage) throw new InvalidOperationException("Unmute did not persist");
        await SaveLanguageAndAudioFromUiForSmokeAsync("zh-CN", true);
        AssertPageMutedForSmoke(true);
        _smokeChecks.Add("Page audio can be muted and unmuted through Save without reloading the website");
    }

    private async Task SaveLanguageAndAudioFromUiForSmokeAsync(string language, bool muted)
    {
        string oldLanguage = L.Language;
        bool oldMuted = _browser!.CoreWebView2.IsMuted;
        var save = ShowSettingsAsync();
        await WaitForConditionAsync(() => _settingsWindow != null, "settings window opened", 5);
        await Task.Delay(250);
        var root = (FrameworkElement)_settingsWindow!.Content;
        var languageBox = (ComboBox)root.FindName("LanguageBox");
        foreach (ComboBoxItem item in languageBox.Items)
            if (item.Tag as string == language) languageBox.SelectedItem = item;
        ((ToggleSwitch)root.FindName("MutePageToggle")).IsOn = muted;
        if (L.Language != oldLanguage || _browser.CoreWebView2.IsMuted != oldMuted)
            throw new InvalidOperationException("Unsaved language/audio choices changed the display");
        var invoke = (IInvokeProvider)new ButtonAutomationPeer((Button)root.FindName("SaveButton")).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        if (!await save.WaitAsync(TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Language/audio save did not complete");
    }

    private async Task CaptureLocalizedSettingsForSmokeAsync(string language)
    {
        var closed = ShowSettingsAsync();
        await WaitForConditionAsync(() => _settingsWindow != null, "localized settings window opened", 5);
        await Task.Delay(300);
        var dialog = _settingsWindow!;
        var root = (FrameworkElement)dialog.Content;
        var languageBox = (ComboBox)root.FindName("LanguageBox");
        var mute = (ToggleSwitch)root.FindName("MutePageToggle");
        if ((languageBox.SelectedItem as ComboBoxItem)?.Tag as string != language ||
            !mute.IsOn || (mute.Header as TextBlock)?.Text != L.Text("网页静音") ||
            (string)((Button)root.FindName("SaveButton")).Content != L.Text("保存设置") ||
            dialog.Title != L.Text("网页展示 · 设置"))
            throw new InvalidOperationException("Reopened settings did not load translated values: " + language);
        var pivot = (Pivot)root.FindName("SettingsPivot");
        var themeBox = (ComboBox)root.FindName("ThemePreferenceBox");
        var displayScroll = (ScrollViewer)root.FindName("DisplayScroll");
        foreach (var (theme, index, name) in new[] { (ElementTheme.Dark, 2, "dark"), (ElementTheme.Light, 1, "light") })
        {
            themeBox.SelectedIndex = index;
            await WaitForConditionAsync(() => root.ActualTheme == theme && RootGrid.ActualTheme == theme, "localized theme preview", 5);
            pivot.SelectedIndex = 0;
            displayScroll.ChangeView(null, 0, null, true);
            await Task.Delay(800);
            await SavePreviewAsync(root, "settings-" + language + "-display-" + name + ".png");
            mute.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
            await Task.Delay(350);
            await SavePreviewAsync(root, "settings-" + language + "-audio-" + name + ".png");
            pivot.SelectedIndex = 1;
            await Task.Delay(800);
            await SavePreviewAsync(root, "settings-" + language + "-system-" + name + ".png");
        }
        dialog.Close();
        if (await closed || L.Language != language || _settings.Language != language)
            throw new InvalidOperationException("Closing the localized preview changed the saved language");
        _smokeChecks.Add(language + " settings reopen with translated controls in both dark and light themes");
    }
}