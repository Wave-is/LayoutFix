using System.Windows.Automation;
using System.Text;
using System.Diagnostics;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

/// <summary>
/// Fails closed when the focused accessibility element cannot be inspected.
/// The single probe gate prevents a broken UI Automation provider from
/// exhausting ThreadPool threads or stalling keyboard processing.
/// </summary>
public sealed class WindowsTextTargetGuard : ITextTargetGuard, IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan BusyWarningThrottle = TimeSpan.FromSeconds(2);
    private readonly IActiveWindowProvider _activeWindow;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly Func<ActiveWindowContext, bool> _probe;
    private readonly Func<nint, bool?> _nativeProbe;
    private readonly Func<uint, TargetInputAccess> _integrityProbe;
    private long _lastBusyWarningTimestamp;
    private long _lastHigherIntegrityWarningTimestamp;
    private long _lastUnavailableIntegrityWarningTimestamp;
    private volatile bool _disposed;

    public WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger)
        : this(
            activeWindow,
            logger,
            ProbeAutomationFocusedElement,
            ProbeNativeEdit,
            WindowsCompatibilityProbe.GetTargetInputAccess)
    {
    }

    internal WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        Func<ActiveWindowContext, bool> probe)
        : this(activeWindow, logger, probe, _ => null, _ => TargetInputAccess.Allowed)
    {
    }

    internal WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        Func<ActiveWindowContext, bool> probe,
        Func<nint, bool?> nativeProbe)
        : this(
            activeWindow,
            logger,
            probe,
            nativeProbe,
            _ => TargetInputAccess.Allowed)
    {
    }

    internal WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        Func<ActiveWindowContext, bool> probe,
        Func<nint, bool?> nativeProbe,
        Func<uint, TargetInputAccess> integrityProbe)
    {
        _activeWindow = activeWindow;
        _logger = logger;
        _probe = probe;
        _nativeProbe = nativeProbe;
        _integrityProbe = integrityProbe;
    }

    public async Task<bool> CanModifyAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!context.IsValid || !_activeWindow.IsSameActiveWindow(context))
            return false;

        try
        {
            switch (_integrityProbe(context.ProcessId))
            {
                case TargetInputAccess.Allowed:
                    break;
                case TargetInputAccess.HigherIntegrity:
                    LogWarningThrottled(
                        ref _lastHigherIntegrityWarningTimestamp,
                        "Target process has higher integrity; operation rejected.");
                    return false;
                default:
                    LogWarningThrottled(
                        ref _lastUnavailableIntegrityWarningTimestamp,
                        "Target process integrity is unavailable; operation rejected.");
                    return false;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("Target process integrity probe failed", exception);
            return false;
        }

        if (!await _probeGate.WaitAsync(0, cancellationToken))
        {
            LogBusyWarningThrottled();
            return false;
        }

        var releaseGate = true;
        try
        {
            // Standard native edits have a bounded non-accessibility path. Keep it
            // on the calling worker so a cold/starved ThreadPool cannot turn a
            // writable field into a false UI Automation timeout.
            var nativeResult = _nativeProbe(context.FocusedWindow);
            if (nativeResult.HasValue)
                return nativeResult.Value && _activeWindow.IsSameActiveWindow(context);

            var probeTask = Task.Run(() => _probe(context), CancellationToken.None);
            var completed = await Task.WhenAny(
                probeTask,
                Task.Delay(ProbeTimeout, cancellationToken));
            if (completed != probeTask)
            {
                releaseGate = false;
                _ = probeTask.ContinueWith(
                    CompleteTimedOutProbe,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogWarning("Text target safety probe timed out; operation rejected.");
                return false;
            }

            return await probeTask && _activeWindow.IsSameActiveWindow(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError("Text target safety probe failed", exception);
            return false;
        }
        finally
        {
            if (releaseGate)
                _probeGate.Release();
        }
    }

    private void CompleteTimedOutProbe(Task<bool> probeTask)
    {
        try
        {
            _ = probeTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            if (!_disposed)
                _logger.LogError("Text target safety probe failed after its timeout", exception);
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private void LogBusyWarningThrottled()
        => LogWarningThrottled(
            ref _lastBusyWarningTimestamp,
            "Text target safety probe is busy; operation rejected.");

    private void LogWarningThrottled(ref long timestamp, string message)
    {
        var now = Stopwatch.GetTimestamp();
        while (true)
        {
            var previous = Volatile.Read(ref timestamp);
            if (previous != 0 &&
                Stopwatch.GetElapsedTime(previous, now) < BusyWarningThrottle)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref timestamp,
                    now,
                    previous) == previous)
            {
                _logger.LogWarning(message);
                return;
            }
        }
    }

    private static bool ProbeAutomationFocusedElement(ActiveWindowContext context)
    {
        var element = AutomationElement.FocusedElement;
        if (element == null || element.Current.ProcessId != context.ProcessId)
            return false;

        var current = element.Current;
        var hasWritableValuePattern = false;
        if (element.TryGetCurrentPattern(
                ValuePattern.Pattern,
                out var valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern)
        {
            hasWritableValuePattern = !valuePattern.Current.IsReadOnly;
        }

        // Browser contenteditable and document-style editors commonly expose
        // TextPattern rather than ValuePattern. Limit this fallback to actual
        // Edit/Document controls so buttons, panes and Adobe canvases fail closed
        // before the clipboard transaction starts.
        var isEditOrDocument = current.ControlType is not null &&
            (current.ControlType == ControlType.Edit ||
             current.ControlType == ControlType.Document);
        var hasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out _);
        return IsEditableAutomationTarget(
            current.IsEnabled,
            current.IsKeyboardFocusable,
            current.IsPassword,
            hasWritableValuePattern,
            isEditOrDocument,
            hasTextPattern);
    }

    internal static bool IsEditableAutomationTarget(
        bool isEnabled,
        bool isKeyboardFocusable,
        bool isPassword,
        bool hasWritableValuePattern,
        bool isEditOrDocument,
        bool hasTextPattern) =>
        isEnabled &&
        isKeyboardFocusable &&
        !isPassword &&
        (hasWritableValuePattern || (isEditOrDocument && hasTextPattern));

    private static bool? ProbeNativeEdit(nint focusedWindow)
    {
        if (focusedWindow == 0)
            return false;

        var className = new StringBuilder(256);
        if (Win32.GetClassName(focusedWindow, className, className.Capacity) == 0)
            return null;
        if (!className.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return null;

        var style = Win32.GetWindowLongPtr(focusedWindow, Win32.GWL_STYLE).ToInt64();
        if (!IsWritableNativeEditStyle(style))
            return false;

        var sent = Win32.SendMessageTimeout(
            focusedWindow,
            Win32.EM_GETPASSWORDCHAR,
            IntPtr.Zero,
            IntPtr.Zero,
            Win32.SMTO_BLOCK | Win32.SMTO_ABORTIFHUNG,
            100,
            out var passwordCharacter);
        return sent == IntPtr.Zero ? false : passwordCharacter == IntPtr.Zero;
    }

    internal static bool IsWritableNativeEditStyle(long style) =>
        (style & (Win32.ES_PASSWORD | Win32.ES_READONLY)) == 0;

    public void Dispose()
    {
        _disposed = true;
        // Do not dispose the gate: a timed-out provider may still complete and
        // release it from its continuation.
    }
}
