using System.Diagnostics;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

public class WindowsTextTargetGuardTests
{
    private static readonly ActiveWindowContext Context = new((nint)1, (nint)2, 3);

    [Theory]
    [InlineData(true, true, false, true, false, false, true)]
    [InlineData(true, true, false, false, true, true, true)]
    [InlineData(true, true, true, true, true, true, false)]
    [InlineData(false, true, false, true, true, true, false)]
    [InlineData(true, false, false, true, true, true, false)]
    [InlineData(true, true, false, false, false, true, false)]
    public void EditableAutomationPolicy_RequiresARealWritableTextTarget(
        bool isEnabled,
        bool isKeyboardFocusable,
        bool isPassword,
        bool hasWritableValuePattern,
        bool isEditOrDocument,
        bool hasTextPattern,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsTextTargetGuard.IsEditableAutomationTarget(
                isEnabled,
                isKeyboardFocusable,
                isPassword,
                hasWritableValuePattern,
                isEditOrDocument,
                hasTextPattern));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0x0020, false)]
    [InlineData(0x0800, false)]
    [InlineData(0x0820, false)]
    public void NativeEditPolicy_RejectsPasswordAndReadOnlyStyles(long style, bool expected)
    {
        Assert.Equal(expected, WindowsTextTargetGuard.IsWritableNativeEditStyle(style));
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1", 4242, 4242u, false, true, "Chrome_WidgetWin_1", true)]
    [InlineData("Notepad", 4242, 4242u, false, true, "Chrome_WidgetWin_1", false)]
    [InlineData("Chrome_WidgetWin_1", 4243, 4242u, false, true, "Chrome_WidgetWin_1", false)]
    [InlineData("Chrome_WidgetWin_1", 4242, 4242u, true, true, "Chrome_WidgetWin_1", false)]
    [InlineData("Chrome_WidgetWin_1", 4242, 4242u, false, false, "Chrome_WidgetWin_1", false)]
    [InlineData("Chrome_WidgetWin_1", 4242, 4242u, false, true, "Chrome_RenderWidgetHostHWND", false)]
    public void ChromiumRootPaneFallback_IsNarrowlyClassified(
        string foregroundClass,
        int focusedProcessId,
        uint expectedProcessId,
        bool isPassword,
        bool isPane,
        string focusedClass,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsTextTargetGuard.IsChromiumRootPaneFallbackCandidate(
                foregroundClass,
                focusedProcessId,
                expectedProcessId,
                isPassword,
                isPane,
                focusedClass));
    }

    [Fact]
    public async Task ChromiumCompatibilityFallback_StillRequiresFocusRecheck()
    {
        var windows = new FakeActiveWindowProvider { RemainingMatches = 1 };
        using var guard = new WindowsTextTargetGuard(
            windows,
            new NullLogger(),
            _ => false,
            _ => null,
            _ => TargetInputAccess.Allowed,
            settingsService: null,
            compatibilityFallbackProbe: _ => true);

        Assert.False(await guard.CanModifyAsync(Context));
    }

    [Fact]
    public async Task ChromiumCompatibilityFallback_AllowsOnlyAfterPrimaryProbeRejects()
    {
        var primaryProbeCalls = 0;
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            new NullLogger(),
            _ =>
            {
                Interlocked.Increment(ref primaryProbeCalls);
                return false;
            },
            _ => null,
            _ => TargetInputAccess.Allowed,
            settingsService: null,
            compatibilityFallbackProbe: _ => true);

        Assert.True(await guard.CanModifyAsync(Context));
        Assert.Equal(1, primaryProbeCalls);
    }

    [Fact]
    public async Task DeniesSecureTarget()
    {
        var windows = new FakeActiveWindowProvider();
        using var guard = new WindowsTextTargetGuard(windows, new NullLogger(), _ => false);

        Assert.False(await guard.CanModifyAsync(Context));
    }

    [Fact]
    public async Task RechecksFocusAfterSuccessfulProbe()
    {
        var windows = new FakeActiveWindowProvider { RemainingMatches = 1 };
        using var guard = new WindowsTextTargetGuard(windows, new NullLogger(), _ => true);

        Assert.False(await guard.CanModifyAsync(Context));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeTargetResult_IsAuthoritativeWithoutAutomationScheduling(
        bool nativeResult)
    {
        var windows = new FakeActiveWindowProvider();
        var automationCalls = 0;
        using var guard = new WindowsTextTargetGuard(
            windows,
            new NullLogger(),
            _ =>
            {
                Interlocked.Increment(ref automationCalls);
                return false;
            },
            _ => nativeResult);

        Assert.Equal(nativeResult, await guard.CanModifyAsync(Context));
        Assert.Equal(0, automationCalls);
    }

    [Fact]
    public async Task EnabledSupportDiagnostics_RecordsProbePathAndReason()
    {
        var logger = new RecordingLogger();
        var settings = new InMemorySettingsService
        {
            Current = new AppSettings { LoggingEnabled = true }
        };
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            logger,
            _ => true,
            _ => false,
            _ => TargetInputAccess.Allowed,
            settings);

        Assert.False(await guard.CanModifyAsync(Context));

        var log = string.Join(Environment.NewLine, logger.Infos);
        Assert.Contains("Phase=target-probe", log);
        Assert.Contains("Reason=native-edit-not-writable", log);
        Assert.Contains("Probe=native", log);
        Assert.DoesNotContain(Context.ProcessId.ToString(), log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)TargetInputAccess.HigherIntegrity, "higher integrity")]
    [InlineData((int)TargetInputAccess.Unavailable, "integrity is unavailable")]
    public async Task UnsafeIntegrity_DeniesBeforeNativeOrAutomationProbe(
        int accessValue,
        string expectedDiagnostic)
    {
        var access = (TargetInputAccess)accessValue;
        var automationCalls = 0;
        var nativeCalls = 0;
        var integrityCalls = 0;
        var logger = new RecordingLogger();
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            logger,
            _ =>
            {
                Interlocked.Increment(ref automationCalls);
                return true;
            },
            _ =>
            {
                Interlocked.Increment(ref nativeCalls);
                return true;
            },
            processId =>
            {
                Assert.Equal(Context.ProcessId, processId);
                Interlocked.Increment(ref integrityCalls);
                return access;
            });

        Assert.False(await guard.CanModifyAsync(Context));
        Assert.Equal(1, integrityCalls);
        Assert.Equal(0, nativeCalls);
        Assert.Equal(0, automationCalls);
        Assert.Contains(logger.Warnings, message => message.Contains(expectedDiagnostic));
        Assert.All(
            logger.Warnings,
            message => Assert.DoesNotContain(Context.ProcessId.ToString(), message));
    }

    [Fact]
    public async Task IntegrityFailures_AreThrottledPerPrivacySafeReason()
    {
        var logger = new RecordingLogger();
        var results = new Queue<TargetInputAccess>(
        [
            TargetInputAccess.HigherIntegrity,
            TargetInputAccess.Unavailable,
            TargetInputAccess.HigherIntegrity,
            TargetInputAccess.Unavailable
        ]);
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            logger,
            _ => true,
            _ => true,
            _ => results.Dequeue());

        for (var attempt = 0; attempt < 4; attempt++)
            Assert.False(await guard.CanModifyAsync(Context));

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Single(logger.Warnings, message => message.Contains("higher integrity"));
        Assert.Single(
            logger.Warnings,
            message => message.Contains("integrity is unavailable"));
    }

    [Fact]
    public async Task HungProviderFailsClosedAndDoesNotStartAnotherProbe()
    {
        using var release = new ManualResetEventSlim(false);
        var probeCalls = 0;
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            new NullLogger(),
            _ =>
            {
                Interlocked.Increment(ref probeCalls);
                release.Wait();
                return true;
            });

        var stopwatch = Stopwatch.StartNew();
        Assert.False(await guard.CanModifyAsync(Context));
        // The production timeout is 800 ms. Shared GitHub runners can delay
        // timer continuations while the three test assemblies run in parallel,
        // so keep the lower bound meaningful but allow scheduler jitter here.
        Assert.InRange(stopwatch.ElapsedMilliseconds, 500, 5_000);

        stopwatch.Restart();
        Assert.False(await guard.CanModifyAsync(Context));
        Assert.True(stopwatch.ElapsedMilliseconds < 100);
        Assert.Equal(1, probeCalls);
        release.Set();
    }

    [Fact]
    public async Task CancellationDoesNotReleaseGateWhileProviderIsStillHung()
    {
        using var release = new ManualResetEventSlim(false);
        var probeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCalls = 0;
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            new NullLogger(),
            _ =>
            {
                Interlocked.Increment(ref probeCalls);
                probeStarted.TrySetResult();
                release.Wait();
                return true;
            });
        using var cancellation = new CancellationTokenSource();

        var pendingProbe = guard.CanModifyAsync(Context, cancellation.Token);
        await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingProbe);
        Assert.False(await guard.CanModifyAsync(Context));
        Assert.Equal(1, probeCalls);
        release.Set();
    }

    [Fact]
    public async Task TimedOutProviderFaultIsObservedAndGuardRecoversWithoutRestart()
    {
        using var release = new ManualResetEventSlim(false);
        var probeCalls = 0;
        var logger = new RecordingLogger();
        using var guard = new WindowsTextTargetGuard(
            new FakeActiveWindowProvider(),
            logger,
            _ =>
            {
                if (Interlocked.Increment(ref probeCalls) == 1)
                {
                    release.Wait();
                    throw new InvalidOperationException("provider failed after timeout");
                }

                return true;
            });

        Assert.False(await guard.CanModifyAsync(Context));
        for (var attempt = 0; attempt < 32; attempt++)
            Assert.False(await guard.CanModifyAsync(Context));
        Assert.Single(logger.Warnings, message => message.Contains("is busy"));
        Assert.Contains(logger.Warnings, message => message.Contains("timed out"));

        release.Set();
        var deadline = Stopwatch.StartNew();
        while (!await guard.CanModifyAsync(Context))
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(2));
            await Task.Delay(10);
        }

        var error = Assert.Single(logger.Errors);
        Assert.Contains("failed after its timeout", error);
        Assert.Equal(2, probeCalls);
    }

    private sealed class FakeActiveWindowProvider : IActiveWindowProvider
    {
        public int RemainingMatches { get; set; } = int.MaxValue;
        public ActiveWindowContext CaptureActiveWindow() => Context;
        public bool IsSameActiveWindow(ActiveWindowContext context) => RemainingMatches-- > 0;
        public string GetActiveProcessName() => "test";
        public string GetActiveLayoutCode() => "en-US";
        public void SwitchToNextLayout() { }
        public bool TrySwitchToLayout(string layoutCode) => true;
    }

    private sealed class NullLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    private sealed class RecordingLogger : ILoggerService
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Infos { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<string> Warnings { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<string> Errors { get; } = new();
        public void LogInfo(string message) => Infos.Enqueue(message);
        public void LogWarning(string message) => Warnings.Enqueue(message);
        public void LogError(string message, Exception? ex = null) => Errors.Enqueue(message);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }
}
