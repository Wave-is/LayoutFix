using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.IntegrationTests;

public class UIAutomationTests
{
    [Fact]
    public async Task FixLayout_CopiesConvertsPastesAndSwitchesLayout()
    {
        var transaction = new RecordingTextTransactionService("ghbdtn");
        var activeWindow = new RecordingActiveWindowProvider();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            activeWindow,
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider());

        await coordinator.ExecuteActionAsync(HotkeyAction.FixLayout);

        Assert.Equal("привет", transaction.ReplacementText);
        Assert.Equal(1, activeWindow.SwitchCount);
        Assert.Equal("ru-RU@04190419", activeWindow.SwitchedLayout);
        Assert.Equal(1, transaction.CaptureCount);
        Assert.Equal(1, transaction.ReplaceCount);
    }

    [Fact]
    public async Task FixLayout_WithThreeLayouts_UsesUniqueDictionaryTarget()
    {
        var transaction = new RecordingTextTransactionService("ghbdtn");
        var activeWindow = new RecordingActiveWindowProvider();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            activeWindow,
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            dictionaryAnalyzer: new FixedDictionaryAnalyzer(
                new LayoutCorrectionSuggestion("привіт", "uk-UA@04220422")));

        await coordinator.ExecuteActionAsync(HotkeyAction.FixLayout);

        Assert.Equal("привіт", transaction.ReplacementText);
        Assert.Equal("uk-UA@04220422", activeWindow.SwitchedLayout);
    }

    [Fact]
    public async Task FixLayout_ForSentence_DoesNotInvokeWordDictionaryTargeting()
    {
        var analyzer = new RecordingDictionaryAnalyzer();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            new RecordingTextTransactionService("hello world"),
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            dictionaryAnalyzer: analyzer);

        await coordinator.ExecuteActionAsync(HotkeyAction.FixLayout);

        Assert.Equal(0, analyzer.CallCount);
    }

    [Fact]
    public async Task FixLayout_LogsPrivacySafeDecisionWithoutCapturedText()
    {
        var logger = new RecordingLogger();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            new RecordingTextTransactionService("ghbdtn"),
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            logger,
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider());

        await coordinator.ExecuteActionAsync(HotkeyAction.FixLayout);

        var diagnostic = Assert.Single(logger.Infos, message =>
            message.Contains("Phase=layout-analysis", StringComparison.Ordinal));
        Assert.Contains("Script=latin", diagnostic, StringComparison.Ordinal);
        Assert.Contains("LetterCount=6", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Reason=layout-fallback", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("ghbdtn", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("привет", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentActions_AreQueuedAndNoneAreSilentlyDropped()
    {
        var transaction = new RecordingTextTransactionService("hello");
        using var coordinator = CreateCoordinator(transaction, new RecordingActiveWindowProvider());

        const int actionCount = 1_000;
        await Task.WhenAll(Enumerable.Range(0, actionCount)
            .Select(_ => coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase)));

        Assert.Equal(actionCount, transaction.CaptureCount);
        Assert.Equal(actionCount, transaction.ReplaceCount);
    }

    [Fact]
    public async Task Dispose_CancelsActiveActionAndFaultsQueuedRequests()
    {
        var transaction = new CancellationObservingTextTransactionService();
        var coordinator = CreateCoordinator(transaction, new RecordingActiveWindowProvider());
        var active = coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase);
        await transaction.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = Enumerable.Range(0, 128)
            .Select(_ => coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase))
            .ToArray();

        coordinator.Dispose();

        await transaction.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        foreach (var request in queued.Prepend(active))
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => request.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        Assert.Equal(1, transaction.CaptureCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase));
    }

    [Fact]
    public async Task TimedOutAction_ReleasesQueueForFollowingRequest()
    {
        var transaction = new TimeoutThenRecordingTextTransactionService("hello");
        var popup = new RecordingPopupService();
        var logger = new RecordingLogger();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            logger,
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup,
            actionTimeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase));
        await coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase);

        Assert.Equal(2, transaction.CaptureCount);
        Assert.Equal(1, transaction.ReplaceCount);
        Assert.Equal("HELLO", transaction.ReplacementText);
        var notification = Assert.Single(popup.Messages);
        Assert.Contains("did not respond in time", notification);
        Assert.Contains("text capture", notification);
        Assert.DoesNotContain("hello", notification, StringComparison.OrdinalIgnoreCase);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Stage: TextCapture", warning);
        Assert.DoesNotContain("hello", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TimedOutReplacement_ReportsStageWithoutSelectedText()
    {
        const string privateText = "private replacement source";
        var popup = new RecordingPopupService();
        var logger = new RecordingLogger();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            new ReplacementTimeoutTextTransactionService(privateText),
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            logger,
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup,
            actionTimeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteActionAsync(HotkeyAction.ChangeCase));

        var notification = Assert.Single(popup.Messages);
        Assert.Contains("text replacement", notification);
        Assert.Contains("LF-HK-002", notification);
        Assert.DoesNotContain(privateText, notification, StringComparison.OrdinalIgnoreCase);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Stage: TextReplacement", warning);
        Assert.Contains("DiagnosticCode: LF-HK-002", warning);
        Assert.DoesNotContain(privateText, warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TimedOutReplacement_CollapsesPendingFallbackSelection()
    {
        var transaction = new FallbackReplacementTimeoutTextTransactionService();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            actionTimeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteActionAsync(HotkeyAction.FixLayout));
        await transaction.SelectionCollapsed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, transaction.CollapseCount);
    }

    [Fact]
    public async Task RejectedTranslation_ShowsTextFreeQueueNotification()
    {
        const string privateText = "private translation source";
        var popup = new RecordingPopupService();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            new RecordingTextTransactionService(privateText),
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new RejectingTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup);

        await coordinator.ExecuteActionAsync(HotkeyAction.Translate1);

        var notification = Assert.Single(popup.Messages);
        Assert.Contains("queue is busy", notification);
        Assert.Contains("LF-TR-001", notification);
        Assert.DoesNotContain(privateText, notification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Undo_RestoresExactAutomaticCorrectionAndLearnsException()
    {
        var transaction = new RecordingTextTransactionService("привет ");
        var activeWindow = new RecordingActiveWindowProvider();
        var settings = new FakeSettingsService();
        var memory = new AutoCorrectionMemory();
        memory.Record(
            "ghbdtn",
            "привет",
            " ",
            activeWindow.CaptureActiveWindow());
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            transaction,
            settings,
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            activeWindow,
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            memory);

        await coordinator.ExecuteActionAsync(HotkeyAction.Undo);

        Assert.Equal("ghbdtn ", transaction.ReplacementText);
        Assert.Contains("ghbdtn", settings.Current.UserExceptions);
    }

    [Fact]
    public void OrdinaryTypingDoesNotPerformProcessLookupInsideHookSubscriber()
    {
        var hook = new FakeKeyboardHook();
        var activeWindow = new RecordingActiveWindowProvider();
        using var coordinator = new HotkeyCoordinator(
            hook,
            new RecordingTextTransactionService("text"),
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            activeWindow,
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider());
        coordinator.Initialize();

        hook.Press(new HotkeyCombo { Key = "a", VirtualKey = 'A' });

        Assert.Equal(0, activeWindow.ProcessNameLookupCount);
    }

    [Fact]
    public void BlacklistMatchesExecutablePathsAndDoesNotSuppressOrQueueHotkey()
    {
        var hook = new FakeKeyboardHook();
        var activeWindow = new RecordingActiveWindowProvider { ProcessName = "Photoshop" };
        var settings = new FakeSettingsService();
        settings.Current.BlacklistedProcesses = [@"C:\Program Files\Adobe\Photoshop.exe"];
        var transaction = new RecordingTextTransactionService("ghbdtn");
        using var coordinator = new HotkeyCoordinator(
            hook,
            transaction,
            settings,
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            activeWindow,
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider());
        coordinator.Initialize();

        var args = hook.Press(HotkeyCombo.Parse("Scroll"));

        Assert.False(args.Handled);
        Assert.Equal(0, transaction.CaptureCount);
    }

    [Fact]
    public async Task ConflictingBusyHotkey_IsRejectedWithoutBacklogAndRecoversAfterCompletion()
    {
        var hook = new FakeKeyboardHook();
        const string privateText = "ghbdtn";
        var transaction = new BlockingThenRecordingTextTransactionService(privateText);
        var popup = new RecordingPopupService();
        var logger = new RecordingLogger();
        using var coordinator = new HotkeyCoordinator(
            hook,
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            logger,
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup);
        coordinator.Initialize();

        var first = hook.Press(HotkeyCombo.Parse("Scroll"));
        await transaction.FirstCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var rejected = Enumerable.Range(0, 16)
            .Select(_ => hook.Press(HotkeyCombo.Parse("Shift+Scroll")))
            .ToArray();
        await popup.Notified.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(first.Handled);
        Assert.All(rejected, args => Assert.True(args.Handled));
        Assert.Equal(1, transaction.CaptureCount);
        var notification = Assert.Single(popup.Messages);
        Assert.Contains("was not queued", notification);
        Assert.Contains("LF-HK-001", notification);
        Assert.DoesNotContain(privateText, notification, StringComparison.OrdinalIgnoreCase);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("another hotkey action is still running", warning);
        Assert.Contains("DiagnosticCode: LF-HK-001", warning);
        Assert.DoesNotContain(privateText, warning, StringComparison.OrdinalIgnoreCase);

        transaction.ReleaseFirstCapture();
        await transaction.FirstActionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        HotkeyEventArgs? recovered = null;
        while (transaction.CaptureCount < 2)
        {
            recovered = hook.Press(HotkeyCombo.Parse("Scroll"));
            if (transaction.CaptureCount < 2)
                await Task.Delay(10);
        }
        await transaction.SecondCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(recovered);
        Assert.True(recovered.Handled);
        Assert.Equal(2, transaction.CaptureCount);
    }

    [Fact]
    public async Task DuplicateBusyHotkey_IsCoalescedWithoutErrorNotification()
    {
        var hook = new FakeKeyboardHook();
        var transaction = new BlockingThenRecordingTextTransactionService("ghbdtn");
        var popup = new RecordingPopupService();
        var logger = new RecordingLogger();
        using var coordinator = new HotkeyCoordinator(
            hook,
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            logger,
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup);
        coordinator.Initialize();

        var first = hook.Press(HotkeyCombo.Parse("Shift+Scroll"));
        await transaction.FirstCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicates = Enumerable.Range(0, 16)
            .Select(_ => hook.Press(HotkeyCombo.Parse("Shift+Scroll")))
            .ToArray();

        Assert.True(first.Handled);
        Assert.All(duplicates, args => Assert.True(args.Handled));
        Assert.Equal(1, transaction.CaptureCount);
        Assert.Empty(popup.Messages);
        Assert.Empty(logger.Warnings);
        Assert.Contains(
            logger.Infos,
            message => message.Contains("duplicate hotkey coalesced", StringComparison.Ordinal));

        transaction.ReleaseFirstCapture();
        await transaction.FirstActionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task BusyHotkey_FeedbackNeverBlocksHookCallback()
    {
        var hook = new FakeKeyboardHook();
        var transaction = new BlockingThenRecordingTextTransactionService("ghbdtn");
        var popup = new BlockingPopupService();
        using var coordinator = new HotkeyCoordinator(
            hook,
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup);
        coordinator.Initialize();

        hook.Press(HotkeyCombo.Parse("Scroll"));
        await transaction.FirstCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var rejectedTask = Task.Run(() => hook.Press(HotkeyCombo.Parse("Shift+Scroll")));

        try
        {
            await popup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(rejectedTask, Task.Delay(500));
            Assert.Same(rejectedTask, completed);
        }
        finally
        {
            popup.Release();
            transaction.ReleaseFirstCapture();
        }

        var rejected = await rejectedTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(rejected.Handled);
    }

    [Fact]
    public async Task SafeCaptureFailureProducesOneTextFreeUserNotification()
    {
        var popup = new RecordingPopupService();
        using var coordinator = new HotkeyCoordinator(
            new FakeKeyboardHook(),
            new MissingTextTransactionService(),
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            new RecordingActiveWindowProvider(),
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider(),
            popupService: popup);

        await coordinator.ExecuteActionAsync(HotkeyAction.FixLayout);

        var notification = Assert.Single(popup.Messages);
        Assert.Contains("No editable text", notification);
        Assert.Contains("LF-HK-004", notification);
    }

    private static HotkeyCoordinator CreateCoordinator(
        ITextTransactionService transaction,
        IActiveWindowProvider activeWindow) => new(
            new FakeKeyboardHook(),
            transaction,
            new FakeSettingsService(),
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            new NullLogger(),
            activeWindow,
            new NullSoundService(),
            new FakeTranslationCoordinator(),
            new NullTranslatorWindowProvider());

    private sealed class RecordingTextTransactionService(string selectedText) : ITextTransactionService
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);
        public string? ReplacementText { get; private set; }
        public int CaptureCount { get; private set; }
        public int ReplaceCount { get; private set; }

        public Task<TextSelection?> CaptureAsync(bool allowPreviousWordFallback, CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult<TextSelection?>(new TextSelection(selectedText, Window, false));
        }

        public Task<bool> ReplaceAsync(TextSelection selection, string replacement, CancellationToken cancellationToken = default)
        {
            ReplaceCount++;
            ReplacementText = replacement;
            return Task.FromResult(true);
        }

        public Task CancelFallbackSelectionAsync(TextSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MissingTextTransactionService : ITextTransactionService
    {
        public Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default) => Task.FromResult<TextSelection?>(null);

        public Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TimeoutThenRecordingTextTransactionService(string selectedText)
        : ITextTransactionService
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);
        public int CaptureCount { get; private set; }
        public int ReplaceCount { get; private set; }
        public string? ReplacementText { get; private set; }

        public async Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            if (CaptureCount == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return new TextSelection(selectedText, Window, false);
        }

        public Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default)
        {
            ReplaceCount++;
            ReplacementText = replacement;
            return Task.FromResult(true);
        }

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ReplacementTimeoutTextTransactionService(string selectedText)
        : ITextTransactionService
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);

        public Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TextSelection?>(new TextSelection(selectedText, Window, false));

        public async Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default)
        {
            // Simulate a provider that cannot observe cancellation while it is
            // inside a native call and returns success only after the action
            // deadline. Waiting for the actual cancellation signal avoids a
            // wall-clock race when a shared runner delays the CancelAfter timer.
            var deadlineReached = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                deadlineReached);
            await deadlineReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FallbackReplacementTimeoutTextTransactionService
        : ITextTransactionService
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);
        private int _collapseCount;
        public TaskCompletionSource SelectionCollapsed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CollapseCount => Volatile.Read(ref _collapseCount);

        public Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TextSelection?>(new TextSelection("ghbdtn", Window, true));

        public async Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default)
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            Interlocked.Increment(ref _collapseCount);
            SelectionCollapsed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingThenRecordingTextTransactionService(string selectedText)
        : ITextTransactionService
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);
        private readonly TaskCompletionSource _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _captureCount;

        public TaskCompletionSource FirstCaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstActionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CaptureCount => Volatile.Read(ref _captureCount);

        public async Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default)
        {
            var capture = Interlocked.Increment(ref _captureCount);
            if (capture == 1)
            {
                FirstCaptureStarted.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else if (capture == 2)
            {
                SecondCaptureStarted.TrySetResult();
            }

            return new TextSelection(selectedText, Window, false);
        }

        public Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default)
        {
            if (CaptureCount == 1)
                FirstActionCompleted.TrySetResult();
            return Task.FromResult(true);
        }

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ReleaseFirstCapture() => _releaseFirst.TrySetResult();
    }

    private sealed class CancellationObservingTextTransactionService : ITextTransactionService
    {
        private int _captureCount;
        public TaskCompletionSource CaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CaptureCount => Volatile.Read(ref _captureCount);

        public async Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _captureCount);
            CaptureStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            return null;
        }

        public Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed;
        public HotkeyEventArgs Press(HotkeyCombo combo)
        {
            var args = new HotkeyEventArgs(combo);
            HotkeyPressed?.Invoke(this, args);
            return args;
        }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = new()
        {
            SoundEnabled = false,
            LayoutOrder = ["en-US", "ru-RU"]
        };

        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class FakeKeyboardLayoutManager : IKeyboardLayoutManager
    {
        private static readonly Layout English = new()
        {
            Code = "en-US",
            Identifier = "en-US@04090409",
            Keys = new Dictionary<string, string>
            {
                ["g"] = "g", ["h"] = "h", ["b"] = "b",
                ["d"] = "d", ["t"] = "t", ["n"] = "n"
            }
        };

        private static readonly Layout Russian = new()
        {
            Code = "ru-RU",
            Identifier = "ru-RU@04190419",
            Keys = new Dictionary<string, string>
            {
                ["g"] = "п", ["h"] = "р", ["b"] = "и",
                ["d"] = "в", ["t"] = "е", ["n"] = "т"
            }
        };

        private static readonly Layout Ukrainian = new()
        {
            Code = "uk-UA",
            Identifier = "uk-UA@04220422",
            Keys = new Dictionary<string, string>
            {
                ["g"] = "п", ["h"] = "р", ["b"] = "и",
                ["d"] = "в", ["t"] = "е", ["n"] = "т"
            }
        };

        private static readonly IReadOnlyList<Layout> Layouts = [English, Russian, Ukrainian];

        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => Layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => Layouts;
    }

    private sealed class RecordingActiveWindowProvider : IActiveWindowProvider
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);
        public int SwitchCount { get; private set; }
        public int ProcessNameLookupCount { get; private set; }
        public string ProcessName { get; set; } = "test-host";
        public string? SwitchedLayout { get; private set; }
        public ActiveWindowContext CaptureActiveWindow() => Window;
        public bool IsSameActiveWindow(ActiveWindowContext context) => context == Window;
        public string GetActiveProcessName()
        {
            ProcessNameLookupCount++;
            return ProcessName;
        }
        public string GetActiveLayoutCode() => "en-US@04090409";
        public void SwitchToNextLayout() => SwitchCount++;
        public bool TrySwitchToLayout(string layoutCode)
        {
            SwitchCount++;
            SwitchedLayout = layoutCode;
            return true;
        }
    }

    private sealed class FixedDictionaryAnalyzer(LayoutCorrectionSuggestion suggestion)
        : IDictionaryAnalyzer
    {
        public bool IsGibberish(string word, string currentLayout) => true;

        public bool TryGetCorrection(
            string word,
            string currentLayout,
            out LayoutCorrectionSuggestion result)
        {
            result = suggestion;
            return true;
        }
    }

    private sealed class RecordingDictionaryAnalyzer : IDictionaryAnalyzer
    {
        public int CallCount { get; private set; }
        public bool IsGibberish(string word, string currentLayout) => false;

        public bool TryGetCorrection(
            string word,
            string currentLayout,
            out LayoutCorrectionSuggestion suggestion)
        {
            CallCount++;
            suggestion = default;
            return false;
        }
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
        public void LogInfo(string message) => Infos.Enqueue(message);
        public void LogWarning(string message) => Warnings.Enqueue(message);
        public void LogError(string message, Exception? ex = null) { }
    }

    private sealed class NullSoundService : ISoundService
    {
        public void PlaySwitchSound() { }
        public void PlayAutoConvertSound() { }
        public void PlayErrorSound() { }
    }

    private sealed class FakeTranslationCoordinator : ITranslationCoordinator
    {
        public ValueTask<bool> QueueTranslationAsync(TextSelection selection, string targetLanguage, string sourceLanguage = "auto", CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public void Dispose() { }
    }

    private sealed class RejectingTranslationCoordinator : ITranslationCoordinator
    {
        public ValueTask<bool> QueueTranslationAsync(TextSelection selection, string targetLanguage, string sourceLanguage = "auto", CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public void Dispose() { }
    }

    private sealed class NullTranslatorWindowProvider : ITranslatorWindowProvider
    {
        public void ShowTranslator(string initialText = "") { }
    }

    private sealed class RecordingPopupService : IPopupService
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public TaskCompletionSource Notified { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ShowTranslationPopup(string text) { }
        public void ShowStatus(string message, bool isError = false)
        {
            Messages.Enqueue(message);
            Notified.TrySetResult();
        }
    }

    private sealed class BlockingPopupService : IPopupService
    {
        private readonly ManualResetEventSlim _release = new(false);
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ShowTranslationPopup(string text) { }

        public void ShowStatus(string message, bool isError = false)
        {
            Entered.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(5));
        }

        public void Release() => _release.Set();
    }
}
