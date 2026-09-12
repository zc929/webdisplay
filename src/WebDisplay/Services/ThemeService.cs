using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace WebDisplay.Services;

/// <summary>Applies an explicit or system theme without changing global Windows settings.</summary>
public sealed class ThemeService : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private bool _disposed;

    public ThemeService(Window window, FrameworkElement root, string preference)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _root.ActualThemeChanged += OnActualThemeChanged;
        _root.Loaded += OnLoaded;
        SetPreference(preference);
    }

    public ElementTheme EffectiveTheme => _root.ActualTheme;

    public event EventHandler? ThemeChanged;

    public void SetPreference(string preference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root.RequestedTheme = preference switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        ApplyTitleBarTheme();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => ApplyTitleBarTheme();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_disposed) return;
        ApplyTitleBarTheme();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTitleBarTheme()
    {
        if (_disposed) return;
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (hwnd == IntPtr.Zero) return;
        int dark = EffectiveTheme == ElementTheme.Dark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on current Windows 10/11;
        // earlier supported Windows 10 builds used attribute 19.
        if (ThemeNative.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) < 0)
            ThemeNative.DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.ActualThemeChanged -= OnActualThemeChanged;
        _root.Loaded -= OnLoaded;
        ThemeChanged = null;
    }
}

internal static class ThemeNative
{
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
}
