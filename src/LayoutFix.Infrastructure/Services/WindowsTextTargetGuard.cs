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
    private readonly ISettingsService? _settingsService;
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
            WindowsCompatibilityProbe.GetTargetInputAccess,
            settingsService: null)
    {
    }

    public WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        ISettingsService settingsService)
        : this(
            activeWindow,
            logger,
            context => ProbeAutomationFocusedElement(
                context,
                logger,
                () => settingsService.Current.LoggingEnabled),
            ProbeNativeEdit,
            WindowsCompatibilityProbe.GetTargetInputAccess,
            settingsService)
    {
    }

    internal WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        Func<ActiveWindowContext, bool> probe)
        : this(activeWindow, logger, probe, _ => null, _ => TargetInputAccess.Allowed, null)
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
            _ => TargetInputAccess.Allowed,
            null)
    {
    }

    internal WindowsTextTargetGuard(
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        Func<ActiveWindowContext, bool> probe,
        Func<nint, bool?> nativeProbe,
        Func<uint, TargetInputAccess> integrityProbe,
        ISettingsService? settingsService = null)
    {
        _activeWindow = activeWindow;
        _logger = logger;
        _probe = probe;
        _nativeProbe = nativeProbe;
        _integrityProbe = integrityProbe;
        _settingsService = settingsService;
    }

    public async Task<bool> CanModifyAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!context.IsValid || !_activeWindow.IsSameActiveWindow(context))
        {
            LogSupportDiagnostic("rejected", "active-window-context-invalid");
            return false;
        }

        LogSupportDiagnostic(
            "started",
            "target-probe",
            $"ForegroundClass={DiagnosticValue(WindowClass(context.ForegroundWindow))}; " +
            $"FocusedClass={DiagnosticValue(WindowClass(context.FocusedWindow))}");

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
                    LogSupportDiagnostic("rejected", "higher-integrity-target");
                    return false;
                default:
                    LogWarningThrottled(
                        ref _lastUnavailableIntegrityWarningTimestamp,
                        "Target process integrity is unavailable; operation rejected.");
                    LogSupportDiagnostic("rejected", "target-integrity-unavailable");
                    return false;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("Target process integrity probe failed", exception);
            LogSupportDiagnostic(
                "failed",
                "integrity-probe-exception",
                $"ExceptionType={DiagnosticValue(exception.GetType().FullName)}; HResult=0x{exception.HResult:X8}");
            return false;
        }

        if (!await _probeGate.WaitAsync(0, cancellationToken))
        {
            LogBusyWarningThrottled();
            LogSupportDiagnostic("rejected", "accessibility-probe-busy");
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
            {
                var sameWindow = _activeWindow.IsSameActiveWindow(context);
                var accepted = nativeResult.Value && sameWindow;
                LogSupportDiagnostic(
                    accepted ? "accepted" : "rejected",
                    nativeResult.Value ? "native-edit-focus-recheck" : "native-edit-not-writable",
                    $"Probe=native; SameWindow={sameWindow}");
                return accepted;
            }

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
                LogSupportDiagnostic("rejected", "accessibility-probe-timeout", "Probe=uia; TimeoutMs=300");
                return false;
            }

            var editable = await probeTask;
            var focusMatches = _activeWindow.IsSameActiveWindow(context);
            var result = editable && focusMatches;
            LogSupportDiagnostic(
                result ? "accepted" : "rejected",
                editable ? "uia-focus-recheck" : "uia-target-not-editable",
                $"Probe=uia; SameWindow={focusMatches}");
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError("Text target safety probe failed", exception);
            LogSupportDiagnostic(
                "failed",
                "target-probe-exception",
                $"ExceptionType={DiagnosticValue(exception.GetType().FullName)}; HResult=0x{exception.HResult:X8}");
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

    private static bool ProbeAutomationFocusedElement(ActiveWindowContext context) =>
        ProbeAutomationFocusedElement(context, logger: null, diagnosticsEnabled: null);

    private static bool ProbeAutomationFocusedElement(
        ActiveWindowContext context,
        ILoggerService? logger,
        Func<bool>? diagnosticsEnabled)
    {
        var element = AutomationElement.FocusedElement;
        if (element == null)
        {
            LogAutomationDiagnostic(logger, diagnosticsEnabled, "FocusedElement=unavailable");
            return false;
        }

        if (element.Current.ProcessId != context.ProcessId)
        {
            LogAutomationDiagnostic(logger, diagnosticsEnabled, "ProcessMatch=False");
            return false;
        }

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
        var accepted = IsEditableAutomationTarget(
            current.IsEnabled,
            current.IsKeyboardFocusable,
            current.IsPassword,
            hasWritableValuePattern,
            isEditOrDocument,
            hasTextPattern);
        LogAutomationDiagnostic(
            logger,
            diagnosticsEnabled,
            $"ProcessMatch=True; ControlType={DiagnosticValue(current.ControlType?.ProgrammaticName)}; " +
            $"Class={DiagnosticValue(current.ClassName)}; Enabled={current.IsEnabled}; " +
            $"KeyboardFocusable={current.IsKeyboardFocusable}; Password={current.IsPassword}; " +
            $"WritableValuePattern={hasWritableValuePattern}; EditOrDocument={isEditOrDocument}; " +
            $"TextPattern={hasTextPattern}; Editable={accepted}");
        return accepted;
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

    private void LogSupportDiagnostic(string outcome, string reason, string? details = null)
    {
        if (_settingsService?.Current.LoggingEnabled != true)
            return;

        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $"; {details}";
        _logger.LogInfo(
            $"SupportDiagnostic: Phase=target-probe; Outcome={outcome}; " +
            $"Reason={reason}{suffix}.");
    }

    private static void LogAutomationDiagnostic(
        ILoggerService? logger,
        Func<bool>? diagnosticsEnabled,
        string details)
    {
        if (logger == null || diagnosticsEnabled?.Invoke() != true)
            return;

        logger.LogInfo($"SupportDiagnostic: Phase=uia-metadata; {details}.");
    }

    private static string WindowClass(nint window)
    {
        if (window == 0)
            return "unavailable";

        var className = new StringBuilder(256);
        return Win32.GetClassName(window, className, className.Capacity) == 0
            ? "unavailable"
            : className.ToString();
    }

    private static string DiagnosticValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";

        return new string(value
            .Take(128)
            .Select(character =>
                char.IsLetterOrDigit(character) || character is ' ' or '.' or '-' or '_' or '(' or ')'
                    ? character
                    : '_')
            .ToArray());
    }

    public void Dispose()
    {
        _disposed = true;
        // Do not dispose the gate: a timed-out provider may still complete and
        // release it from its continuation.
    }
}
