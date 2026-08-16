using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public sealed class HookRecoveryCoordinatorTests
{
    [Fact]
    public void NoRequest_DoesNotTouchHooks()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, new FakeLogger());

        var recovered = coordinator.RecoverIfRequested();

        Assert.False(recovered);
        Assert.Equal(0, keyboard.StartCount + keyboard.StopCount);
        Assert.Equal(0, mouse.StartCount + mouse.StopCount);
    }

    [Fact]
    public void ConcurrentRequests_CoalesceIntoOneRestart()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook();
        var logger = new FakeLogger();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, logger);
        Parallel.For(0, 64, _ => coordinator.RequestRecovery());

        var recovered = coordinator.RecoverIfRequested();
        var duplicateRecovery = coordinator.RecoverIfRequested();

        Assert.True(recovered);
        Assert.True(coordinator.IsOperational);
        Assert.False(duplicateRecovery);
        Assert.Equal(1, keyboard.StopCount);
        Assert.Equal(1, keyboard.StartCount);
        Assert.Equal(1, mouse.StopCount);
        Assert.Equal(1, mouse.StartCount);
        Assert.Equal(1, logger.InfoCount);
    }

    [Fact]
    public void FailedRestart_IsRetriedOnNextTick()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook { StartFailuresRemaining = 1 };
        var logger = new FakeLogger();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, logger);
        coordinator.RequestRecovery();

        var firstAttempt = coordinator.RecoverIfRequested();
        Assert.False(firstAttempt);
        Assert.False(coordinator.IsOperational);

        var secondAttempt = coordinator.RecoverIfRequested();
        Assert.True(secondAttempt);
        Assert.True(coordinator.IsOperational);
        Assert.Equal(2, keyboard.StopCount);
        Assert.Equal(2, keyboard.StartCount);
        Assert.Equal(2, mouse.StopCount);
        Assert.Equal(2, mouse.StartCount);
        Assert.Equal(1, logger.ErrorCount);
        Assert.Equal(1, logger.InfoCount);
        Assert.Equal(2, coordinator.RecoveryAttemptCount);
        Assert.Equal(1, coordinator.RecoveryFailureCount);
        Assert.Equal(0, coordinator.ConsecutiveFailureCount);
    }

    [Fact]
    public void RepeatedFailures_RemainPendingUntilSuccessfulRestart()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook { StartFailuresRemaining = 3 };
        var logger = new FakeLogger();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, logger);
        coordinator.RequestRecovery();

        Assert.False(coordinator.RecoverIfRequested());
        Assert.False(coordinator.RecoverIfRequested());
        Assert.False(coordinator.RecoverIfRequested());
        Assert.False(coordinator.IsOperational);
        Assert.Equal(3, coordinator.RecoveryAttemptCount);
        Assert.Equal(3, coordinator.RecoveryFailureCount);
        Assert.Equal(3, coordinator.ConsecutiveFailureCount);

        Assert.True(coordinator.RecoverIfRequested());
        Assert.True(coordinator.IsOperational);
        Assert.Equal(4, coordinator.RecoveryAttemptCount);
        Assert.Equal(3, coordinator.RecoveryFailureCount);
        Assert.Equal(0, coordinator.ConsecutiveFailureCount);
        Assert.Equal(3, logger.ErrorCount);
        Assert.Equal(1, logger.InfoCount);
    }

    [Fact]
    public void SustainedFailure_ThrottlesDiagnosticsAndReportsMonotonicDuration()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook { StartFailuresRemaining = 10 };
        var logger = new FakeLogger();
        var timeProvider = new ManualTimeProvider();
        var coordinator = new HookRecoveryCoordinator(
            keyboard,
            mouse,
            logger,
            timeProvider);
        coordinator.RequestRecovery();

        for (var failure = 1; failure <= 10; failure++)
        {
            Assert.False(coordinator.RecoverIfRequested());
            timeProvider.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(10, coordinator.ConsecutiveFailureCount);
        Assert.Equal(5, coordinator.SuppressedFailureDiagnosticCount);
        Assert.Equal(TimeSpan.FromSeconds(50), coordinator.CurrentDegradationDuration);
        Assert.Equal(5, logger.ErrorCount);
        Assert.Contains(logger.ErrorMessages, message => message.Contains("failures: 1)"));
        Assert.Contains(logger.ErrorMessages, message => message.Contains("failures: 2)"));
        Assert.Contains(logger.ErrorMessages, message => message.Contains("failures: 3)"));
        Assert.Contains(logger.ErrorMessages, message => message.Contains("failures: 4)"));
        Assert.Contains(logger.ErrorMessages, message => message.Contains("failures: 8)"));

        Assert.True(coordinator.RecoverIfRequested());
        Assert.True(coordinator.IsOperational);
        Assert.Equal(0, coordinator.ConsecutiveFailureCount);
        Assert.Equal(0, coordinator.SuppressedFailureDiagnosticCount);
        Assert.Equal(5, coordinator.LastSuppressedFailureDiagnosticCount);
        Assert.Equal(TimeSpan.Zero, coordinator.CurrentDegradationDuration);
        Assert.Equal(TimeSpan.FromSeconds(50), coordinator.LastDegradationDuration);
        Assert.Contains(
            logger.InfoMessages,
            message => message.Contains(
                "after 10 consecutive failures over 50000 ms " +
                "(suppressed failure diagnostics: 5).",
                StringComparison.Ordinal));

        coordinator.RequestRecovery();
        Assert.True(coordinator.RecoverIfRequested());
        Assert.Equal(TimeSpan.FromSeconds(50), coordinator.LastDegradationDuration);
        Assert.Equal(5, coordinator.LastSuppressedFailureDiagnosticCount);
    }

    [Fact]
    public void FailureAfterSuccess_PublishesOfflineAndRecoveredTransitions()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, new FakeLogger());
        var observedStates = new List<bool>();
        coordinator.OperationalStateChanged += (_, _) =>
            observedStates.Add(coordinator.IsOperational);

        coordinator.RequestRecovery();
        Assert.True(coordinator.RecoverIfRequested());

        mouse.StartFailuresRemaining = 1;
        coordinator.RequestRecovery();
        Assert.False(coordinator.RecoverIfRequested());
        Assert.False(coordinator.IsOperational);

        Assert.True(coordinator.RecoverIfRequested());
        Assert.True(coordinator.IsOperational);
        Assert.Equal(new[] { true, false, true }, observedStates);
    }

    [Fact]
    public void StatusSubscriberFailure_DoesNotTurnSuccessfulRecoveryIntoRetry()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook();
        var logger = new FakeLogger();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, logger);
        coordinator.OperationalStateChanged += (_, _) =>
            throw new InvalidOperationException("simulated UI failure");
        coordinator.RequestRecovery();

        var recovered = coordinator.RecoverIfRequested();
        var unexpectedRetry = coordinator.RecoverIfRequested();

        Assert.True(recovered);
        Assert.True(coordinator.IsOperational);
        Assert.False(unexpectedRetry);
        Assert.Equal(1, keyboard.StartCount);
        Assert.Equal(1, mouse.StartCount);
        Assert.Equal(1, logger.ErrorCount);
        Assert.Equal(1, logger.InfoCount);
    }

    [Fact]
    public void DiagnosticSinkFailure_DoesNotBreakRetryOrSuccessfulRecovery()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook { StartFailuresRemaining = 1 };
        var coordinator = new HookRecoveryCoordinator(
            keyboard,
            mouse,
            new ThrowingLogger());
        coordinator.RequestRecovery();

        Assert.False(coordinator.RecoverIfRequested());
        Assert.False(coordinator.IsOperational);
        Assert.True(coordinator.RecoverIfRequested());
        Assert.True(coordinator.IsOperational);
        Assert.Equal(2, coordinator.RecoveryAttemptCount);
        Assert.Equal(1, coordinator.RecoveryFailureCount);
    }

    [Fact]
    public async Task RequestDuringActiveRestart_RemainsPendingForNextTick()
    {
        var keyboard = new FakeKeyboardHook();
        var mouse = new FakeMouseHook();
        var coordinator = new HookRecoveryCoordinator(keyboard, mouse, new FakeLogger());
        coordinator.RequestRecovery();
        Assert.True(coordinator.RecoverIfRequested());

        using var startEntered = new ManualResetEventSlim();
        using var releaseStart = new ManualResetEventSlim();
        keyboard.StartAction = () =>
        {
            startEntered.Set();
            Assert.True(releaseStart.Wait(TimeSpan.FromSeconds(5)));
        };
        coordinator.RequestRecovery();
        var activeRecovery = Task.Run(coordinator.RecoverIfRequested);
        Assert.True(startEntered.Wait(TimeSpan.FromSeconds(5)));

        coordinator.RequestRecovery();
        releaseStart.Set();
        Assert.True(await activeRecovery.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(coordinator.HasPendingRecovery);

        keyboard.StartAction = null;
        Assert.True(coordinator.RecoverIfRequested());
        Assert.False(coordinator.HasPendingRecovery);
        Assert.Equal(3, coordinator.RecoveryAttemptCount);
        Assert.Equal(3, coordinator.RecoveryRequestCount);
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed
        {
            add { }
            remove { }
        }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public Action? StartAction { get; set; }
        public void Start()
        {
            StartCount++;
            StartAction?.Invoke();
        }
        public void Stop() => StopCount++;
        public void Dispose() { }
    }

    private sealed class FakeMouseHook : IMouseHook
    {
        public event EventHandler? MouseClicked
        {
            add { }
            remove { }
        }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int StartFailuresRemaining { get; set; }

        public void Start()
        {
            StartCount++;
            if (StartFailuresRemaining > 0)
            {
                StartFailuresRemaining--;
                throw new InvalidOperationException("simulated hook failure");
            }
        }

        public void Stop() => StopCount++;
        public void Dispose() { }
    }

    private sealed class FakeLogger : ILoggerService
    {
        public List<string> InfoMessages { get; } = [];
        public List<string> ErrorMessages { get; } = [];
        public int InfoCount => InfoMessages.Count;
        public int ErrorCount => ErrorMessages.Count;
        public void LogInfo(string message) => InfoMessages.Add(message);
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) => ErrorMessages.Add(message);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed class ThrowingLogger : ILoggerService
    {
        public void LogInfo(string message) => throw new IOException("simulated log failure");
        public void LogWarning(string message) => throw new IOException("simulated log failure");
        public void LogError(string message, Exception? ex = null) =>
            throw new IOException("simulated log failure");
    }
}
