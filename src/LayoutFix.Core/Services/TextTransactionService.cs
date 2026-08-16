using System.Globalization;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public sealed class TextTransactionService : ITextTransactionService
{
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(750);
    private const int CopyAttempts = 3;
    private const int RollbackModifierReleaseTimeoutMilliseconds = 250;
    private readonly IInputInjector _input;
    private readonly IClipboardService _clipboard;
    private readonly IActiveWindowProvider _activeWindow;
    private readonly ILoggerService _logger;
    private readonly ITextTargetGuard? _targetGuard;
    private readonly IKeyboardHook? _keyboardHook;
    private readonly IMouseHook? _mouseHook;
    private readonly IDirectTextAdapter? _directTextAdapter;

    public TextTransactionService(
        IInputInjector input,
        IClipboardService clipboard,
        IActiveWindowProvider activeWindow,
        ILoggerService logger,
        ITextTargetGuard? targetGuard = null,
        IKeyboardHook? keyboardHook = null,
        IMouseHook? mouseHook = null,
        IDirectTextAdapter? directTextAdapter = null)
    {
        _input = input;
        _clipboard = clipboard;
        _activeWindow = activeWindow;
        _logger = logger;
        _targetGuard = targetGuard;
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _directTextAdapter = directTextAdapter;
    }

    public async Task<TextSelection?> CaptureAsync(
        bool allowPreviousWordFallback,
        CancellationToken cancellationToken = default)
    {
        var window = _activeWindow.CaptureActiveWindow();
        if (!window.IsValid)
            return null;
        var captureInputGeneration = CaptureInputGeneration();
        if (_targetGuard != null &&
            !await _targetGuard.CanModifyAsync(window, cancellationToken))
        {
            _logger.LogWarning("Text capture rejected because the focused control is secure or cannot be verified.");
            return null;
        }

        var fallbackSelectionMade = false;
        try
        {
            await _input.WaitForModifiersReleaseAsync();
            if (InputChanged(captureInputGeneration) ||
                !_activeWindow.IsSameActiveWindow(window))
                return null;

            if (_directTextAdapter != null)
            {
                var directCapture = await _directTextAdapter.TryCaptureAsync(
                    window,
                    cancellationToken);
                if (directCapture.IsApplicable)
                {
                    if (string.IsNullOrEmpty(directCapture.Text) ||
                        string.IsNullOrEmpty(directCapture.AdapterId) ||
                        InputChanged(captureInputGeneration) ||
                        !_activeWindow.IsSameActiveWindow(window))
                    {
                        return null;
                    }

                    return new TextSelection(
                        directCapture.Text,
                        window,
                        WasSelectedByFallback: false,
                        captureInputGeneration.Keyboard,
                        captureInputGeneration.Mouse,
                        directCapture.AdapterId);
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
                        return null;

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
                return null;
            }

            if (InputChanged(captureInputGeneration) ||
                !_activeWindow.IsSameActiveWindow(window))
                return null;

            return new TextSelection(
                selectedText,
                window,
                fallbackSelectionMade,
                captureInputGeneration.Keyboard,
                captureInputGeneration.Mouse);
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
        if (string.IsNullOrEmpty(replacement) ||
            InputChanged(selectionInputGeneration) ||
            !_activeWindow.IsSameActiveWindow(selection.Window))
            return false;
        if (_targetGuard != null &&
            !await _targetGuard.CanModifyAsync(selection.Window, cancellationToken))
            return false;
        if (InputChanged(selectionInputGeneration) ||
            !_activeWindow.IsSameActiveWindow(selection.Window))
            return false;

        if (selection.DirectAdapterId != null)
        {
            if (_directTextAdapter == null)
                return false;
            try
            {
                return await _directTextAdapter.TryReplaceAsync(
                    selection.DirectAdapterId,
                    selection.Window,
                    selection.Text,
                    replacement,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Direct text replacement transaction failed", ex);
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
                return false;
            }

            replacementInputGeneration = CaptureInputGeneration();
            await _input.SendTextAsync(replacement);
            return true;
        }
        catch (InputInjectionException ex)
            when (ex.Operation == InputInjectionOperation.Text && ex.AffectedUnitCount > 0)
        {
            _logger.LogError("Text replacement was only partially injected", ex);
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

    private readonly record struct InputGenerationSnapshot(long? Keyboard, long? Mouse);
}
