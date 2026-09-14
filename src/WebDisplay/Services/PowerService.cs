using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WebDisplay.Services;

// Called only on the UI thread: execution state belongs to the calling thread.
public sealed class PowerService : IDisposable
{
    public bool IsActive { get; private set; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SetThreadExecutionState(uint flags);
    public void SetEnabled(bool enabled)
    {
        uint flags = 0x80000000u | (enabled ? 0x00000001u | 0x00000002u : 0u);
        if (SetThreadExecutionState(flags) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), L.Text("无法更新防休眠状态"));
        IsActive = enabled;
    }
    public void Dispose() { SetThreadExecutionState(0x80000000u); IsActive = false; }
}
