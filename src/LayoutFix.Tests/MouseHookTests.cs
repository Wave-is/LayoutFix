using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Tests;

public sealed class MouseHookTests
{
    [Theory]
    [InlineData(Win32.WM_LBUTTONDOWN)]
    [InlineData(Win32.WM_RBUTTONDOWN)]
    [InlineData(Win32.WM_MBUTTONDOWN)]
    [InlineData(Win32.WM_XBUTTONDOWN)]
    public void ButtonDownMessages_AreObservedAsPhysicalInput(int message)
    {
        Assert.True(MouseHook.IsButtonDownMessage(message));
    }

    [Theory]
    [InlineData(0x0202)] // WM_LBUTTONUP
    [InlineData(0x020A)] // WM_MOUSEWHEEL
    [InlineData(0x020C)] // WM_XBUTTONUP
    public void NonButtonDownMessages_DoNotAdvanceInputGeneration(int message)
    {
        Assert.False(MouseHook.IsButtonDownMessage(message));
    }
}
