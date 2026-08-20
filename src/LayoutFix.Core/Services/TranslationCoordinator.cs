using System.Threading.Channels;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public sealed class TranslationCoordinator : ITranslationCoordinator
{
    private const int QueueCapacity = 8;
    private const string TranslationFailedDiagnosticCode = "LF-TR-002";
    private const string SelectionChangedDiagnosticCode = "LF-TR-003";
    private readonly Channel<TranslationRequest> _queue;
    private readonly Task _processor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ITranslationService _onlineTranslation;
    private readonly IOfflineTranslationService _offlineTranslation;
    private readonly ITextTransactionService _textTransaction;
    private readonly ISettingsService _settings;
    private readonly ISoundService _sound;
    private readonly ILoggerService _logger;
    private readonly IPopupService? _popupService;
    private long _nextRequestId;
    private bool _disposed;

    public TranslationCoordinator(
        ITranslationService onlineTranslation,
        IOfflineTranslationService offlineTranslation,
        ITextTransactionService textTransaction,
        ISettingsService settings,
        ISoundService sound,
        ILoggerService logger,
        IPopupService? popupService = null)
    {
        _onlineTranslation = onlineTranslation;
        _offlineTranslation = offlineTranslation;
        _textTransaction = textTransaction;
        _settings = settings;
        _sound = sound;
        _logger = logger;
        _popupService = popupService;
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
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var provider = settingsAtQueueTime.UseOfflineTranslation ? "offline" : "online";
        if (_queue.Writer.TryWrite(
                new TranslationRequest(
                    requestId,
                    selection,
                    targetLanguage,
                    sourceLanguage,
                    settingsAtQueueTime.UseOfflineTranslation,
                    settingsAtQueueTime.OnlineTranslationEnabled,
                    settingsAtQueueTime.OfflineModelType,
                    cancellationToken)))
        {
            _logger.LogInfo(
                $"SupportDiagnostic: TranslationRequestId={requestId}; Phase=translation-queue; " +
                $"Outcome=accepted; Provider={provider}; Model={DiagnosticValue(settingsAtQueueTime.OfflineModelType)}; " +
                $"SourceLanguage={DiagnosticValue(sourceLanguage)}; TargetLanguage={DiagnosticValue(targetLanguage)}; " +
                $"InputLength={selection.Text.Length}.");
            return ValueTask.FromResult(true);
        }

        if (_disposed)
            return RejectDisposedRequestAsync(selection, requestId);

        return RejectNewestRequestAsync(selection, requestId);
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
                var provider = request.UseOfflineTranslationAtQueueTime ? "offline" : "online";
                _logger.LogInfo(
                    $"SupportDiagnostic: TranslationRequestId={request.RequestId}; Phase=translation; " +
                    $"Outcome=started; Provider={provider}; Model={DiagnosticValue(request.OfflineModelAtQueueTime)}; " +
                    $"SourceLanguage={DiagnosticValue(request.SourceLanguage)}; " +
                    $"TargetLanguage={DiagnosticValue(request.TargetLanguage)}; " +
                    $"InputLength={request.Selection.Text.Length}.");
                var envelope = TranslationTextEnvelope.Create(request.Selection.Text);
                if (envelope.Core.Length == 0)
                {
                    _logger.LogInfo("Translation skipped because the selection contains only whitespace.");
                    _logger.LogInfo(
                        $"SupportDiagnostic: TranslationRequestId={request.RequestId}; Phase=translation; " +
                        "Outcome=rejected; Reason=whitespace-only-selection.");
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
                _logger.LogInfo(
                    $"SupportDiagnostic: TranslationRequestId={request.RequestId}; Phase=translation-provider; " +
                    $"Outcome=accepted; ResultLength={translated.Length}.");
                var replaced = await _textTransaction.ReplaceAsync(
                    request.Selection,
                    translated,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (replaced)
                {
                    _logger.LogInfo(
                        $"SupportDiagnostic: TranslationRequestId={request.RequestId}; Phase=translation-insert; " +
                        "Outcome=accepted.");
                    if (_settings.Current.SoundEnabled) _sound.PlaySwitchSound();
                }
                else
                {
                    _logger.LogInfo("Translation result was not inserted because the target selection changed.");
                    _logger.LogWarning(
                        $"DiagnosticCode: {SelectionChangedDiagnosticCode}; " +
                        $"TranslationRequestId: {request.RequestId}; Stage: TextReplacement; " +
                        "Outcome: result not inserted because the target selection changed or became unsafe.");
                    _popupService?.ShowStatus(
                        WithDiagnosticCode(
                            "The translation finished, but the original selection changed or became unavailable.",
                            SelectionChangedDiagnosticCode),
                        isError: true);
                    await CancelSelectionSafelyAsync(request.Selection);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Translation was cancelled.");
                _logger.LogInfo(
                    $"SupportDiagnostic: TranslationRequestId={request.RequestId}; Phase=translation; " +
                    "Outcome=cancelled.");
                await CancelSelectionSafelyAsync(request.Selection);
            }
            catch (Exception ex)
            {
                var failureReason = ClassifyFailure(ex, request);
                var provider = request.UseOfflineTranslationAtQueueTime ? "offline" : "online";
                _logger.LogError(
                    $"DiagnosticCode: {TranslationFailedDiagnosticCode}; " +
                    $"TranslationRequestId: {request.RequestId}; Stage: Translation; " +
                    $"Outcome: failed; Reason: {failureReason}; Provider: {provider}; " +
                    $"Model: {DiagnosticValue(request.OfflineModelAtQueueTime)}; " +
                    $"SourceLanguage: {DiagnosticValue(request.SourceLanguage)}; " +
                    $"TargetLanguage: {DiagnosticValue(request.TargetLanguage)}; " +
                    $"InputLength: {request.Selection.Text.Length}",
                    ex);
                _popupService?.ShowStatus(
                    WithDiagnosticCode(
                        "LayoutFix could not complete the translation. Diagnostic logging can provide the exact failure stage.",
                        TranslationFailedDiagnosticCode),
                    isError: true);
                TryPlayErrorSound();
                await CancelSelectionSafelyAsync(request.Selection);
            }
        }
    }

    private async ValueTask<bool> RejectDisposedRequestAsync(TextSelection selection, long requestId)
    {
        _logger.LogWarning(
            $"SupportDiagnostic: TranslationRequestId={requestId}; Phase=translation-queue; " +
            "Outcome=rejected; Reason=coordinator-disposed.");
        await CancelSelectionSafelyAsync(selection);
        throw new ObjectDisposedException(nameof(TranslationCoordinator));
    }

    private async ValueTask<bool> RejectNewestRequestAsync(TextSelection selection, long requestId)
    {
        _logger.LogWarning("Translation queue is full; the newest request was rejected.");
        _logger.LogWarning(
            $"SupportDiagnostic: TranslationRequestId={requestId}; Phase=translation-queue; " +
            "Outcome=rejected; Reason=queue-full.");
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

    private static string ClassifyFailure(Exception exception, TranslationRequest request) => exception switch
    {
        FileNotFoundException => request.UseOfflineTranslationAtQueueTime
            ? "offline-model-or-worker-missing"
            : "required-file-missing",
        NotSupportedException => "unsupported-language-or-model",
        TimeoutException => request.UseOfflineTranslationAtQueueTime
            ? "offline-worker-timeout"
            : "online-provider-timeout",
        HttpRequestException => "online-provider-network-failure",
        InvalidDataException => "offline-worker-invalid-response",
        IOException => request.UseOfflineTranslationAtQueueTime
            ? "offline-worker-io-failure"
            : "provider-io-failure",
        InvalidOperationException when !request.UseOfflineTranslationAtQueueTime &&
                                       !request.OnlineTranslationEnabledAtQueueTime =>
            "online-translation-disabled",
        InvalidOperationException when request.UseOfflineTranslationAtQueueTime =>
            "offline-worker-rejected-result",
        InvalidOperationException => "translation-provider-rejected-request",
        ArgumentException => "invalid-translation-request",
        _ => "unexpected-translation-failure"
    };

    private static string WithDiagnosticCode(string message, string code) => $"{message} [{code}]";

    private static string DiagnosticValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";

        return new string(value
            .Take(64)
            .Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '_')
            .ToArray());
    }

    private sealed record TranslationRequest(
        long RequestId,
        TextSelection Selection,
        string TargetLanguage,
        string SourceLanguage,
        bool UseOfflineTranslationAtQueueTime,
        bool OnlineTranslationEnabledAtQueueTime,
        string OfflineModelAtQueueTime,
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
