using System.ComponentModel;
using System.Runtime.InteropServices;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Hooks;

internal static class WindowsHookLifecycle
{
    internal static void EnsureUnhooked(
        IntPtr hookHandle,
        string hookName,
        Func<IntPtr, bool>? unhook = null,
        Func<int>? getLastError = null)
    {
        unhook ??= Win32.UnhookWindowsHookEx;
        if (unhook(hookHandle))
            return;

        getLastError ??= Marshal.GetLastWin32Error;
        var error = getLastError();
        if (error == Win32.ERROR_INVALID_HOOK_HANDLE)
            return;

        throw new Win32Exception(error, $"Unable to remove the global {hookName} hook.");
    }
}
