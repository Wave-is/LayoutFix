using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Services;
using Microsoft.Win32;

namespace LayoutFix.Tests;

public sealed class WindowsSessionRecoveryMonitorTests
{
    [Fact]
    public void SupportedSignals_RequestRecoveryAndUnsupportedSignalsAreIgnored()
    {
        var coordinator = new HookRecoveryCoordinator(
            new FakeKeyboardHook(),
            new FakeMouseHook(),
            new FakeLogger());
        using var monitor = new WindowsSessionRecoveryMonitor(coordinator, new ThrowingLogger());

        Assert.False(monitor.HandlePowerModeChanged(PowerModes.Suspend));
        Assert.False(monitor.HandlePowerModeChanged(PowerModes.StatusChange));
        Assert.False(monitor.HandleSessionSwitch(SessionSwitchReason.SessionLock));
        Assert.False(monitor.HandleSessionSwitch(SessionSwitchReason.SessionLogoff));
        Assert.True(monitor.HandlePowerModeChanged(PowerModes.Resume));
        Assert.True(monitor.HandleSessionSwitch(SessionSwitchReason.SessionUnlock));
        Assert.True(monitor.HandleSessionSwitch(SessionSwitchReason.RemoteConnect));
        Assert.True(monitor.HandleSessionSwitch(SessionSwitchReason.ConsoleConnect));

        Assert.Equal(4, coordinator.RecoveryRequestCount);
        Assert.True(coordinator.HasPendingRecovery);
        Assert.True(coordinator.RecoverIfRequested());
        Assert.False(coordinator.HasPendingRecovery);
        Assert.Equal(1, coordinator.RecoveryAttemptCount);
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed
        {
            add { }
            remove { }
        }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeMouseHook : IMouseHook
    {
        public event EventHandler? MouseClicked
        {
            add { }
            remove { }
        }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    private sealed class ThrowingLogger : ILoggerService
    {
        public void LogInfo(string message) => throw new IOException("simulated log failure");
        public void LogWarning(string message) => throw new IOException("simulated log failure");
        public void LogError(string message, Exception? ex = null) =>
            throw new IOException("simulated log failure");
    }
}
