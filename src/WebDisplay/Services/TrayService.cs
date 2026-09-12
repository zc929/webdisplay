using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WebDisplay.Services;

/// <summary>Native notification-area integration without a WinForms message loop.</summary>
public sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 137;
    private const uint FirstCommand = 0x7201;
    private static readonly string[] Labels = { "显示窗口", "设置", "刷新网页", "切换全屏", "退出程序" };
    private static readonly string[] Shortcuts = { "", "Ctrl+,", "Ctrl+R", "F11", "" };
    private readonly Window _owner;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _retryTimer;
    private readonly Action[] _actions;
    private readonly Func<bool> _isDark;
    private readonly TrayNative.SubclassProc _subclass;
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreated;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly UIntPtr _subclassId = new UIntPtr(0x57445452);
    private bool _subclassInstalled;
    private bool _registered;
    private bool _version4;
    private bool _disposed;
    private bool _menuOpen;
    private IntPtr _activeMenu;
    private IntPtr _menuFont;
    private bool _menuDark;
    private double _menuScale = 1;

    public TrayService(Window owner, Action show, Action settings, Action refresh,
        Action fullscreen, Action exit, Func<bool> isDark)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _dispatcher = owner.DispatcherQueue;
        _actions = new[] { show, settings, refresh, fullscreen, exit };
        _isDark = isDark ?? throw new ArgumentNullException(nameof(isDark));
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        _taskbarCreated = TrayNative.RegisterWindowMessage("TaskbarCreated");
        _subclass = WindowProcedure;
        _retryTimer = _dispatcher.CreateTimer();
        _retryTimer.Interval = TimeSpan.FromSeconds(4);
        _retryTimer.IsRepeating = true;
        _retryTimer.Tick += OnRetry;
        (IntPtr large, IntPtr small) = WindowInteropNative.ExtractApplicationIcons();
        _icon = small != IntPtr.Zero ? small : large;
        _ownsIcon = _icon != IntPtr.Zero;
        if (small != IntPtr.Zero && large != IntPtr.Zero && small != large)
            WindowInteropNative.DestroyIcon(large);
        if (_icon == IntPtr.Zero)
            _icon = TrayNative.LoadIcon(IntPtr.Zero, new IntPtr(32512));
        if (!TrayNative.SetWindowSubclass(_hwnd, _subclass, _subclassId, UIntPtr.Zero))
        {
            _retryTimer.Tick -= OnRetry;
            if (_ownsIcon) WindowInteropNative.DestroyIcon(_icon);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建通知区域图标的窗口消息处理程序。");
        }
        _subclassInstalled = true;
        _owner.Closed += OnOwnerClosed;
        AddIcon();
    }

    private TrayNative.NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<TrayNative.NotifyIconData>(),
        Window = _hwnd,
        Id = 1,
        Flags = 0x0001 | 0x0002 | 0x0004 | 0x0080, // MESSAGE, ICON, TIP, SHOWTIP
        CallbackMessage = CallbackMessage,
        Icon = _icon,
        Tip = "网页展示器 — 双击显示，右键打开菜单",
        Info = string.Empty,
        InfoTitle = string.Empty,
        Version = 4
    };

    private void AddIcon()
    {
        if (_disposed) return;
        TrayNative.NotifyIconData data = IconData();
        _registered = TrayNative.ShellNotifyIcon(0, ref data);
        if (_registered)
        {
            _version4 = TrayNative.ShellNotifyIcon(4, ref data);
            _retryTimer.Stop();
        }
        else
        {
            _retryTimer.Start();
        }
    }

    private void OnRetry(DispatcherQueueTimer sender, object args)
    {
        if (!_registered) AddIcon();
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        // Exceptions must never cross a native window-procedure callback.
        try
        {
            if (!_disposed && _taskbarCreated != 0 && message == _taskbarCreated)
            {
                _registered = false;
                AddIcon();
            }
            else if (!_disposed && message == CallbackMessage)
            {
                uint notification = _version4 ? (uint)(lParam.ToInt64() & 0xFFFF) : unchecked((uint)lParam.ToInt64());
                if (notification == 0x0203 || notification == 0x0401) // double-click / keyboard select
                    QueueAction(0);
                else if (notification == 0x007B || notification == 0x0205) // context menu / legacy right-up
                    ShowMenu(wParam);
                return IntPtr.Zero;
            }
            else if (!_disposed && message == 0x002C && MeasureMenuItem(lParam))
                return new IntPtr(1);
            else if (!_disposed && message == 0x002B && DrawMenuItem(lParam))
                return new IntPtr(1);
            else if (message == 0x0082) // WM_NCDESTROY
                DisposeCore();
        }
        catch (Exception ex)
        {
            AppLog.Write("Tray callback: " + ex.GetType().Name);
        }
        return TrayNative.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void QueueAction(int index)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { _actions[index](); }
            catch (Exception ex)
            {
                AppLog.Write("Tray action: " + ex.GetType().Name);
                WindowInteropService.ShowMessage(_owner, "操作未完成", ex.Message);
            }
        });
    }

    private void ShowMenu(UIntPtr position)
    {
        if (_menuOpen || _disposed) return;
        _menuOpen = true;
        _menuDark = _isDark();
        _menuScale = Math.Max(96, WindowInteropNative.GetDpiForWindow(_hwnd)) / 96.0;
        IntPtr background = TrayNative.CreateSolidBrush(_menuDark ? 0x00282828u : 0x00F8F8F8u);
        _activeMenu = TrayNative.CreatePopupMenu();
        _menuFont = TrayNative.CreateFont(-Scale(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        uint selected = 0;
        try
        {
            if (_activeMenu == IntPtr.Zero) return;
            var menuInfo = new TrayNative.MenuInfo
            {
                Size = (uint)Marshal.SizeOf<TrayNative.MenuInfo>(),
                Mask = 0x00000002,
                Background = background
            };
            TrayNative.SetMenuInfo(_activeMenu, ref menuInfo);
            for (uint i = 0; i < Labels.Length; i++)
            {
                var item = new TrayNative.MenuItemInfo
                {
                    Size = (uint)Marshal.SizeOf<TrayNative.MenuItemInfo>(),
                    Mask = 0x00000002 | 0x00000020 | 0x00000040 | 0x00000100,
                    Type = 0x00000100,
                    Id = FirstCommand + i,
                    ItemData = new UIntPtr(FirstCommand + i),
                    TypeData = Labels[i],
                    TextLength = (uint)Labels[i].Length
                };
                TrayNative.InsertMenuItem(_activeMenu, i, true, ref item);
            }
            TrayNative.GetCursorPos(out TrayNative.Point cursor);
            if (_version4)
            {
                long packed = unchecked((long)position.ToUInt64());
                int x = (short)(packed & 0xFFFF);
                int y = (short)((packed >> 16) & 0xFFFF);
                if (x != -1 || y != -1)
                    cursor = new TrayNative.Point { X = x, Y = y };
                else
                {
                    var identifier = new TrayNative.NotifyIconIdentifier
                    {
                        Size = (uint)Marshal.SizeOf<TrayNative.NotifyIconIdentifier>(), Window = _hwnd, Id = 1
                    };
                    if (TrayNative.ShellNotifyIconGetRect(ref identifier, out WindowInteropNative.Rect rect) == 0)
                        cursor = new TrayNative.Point { X = rect.Left, Y = rect.Top };
                }
            }
            WindowInteropNative.SetForegroundWindow(_hwnd);
            selected = TrayNative.TrackPopupMenuEx(_activeMenu, 0x0100 | 0x0080 | 0x0002 | 0x0020,
                cursor.X, cursor.Y, _hwnd, IntPtr.Zero);
            TrayNative.PostMessage(_hwnd, 0, UIntPtr.Zero, IntPtr.Zero);
            if (selected == 0 && _registered)
            {
                TrayNative.NotifyIconData data = IconData();
                TrayNative.ShellNotifyIcon(3, ref data);
            }
        }
        finally
        {
            if (_activeMenu != IntPtr.Zero) TrayNative.DestroyMenu(_activeMenu);
            if (_menuFont != IntPtr.Zero) TrayNative.DeleteObject(_menuFont);
            if (background != IntPtr.Zero) TrayNative.DeleteObject(background);
            _activeMenu = IntPtr.Zero;
            _menuFont = IntPtr.Zero;
            _menuOpen = false;
        }
        if (selected >= FirstCommand && selected < FirstCommand + Labels.Length)
            QueueAction((int)(selected - FirstCommand));
    }

    private int Scale(int logicalPixels) => (int)Math.Round(logicalPixels * _menuScale);

    private bool MeasureMenuItem(IntPtr pointer)
    {
        if (!_menuOpen || pointer == IntPtr.Zero) return false;
        TrayNative.MeasureItem item = Marshal.PtrToStructure<TrayNative.MeasureItem>(pointer);
        if (item.ControlType != 1 || item.ItemId < FirstCommand || item.ItemId >= FirstCommand + Labels.Length)
            return false;
        item.Width = (uint)Scale(250);
        item.Height = (uint)Scale(38);
        Marshal.StructureToPtr(item, pointer, false);
        return true;
    }

    private bool DrawMenuItem(IntPtr pointer)
    {
        if (!_menuOpen || pointer == IntPtr.Zero) return false;
        TrayNative.DrawItem item = Marshal.PtrToStructure<TrayNative.DrawItem>(pointer);
        if (item.ControlType != 1 || item.ItemWindow != _activeMenu
            || item.ItemId < FirstCommand || item.ItemId >= FirstCommand + Labels.Length)
            return false;
        int index = (int)(item.ItemId - FirstCommand);
        bool selected = (item.State & 0x0001) != 0;
        uint background = _menuDark ? (selected ? 0x00414141u : 0x00282828u) : (selected ? 0x00E9E9E9u : 0x00F8F8F8u);
        uint foreground = _menuDark ? 0x00F2F2F2u : 0x001F1F1Fu;
        uint secondary = _menuDark ? 0x00B8B8B8u : 0x00606060u;
        int saved = TrayNative.SaveDC(item.DeviceContext);
        IntPtr brush = TrayNative.CreateSolidBrush(background);
        try
        {
            TrayNative.FillRect(item.DeviceContext, ref item.Rectangle, brush);
            if (_menuFont != IntPtr.Zero) TrayNative.SelectObject(item.DeviceContext, _menuFont);
            TrayNative.SetBkMode(item.DeviceContext, 1);
            WindowInteropNative.Rect text = item.Rectangle;
            text.Left += Scale(18);
            text.Right -= Scale(18);
            TrayNative.SetTextColor(item.DeviceContext, foreground);
            TrayNative.DrawText(item.DeviceContext, Labels[index], -1, ref text, 0x0020 | 0x0004 | 0x0800);
            if (Shortcuts[index].Length > 0)
            {
                TrayNative.SetTextColor(item.DeviceContext, secondary);
                TrayNative.DrawText(item.DeviceContext, Shortcuts[index], -1, ref text, 0x0020 | 0x0004 | 0x0002 | 0x0800);
            }
        }
        finally
        {
            TrayNative.RestoreDC(item.DeviceContext, saved);
            if (brush != IntPtr.Zero) TrayNative.DeleteObject(brush);
        }
        return true;
    }

    private void OnOwnerClosed(object sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        if (_dispatcher.HasThreadAccess) DisposeCore();
        else _dispatcher.TryEnqueue(DisposeCore);
    }

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        _retryTimer.Stop();
        _retryTimer.Tick -= OnRetry;
        _owner.Closed -= OnOwnerClosed;
        if (_registered)
        {
            TrayNative.NotifyIconData data = IconData();
            TrayNative.ShellNotifyIcon(2, ref data);
            _registered = false;
        }
        if (_subclassInstalled)
        {
            TrayNative.RemoveWindowSubclass(_hwnd, _subclass, _subclassId);
            _subclassInstalled = false;
        }
        if (_ownsIcon && _icon != IntPtr.Zero) WindowInteropNative.DestroyIcon(_icon);
    }
}

