using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using Microsoft.Win32;

namespace LayoutFix.Services;

internal sealed class WindowsSessionRecoveryMonitor : IDisposable
{
    private readonly HookRecoveryCoordinator _hookRecovery;
    private readonly ILoggerService _logger;
    private bool _started;
    private bool _disposed;

    public WindowsSessionRecoveryMonitor(
        HookRecoveryCoordinator hookRecovery,
        ILoggerService logger)
    {
        _hookRecovery = hookRecovery;
        _logger = logger;
    }

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsSessionRecoveryMonitor));
        if (_started)
            return;

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _started = true;
        }
        catch (Exception exception)
        {
            TryUnsubscribe();
            TryLogError(
                "Windows session notifications are unavailable; " +
                "automatic session-triggered hook recovery is limited.",
                exception);
        }
    }

    internal bool HandlePowerModeChanged(PowerModes mode)
    {
        if (mode != PowerModes.Resume)
            return false;
        _hookRecovery.RequestRecovery();
        TryLogInfo("Global input hook recovery requested after Windows resume.");
        return true;
    }

    internal bool HandleSessionSwitch(SessionSwitchReason reason)
    {
        if (reason is not (SessionSwitchReason.SessionUnlock or
            SessionSwitchReason.RemoteConnect or
            SessionSwitchReason.ConsoleConnect))
        {
            return false;
        }

        _hookRecovery.RequestRecovery();
        TryLogInfo($"Global input hook recovery requested after Windows {reason}.");
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        TryUnsubscribe();
        _started = false;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs) =>
        HandlePowerModeChanged(eventArgs.Mode);

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs) =>
        HandleSessionSwitch(eventArgs.Reason);

    private void TryLogError(string message, Exception exception)
    {
        try { _logger.LogError(message, exception); } catch { }
    }

    private void TryLogInfo(string message)
    {
        try { _logger.LogInfo(message); } catch { }
    }

    private void TryUnsubscribe()
    {
        try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
    }
}
