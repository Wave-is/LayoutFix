using System;
using System.Diagnostics;
using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

public class ActiveWindowProvider : IActiveWindowProvider
{
    public string GetActiveProcessName()
    {
        try
        {
            IntPtr hwnd = Win32.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid > 0)
                {
                    using var process = Process.GetProcessById((int)pid);
                    return process.ProcessName;
                }
            }
        }
        catch { }
        return string.Empty;
    }
    public string GetActiveLayoutCode()
    {
        return Win32.GetActiveLayoutCode();
    }

    public void SwitchToNextLayout()
    {
        var hwnd = Win32.GetForegroundWindow();
        Win32.PostMessage(hwnd, Win32.WM_INPUTLANGCHANGEREQUEST, (IntPtr)Win32.INPUTLANGCHANGE_FORWARD, IntPtr.Zero);
    }
}