internal static class TrayNative
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid GuidItem;
        internal IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotifyIconIdentifier { internal uint Size; internal IntPtr Window; internal uint Id; internal Guid GuidItem; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MenuInfo
    {
        internal uint Size;
        internal uint Mask;
        internal uint Style;
        internal uint MaxHeight;
        internal IntPtr Background;
        internal uint ContextHelpId;
        internal UIntPtr MenuData;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MenuItemInfo
    {
        internal uint Size;
        internal uint Mask;
        internal uint Type;
        internal uint State;
        internal uint Id;
        internal IntPtr SubMenu;
        internal IntPtr CheckedBitmap;
        internal IntPtr UncheckedBitmap;
        internal UIntPtr ItemData;
        [MarshalAs(UnmanagedType.LPWStr)] internal string TypeData;
        internal uint TextLength;
        internal IntPtr ItemBitmap;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MeasureItem
    {
        internal uint ControlType;
        internal uint ControlId;
        internal uint ItemId;
        internal uint Width;
        internal uint Height;
        internal UIntPtr ItemData;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DrawItem
    {
        internal uint ControlType;
        internal uint ControlId;
        internal uint ItemId;
        internal uint Action;
        internal uint State;
        internal IntPtr ItemWindow;
        internal IntPtr DeviceContext;
        internal WindowInteropNative.Rect Rectangle;
        internal UIntPtr ItemData;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")]
    internal static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string message);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShellNotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    internal static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identifier, out WindowInteropNative.Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenuInfo(IntPtr menu, ref MenuInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InsertMenuItem(IntPtr menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo info);
    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")]
    internal static extern int SaveDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RestoreDC(IntPtr dc, int saved);
    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("user32.dll")]
    internal static extern int FillRect(IntPtr dc, ref WindowInteropNative.Rect rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(IntPtr dc, string text, int length, ref WindowInteropNative.Rect rect, uint format);
}
