using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public interface IHotkeyCoordinator : IDisposable
{
    void Initialize();
    Task ExecuteActionAsync(HotkeyAction action);
}

public class HotkeyCoordinator : IHotkeyCoordinator
{
    private const string BusyDiagnosticCode = "LF-HK-001";
    private const string TimeoutDiagnosticCode = "LF-HK-002";
    private const string BlockedDiagnosticCode = "LF-HK-003";
    private const string NoTextDiagnosticCode = "LF-HK-004";
    private const string NoChangeDiagnosticCode = "LF-HK-005";
    private const string UnsafeReplacementDiagnosticCode = "LF-HK-006";
    private const string FailedDiagnosticCode = "LF-HK-007";
    private const string TranslationBusyDiagnosticCode = "LF-TR-001";
    private const string MissingLayoutDiagnosticCode = "LF-LY-001";
    private const int QueueCapacity = 64;
    private static readonly TimeSpan DefaultActionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BusyNotificationThrottle = TimeSpan.FromSeconds(2);
    // A physical hotkey can arrive twice around the completion boundary. Measure
    // the debounce window from the accepted key-down, not from action completion:
    // a slow transaction must not make the next deliberate shortcut unresponsive.
    private static readonly TimeSpan AcceptedDuplicateWindow = TimeSpan.FromMilliseconds(250);
    private readonly Channel<ActionRequest> _executionQueue;
    private readonly Task _queueProcessor;
    private readonly TimeSpan _actionTimeout;
    private readonly CancellationTokenSource _bindingRefreshCancellation = new();
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private Task? _bindingRefreshTask;
    private ShortcutBinding[] _shortcutBindings = [];
    // Zero means idle; otherwise the value is the pending HotkeyAction plus one.
    // Keeping the action in the atomic state lets us collapse a quick duplicate
    // press without showing an error while still rejecting a conflicting action.
    private int _pendingHotkeyAction;
    private int _lastAcceptedHotkeyAction;
    private long _lastAcceptedHotkeyTimestamp;
    private long _lastBusyNotificationTimestamp;
    private bool _disposed;
    private readonly IKeyboardHook _keyboardHook;
    private readonly ITextTransactionService _textTransactionService;
    private readonly ISettingsService _settingsService;
    private readonly IKeyboardLayoutManager _keyboardLayoutManager;
    private readonly ILayoutConverter _layoutConverter;
    private readonly ITextTransformer _textTransformer;
    private readonly TransliterationService _transliterationService;
    private readonly INumberToTextConverter _numberToTextConverter;
    private readonly ILoggerService _logger;

    private readonly IActiveWindowProvider _activeWindowProvider;
    private readonly ISoundService _soundService;
    private readonly ITranslationCoordinator _translationCoordinator;
    private readonly ITranslatorWindowProvider _translatorWindowProvider;
    private readonly IAutoCorrectionMemory? _correctionMemory;
    private readonly IPopupService? _popupService;
    private readonly IDictionaryAnalyzer? _dictionaryAnalyzer;

    public HotkeyCoordinator(
        IKeyboardHook keyboardHook,
        ITextTransactionService textTransactionService,
        ISettingsService settingsService,
        IKeyboardLayoutManager keyboardLayoutManager,
        ILayoutConverter layoutConverter,
        ITextTransformer textTransformer,
        TransliterationService transliterationService,
        INumberToTextConverter numberToTextConverter,
        ILoggerService logger,
        IActiveWindowProvider activeWindowProvider,
        ISoundService soundService,
        ITranslationCoordinator translationCoordinator,
        ITranslatorWindowProvider translatorWindowProvider,
        IAutoCorrectionMemory? correctionMemory = null,
        IPopupService? popupService = null,
        IDictionaryAnalyzer? dictionaryAnalyzer = null,
        TimeSpan? actionTimeout = null)
    {
        _keyboardHook = keyboardHook;
        _textTransactionService = textTransactionService;
        _settingsService = settingsService;
        _keyboardLayoutManager = keyboardLayoutManager;
        _layoutConverter = layoutConverter;
        _textTransformer = textTransformer;
        _transliterationService = transliterationService;
        _numberToTextConverter = numberToTextConverter;
        _logger = logger;
        _activeWindowProvider = activeWindowProvider;
        _soundService = soundService;
        _translationCoordinator = translationCoordinator;
        _translatorWindowProvider = translatorWindowProvider;
        _correctionMemory = correctionMemory;
        _popupService = popupService;
        _dictionaryAnalyzer = dictionaryAnalyzer;
        _actionTimeout = actionTimeout ?? DefaultActionTimeout;
        if (_actionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(actionTimeout));

        _executionQueue = Channel.CreateBounded<ActionRequest>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _queueProcessor = Task.Run(ProcessActionQueueAsync);
    }

