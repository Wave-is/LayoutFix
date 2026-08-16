using System.Threading.Channels;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public sealed class TranslationCoordinator : ITranslationCoordinator
{
    private const int QueueCapacity = 8;
    private readonly Channel<TranslationRequest> _queue;
    private readonly Task _processor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ITranslationService _onlineTranslation;
    private readonly IOfflineTranslationService _offlineTranslation;
    private readonly ITextTransactionService _textTransaction;
    private readonly ISettingsService _settings;
    private readonly ISoundService _sound;
    private readonly ILoggerService _logger;
    private bool _disposed;

    public TranslationCoordinator(
        ITranslationService onlineTranslation,
        IOfflineTranslationService offlineTranslation,
        ITextTransactionService textTransaction,
        ISettingsService settings,
        ISoundService sound,
        ILoggerService logger)
    {
        _onlineTranslation = onlineTranslation;
        _offlineTranslation = offlineTranslation;
        _textTransaction = textTransaction;
        _settings = settings;
        _sound = sound;
        _logger = logger;
        _queue = Channel.CreateBounded<TranslationRequest>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _processor = Task.Run(ProcessQueueAsync);
    }

    public ValueTask<bool> QueueTranslationAsync(
        TextSelection selection,
        string targetLanguage,
        string sourceLanguage = "auto",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<bool>(cancellationToken);

        var settingsAtQueueTime = _settings.Current;
        if (_queue.Writer.TryWrite(
                new TranslationRequest(
                    selection,
                    targetLanguage,
                    sourceLanguage,
                    settingsAtQueueTime.UseOfflineTranslation,
                    settingsAtQueueTime.OnlineTranslationEnabled,
                    cancellationToken)))
            return ValueTask.FromResult(true);

        if (_disposed)
            return RejectDisposedRequestAsync(selection);

        return RejectNewestRequestAsync(selection);
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var request in _queue.Reader.ReadAllAsync())
        {
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _shutdown.Token,
                request.CancellationToken);
            var cancellationToken = requestCancellation.Token;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var envelope = TranslationTextEnvelope.Create(request.Selection.Text);
                if (envelope.Core.Length == 0)
                {
                    _logger.LogInfo("Translation skipped because the selection contains only whitespace.");
                    await CancelSelectionSafelyAsync(request.Selection);
                    continue;
                }

                string translated;
                if (request.UseOfflineTranslationAtQueueTime)
                {
                    if (!_offlineTranslation.IsModelAvailable())
                        throw new FileNotFoundException("Offline translation model is not downloaded.");
                    translated = await _offlineTranslation.TranslateAsync(
                        envelope.Core,
                        request.TargetLanguage,
                        request.SourceLanguage,
                        cancellationToken);
                }
                else
                {
                    if (!request.OnlineTranslationEnabledAtQueueTime ||
                        !_settings.Current.OnlineTranslationEnabled)
                        throw new InvalidOperationException(
                            "Online translation is disabled. Enable it explicitly in Settings > Translation.");
                    translated = await _onlineTranslation.TranslateAsync(
                        envelope.Core,
                        request.TargetLanguage,
                        request.SourceLanguage,
                        cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                translated = envelope.Wrap(translated);
                _logger.LogInfo($"Translation completed. Result length: {translated.Length}");
                var replaced = await _textTransaction.ReplaceAsync(
                    request.Selection,
                    translated,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (replaced)
                {
                    if (_settings.Current.SoundEnabled) _sound.PlaySwitchSound();
                }
                else
                {
                    _logger.LogInfo("Translation result was not inserted because the target selection changed.");
                    await CancelSelectionSafelyAsync(request.Selection);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Translation was cancelled.");
                await CancelSelectionSafelyAsync(request.Selection);
            }
            catch (Exception ex)
            {
                _logger.LogError("Translation failed", ex);
                TryPlayErrorSound();
                await CancelSelectionSafelyAsync(request.Selection);
            }
        }
    }

    private async ValueTask<bool> RejectDisposedRequestAsync(TextSelection selection)
    {
        await CancelSelectionSafelyAsync(selection);
        throw new ObjectDisposedException(nameof(TranslationCoordinator));
    }

    private async ValueTask<bool> RejectNewestRequestAsync(TextSelection selection)
    {
        _logger.LogWarning("Translation queue is full; the newest request was rejected.");
        TryPlayErrorSound();
        await CancelSelectionSafelyAsync(selection);
        return false;
    }

    private async Task CancelSelectionSafelyAsync(TextSelection selection)
    {
        try
        {
            await _textTransaction.CancelFallbackSelectionAsync(selection);
        }
        catch (Exception exception)
        {
            _logger.LogError("Translation selection cleanup failed", exception);
        }
    }

    private void TryPlayErrorSound()
    {
        try
        {
            _sound.PlayErrorSound();
        }
        catch (Exception exception)
        {
            _logger.LogError("Translation error sound failed", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        var processorStopped = false;
        try
        {
            processorStopped = _processor.Wait(TimeSpan.FromSeconds(2));
            if (!processorStopped)
                _logger.LogWarning("Translation worker did not stop within the shutdown deadline.");
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        finally
        {
            if (processorStopped)
                _shutdown.Dispose();
        }
    }

    private sealed record TranslationRequest(
        TextSelection Selection,
        string TargetLanguage,
        string SourceLanguage,
        bool UseOfflineTranslationAtQueueTime,
        bool OnlineTranslationEnabledAtQueueTime,
        CancellationToken CancellationToken);

    private readonly record struct TranslationTextEnvelope(
        string Prefix,
        string Core,
        string Suffix)
    {
        public static TranslationTextEnvelope Create(string text)
        {
            var start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
                start++;

            var end = text.Length;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;

            return new(
                text[..start],
                text[start..end],
                text[end..]);
        }

        public string Wrap(string translated) => Prefix + translated + Suffix;
    }
}
