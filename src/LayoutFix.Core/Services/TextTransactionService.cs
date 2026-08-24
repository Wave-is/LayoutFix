using System.Diagnostics;
using System.Globalization;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public sealed class TextTransactionService : ITextTransactionService
{
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(750);
    private const int CopyAttempts = 3;
    // SendInput is materially more reliable than Ctrl+V for ordinary selections:
    // several target UI threads acknowledge the injected paste chord before they
    // consume the clipboard. Keep the clipboard path for genuinely large payloads
    // where thousands of Unicode key events would be excessive.
    private const int ClipboardPasteThreshold = 2_048;
    private const int RollbackModifierReleaseTimeoutMilliseconds = 250;
    private readonly IInputInjector _input;
    private readonly IClipboardService _clipboard;
    private readonly IActiveWindowProvider _activeWindow;
    private readonly ILoggerService _logger;
    private readonly ITextTargetGuard? _targetGuard;
    private readonly IKeyboardHook? _keyboardHook;
    private readonly IMouseHook? _mouseHook;
    private readonly IDirectTextAdapter? _directTextAdapter;
    private readonly ISettingsService? _settingsService;
    private long _nextDiagnosticCaptureId;

    public TextTransactionService(
        IInputInjector input,
        IClipboardService clipboard,
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        ITextTargetGuard? targetGuard = null,
        IKeyboardHook? keyboardHook = null,
        IMouseHook? mouseHook = null,
        IDirectTextAdapter? directTextAdapter = null,
        ISettingsService? settingsService = null)
    {
        _input = input;
        _clipboard = clipboard;
        _activeWindow = activeWindow;
        _logger = logger;
        _targetGuard = targetGuard;
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _directTextAdapter = directTextAdapter;
        _settingsService = settingsService;
    }

    public async Task<TextSelection?> CaptureAsync(
        bool allowPreviousWordFallback,
        CancellationToken cancellationToken = default)
    {
        var captureId = Interlocked.Increment(ref _nextDiagnosticCaptureId);
        var window = _activeWindow.CaptureActiveWindow();
        if (!window.IsValid)
        {
            LogSupportDiagnostic(captureId, "capture", "rejected", "active-window-unavailable");
            return null;
        }
        LogCaptureTarget(captureId, window, allowPreviousWordFallback);
        var captureInputGeneration = CaptureInputGeneration();
        if (_targetGuard != null &&
            !await _targetGuard.CanModifyAsync(window, cancellationToken))
        {
            _logger.LogWarning("Text capture rejected because the focused control is secure or cannot be verified.");
            LogSupportDiagnostic(captureId, "capture", "rejected", "target-safety-check-failed");
            return null;
        }

        var fallbackSelectionMade = false;
        try
        {
            await _input.WaitForModifiersReleaseAsync();
            if (InputChanged(captureInputGeneration))
            {
                LogSupportDiagnostic(captureId, "capture", "rejected", "input-changed-after-modifier-release");
                return null;
            }
            if (!_activeWindow.IsSameActiveWindow(window))
            {
                LogSupportDiagnostic(captureId, "capture", "rejected", "focus-changed-after-modifier-release");
                return null;
            }

            if (_directTextAdapter != null)
            {
                var directCapture = await _directTextAdapter.TryCaptureAsync(
                    window,
                    cancellationToken);
                if (directCapture.IsApplicable)
                {
                    if ((string.IsNullOrEmpty(directCapture.Text) ||
                         string.IsNullOrEmpty(directCapture.AdapterId)) &&
                        allowPreviousWordFallback &&
                        !InputChanged(captureInputGeneration) &&
                        _activeWindow.IsSameActiveWindow(window))
                    {
                        await _input.SelectWordLeftAsync();
                        fallbackSelectionMade = true;
                        directCapture = await _directTextAdapter.TryCaptureAsync(
                            window,
                            cancellationToken);
                    }

                    if (string.IsNullOrEmpty(directCapture.Text) ||
                        string.IsNullOrEmpty(directCapture.AdapterId))
                    {
                        if (fallbackSelectionMade)
                        {
                            await CollapseFallbackSelectionAsync(
                                window,
                                captureInputGeneration,
                                cancellationToken);
                        }
                        LogSupportDiagnostic(captureId, "capture", "rejected", "direct-adapter-selection-unverified");
                        return null;
                    }
                    if (InputChanged(captureInputGeneration))
                    {
                        LogSupportDiagnostic(captureId, "capture", "rejected", "input-changed-after-direct-capture");
                        return null;
                    }
                    if (!_activeWindow.IsSameActiveWindow(window))
                    {
                        LogSupportDiagnostic(captureId, "capture", "rejected", "focus-changed-after-direct-capture");
                        return null;
                    }

                    LogSupportDiagnostic(
                        captureId,
                        "capture",
                        "accepted",
                        "direct-adapter",
                        $"Adapter={DiagnosticValue(directCapture.AdapterId)}; Length={directCapture.Text.Length}");
                    return new TextSelection(
                        directCapture.Text,
                        window,
                        WasSelectedByFallback: fallbackSelectionMade,
                        captureInputGeneration.Keyboard,
                        captureInputGeneration.Mouse,
                        directCapture.AdapterId,
                        captureId);
                }
            }

            using var snapshot = await _clipboard.CaptureAsync(cancellationToken);
            _logger.LogInfo("Clipboard snapshot captured for text capture.");
            string? selectedText;
            try
            {
                selectedText = await CopySelectionAsync(window, cancellationToken);
                _logger.LogInfo("Selection copy completed for text capture.");
                if (string.IsNullOrEmpty(selectedText) && allowPreviousWordFallback)
                {
                    if (InputChanged(captureInputGeneration) ||
                        !_activeWindow.IsSameActiveWindow(window))
                    {
                        LogSupportDiagnostic(captureId, "capture", "rejected", "context-changed-before-fallback-selection");
                        return null;
                    }

                    await _input.SelectWordLeftAsync();
                    fallbackSelectionMade = true;
                    selectedText = await CopySelectionAsync(window, cancellationToken);
                }
            }
            finally
            {
                // Restoration is deliberately uncancellable: once Ctrl+C has changed
                // the clipboard, returning it to the user takes precedence.
                await _clipboard.RestoreAsync(snapshot, CancellationToken.None);
                _logger.LogInfo("Clipboard snapshot restored after text capture.");
            }

            if (string.IsNullOrEmpty(selectedText))
            {
                if (fallbackSelectionMade)
                {
                    await CollapseFallbackSelectionAsync(
                        window,
                        captureInputGeneration,
                        cancellationToken);
                }
                LogSupportDiagnostic(captureId, "capture", "rejected", "clipboard-copy-returned-no-text");
                return null;
            }

            if (InputChanged(captureInputGeneration))
            {
                LogSupportDiagnostic(captureId, "capture", "rejected", "input-changed-after-copy");
                return null;
            }
            if (!_activeWindow.IsSameActiveWindow(window))
            {
                LogSupportDiagnostic(captureId, "capture", "rejected", "focus-changed-after-copy");
                return null;
            }

            LogSupportDiagnostic(
                captureId,
                "capture",
                "accepted",
                fallbackSelectionMade ? "previous-word-fallback" : "explicit-selection",
                $"Length={selectedText.Length}");
            return new TextSelection(
                selectedText,
                window,
                fallbackSelectionMade,
                captureInputGeneration.Keyboard,
                captureInputGeneration.Mouse,
                DiagnosticCaptureId: captureId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (fallbackSelectionMade)
            {
                await CollapseFallbackSelectionAsync(
                    window,
                    captureInputGeneration,
                    CancellationToken.None);
            }
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Text capture transaction failed", ex);
            LogSupportDiagnostic(
                captureId,
                "capture",
                "failed",
                "transaction-exception",
                $"ExceptionType={DiagnosticValue(ex.GetType().FullName)}; HResult=0x{ex.HResult:X8}");
            if (fallbackSelectionMade)
            {
                await CollapseFallbackSelectionAsync(
                    window,
                    captureInputGeneration,
                    CancellationToken.None);
            }
            return null;
        }
    }

    public async Task<bool> ReplaceAsync(
        TextSelection selection,
        string replacement,
        CancellationToken cancellationToken = default)
    {
        var selectionInputGeneration = GetInputGeneration(selection);
        var captureId = selection.DiagnosticCaptureId ??
            Interlocked.Increment(ref _nextDiagnosticCaptureId);
        if (string.IsNullOrEmpty(replacement))
        {
            LogSupportDiagnostic(captureId, "replacement", "rejected", "replacement-empty");
            return false;
        }
        if (InputChanged(selectionInputGeneration))
        {
            LogSupportDiagnostic(captureId, "replacement", "rejected", "input-changed-before-replacement");
            return false;
        }
        if (!_activeWindow.IsSameActiveWindow(selection.Window))
        {
            LogSupportDiagnostic(captureId, "replacement", "rejected", "focus-changed-before-replacement");
            return false;
        }
        if (_targetGuard != null &&
            !await _targetGuard.CanModifyAsync(selection.Window, cancellationToken))
        {
            LogSupportDiagnostic(captureId, "replacement", "rejected", "target-safety-recheck-failed");
            return false;
        }
        if (InputChanged(selectionInputGeneration))
        {
            LogSupportDiagnostic(captureId, "replacement", "rejected", "input-changed-after-safety-recheck");
            return false;
        }
        if (!_activeWindow.IsSameActiveWindow(selection.Window))
        {
            LogSupportDiagnostic(captureId, "replacement", "rejected", "focus-changed-after-safety-recheck");
            return false;
        }

        if (selection.DirectAdapterId != null)
        {
            if (_directTextAdapter == null)
            {
                LogSupportDiagnostic(captureId, "replacement", "failed", "direct-adapter-unavailable");
                return false;
            }
            try
            {
                var directResult = await _directTextAdapter.TryReplaceAsync(
                    selection.DirectAdapterId,
                    selection.Window,
                    selection.Text,
                    replacement,
                    cancellationToken);
                LogSupportDiagnostic(
                    captureId,
                    "replacement",
                    directResult ? "accepted" : "rejected",
                    directResult ? "direct-adapter" : "direct-adapter-refused",
                    $"Adapter={DiagnosticValue(selection.DirectAdapterId)}; SourceLength={selection.Text.Length}; ResultLength={replacement.Length}");
                return directResult;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Direct text replacement transaction failed", ex);
                LogSupportDiagnostic(
                    captureId,
                    "replacement",
                    "failed",
                    "direct-adapter-exception",
                    $"ExceptionType={DiagnosticValue(ex.GetType().FullName)}; HResult=0x{ex.HResult:X8}");
                return false;
            }
        }

        var replacementInputGeneration = default(InputGenerationSnapshot);
        try
        {
            // Long translations can finish after the user has moved the caret. Verify
            // that the exact original selection still exists before replacing it.
            using var snapshot = await _clipboard.CaptureAsync(cancellationToken);
            _logger.LogInfo("Clipboard snapshot captured for replacement verification.");
            string? currentSelection;
            try
            {
                currentSelection = await CopySelectionAsync(selection.Window, cancellationToken);
                _logger.LogInfo("Selection copy completed for replacement verification.");
            }
            finally
            {
                await _clipboard.RestoreAsync(snapshot, CancellationToken.None);
                _logger.LogInfo("Clipboard snapshot restored after replacement verification.");
            }

            if (!string.Equals(currentSelection, selection.Text, StringComparison.Ordinal) ||
                InputChanged(selectionInputGeneration) ||
                !_activeWindow.IsSameActiveWindow(selection.Window))
            {
                var reason = !string.Equals(currentSelection, selection.Text, StringComparison.Ordinal)
                    ? "selection-content-changed"
                    : InputChanged(selectionInputGeneration)
                        ? "input-changed-during-verification"
                        : "focus-changed-during-verification";
                LogSupportDiagnostic(captureId, "replacement", "rejected", reason);
                return false;
            }

            replacementInputGeneration = CaptureInputGeneration();
            var replacementReason = "input-injected";
            if (replacement.Length >= ClipboardPasteThreshold)
            {
                await PasteTextAsync(replacement, cancellationToken);
                replacementReason = "clipboard-paste";
            }
            else
            {
                await _input.SendTextAsync(replacement);
            }
            LogSupportDiagnostic(
                captureId,
                "replacement",
                "accepted",
                replacementReason,
                $"SourceLength={selection.Text.Length}; ResultLength={replacement.Length}");
            return true;
        }
        catch (InputInjectionException ex)
            when (ex.Operation == InputInjectionOperation.Text && ex.AffectedUnitCount > 0)
        {
            _logger.LogError("Text replacement was only partially injected", ex);
            LogSupportDiagnostic(
                captureId,
                "replacement",
                "failed",
                "partial-input-injection",
                $"RequestedUnits={ex.RequestedUnitCount}; AffectedUnits={ex.AffectedUnitCount}");
            await TryRollbackPartialReplacementAsync(
                selection,
                replacement,
                ex.AffectedUnitCount,
                replacementInputGeneration);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Text replacement transaction failed", ex);
            LogSupportDiagnostic(
                captureId,
                "replacement",
                "failed",
                "transaction-exception",
                $"ExceptionType={DiagnosticValue(ex.GetType().FullName)}; HResult=0x{ex.HResult:X8}");
            return false;
        }
    }

    public Task CancelFallbackSelectionAsync(
        TextSelection selection,
        CancellationToken cancellationToken = default) =>
        selection.WasSelectedByFallback && !InputChanged(GetInputGeneration(selection))
            ? CollapseFallbackSelectionAsync(
                selection.Window,
                GetInputGeneration(selection),
                cancellationToken)
            : Task.CompletedTask;

    private async Task<string?> CopySelectionAsync(
        ActiveWindowContext window,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= CopyAttempts; attempt++)
        {
            if (!_activeWindow.IsSameActiveWindow(window))
                return null;

            var sequence = _clipboard.GetSequenceNumber();
            await _input.SendKeyCombinationAsync(true, false, false, "c");

            var deadline = DateTime.UtcNow + CopyTimeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_activeWindow.IsSameActiveWindow(window))
                    return null;

                if (_clipboard.GetSequenceNumber() != sequence)
                {
                    await Task.Delay(25, cancellationToken);
                    var text = await _clipboard.ReadTextAsync(cancellationToken);
                    return text;
                }

                await Task.Delay(15, cancellationToken);
            }

            if (attempt < CopyAttempts)
                _logger.LogWarning($"Clipboard copy attempt {attempt} did not complete; retrying.");
        }

        _logger.LogWarning("Clipboard copy did not complete after bounded retries.");
        return null;
    }

    private async Task PasteTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        using var snapshot = await _clipboard.CaptureAsync(cancellationToken);
        _logger.LogInfo("Clipboard snapshot captured for long text replacement.");
        try
        {
            await _clipboard.SetTextAsync(text, cancellationToken);
            await _input.SendKeyCombinationAsync(true, false, false, "v");
            // Ctrl+V is queued to the target UI thread. Give it one bounded
            // processing turn before restoring every original clipboard format.
            await Task.Delay(100, cancellationToken);
        }
        finally
        {
            await _clipboard.RestoreAsync(snapshot, CancellationToken.None);
            _logger.LogInfo("Clipboard snapshot restored after long text replacement.");
        }
    }

    private async Task CollapseFallbackSelectionAsync(
        ActiveWindowContext window,
        InputGenerationSnapshot expectedInputGeneration,
        CancellationToken cancellationToken)
    {
        if (!InputChanged(expectedInputGeneration) &&
            _activeWindow.IsSameActiveWindow(window))
            await _input.SendKeyCombinationAsync(false, false, false, "right");
    }

    private async Task TryRollbackPartialReplacementAsync(
        TextSelection selection,
        string replacement,
        int affectedUtf16Length,
        InputGenerationSnapshot expectedInputGeneration)
    {
        try
        {
            if (InputChanged(expectedInputGeneration) ||
                !_activeWindow.IsSameActiveWindow(selection.Window))
            {
                _logger.LogWarning(
                    "Partial text replacement rollback skipped because input or focus changed.");
                return;
            }

            try
            {
                await _input.WaitForModifiersReleaseAsync(
                    RollbackModifierReleaseTimeoutMilliseconds);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "Partial text replacement rollback skipped because modifier keys remained pressed.");
                return;
            }

            if (_targetGuard != null &&
                !await _targetGuard.CanModifyAsync(selection.Window, CancellationToken.None))
            {
                _logger.LogWarning(
                    "Partial text replacement rollback skipped because the target is no longer safe.");
                return;
            }

            if (InputChanged(expectedInputGeneration) ||
                !_activeWindow.IsSameActiveWindow(selection.Window))
            {
                _logger.LogWarning(
                    "Partial text replacement rollback skipped because input or focus changed during validation.");
                return;
            }

            var boundedLength = Math.Clamp(affectedUtf16Length, 0, replacement.Length);
            var affectedTextElementCount = StringInfo.ParseCombiningCharacters(
                replacement[..boundedLength]).Length;
            if (affectedTextElementCount > 0)
                await _input.SendBackspacesAsync(affectedTextElementCount);
            await _input.SendTextAsync(selection.Text);
        }
        catch (Exception rollbackException)
        {
            _logger.LogError("Partial text replacement rollback failed", rollbackException);
        }
    }

    private InputGenerationSnapshot CaptureInputGeneration() => new(
        _keyboardHook?.InputGeneration,
        _mouseHook?.InputGeneration);

    private static InputGenerationSnapshot GetInputGeneration(TextSelection selection) => new(
        selection.KeyboardInputGeneration,
        selection.MouseInputGeneration);

    private bool InputChanged(InputGenerationSnapshot expectedGeneration) =>
        (expectedGeneration.Keyboard.HasValue &&
         _keyboardHook!.InputGeneration != expectedGeneration.Keyboard.Value) ||
        (expectedGeneration.Mouse.HasValue &&
         _mouseHook!.InputGeneration != expectedGeneration.Mouse.Value);

    private void LogCaptureTarget(
        long captureId,
        ActiveWindowContext window,
        bool allowPreviousWordFallback)
    {
        if (_settingsService?.Current.LoggingEnabled != true)
            return;

        var processName = "unavailable";
        var processVersion = "unavailable";
        try
        {
            using var process = Process.GetProcessById(checked((int)window.ProcessId));
            processName = DiagnosticValue(process.ProcessName);
            try
            {
                processVersion = DiagnosticValue(process.MainModule?.FileVersionInfo.FileVersion);
            }
            catch
            {
                processVersion = "unavailable";
            }
        }
        catch
        {
            var activeProcess = _activeWindow.GetActiveProcessName();
            if (!string.IsNullOrWhiteSpace(activeProcess))
                processName = DiagnosticValue(activeProcess);
        }

        _logger.LogInfo(
            $"SupportDiagnostic: CaptureId={captureId}; Phase=capture; Outcome=started; " +
            $"TargetProcess={processName}; TargetVersion={processVersion}; " +
            $"PreviousWordFallback={allowPreviousWordFallback}.");
    }

    private void LogSupportDiagnostic(
        long captureId,
        string phase,
        string outcome,
        string reason,
        string? details = null)
    {
        if (_settingsService?.Current.LoggingEnabled != true)
            return;

        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $"; {details}";
        _logger.LogInfo(
            $"SupportDiagnostic: CaptureId={captureId}; Phase={phase}; " +
            $"Outcome={outcome}; Reason={reason}{suffix}.");
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

    private readonly record struct InputGenerationSnapshot(long? Keyboard, long? Mouse);
}