    public void Initialize()
    {
        _keyboardLayoutManager.LoadAll();
        WarmUpManualCorrectionPipeline();
        RefreshShortcutBindings();
        LogHotkeyConflicts();
        _keyboardHook.HotkeyPressed += OnHotkeyPressed;
        _bindingRefreshTask ??= Task.Run(RefreshShortcutBindingsLoopAsync);
    }

    private void WarmUpManualCorrectionPipeline()
    {
        try
        {
            const string probe = "layoutfixwarmup";
            var currentLayout = _activeWindowProvider.GetActiveLayoutCode();
            var activeLayouts = _keyboardLayoutManager.GetLayoutOrder(currentLayout);
            if (_dictionaryAnalyzer != null)
                _ = _dictionaryAnalyzer.TryGetCorrection(probe, currentLayout, out _);
            _ = _layoutConverter.AutoConvert(probe, activeLayouts, currentLayout);
        }
        catch (Exception exception)
        {
            // Prewarming is an optimization only. A missing layout or dictionary
            // must not prevent startup; the real action retains its normal path.
            _logger.LogError("Manual correction pipeline warm-up failed", exception);
        }
    }

    private async Task RefreshShortcutBindingsLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, _bindingRefreshCancellation.Token);
                RefreshShortcutBindings();
            }
        }
        catch (OperationCanceledException) when (_bindingRefreshCancellation.IsCancellationRequested)
        {
        }
    }

    private void RefreshShortcutBindings()
    {
        try
        {
            var bindings = (_settingsService.Current.HotkeyConfigs ?? [])
                .Where(config => config.Enabled &&
                    Enum.TryParse<HotkeyAction>(config.Action, true, out _))
                .Select(config => new ShortcutBinding(
                    HotkeyCombo.Parse(config.Hotkey),
                    Enum.Parse<HotkeyAction>(config.Action, true)))
                .Where(binding => binding.Combo.VirtualKey != 0)
                .ToArray();
            Volatile.Write(ref _shortcutBindings, bindings);
        }
        catch (InvalidOperationException)
        {
            // The settings UI may be editing the list concurrently. Keep the last
            // complete immutable snapshot and retry on the next refresh tick.
        }
    }

    private void LogHotkeyConflicts()
    {
        var enabled = _settingsService.Current.HotkeyConfigs.Where(config => config.Enabled).ToArray();
        for (var left = 0; left < enabled.Length; left++)
        {
            var leftCombo = HotkeyCombo.Parse(enabled[left].Hotkey);
            for (var right = left + 1; right < enabled.Length; right++)
            {
                if (leftCombo.Matches(HotkeyCombo.Parse(enabled[right].Hotkey)))
                {
                    _logger.LogWarning(
                        $"Hotkey conflict: '{enabled[left].Hotkey}' is assigned to " +
                        $"{enabled[left].Action} and {enabled[right].Action}.");
                }
            }
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        try
        {
            if (e.IsRepeat) return;
            var binding = Volatile.Read(ref _shortcutBindings)
                .FirstOrDefault(candidate => IsComboMatch(candidate.Combo, e.Combo));
            if (binding == null || IsBlacklisted()) return;

            e.Handled = true;
            var requestedState = (int)binding.Action + 1;
            var pendingState = Interlocked.CompareExchange(
                ref _pendingHotkeyAction,
                requestedState,
                0);
            if (pendingState != 0)
            {
                if (pendingState == requestedState)
                {
                    _logger.LogInfo(
                        $"Action: {binding.Action}; Outcome: duplicate hotkey coalesced " +
                        "because the same action is already running.");
                }
                else
                {
                    ReportBusyHotkey(binding.Action);
                }
                return;
            }

            if (IsRecentlyAcceptedDuplicate(requestedState))
            {
                Interlocked.CompareExchange(ref _pendingHotkeyAction, 0, requestedState);
                _logger.LogInfo(
                    $"Action: {binding.Action}; Outcome: duplicate hotkey coalesced " +
                    "because the same physical gesture was already accepted.");
                return;
            }

            Volatile.Write(ref _lastAcceptedHotkeyAction, requestedState);
            Volatile.Write(ref _lastAcceptedHotkeyTimestamp, Stopwatch.GetTimestamp());
            if (!_executionQueue.Writer.TryWrite(new ActionRequest(
                    binding.Action,
                    Completion: null,
                    IsHotkey: true)))
            {
                Interlocked.CompareExchange(ref _pendingHotkeyAction, 0, requestedState);
                ReportBusyHotkey(binding.Action);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error in OnHotkeyPressed", ex);
        }
    }

    private bool IsRecentlyAcceptedDuplicate(int requestedState)
    {
        if (Volatile.Read(ref _lastAcceptedHotkeyAction) != requestedState)
            return false;

        var acceptedAt = Volatile.Read(ref _lastAcceptedHotkeyTimestamp);
        return acceptedAt != 0 &&
            Stopwatch.GetElapsedTime(acceptedAt) < AcceptedDuplicateWindow;
    }

    private void ReportBusyHotkey(HotkeyAction action)
    {
        var now = Stopwatch.GetTimestamp();
        while (true)
        {
            var previous = Volatile.Read(ref _lastBusyNotificationTimestamp);
            if (previous != 0 &&
                Stopwatch.GetElapsedTime(previous, now) < BusyNotificationThrottle)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _lastBusyNotificationTimestamp,
                    now,
                    previous) == previous)
            {
                break;
            }
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state => state.Coordinator.ShowBusyHotkeyStatus(state.Action),
            (Coordinator: this, Action: action),
            preferLocal: false);
    }

    private void ShowBusyHotkeyStatus(HotkeyAction action)
    {
        try
        {
            _logger.LogWarning(
                $"DiagnosticCode: {BusyDiagnosticCode}; Action: {action}; " +
                "Outcome: rejected because another hotkey action is still running.");
            _popupService?.ShowStatus(
                WithDiagnosticCode(
                    "LayoutFix is still processing the previous shortcut. " +
                    "This shortcut was not queued; try again in a moment.",
                    BusyDiagnosticCode),
                isError: true);
        }
        catch (Exception exception)
        {
            _logger.LogError("Unable to show the busy hotkey notification", exception);
        }
    }

    private bool IsBlacklisted()
    {
        var blacklisted = _settingsService.Current.BlacklistedProcesses;
        if (blacklisted == null || blacklisted.Count == 0) return false;
        
        string procName = _activeWindowProvider.GetActiveProcessName();
        if (string.IsNullOrEmpty(procName)) return false;
        
        var actualName = Path.GetFileNameWithoutExtension(procName);
        return blacklisted.Any(configured => string.Equals(
            Path.GetFileNameWithoutExtension(configured),
            actualName,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool IsComboMatch(HotkeyCombo expected, HotkeyCombo actual)
        => expected.Matches(actual);

    public async Task ExecuteActionAsync(HotkeyAction action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _executionQueue.Writer.WriteAsync(new ActionRequest(
                action,
                completion,
                IsHotkey: false));
        }
        catch (ChannelClosedException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(HotkeyCoordinator));
        }
        await completion.Task;
    }

    private async Task ProcessActionQueueAsync()
    {
        await foreach (var request in _executionQueue.Reader.ReadAllAsync())
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _shutdownCancellation.Token);
            cancellation.CancelAfter(_actionTimeout);
            var progress = new ActionExecutionProgress();
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await ExecuteActionCoreAsync(
                    request.Action,
                    cancellation.Token,
                    progress);
                request.Completion?.TrySetResult();
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
                request.Completion?.TrySetException(
                    new ObjectDisposedException(nameof(HotkeyCoordinator)));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                var stage = progress.CurrentStage;
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                _logger.LogWarning(
                    $"DiagnosticCode: {TimeoutDiagnosticCode}; Action: {request.Action}; " +
                    "Outcome: bounded execution deadline exceeded. " +
                    $"Stage: {stage}; ElapsedMs: {elapsedMilliseconds:0}.");
                _popupService?.ShowStatus(
                    WithDiagnosticCode(
                        "LayoutFix stopped this operation because the target application did not respond " +
                        $"in time during {GetStageDisplayName(stage)}.",
                        TimeoutDiagnosticCode),
                    isError: true);
                request.Completion?.TrySetException(new TimeoutException(
                    $"LayoutFix action {request.Action} exceeded its execution deadline during {stage}."));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unhandled queued action error for {request.Action}", ex);
                request.Completion?.TrySetException(ex);
            }
            finally
            {
                _logger.LogInfo(
                    $"SupportDiagnostic: Phase=action-timing; Action={request.Action}; " +
                    $"ElapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}.");
                if (request.IsHotkey)
                {
                    Volatile.Write(ref _pendingHotkeyAction, 0);
                }
            }
        }
    }

    private async Task ExecuteActionCoreAsync(
        HotkeyAction action,
        CancellationToken cancellationToken,
        ActionExecutionProgress progress)
    {
        TextSelection? pendingFallbackSelection = null;
        progress.Enter(ActionExecutionStage.PolicyCheck);
        cancellationToken.ThrowIfCancellationRequested();
        var isBlacklisted = IsBlacklisted();
        cancellationToken.ThrowIfCancellationRequested();
        if (isBlacklisted)
        {
            _logger.LogWarning(
                $"DiagnosticCode: {BlockedDiagnosticCode}; Action: {action}; " +
                "Outcome: blocked by application policy.");
            _popupService?.ShowStatus(WithDiagnosticCode(
                "LayoutFix is disabled for this application.",
                BlockedDiagnosticCode));
            return;
        }

        try
        {
            _logger.LogInfo($"--- ExecuteActionAsync Started for action: {action} ---");

            if (action == HotkeyAction.SwitchLayout)
            {
                progress.Enter(ActionExecutionStage.LayoutActivation);
                _activeWindowProvider.SwitchToNextLayout();
                cancellationToken.ThrowIfCancellationRequested();
                if (_settingsService.Current.SoundEnabled)
                {
                    progress.Enter(ActionExecutionStage.Feedback);
                    _soundService.PlaySwitchSound();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return;
            }

            progress.Enter(ActionExecutionStage.TextCapture);
            var selection = await _textTransactionService.CaptureAsync(
                allowPreviousWordFallback: action is HotkeyAction.FixLayout or HotkeyAction.Undo,
                cancellationToken);
            if (selection?.WasSelectedByFallback == true)
                pendingFallbackSelection = selection;
            cancellationToken.ThrowIfCancellationRequested();
            var text = selection?.Text;
            _logger.LogInfo($"Text capture completed. Length: {text?.Length ?? 0}");

            if (action == HotkeyAction.OpenTranslator)
            {
                progress.Enter(ActionExecutionStage.TranslatorOpening);
                _logger.LogInfo("Opening Translator Window...");
                _translatorWindowProvider.ShowTranslator(text ?? "");
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            if (selection == null || string.IsNullOrEmpty(text))
            {
                await CancelFallbackSelectionSafelyAsync(pendingFallbackSelection);
                pendingFallbackSelection = null;
                _logger.LogWarning(
                    $"DiagnosticCode: {NoTextDiagnosticCode}; Action: {action}; " +
                    "Stage: TextCapture; Outcome: no editable text captured.");
                _popupService?.ShowStatus(
                    WithDiagnosticCode(
                        "No editable text was captured. The field may be secure, busy, or unsupported.",
                        NoTextDiagnosticCode));
                return;
            }

            var replacementSucceeded = false;
            if (action == HotkeyAction.Translate1 || action == HotkeyAction.Translate2 || action == HotkeyAction.Translate3)
            {
                string targetLang = action switch
                {
                    HotkeyAction.Translate1 => _settingsService.Current.TranslateLang1,
                    HotkeyAction.Translate2 => _settingsService.Current.TranslateLang2,
                    _ => _settingsService.Current.TranslateLang3
                };

                progress.Enter(ActionExecutionStage.TranslationQueue);
                var accepted = await _translationCoordinator.QueueTranslationAsync(
                    selection,
                    targetLang,
                    cancellationToken: cancellationToken);
                pendingFallbackSelection = null;
                cancellationToken.ThrowIfCancellationRequested();
                if (accepted)
                {
                    _logger.LogInfo($"Translation queued for target language {targetLang}.");
                }
                else
                {
                    _logger.LogWarning(
                        $"DiagnosticCode: {TranslationBusyDiagnosticCode}; Action: {action}; " +
                        "Stage: TranslationQueue; Outcome: translation request rejected.");
                    _popupService?.ShowStatus(
                        WithDiagnosticCode(
                            "The translation queue is busy. Try again after the current translation finishes.",
                            TranslationBusyDiagnosticCode),
                        isError: true);
                }
                return;
            }
            else
            {
                progress.Enter(ActionExecutionStage.TextProcessing);
                AutoCorrectionUndoCandidate undoCandidate = default;
                var isAutoCorrectionUndo =
                    action is HotkeyAction.FixLayout or HotkeyAction.Undo &&
                    _correctionMemory?.TryPrepareUndo(selection, out undoCandidate) == true;
                var (newText, targetLayoutCode) = isAutoCorrectionUndo
                    ? (undoCandidate.RestoredSelectionText, null)
                    : ProcessText(text, action);
                cancellationToken.ThrowIfCancellationRequested();
                var textChanged = newText != null && newText != text;
                _logger.LogInfo($"Text processing completed. Changed: {textChanged}, TargetLayout: '{targetLayoutCode}'");

                if (textChanged)
                {
                    progress.Enter(ActionExecutionStage.TextReplacement);
                    replacementSucceeded = await _textTransactionService.ReplaceAsync(
                        selection,
                        newText!,
                        cancellationToken);
                    if (replacementSucceeded)
                        pendingFallbackSelection = null;
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    progress.Enter(ActionExecutionStage.SelectionCleanup);
                    await CancelFallbackSelectionSafelyAsync(pendingFallbackSelection);
                    pendingFallbackSelection = null;
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogWarning(
                        $"DiagnosticCode: {NoChangeDiagnosticCode}; Action: {action}; " +
                        "Stage: TextProcessing; Outcome: conversion produced no change.");
                    _popupService?.ShowStatus(WithDiagnosticCode(
                        "The selected text does not need this conversion.",
                        NoChangeDiagnosticCode));
                    return;
                }

                if (replacementSucceeded &&
                    targetLayoutCode != null &&
                    selection.AllowTargetLayoutActivation)
                {
                    progress.Enter(ActionExecutionStage.LayoutActivation);
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInfo($"Switching active layout to {targetLayoutCode}");
                    if (!_activeWindowProvider.TrySwitchToLayout(targetLayoutCode))
                    {
                        _logger.LogWarning(
                            $"DiagnosticCode: {MissingLayoutDiagnosticCode}; Action: {action}; " +
                            "Stage: LayoutActivation; Outcome: target layout is unavailable.");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else if (replacementSucceeded &&
                         targetLayoutCode != null &&
                         !selection.AllowTargetLayoutActivation)
                {
                    _logger.LogInfo(
                        "Target layout activation skipped by the direct adapter safety contract.");
                }

                if (replacementSucceeded && isAutoCorrectionUndo)
                {
                    progress.Enter(ActionExecutionStage.UndoLearning);
                    cancellationToken.ThrowIfCancellationRequested();
                    _correctionMemory!.CommitUndo(undoCandidate.Generation);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_settingsService.Current.UserExceptions.Contains(
                            undoCandidate.OriginalText,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        _settingsService.Current.UserExceptions.Add(undoCandidate.OriginalText);
                        _settingsService.Save(_settingsService.Current);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    _logger.LogInfo("Automatic correction was undone and learned as a user exception.");
                }
            }

            if (replacementSucceeded && _settingsService.Current.SoundEnabled)
            {
                progress.Enter(ActionExecutionStage.Feedback);
                _soundService.PlaySwitchSound();
                cancellationToken.ThrowIfCancellationRequested();
            }
            else if (!replacementSucceeded)
            {
                progress.Enter(ActionExecutionStage.SelectionCleanup);
                await CancelFallbackSelectionSafelyAsync(pendingFallbackSelection);
                pendingFallbackSelection = null;
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogWarning(
                    $"DiagnosticCode: {UnsafeReplacementDiagnosticCode}; Action: {action}; " +
                    "Stage: SelectionCleanup; Outcome: replacement rejected as unsafe.");
                _popupService?.ShowStatus(WithDiagnosticCode(
                    "LayoutFix did not change the text because the operation was not safe.",
                    UnsafeReplacementDiagnosticCode));
            }

            _logger.LogInfo($"--- ExecuteActionAsync Finished ---");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CancelFallbackSelectionSafelyAsync(pendingFallbackSelection);
            throw;
        }
        catch (Exception ex)
        {
            await CancelFallbackSelectionSafelyAsync(pendingFallbackSelection);
            _logger.LogError(
                $"DiagnosticCode: {FailedDiagnosticCode}; Action: {action}; " +
                $"Stage: {progress.CurrentStage}; Outcome: action failed.",
                ex);
            _popupService?.ShowStatus(
                WithDiagnosticCode(
                    "LayoutFix could not complete the requested action.",
                    FailedDiagnosticCode),
                isError: true);
        }
    }

    private async Task CancelFallbackSelectionSafelyAsync(TextSelection? selection)
    {
        if (selection?.WasSelectedByFallback != true)
            return;

        try
        {
            await _textTransactionService.CancelFallbackSelectionAsync(
                selection,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError("Unable to collapse a pending fallback selection", exception);
        }
    }

    private static string GetStageDisplayName(ActionExecutionStage stage) => stage switch
    {
        ActionExecutionStage.PolicyCheck => "application safety checks",
        ActionExecutionStage.TextCapture => "text capture",
        ActionExecutionStage.TextProcessing => "text processing",
        ActionExecutionStage.TextReplacement => "text replacement",
        ActionExecutionStage.SelectionCleanup => "selection cleanup",
        ActionExecutionStage.TranslationQueue => "translation queueing",
        ActionExecutionStage.TranslatorOpening => "opening the translator",
        ActionExecutionStage.LayoutActivation => "layout activation",
        ActionExecutionStage.UndoLearning => "undo learning",
        ActionExecutionStage.Feedback => "operation feedback",
        _ => "operation setup"
    };

    private static string WithDiagnosticCode(string message, string diagnosticCode) =>
        $"{message} [{diagnosticCode}]";

    private (string? newText, string? targetLayoutCode) ProcessText(string text, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.FixLayout:
            case HotkeyAction.FixLayoutSelected:
                string currentLayout = _activeWindowProvider.GetActiveLayoutCode();
                var dictionaryAttempted = _dictionaryAnalyzer != null && IsDictionaryWord(text);
                if (_dictionaryAnalyzer != null &&
                    dictionaryAttempted &&
                    _dictionaryAnalyzer.TryGetCorrection(
                        text,
                        currentLayout,
                        out var dictionarySuggestion))
                {
                    LogLayoutAnalysis(
                        action,
                        text,
                        currentLayout,
                        dictionaryAttempted,
                        dictionaryAccepted: true,
                        sourceLayout: currentLayout,
                        targetLayout: dictionarySuggestion.TargetLayoutCode,
                        changed: !string.Equals(
                            dictionarySuggestion.Replacement,
                            text,
                            StringComparison.Ordinal),
                        reason: "dictionary-candidate");
                    return (
                        dictionarySuggestion.Replacement,
                        dictionarySuggestion.TargetLayoutCode);
                }

                var activeLayouts = _keyboardLayoutManager.GetLayoutOrder(currentLayout);
                var (converted, sourceLayout, targetLayout) = _layoutConverter.AutoConvert(
                    text,
                    activeLayouts,
                    currentLayout);
                LogLayoutAnalysis(
                    action,
                    text,
                    currentLayout,
                    dictionaryAttempted,
                    dictionaryAccepted: false,
                    sourceLayout: sourceLayout?.EffectiveIdentifier,
                    targetLayout: targetLayout?.EffectiveIdentifier,
                    changed: converted != null && !string.Equals(converted, text, StringComparison.Ordinal),
                    reason: converted == null ? "no-visible-candidate" : "layout-fallback");
                return (converted, targetLayout?.EffectiveIdentifier);
            case HotkeyAction.ChangeCase:
                return (_textTransformer.ChangeCase(text), null);
            case HotkeyAction.Transliterate:
                return (_transliterationService.Transliterate(text), null);
            case HotkeyAction.NumberToText:
                if (long.TryParse(text, out long num))
                {
                    return (_numberToTextConverter.Convert(num, "ru-RU"), null);
                }
                return (text, null);
            case HotkeyAction.ConvertToEnglish:
                return (ConvertToLayoutCode(text, "en-US"), null);
            case HotkeyAction.ConvertToRussian:
                return (ConvertToLayoutCode(text, "ru-RU"), null);
            case HotkeyAction.ConvertToUkrainian:
                return (ConvertToLayoutCode(text, "uk-UA"), null);
            default:
                return (null, null);
        }
    }

    private string? ConvertToLayoutCode(string text, string code)
    {
        var currentLayout = _activeWindowProvider.GetActiveLayoutCode();
        var activeLayouts = _keyboardLayoutManager.GetLayoutOrder(currentLayout);
        var target = activeLayouts.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (target == null) return text;
        
        var (_, source, _) = _layoutConverter.AutoConvert(text, activeLayouts, currentLayout);
        if (source == null || source.Code == target.Code) return text;
        
        return _layoutConverter.ConvertTo(text, target, source);
    }

    private static bool IsDictionaryWord(string text) =>
        text is { Length: >= 2 and <= 128 } &&
        text.Any(char.IsLetter) &&
        text.All(character =>
            char.IsLetter(character) || character is '\'' or '’' or '-');

    private void LogLayoutAnalysis(
        HotkeyAction action,
        string text,
        string? currentLayout,
        bool dictionaryAttempted,
        bool dictionaryAccepted,
        string? sourceLayout,
        string? targetLayout,
        bool changed,
        string reason)
    {
        _logger.LogInfo(
            $"SupportDiagnostic: Phase=layout-analysis; " +
            $"Outcome={(changed ? "accepted" : "rejected")}; Reason={reason}; " +
            $"Action={action}; Script={DescribeScript(text)}; LetterCount={text.Count(char.IsLetter)}; " +
            $"CurrentLayout={DescribeLayout(currentLayout)}; " +
            $"DictionaryAttempted={dictionaryAttempted}; DictionaryAccepted={dictionaryAccepted}; " +
            $"SourceLayout={DescribeLayout(sourceLayout)}; TargetLayout={DescribeLayout(targetLayout)}.");
    }

    private static string DescribeLayout(string? identifierOrCode) =>
        string.IsNullOrWhiteSpace(identifierOrCode)
            ? "none"
            : KeyboardLayoutIdentity.GetCultureCode(identifierOrCode);

    private static string DescribeScript(string text)
    {
        var hasLatin = false;
        var hasCyrillic = false;
        var hasOther = false;
        foreach (var character in text.Where(char.IsLetter))
        {
            if (character is >= '\u0041' and <= '\u024F')
                hasLatin = true;
            else if (character is >= '\u0400' and <= '\u052F')
                hasCyrillic = true;
            else
                hasOther = true;
        }

        var scriptCount = (hasLatin ? 1 : 0) + (hasCyrillic ? 1 : 0) + (hasOther ? 1 : 0);
        if (scriptCount == 0) return "none";
        if (scriptCount > 1) return "mixed";
        if (hasLatin) return "latin";
        if (hasCyrillic) return "cyrillic";
        return "other";
    }

    public void Dispose()
    {
        if (_disposed) return;

        _keyboardHook.HotkeyPressed -= OnHotkeyPressed;
        _disposed = true;
        _bindingRefreshCancellation.Cancel();
        _shutdownCancellation.Cancel();
        _executionQueue.Writer.TryComplete();
        var queueProcessorStopped = false;
        try
        {
            queueProcessorStopped = _queueProcessor.Wait(TimeSpan.FromSeconds(4));
            if (!queueProcessorStopped)
                _logger.LogWarning("Hotkey action worker did not stop within the shutdown deadline.");
        }
        catch (AggregateException exception)
        {
            _logger.LogError("Hotkey action worker failed during shutdown", exception.Flatten());
        }
        try
        {
            _bindingRefreshTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        _bindingRefreshCancellation.Dispose();
        if (queueProcessorStopped)
            _shutdownCancellation.Dispose();
    }

    private sealed record ActionRequest(
        HotkeyAction Action,
        TaskCompletionSource? Completion,
        bool IsHotkey);

    private sealed class ActionExecutionProgress
    {
        private int _stage = (int)ActionExecutionStage.Starting;

        public ActionExecutionStage CurrentStage =>
            (ActionExecutionStage)Volatile.Read(ref _stage);

        public void Enter(ActionExecutionStage stage) =>
            Volatile.Write(ref _stage, (int)stage);
    }

    private enum ActionExecutionStage
    {
        Starting,
        PolicyCheck,
        TextCapture,
        TextProcessing,
        TextReplacement,
        SelectionCleanup,
        TranslationQueue,
        TranslatorOpening,
        LayoutActivation,
        UndoLearning,
        Feedback
    }

    private sealed record ShortcutBinding(HotkeyCombo Combo, HotkeyAction Action);
}
