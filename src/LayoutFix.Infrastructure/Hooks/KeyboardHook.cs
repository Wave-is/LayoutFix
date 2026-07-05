using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Hooks;

public class KeyboardHook : IKeyboardHook
{
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly Win32.LowLevelKeyboardProc _proc;
    private readonly ILoggerService _logger;
    
    public static readonly IntPtr InjectedExtraInfo = new IntPtr(0x1337);

    public KeyboardHook(ILoggerService logger)
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
        return Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, proc,
            Win32.GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN))
            {
                var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

                if (hookStruct.dwExtraInfo == InjectedExtraInfo)
                {
                    return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                int vkCode = (int)hookStruct.vkCode;

                if (vkCode == Win32.VK_SHIFT || vkCode == Win32.VK_CONTROL || vkCode == Win32.VK_MENU || 
                    vkCode == Win32.VK_LWIN || vkCode == Win32.VK_RWIN || 
                    vkCode == 0xA0 || vkCode == 0xA1 || vkCode == 0xA2 || 
                    vkCode == 0xA3 || vkCode == 0xA4 || vkCode == 0xA5)
                {
                    return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                bool isAlt = IsKeyPressed(Win32.VK_MENU);
                bool isCtrl = IsKeyPressed(Win32.VK_CONTROL);
                bool isShift = IsKeyPressed(Win32.VK_SHIFT);
                bool isWin = IsKeyPressed(Win32.VK_LWIN) || IsKeyPressed(Win32.VK_RWIN);
                bool isPrintScreen = IsKeyPressed(0x2C); // VK_SNAPSHOT

                string keyStr = MapVirtualKeyToString(vkCode);

                var combo = new HotkeyCombo
                {
                    Alt = isAlt,
                    Ctrl = isCtrl,
                    Shift = isShift,
                    Win = isWin,
                    PrintScreen = isPrintScreen,
                    Key = keyStr,
                    VirtualKey = vkCode
                };

                var args = new HotkeyEventArgs(combo);
                HotkeyPressed?.Invoke(this, args);

                if (args.Handled)
                {
                    return new IntPtr(1);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception in HookCallback", ex);
        }
        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool IsKeyPressed(int vKey)
    {
        return (Win32.GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    private string MapVirtualKeyToString(int vkCode)
    {
        if (vkCode >= 'A' && vkCode <= 'Z') return ((char)vkCode).ToString().ToLower();
        if (vkCode >= '0' && vkCode <= '9') return ((char)vkCode).ToString();
        if (vkCode >= 0x70 && vkCode <= 0x7B) return "f" + (vkCode - 0x70 + 1);
        
        return vkCode switch
        {
            0x20 => "space",
            0x0D => "enter",
            0x1B => "esc",
            0x09 => "tab",
            0x13 => "pause",
            0x14 => "capslock",
            0x91 => "scroll",
            0x2D => "insert",
            0x08 => "backspace",
            0x2E => "delete",
            0x24 => "home",
            0x23 => "end",
            0x21 => "pageup",
            0x22 => "pagedown",
            0x25 => "left",
            0x26 => "up",
            0x27 => "right",
            0x28 => "down",
            0x2C => "printscreen",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0xBA => ";",
            0xDE => "'",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xC0 => "`",
            0xBD => "-",
            0xBB => "=",
            _ => "unknown"
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
