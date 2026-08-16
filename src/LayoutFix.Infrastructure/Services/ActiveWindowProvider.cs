using System;
using System.Diagnostics;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

public class ActiveWindowProvider : IActiveWindowProvider
{
    private readonly ActiveProcessNameCache _processNames = new();

    public ActiveWindowContext CaptureActiveWindow()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return default;

        var threadId = Win32.GetWindowThreadProcessId(foreground, out var processId);
        var focused = foreground;
        var guiInfo = new Win32.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.GUITHREADINFO>() };
        if (threadId != 0 && Win32.GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndFocus != IntPtr.Zero)
            focused = guiInfo.hwndFocus;

        return new ActiveWindowContext(foreground, focused, processId);
    }

    public bool IsSameActiveWindow(ActiveWindowContext context) =>
        context.IsValid && CaptureActiveWindow() == context;

    public string GetActiveProcessName()
    {
        try
        {
            var window = Win32.GetForegroundWindow();
            if (window == IntPtr.Zero)
                return string.Empty;

            Win32.GetWindowThreadProcessId(window, out var processId);
            return _processNames.GetOrResolve(window, processId, ResolveProcessName);
        }
        catch { }
        return string.Empty;
    }

    private static string ResolveProcessName(uint processId)
    {
        using var process = Process.GetProcessById(checked((int)processId));
        return process.ProcessName;
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

    public bool TrySwitchToLayout(string layoutCode)
    {
        var installed = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>();
        InputLanguage? target;
        if (KeyboardLayoutIdentity.TryGetNativeHandle(layoutCode, out var nativeHandle))
        {
            target = installed.FirstOrDefault(language =>
                unchecked((uint)language.Handle.ToInt64()) == nativeHandle);
        }
        else
        {
            var cultureCode = KeyboardLayoutIdentity.GetCultureCode(layoutCode);
            target = installed.FirstOrDefault(language => string.Equals(
                language.Culture.Name,
                cultureCode,
                StringComparison.OrdinalIgnoreCase));
        }

        if (target == null)
            return false;

        var hwnd = Win32.GetForegroundWindow();
        return hwnd != IntPtr.Zero && Win32.PostMessage(
            hwnd,
            Win32.WM_INPUTLANGCHANGEREQUEST,
            IntPtr.Zero,
            target.Handle);
    }
}
