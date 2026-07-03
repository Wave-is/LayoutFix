using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Hooks;

public class MouseHook : IMouseHook
{
    public event EventHandler? MouseClicked;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly Win32.LowLevelKeyboardProc _proc;
    private readonly ILoggerService _logger;

    public MouseHook(ILoggerService logger)
    {
        _logger = logger;
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId == IntPtr.Zero)
        {
            _hookId = SetHook(_proc);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr SetHook(Win32.LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        return Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, proc,
            Win32.GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN || msg == Win32.WM_MBUTTONDOWN)
                {
                    MouseClicked?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception in MouseHook HookCallback", ex);
        }
        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }
}
