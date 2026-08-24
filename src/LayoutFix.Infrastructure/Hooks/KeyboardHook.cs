using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly KeyboardStateTracker _state = new();
    private long _inputGeneration;
    
    public static readonly IntPtr InjectedExtraInfo = new IntPtr(0x1337);
    public long InputGeneration => Interlocked.Read(ref _inputGeneration);

    public KeyboardHook(ILoggerService logger)
    {
        _logger = logger;
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId == IntPtr.Zero)
        {
            _state.Reset();
            _state.SeedModifiers(IsKeyPressed);
            _state.SeedToggleKeys(IsKeyToggled);
            _hookId = SetHook(_proc);
            if (_hookId == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the global keyboard hook.");
            Interlocked.Increment(ref _inputGeneration);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            WindowsHookLifecycle.EnsureUnhooked(_hookId, "keyboard");
            _hookId = IntPtr.Zero;
            _state.Reset();
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
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

                if (hookStruct.dwExtraInfo == InjectedExtraInfo)
                {
                    return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                var isKeyDown = wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN;
                var isKeyUp = wParam == (IntPtr)Win32.WM_KEYUP || wParam == (IntPtr)Win32.WM_SYSKEYUP;
                if (!isKeyDown && !isKeyUp)
                    return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);

                var vkCode = (int)hookStruct.vkCode;
                if (isKeyUp)
                {
                    _state.ProcessKeyUp(vkCode);
                    if (_state.ReleaseSuppression(vkCode))
                        return new IntPtr(1);

                    return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                _state.ReconcilePriorStateBeforeKeyDown(
                    IsKeyPressed,
                    vkCode,
                    hookStruct.time);
                var transition = _state.ProcessKeyDown(vkCode, hookStruct.flags, hookStruct.time);
                var suppressedRepeat = transition.IsRepeat && _state.IsSuppressed(vkCode);
                if (ShouldAdvanceInputGeneration(transition, suppressedRepeat))
                    Interlocked.Increment(ref _inputGeneration);

                if (transition.Combo == null)
                    return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);

                if (suppressedRepeat)
                    return new IntPtr(1);

                var text = GetTypedText(hookStruct);
                var args = new HotkeyEventArgs(
                    transition.Combo,
                    transition.IsRepeat,
                    text.Text,
                    text.IsDeadKey);
                HotkeyPressed?.Invoke(this, args);

                if (args.Handled)
                {
                    _state.SuppressUntilKeyUp(vkCode);
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

    private static bool IsKeyToggled(int virtualKey) =>
        (Win32.GetKeyState(virtualKey) & 0x0001) != 0;

    private KeyboardTextObservation GetTypedText(Win32.KBDLLHOOKSTRUCT keyboardEvent)
    {
        var foreground = Win32.GetForegroundWindow();
        var threadId = foreground == IntPtr.Zero
            ? 0
            : Win32.GetWindowThreadProcessId(foreground, out _);
        var keyboardLayout = Win32.GetKeyboardLayout(threadId);
        var buffer = new StringBuilder(8);
        var result = Win32.ToUnicodeEx(
            keyboardEvent.vkCode,
            keyboardEvent.scanCode,
            _state.CreateKeyboardState(),
            buffer,
            buffer.Capacity,
            Win32.TOUNICODE_NO_STATE_CHANGE,
            keyboardLayout);

        return KeyboardTextDecoder.Decode(result, buffer);
    }

    internal static string MapVirtualKeyToString(int vkCode)
    {
        var canonical = HotkeyCombo.GetCanonicalKeyName(vkCode);
        return canonical.Length == 0 ? "unknown" : canonical;
    }

    internal static bool ShouldAdvanceInputGeneration(
        KeyboardTransition transition,
        bool suppressedRepeat) =>
        !transition.IsRepeat || !suppressedRepeat;

    public void Dispose()
    {
        Stop();
    }
}
