using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace WebDisplay.Services;

public static class WindowInteropService
{
    /// <summary>Sizes in logical pixels, with centering and clamping to the monitor work area.</summary>
    public static void Initialize(Window window, int width, int height, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        IntPtr ownerHwnd = owner is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(owner);
        if (hwnd == IntPtr.Zero) return;
        if (ownerHwnd != IntPtr.Zero)
            WindowInteropNative.SetOwner(hwnd, ownerHwnd);

        IntPtr monitor = WindowInteropNative.MonitorFromWindow(ownerHwnd != IntPtr.Zero ? ownerHwnd : hwnd, 2);
        var info = new WindowInteropNative.MonitorInfo { Size = Marshal.SizeOf<WindowInteropNative.MonitorInfo>() };
        if (WindowInteropNative.GetMonitorInfo(monitor, ref info))
        {
            uint dpi = WindowInteropNative.GetDpiForWindow(ownerHwnd != IntPtr.Zero ? ownerHwnd : hwnd);
            double scale = Math.Max(96, dpi) / 96.0;
            int workWidth = info.Work.Right - info.Work.Left;
            int workHeight = info.Work.Bottom - info.Work.Top;
            int actualWidth = Math.Clamp((int)Math.Round(width * scale), Math.Min(320, workWidth), workWidth);
            int actualHeight = Math.Clamp((int)Math.Round(height * scale), Math.Min(240, workHeight), workHeight);
            WindowInteropNative.Rect center = info.Work;
            if (ownerHwnd != IntPtr.Zero && WindowInteropNative.GetWindowRect(ownerHwnd, out WindowInteropNative.Rect ownerRect))
                center = ownerRect;
            int x = Math.Clamp(center.Left + (center.Right - center.Left - actualWidth) / 2, info.Work.Left, info.Work.Right - actualWidth);
            int y = Math.Clamp(center.Top + (center.Bottom - center.Top - actualHeight) / 2, info.Work.Top, info.Work.Bottom - actualHeight);
            WindowInteropNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, actualWidth, actualHeight, 0x0004 | 0x0010);
        }

        (IntPtr large, IntPtr small) = WindowInteropNative.ExtractApplicationIcons();
        if (large != IntPtr.Zero) WindowInteropNative.SendMessage(hwnd, 0x0080, new IntPtr(1), large);
        if (small != IntPtr.Zero) WindowInteropNative.SendMessage(hwnd, 0x0080, IntPtr.Zero, small);
        if (large != IntPtr.Zero || small != IntPtr.Zero)
        {
            // Icons extracted with ExtractIconEx are owned by this application.
            // Retain them for the window lifetime instead of releasing handles
            // immediately after WM_SETICON.
            void OnClosed(object sender, WindowEventArgs args)
            {
                window.Closed -= OnClosed;
                if (large != IntPtr.Zero) WindowInteropNative.DestroyIcon(large);
                if (small != IntPtr.Zero && small != large) WindowInteropNative.DestroyIcon(small);
            }
            window.Closed += OnClosed;
        }
    }

    public static void SetEnabled(Window window, bool enabled)
        => WindowInteropNative.EnableWindow(WinRT.Interop.WindowNative.GetWindowHandle(window), enabled);

    public static void BringToFront(Window window)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WindowInteropNative.ShowWindow(hwnd, WindowInteropNative.IsIconic(hwnd) ? 9 : 5);
        window.Activate();
        WindowInteropNative.SetForegroundWindow(hwnd);
    }

    public static bool IsMinimized(Window window)
        => WindowInteropNative.IsIconic(WinRT.Interop.WindowNative.GetWindowHandle(window));

    /// <summary>Last-resort startup/error UI when a XAML dialog cannot be shown.</summary>
    public static void ShowMessage(Window? owner, string title, string message)
    {
        IntPtr hwnd = owner is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(owner);
        WindowInteropNative.MessageBox(hwnd, L.Text(message), L.Text(title), 0x00000010 | 0x00010000);
    }
}

internal static class WindowInteropNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
    }

    internal static (IntPtr Large, IntPtr Small) ExtractApplicationIcons()
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path)) return (IntPtr.Zero, IntPtr.Zero);
        ExtractIconEx(path, 0, out IntPtr large, out IntPtr small, 1);
        return (large, small);
    }

    internal static void SetOwner(IntPtr hwnd, IntPtr owner)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(hwnd, -8, owner);
        else SetWindowLong(hwnd, -8, owner.ToInt32());
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);
}
