using System.ComponentModel;
using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Tests;

public sealed class WindowsHookLifecycleTests
{
    [Fact]
    public void SuccessfulUnhook_CompletesWithoutReadingLastError()
    {
        var lastErrorRead = false;

        WindowsHookLifecycle.EnsureUnhooked(
            new IntPtr(42),
            "test",
            handle => handle == new IntPtr(42),
            () =>
            {
                lastErrorRead = true;
                return 5;
            });

        Assert.False(lastErrorRead);
    }

    [Fact]
    public void AlreadyRemovedHook_IsSafeToForget()
    {
        WindowsHookLifecycle.EnsureUnhooked(
            new IntPtr(42),
            "test",
            _ => false,
            () => Win32.ERROR_INVALID_HOOK_HANDLE);
    }

    [Fact]
    public void OtherUnhookFailure_ThrowsAndPreservesNativeError()
    {
        var exception = Assert.Throws<Win32Exception>(() =>
            WindowsHookLifecycle.EnsureUnhooked(
                new IntPtr(42),
                "keyboard",
                _ => false,
                () => 5));

        Assert.Equal(5, exception.NativeErrorCode);
        Assert.Contains("keyboard", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
