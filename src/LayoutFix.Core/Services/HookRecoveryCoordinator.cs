using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public sealed class HookRecoveryCoordinator
{
    private const long NoDegradationTimestamp = long.MinValue;
    private readonly IKeyboardHook _keyboardHook;
    private readonly IMouseHook _mouseHook;
    private readonly ILoggerService _logger;
    private readonly TimeProvider _timeProvider;
    private int _recoveryRequested;
    private int _recoveryRequestCount;
    private int _isOperational;
    private int _recoveryAttemptCount;
    private int _recoveryFailureCount;
    private int _consecutiveFailureCount;
    private int _suppressedFailureDiagnosticCount;
    private int _lastSuppressedFailureDiagnosticCount;
    private long _degradationStartedTimestamp = NoDegradationTimestamp;
    private long _lastDegradationDurationTicks;

    public event EventHandler? OperationalStateChanged;
    public bool IsOperational => Volatile.Read(ref _isOperational) != 0;
    public bool HasPendingRecovery => Volatile.Read(ref _recoveryRequested) != 0;
    public int RecoveryRequestCount => Volatile.Read(ref _recoveryRequestCount);
    public int RecoveryAttemptCount => Volatile.Read(ref _recoveryAttemptCount);
    public int RecoveryFailureCount => Volatile.Read(ref _recoveryFailureCount);
    public int ConsecutiveFailureCount => Volatile.Read(ref _consecutiveFailureCount);
    public int SuppressedFailureDiagnosticCount =>
        Volatile.Read(ref _suppressedFailureDiagnosticCount);
    public int LastSuppressedFailureDiagnosticCount =>
        Volatile.Read(ref _lastSuppressedFailureDiagnosticCount);
    public TimeSpan CurrentDegradationDuration
    {
        get
        {
            var started = Volatile.Read(ref _degradationStartedTimestamp);
            return started == NoDegradationTimestamp
                ? TimeSpan.Zero
                : _timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp());
        }
    }
    public TimeSpan LastDegradationDuration =>
        TimeSpan.FromTicks(Volatile.Read(ref _lastDegradationDurationTicks));

    public HookRecoveryCoordinator(
        IKeyboardHook keyboardHook,
        IMouseHook mouseHook,
        ILoggerService logger)
        : this(keyboardHook, mouseHook, logger, TimeProvider.System)
    {
    }

    internal HookRecoveryCoordinator(
        IKeyboardHook keyboardHook,
        IMouseHook mouseHook,
        ILoggerService logger,
        TimeProvider timeProvider)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void RequestRecovery()
    {
        Interlocked.Increment(ref _recoveryRequestCount);
        Interlocked.Exchange(ref _recoveryRequested, 1);
    }

    public bool RecoverIfRequested()
    {
        if (Interlocked.Exchange(ref _recoveryRequested, 0) == 0)
            return false;

        var attempt = Interlocked.Increment(ref _recoveryAttemptCount);
        try
        {
            _keyboardHook.Stop();
            _mouseHook.Stop();
            _keyboardHook.Start();
            _mouseHook.Start();
        }
        catch (Exception exception)
        {
            var consecutiveFailures = Interlocked.Increment(ref _consecutiveFailureCount);
            Interlocked.Increment(ref _recoveryFailureCount);
            Interlocked.CompareExchange(
                ref _degradationStartedTimestamp,
                _timeProvider.GetTimestamp(),
                NoDegradationTimestamp);
            Interlocked.Exchange(ref _recoveryRequested, 1);
            PublishOperationalState(false);
            if (ShouldPublishFailureDiagnostic(consecutiveFailures))
            {
                TryLogError(
                    $"Global input hook recovery attempt {attempt} failed " +
                    $"(consecutive failures: {consecutiveFailures}); retry scheduled.",
                    exception);
            }
            else
            {
                Interlocked.Increment(ref _suppressedFailureDiagnosticCount);
            }
            return false;
        }

        var recoveredFailureCount = Interlocked.Exchange(ref _consecutiveFailureCount, 0);
        var suppressedDiagnostics = Interlocked.Exchange(
            ref _suppressedFailureDiagnosticCount,
            0);
        var degradationStarted = Interlocked.Exchange(
            ref _degradationStartedTimestamp,
            NoDegradationTimestamp);
        var degradationDuration = degradationStarted == NoDegradationTimestamp
            ? TimeSpan.Zero
            : _timeProvider.GetElapsedTime(
                degradationStarted,
                _timeProvider.GetTimestamp());
        if (recoveredFailureCount > 0)
        {
            Interlocked.Exchange(
                ref _lastSuppressedFailureDiagnosticCount,
                suppressedDiagnostics);
            Interlocked.Exchange(
                ref _lastDegradationDurationTicks,
                degradationDuration.Ticks);
        }
        PublishOperationalState(true);
        TryLogInfo(
            recoveredFailureCount == 0
                ? $"Global input hooks installed on attempt {attempt}."
                : $"Global input hooks recovered on attempt {attempt} after " +
                  $"{recoveredFailureCount} consecutive failures over " +
                  $"{Math.Max(0, (long)degradationDuration.TotalMilliseconds)} ms " +
                  $"(suppressed failure diagnostics: {suppressedDiagnostics}).");
        return true;
    }

    private static bool ShouldPublishFailureDiagnostic(int consecutiveFailures) =>
        consecutiveFailures <= 3 ||
        (consecutiveFailures & (consecutiveFailures - 1)) == 0;

    private void PublishOperationalState(bool operational)
    {
        var next = operational ? 1 : 0;
        if (Interlocked.Exchange(ref _isOperational, next) == next)
            return;

        var handlers = OperationalStateChanged;
        if (handlers == null)
            return;

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                TryLogError("Input hook status subscriber failed.", exception);
            }
        }
    }

    private void TryLogInfo(string message)
    {
        try { _logger.LogInfo(message); } catch { }
    }

    private void TryLogError(string message, Exception exception)
    {
        try { _logger.LogError(message, exception); } catch { }
    }
}
