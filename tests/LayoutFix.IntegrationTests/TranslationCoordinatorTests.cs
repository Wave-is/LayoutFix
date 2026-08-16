using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.IntegrationTests;

public class TranslationCoordinatorTests
{
    private static readonly TextSelection Selection = new(
        "source",
        new ActiveWindowContext((nint)1, (nint)2, 3),
        false);

    [Fact]
    public async Task Queue_ReturnsWithoutWaitingForSlowTranslation()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        using var coordinator = CreateCoordinator(online, transaction);

        await coordinator.QueueTranslationAsync(Selection, "uk", "en");
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(transaction.Replaced.Task.IsCompleted);
        online.Complete("переклад");
        var replacement = await transaction.Replaced.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("переклад", replacement);
        Assert.Equal("en", online.SourceLanguage);
        Assert.Equal("uk", online.TargetLanguage);
    }

    [Fact]
    public async Task InPlaceTranslation_PreservesExactEdgeWhitespace()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        using var coordinator = CreateCoordinator(online, transaction);
        var selection = Selection with { Text = "\r\n  source\t " };

        await coordinator.QueueTranslationAsync(selection, "uk", "en");
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("source", online.InputText);

        online.Complete("переклад");
        var replacement = await transaction.Replaced.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("\r\n  переклад\t ", replacement);
    }

    [Fact]
    public async Task WhitespaceOnlySelection_DoesNotInvokeProviderOrReplaceText()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        using var coordinator = CreateCoordinator(online, transaction);
        var selection = Selection with { Text = " \r\n\t", WasSelectedByFallback = true };

        await coordinator.QueueTranslationAsync(selection, "uk", "en");
        var cancelled = await transaction.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(selection, cancelled);
        Assert.False(online.Started.Task.IsCompleted);
        Assert.False(transaction.Replaced.Task.IsCompleted);
    }

    [Fact]
    public async Task Failure_IsContainedAndDoesNotModifyText()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        var sound = new RecordingSoundService();
        using var coordinator = CreateCoordinator(online, transaction, sound);

        await coordinator.QueueTranslationAsync(Selection, "uk");
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        online.Fail(new HttpRequestException("provider unavailable"));
        await sound.ErrorPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(transaction.Replaced.Task.IsCompleted);
    }

    [Fact]
    public async Task Cancellation_StopsRunningTranslationWithoutErrorSoundOrReplacement()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        var sound = new RecordingSoundService();
        using var coordinator = CreateCoordinator(online, transaction, sound);
        using var cancellation = new CancellationTokenSource();

        await coordinator.QueueTranslationAsync(Selection, "uk", cancellationToken: cancellation.Token);
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await transaction.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(transaction.Replaced.Task.IsCompleted);
        Assert.False(sound.ErrorPlayed.Task.IsCompleted);
    }

    [Fact]
    public async Task OnlineTranslationRequiresExplicitConsentBeforeNetworkServiceIsCalled()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        var sound = new RecordingSoundService();
        var settings = new FakeSettingsService
        {
            Current = new AppSettings
            {
                OnlineTranslationEnabled = false,
                UseOfflineTranslation = false
            }
        };
        using var coordinator = CreateCoordinator(online, transaction, sound, settings);

        await coordinator.QueueTranslationAsync(Selection, "uk");
        await sound.ErrorPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(online.Started.Task.IsCompleted);
        Assert.False(transaction.Replaced.Task.IsCompleted);
    }

    [Fact]
    public async Task MissingOfflineModelNeverFallsBackToOnlineProvider()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        var sound = new RecordingSoundService();
        var settings = new FakeSettingsService
        {
            Current = new AppSettings
            {
                OnlineTranslationEnabled = true,
                UseOfflineTranslation = true
            }
        };
        using var coordinator = CreateCoordinator(online, transaction, sound, settings);

        await coordinator.QueueTranslationAsync(Selection, "uk");
        await sound.ErrorPlayed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(online.Started.Task.IsCompleted);
        Assert.False(transaction.Replaced.Task.IsCompleted);
    }

    [Fact]
    public async Task QueuedOfflineTranslationNeverGetsReroutedToOnlineProvider()
    {
        var online = new ControllableTranslationService();
        var offline = new RecordingOfflineTranslationService();
        var transaction = new RecordingTextTransactionService();
        var settings = new FakeSettingsService();
        using var coordinator = CreateCoordinator(
            online,
            transaction,
            settings: settings,
            offline: offline);

        await coordinator.QueueTranslationAsync(Selection, "uk", "en");
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        settings.Current.UseOfflineTranslation = true;
        var privateSelection = Selection with { Text = "private offline text" };
        await coordinator.QueueTranslationAsync(privateSelection, "uk", "en");
        settings.Current.UseOfflineTranslation = false;
        online.Complete("first result");

        await offline.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("private offline text", offline.InputText);
        Assert.Equal(1, offline.CallCount);
        Assert.Equal(1, online.CallCount);
    }

    [Fact]
    public async Task RevokedOnlineConsentPreventsQueuedRequestFromReachingNetworkProvider()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        var sound = new RecordingSoundService();
        var settings = new FakeSettingsService();
        using var coordinator = CreateCoordinator(online, transaction, sound, settings);

        await coordinator.QueueTranslationAsync(Selection, "uk", "en");
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var queuedSelection = Selection with
        {
            Text = "must not leave this computer",
            WasSelectedByFallback = true
        };
        await coordinator.QueueTranslationAsync(queuedSelection, "uk", "en");
        settings.Current.OnlineTranslationEnabled = false;
        online.Complete("first result");

        var cancelled = await transaction.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(queuedSelection, cancelled);
        Assert.Equal(1, online.CallCount);
        Assert.True(sound.ErrorPlayed.Task.IsCompleted);
    }

    [Fact]
    public async Task FullQueue_RejectsNewestRequestAndCollapsesItsFallbackSelection()
    {
        var online = new ControllableTranslationService();
        var transaction = new RecordingTextTransactionService();
        var sound = new RecordingSoundService();
        using var coordinator = CreateCoordinator(online, transaction, sound);

        await coordinator.QueueTranslationAsync(Selection, "uk");
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var index = 0; index < 8; index++)
        {
            await coordinator.QueueTranslationAsync(
                Selection with { Text = $"queued-{index}" },
                "uk");
        }

        var rejected = Selection with { Text = "rejected", WasSelectedByFallback = true };
        var accepted = await coordinator.QueueTranslationAsync(rejected, "uk");

        Assert.False(accepted);
        var cancelled = await transaction.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(rejected, cancelled);
        Assert.True(sound.ErrorPlayed.Task.IsCompleted);

        online.Complete("переклад");
    }

    [Fact]
    public async Task Dispose_PreventsLateReplacementAndCleansEveryQueuedSelection()
    {
        var online = new CancellationIgnoringTranslationService();
        var transaction = new BatchRecordingTextTransactionService(expectedCancellations: 4);
        var coordinator = new TranslationCoordinator(
            online,
            new UnavailableOfflineTranslationService(),
            transaction,
            new FakeSettingsService(),
            new RecordingSoundService(),
            new NullLogger());
        var selections = Enumerable.Range(0, 4)
            .Select(index => Selection with
            {
                Text = $"private-{index}",
                WasSelectedByFallback = true
            })
            .ToArray();

        foreach (var selection in selections)
            Assert.True(await coordinator.QueueTranslationAsync(selection, "uk"));
        await online.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = Task.Run(coordinator.Dispose);
        await online.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        online.Complete("late result");
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
        await transaction.AllCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(transaction.Replacements);
        Assert.Equal(
            selections.OrderBy(selection => selection.Text),
            transaction.Cancellations.OrderBy(selection => selection.Text));
        var lateSelection = Selection with { WasSelectedByFallback = true };
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await coordinator.QueueTranslationAsync(lateSelection, "uk"));
    }

    private static TranslationCoordinator CreateCoordinator(
        ITranslationService online,
        RecordingTextTransactionService transaction,
        RecordingSoundService? sound = null,
        FakeSettingsService? settings = null,
        IOfflineTranslationService? offline = null) => new(
            online,
            offline ?? new UnavailableOfflineTranslationService(),
            transaction,
            settings ?? new FakeSettingsService(),
            sound ?? new RecordingSoundService(),
            new NullLogger());

    private sealed class ControllableTranslationService : ITranslationService
    {
        private int _callCount;
        private readonly TaskCompletionSource<string> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? TargetLanguage { get; private set; }
        public string? SourceLanguage { get; private set; }
        public string? InputText { get; private set; }
        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<string> TranslateAsync(
            string text,
            string targetLanguage,
            string sourceLanguage = "auto",
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            InputText = text;
            TargetLanguage = targetLanguage;
            SourceLanguage = sourceLanguage;
            Started.TrySetResult();
            return await _result.Task.WaitAsync(cancellationToken);
        }

        public void Complete(string result) => _result.TrySetResult(result);
        public void Fail(Exception exception) => _result.TrySetException(exception);
    }

    private sealed class CancellationIgnoringTranslationService : ITranslationService
    {
        private readonly TaskCompletionSource<string> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> TranslateAsync(
            string text,
            string targetLanguage,
            string sourceLanguage = "auto",
            CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(
                () => CancellationObserved.TrySetResult());
            Started.TrySetResult();
            return await _result.Task;
        }

        public void Complete(string result) => _result.TrySetResult(result);
    }

    private sealed class UnavailableOfflineTranslationService : IOfflineTranslationService
    {
        public bool IsModelAvailable() => false;
        public Task<string> TranslateAsync(string text, string targetLanguageCode, string sourceLanguageCode = "auto", CancellationToken cancellationToken = default) =>
            Task.FromResult(text);
    }

    private sealed class RecordingOfflineTranslationService : IOfflineTranslationService
    {
        private int _callCount;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? InputText { get; private set; }
        public int CallCount => Volatile.Read(ref _callCount);

        public bool IsModelAvailable() => true;

        public Task<string> TranslateAsync(
            string text,
            string targetLanguageCode,
            string sourceLanguageCode = "auto",
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            InputText = text;
            Started.TrySetResult();
            return Task.FromResult("local result");
        }
    }

    private sealed class RecordingTextTransactionService : ITextTransactionService
    {
        public TaskCompletionSource<string> Replaced { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TextSelection> Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TextSelection?> CaptureAsync(bool allowPreviousWordFallback, CancellationToken cancellationToken = default) =>
            Task.FromResult<TextSelection?>(Selection);
        public Task<bool> ReplaceAsync(TextSelection selection, string replacement, CancellationToken cancellationToken = default)
        {
            Replaced.TrySetResult(replacement);
            return Task.FromResult(true);
        }
        public Task CancelFallbackSelectionAsync(TextSelection selection, CancellationToken cancellationToken = default)
        {
            Cancelled.TrySetResult(selection);
            return Task.CompletedTask;
        }
    }

    private sealed class BatchRecordingTextTransactionService(int expectedCancellations)
        : ITextTransactionService
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<TextSelection> _cancellations = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _replacements = new();
        public IReadOnlyCollection<TextSelection> Cancellations => _cancellations.ToArray();
        public IReadOnlyCollection<string> Replacements => _replacements.ToArray();
        public TaskCompletionSource AllCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TextSelection?> CaptureAsync(
            bool allowPreviousWordFallback,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TextSelection?>(Selection);

        public Task<bool> ReplaceAsync(
            TextSelection selection,
            string replacement,
            CancellationToken cancellationToken = default)
        {
            _replacements.Enqueue(replacement);
            return Task.FromResult(true);
        }

        public Task CancelFallbackSelectionAsync(
            TextSelection selection,
            CancellationToken cancellationToken = default)
        {
            _cancellations.Enqueue(selection);
            if (_cancellations.Count >= expectedCancellations)
                AllCancelled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new()
        {
            UseOfflineTranslation = false,
            OnlineTranslationEnabled = true
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class RecordingSoundService : ISoundService
    {
        public TaskCompletionSource ErrorPlayed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PlaySwitchSound() { }
        public void PlayAutoConvertSound() { }
        public void PlayErrorSound() => ErrorPlayed.TrySetResult();
    }

    private sealed class NullLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }
}
