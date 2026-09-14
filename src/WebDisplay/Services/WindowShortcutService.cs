using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WebDisplay.Services;

public enum WindowShortcut
{
    ToggleFullscreen,
    ExitFullscreen,
    OpenSettings,
    Refresh
}

/// <summary>
/// Handles application shortcuts before WinUI/WebView2 dispatch on this window's
/// own UI thread. No global hook or system-wide hotkey registration is used.
/// </summary>
public sealed class WindowShortcutService : IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isEnabled;
    private readonly Action<WindowShortcut> _onShortcut;
    private readonly ShortcutNative.HookProc _callback;
    private readonly HashSet<uint> _suppressedKeys = new();
    private IntPtr _hook;
    private bool _disposed;

    public WindowShortcutService(Window window, Func<bool> isEnabled, Action<WindowShortcut> onShortcut)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _dispatcher = window.DispatcherQueue;
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _onShortcut = onShortcut ?? throw new ArgumentNullException(nameof(onShortcut));
        _callback = OnGetMessage;
        uint threadId = ShortcutNative.GetWindowThreadProcessId(_windowHandle, out uint processId);
        if (threadId == 0 || processId != (uint)Environment.ProcessId)
            throw new InvalidOperationException(L.Text("无法为当前应用窗口初始化快捷键。"));
        // WH_GETMESSAGE with a nonzero thread ID observes only that thread.
        _hook = ShortcutNative.SetWindowsHookEx(3, _callback, IntPtr.Zero, threadId);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.Text("无法初始化窗口快捷键。"));
    }

    private IntPtr OnGetMessage(int code, UIntPtr removeFlag, IntPtr messagePointer)
    {
        try
        {
            // PM_REMOVE only: a PM_NOREMOVE peek must not trigger an action twice.
            if (!_disposed && code >= 0 && removeFlag.ToUInt64() == 1 && messagePointer != IntPtr.Zero)
            {
                var message = Marshal.PtrToStructure<ShortcutNative.Message>(messagePointer);
                bool down = message.Id is 0x0100 or 0x0104;
                bool up = message.Id is 0x0101 or 0x0105;
                if (down || up)
                {
                    uint key = unchecked((uint)message.WParam.ToUInt64());
                    bool suppressed = up ? _suppressedKeys.Remove(key) : _suppressedKeys.Contains(key);
                    if (BelongsToWindow(message.Window) && IsForegroundWindow() && _isEnabled())
                    {
                        if (suppressed)
                        {
                            Consume(ref message, messagePointer);
                        }
                        else if (down && TryGetShortcut(key, out WindowShortcut shortcut))
                        {
                            // Bit 30 is set on an auto-repeated key-down message.
                            bool repeated = (message.LParam.ToInt64() & (1L << 30)) != 0;
                            _suppressedKeys.Add(key);
                            Consume(ref message, messagePointer);
                            if (!repeated)
                            {
                                _dispatcher.TryEnqueue(() =>
                                {
                                    if (_disposed || !IsForegroundWindow() || !_isEnabled()) return;
                                    try { _onShortcut(shortcut); }
                                    catch (Exception ex) { AppLog.Write("Window shortcut action: " + ex.GetType().Name); }
                                });
                            }
                        }
                    }
                    else if (down)
                    {
                        // Do not retain a key state when another app/window has focus.
                        _suppressedKeys.Remove(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Managed exceptions must never escape a native hook callback.
            AppLog.Write("Window shortcut dispatch: " + ex.GetType().Name);
        }
        return ShortcutNative.CallNextHookEx(_hook, code, removeFlag, messagePointer);
    }

    private bool BelongsToWindow(IntPtr handle) => handle == _windowHandle || ShortcutNative.IsChild(_windowHandle, handle);
    private bool IsForegroundWindow() => BelongsToWindow(ShortcutNative.GetForegroundWindow());
    private static bool IsDown(int key) => (ShortcutNative.GetKeyState(key) & 0x8000) != 0;

    private static bool TryGetShortcut(uint key, out WindowShortcut shortcut)
    {
        shortcut = default;
        // Inspect modifiers only for the four application shortcut keys.
        if (key is not (0x7A or 0x1B or 0xBC or 0x52)) return false;
        if (IsDown(0x10) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C)) return false;
        bool control = IsDown(0x11);
        switch (key)
        {
            case 0x7A when !control: shortcut = WindowShortcut.ToggleFullscreen; return true;
            case 0x1B when !control: shortcut = WindowShortcut.ExitFullscreen; return true;
            case 0xBC when control: shortcut = WindowShortcut.OpenSettings; return true;
            case 0x52 when control: shortcut = WindowShortcut.Refresh; return true;
            default: return false;
        }
    }

    private static void Consume(ref ShortcutNative.Message message, IntPtr pointer)
    {
        message.Id = 0; // WM_NULL: the browser and XAML must not act on it again.
        message.WParam = UIntPtr.Zero;
        message.LParam = IntPtr.Zero;
        Marshal.StructureToPtr(message, pointer, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            ShortcutNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _suppressedKeys.Clear();
    }
}

internal static class ShortcutNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X; internal int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal IntPtr Window;
        internal uint Id;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal Point Position;
        internal uint Private;
    }

    internal delegate IntPtr HookProc(int code, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int virtualKey);
}
