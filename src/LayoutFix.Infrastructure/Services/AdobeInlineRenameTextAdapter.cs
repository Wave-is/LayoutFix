using System.Diagnostics;
using System.Text;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

public sealed class AdobeInlineRenameTextAdapter : IDirectTextAdapter, IDisposable
{
    internal enum ReplacementContract
    {
        Rejected,
        ClipboardPaste
    }

    internal const string AfterEffectsAdapterId = "after-effects-rename-paste-v2";
    internal const string PremiereAdapterId = "premiere-rename-paste-v2";
    internal const string PhotoshopSaveDialogAdapterId = "photoshop-save-dialog-v1";
    private const int MaximumNativeEditLength = 32_767;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(300);
    private readonly IActiveWindowProvider _activeWindow;
    private readonly IInputInjector _input;
    private readonly IClipboardService _clipboard;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private volatile bool _disposed;

    public AdobeInlineRenameTextAdapter(
        IActiveWindowProvider activeWindow,
        IInputInjector input,
        IClipboardService clipboard,
        ILoggerService logger)
    {
        _activeWindow = activeWindow;
        _input = input;
        _clipboard = clipboard;
        _logger = logger;
    }

    public Task<DirectTextCaptureResult> TryCaptureAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryGetAdapterId(context, out var adapterId))
            return Task.FromResult(DirectTextCaptureResult.NotApplicable);

        return RunBoundedAsync(
            () => CaptureCore(context, adapterId),
            DirectTextCaptureResult.Rejected(adapterId),
            cancellationToken);
    }

    public async Task<bool> TryReplaceAsync(
        string adapterId,
        ActiveWindowContext context,
        string expectedText,
        string replacement,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryGetAdapterId(context, out var currentAdapterId) ||
            !string.Equals(adapterId, currentAdapterId, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(expectedText) ||
            string.IsNullOrEmpty(replacement))
        {
            return false;
        }

        // Every supported Adobe field uses the safe user-paste contract. Native
        // messages may inspect the focused selection but never mutate it.
        var replacementContract = ResolveReplacementContract(adapterId);
        if (replacementContract != ReplacementContract.ClipboardPaste)
            return false;

        var expectedValue = await RunBoundedAsync(
            () => BuildExpectedPasteReplacementCore(
                context,
                expectedText,
                replacement),
            timeoutResult: null,
            cancellationToken);
        if (expectedValue == null)
        {
            _logger.LogInfo(
                $"AdobeAdapterDiagnostic: Adapter={adapterId}; Strategy=clipboard-paste-v2; " +
                $"Outcome=rejected; Phase=preflight; Reason=selection-changed; " +
                $"SourceLength={expectedText.Length}; ResultLength={replacement.Length}.");
            return false;
        }

        // Premiere and After Effects own an internal edit transaction around these
        // transient controls. A synchronous cross-process EM_REPLACESEL can report
        // success and still deadlock that transaction later (for example on Save).
        // Drive the proven field exactly as a user does instead. Preserve every
        // clipboard format, revalidate immediately before Ctrl+V, verify the exact
        // resulting value before restoring the clipboard, and always restore it.
        var timer = Stopwatch.StartNew();
        var phase = "revalidation";
        try
        {
            var result = await ExecuteClipboardPasteAsync(
                _input,
                _clipboard,
                replacement,
                async token =>
                {
                    var revalidatedValue = await RunBoundedAsync(
                        () => BuildExpectedPasteReplacementCore(
                            context,
                            expectedText,
                            replacement),
                        timeoutResult: null,
                        token);
                    return string.Equals(
                        revalidatedValue,
                        expectedValue,
                        StringComparison.Ordinal);
                },
                token =>
                {
                    phase = "verification";
                    return VerifyReplacementAfterPasteAsync(
                        context,
                        expectedValue,
                        token);
                },
                cancellationToken);
            _logger.LogInfo(
                $"AdobeAdapterDiagnostic: Adapter={adapterId}; Strategy=clipboard-paste-v2; " +
                $"Outcome={(result ? "accepted" : "rejected")}; Phase={phase}; " +
                $"SourceLength={expectedText.Length}; ResultLength={replacement.Length}; " +
                $"DurationMs={timer.ElapsedMilliseconds}.");
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                $"AdobeAdapterDiagnostic: Adapter={adapterId}; Strategy=clipboard-paste-v2; " +
                $"Outcome=failed; Phase={phase}; ExceptionType={exception.GetType().FullName}; " +
                $"HResult=0x{exception.HResult:X8}; DurationMs={timer.ElapsedMilliseconds}.");
            throw;
        }
    }

    internal static async Task<bool> ExecuteClipboardPasteAsync(
        IInputInjector input,
        IClipboardService clipboard,
        string replacement,
        Func<CancellationToken, Task<bool>> revalidate,
        Func<CancellationToken, Task<bool>> verify,
        CancellationToken cancellationToken)
    {
        using var snapshot = await clipboard.CaptureAsync(cancellationToken);
        try
        {
            await clipboard.SetTextAsync(replacement, cancellationToken);
            if (!await revalidate(cancellationToken))
                return false;

            // InputInjector neutralizes a still-held Shift/Ctrl/Alt/Win key in the
            // same atomic SendInput batch, so Shift+Scroll cannot turn this into
            // Ctrl+Shift+V and no two-second modifier wait is added.
            await input.SendKeyCombinationAsync(true, false, false, "v");
            return await verify(cancellationToken);
        }
        finally
        {
            await clipboard.RestoreAsync(snapshot, CancellationToken.None);
        }
    }

    private DirectTextCaptureResult CaptureCore(
        ActiveWindowContext context,
        string adapterId)
    {
        if (ResolveReplacementContract(adapterId) == ReplacementContract.ClipboardPaste)
        {
            if (!TryGetNativeEditSelection(
                    context,
                    out _,
                    out var nativeSelectedText))
            {
                return DirectTextCaptureResult.Rejected(adapterId);
            }

            return nativeSelectedText.Length > 0
                ? DirectTextCaptureResult.Captured(
                    adapterId,
                    nativeSelectedText,
                    allowTargetLayoutActivation: false)
                : DirectTextCaptureResult.SelectionMissing(adapterId);
        }

        return DirectTextCaptureResult.Rejected(adapterId);
    }

    private string? BuildExpectedPasteReplacementCore(
        ActiveWindowContext context,
        string expectedText,
        string replacement)
    {
        if (!TryGetNativeEditSelection(
                context,
                out var currentValue,
                out var selectedText) ||
            !string.Equals(selectedText, expectedText, StringComparison.Ordinal))
        {
            return null;
        }

        // EM_GETSEL is bounded and read-only. It supplies the exact offset when
        // the same substring occurs more than once; EM_REPLACESEL is never used
        // for Premiere or After Effects.
        if (!TryGetSelectionRange(
                context.FocusedWindow,
                currentValue,
                expectedText,
                out var start,
                out var end) ||
            !_activeWindow.IsSameActiveWindow(context))
        {
            return null;
        }

        return string.Concat(
            currentValue.AsSpan(0, start),
            replacement.AsSpan(),
            currentValue.AsSpan(end));
    }

    private async Task<bool> VerifyReplacementAfterPasteAsync(
        ActiveWindowContext context,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        const int verificationAttempts = 6;
        for (var attempt = 0; attempt < verificationAttempts; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(15, cancellationToken);

            if (await RunBoundedAsync(
                () => VerifyPasteReplacementCore(
                    context,
                    expectedValue),
                timeoutResult: false,
                cancellationToken))
            {
                return true;
            }

            if (!_activeWindow.IsSameActiveWindow(context))
                return false;
        }

        return false;
    }

    private bool VerifyPasteReplacementCore(
        ActiveWindowContext context,
        string expectedValue)
    {
        return TryGetNativeEditSelection(
                context,
                out var currentValue,
                out _) &&
            string.Equals(currentValue, expectedValue, StringComparison.Ordinal) &&
            _activeWindow.IsSameActiveWindow(context);
    }

    internal static bool TryGetSelectionRange(
        IntPtr editWindow,
        string currentValue,
        string expectedText,
        out int start,
        out int end)
    {
        start = end = 0;
        var sent = Win32.SendMessageTimeout(
            editWindow,
            Win32.EM_GETSEL,
            IntPtr.Zero,
            IntPtr.Zero,
            Win32.SMTO_BLOCK | Win32.SMTO_ABORTIFHUNG,
            100,
            out var result);
        if (sent == IntPtr.Zero)
            return false;

        var packedRange = unchecked((uint)result.ToInt64());
        start = (int)(packedRange & 0xFFFF);
        end = (int)(packedRange >> 16);
        return start >= 0 &&
            end > start &&
            end <= currentValue.Length &&
            string.Equals(currentValue[start..end], expectedText, StringComparison.Ordinal);
    }

    internal static bool TryReadNativeEditSelection(
        IntPtr editWindow,
        out string currentValue,
        out string selectedText)
    {
        currentValue = string.Empty;
        selectedText = string.Empty;
        if (editWindow == IntPtr.Zero ||
            Win32.SendMessageTimeout(
                editWindow,
                Win32.WM_GETTEXTLENGTH,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32.SMTO_BLOCK | Win32.SMTO_ABORTIFHUNG,
                100,
                out var lengthResult) == IntPtr.Zero)
        {
            return false;
        }

        var length = checked((int)lengthResult.ToInt64());
        if (length < 0 || length > MaximumNativeEditLength)
            return false;

        var buffer = new StringBuilder(length + 1);
        if (Win32.SendMessageTimeout(
                editWindow,
                Win32.WM_GETTEXT,
                new IntPtr(buffer.Capacity),
                buffer,
                Win32.SMTO_BLOCK | Win32.SMTO_ABORTIFHUNG,
                100,
                out _) == IntPtr.Zero)
        {
            return false;
        }

        currentValue = buffer.ToString();
        if (Win32.SendMessageTimeout(
                editWindow,
                Win32.EM_GETSEL,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32.SMTO_BLOCK | Win32.SMTO_ABORTIFHUNG,
                100,
                out var selectionResult) == IntPtr.Zero)
        {
            currentValue = string.Empty;
            return false;
        }

        var packedRange = unchecked((uint)selectionResult.ToInt64());
        var start = (int)(packedRange & 0xFFFF);
        var end = (int)(packedRange >> 16);
        if (start < 0 || end < start || end > currentValue.Length)
        {
            currentValue = string.Empty;
            return false;
        }

        selectedText = currentValue[start..end];
        return true;
    }

    private bool TryGetNativeEditSelection(
        ActiveWindowContext context,
        out string currentValue,
        out string selectedText)
    {
        currentValue = string.Empty;
        selectedText = string.Empty;
        if (!_activeWindow.IsSameActiveWindow(context) ||
            Win32.GetWindowThreadProcessId(context.FocusedWindow, out var processId) == 0 ||
            processId != context.ProcessId)
        {
            return false;
        }

        var focusedClass = new StringBuilder(32);
        if (Win32.GetClassName(
                context.FocusedWindow,
                focusedClass,
                focusedClass.Capacity) <= 0 ||
            !string.Equals(focusedClass.ToString(), "Edit", StringComparison.Ordinal))
        {
            return false;
        }

        var style = Win32.GetWindowLongPtr(context.FocusedWindow, Win32.GWL_STYLE).ToInt64();
        if ((style & (Win32.ES_PASSWORD | Win32.ES_READONLY)) != 0)
            return false;

        return TryReadNativeEditSelection(
            context.FocusedWindow,
            out currentValue,
            out selectedText);
    }

    internal static ReplacementContract ResolveReplacementContract(string adapterId)
    {
        if (string.Equals(adapterId, PremiereAdapterId, StringComparison.Ordinal) ||
            string.Equals(adapterId, AfterEffectsAdapterId, StringComparison.Ordinal) ||
            string.Equals(adapterId, PhotoshopSaveDialogAdapterId, StringComparison.Ordinal))
        {
            return ReplacementContract.ClipboardPaste;
        }

        return ReplacementContract.Rejected;
    }

    private bool TryGetAdapterId(
        ActiveWindowContext context,
        out string adapterId)
    {
        adapterId = string.Empty;
        if (!context.IsValid ||
            context.FocusedWindow == IntPtr.Zero ||
            !_activeWindow.IsSameActiveWindow(context))
        {
            return false;
        }

        var mainClass = new StringBuilder(256);
        var focusedClass = new StringBuilder(256);
        if (Win32.GetClassName(context.ForegroundWindow, mainClass, mainClass.Capacity) <= 0 ||
            Win32.GetClassName(context.FocusedWindow, focusedClass, focusedClass.Capacity) <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)context.ProcessId));
            adapterId = ResolveAdapterId(
                process.ProcessName,
                mainClass.ToString(),
                focusedClass.ToString()) ?? string.Empty;
            return adapterId.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    internal static string? ResolveAdapterId(
        string processName,
        string mainClass,
        string focusedClass)
    {
        if (!string.Equals(focusedClass, "Edit", StringComparison.Ordinal))
            return null;

        if (string.Equals(
                processName,
                "AfterFX",
                StringComparison.OrdinalIgnoreCase) &&
            mainClass.StartsWith("AE_CApplication_", StringComparison.Ordinal))
        {
            return AfterEffectsAdapterId;
        }

        if (string.Equals(
                processName,
                "Adobe Premiere Pro",
                StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(mainClass, "Premiere Pro", StringComparison.Ordinal) ||
             string.Equals(mainClass, "DroverLord - Window Class", StringComparison.Ordinal) ||
             string.Equals(mainClass, "#32770", StringComparison.Ordinal)))
        {
            return PremiereAdapterId;
        }

        if (string.Equals(
                processName,
                "Photoshop",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(mainClass, "#32770", StringComparison.Ordinal))
        {
            return PhotoshopSaveDialogAdapterId;
        }

        return null;
    }

    private async Task<T> RunBoundedAsync<T>(
        Func<T> probe,
        T timeoutResult,
        CancellationToken cancellationToken) =>
        await RunBoundedAsync(
            () => Task.FromResult(probe()),
            timeoutResult,
            cancellationToken);

    private async Task<T> RunBoundedAsync<T>(
        Func<Task<T>> probe,
        T timeoutResult,
        CancellationToken cancellationToken)
    {
        if (!await _probeGate.WaitAsync(0, cancellationToken))
            return timeoutResult;

        var releaseGate = true;
        try
        {
            var probeTask = Task.Run(probe, CancellationToken.None);
            var completed = await Task.WhenAny(
                probeTask,
                Task.Delay(ProbeTimeout, cancellationToken));
            if (completed != probeTask)
            {
                releaseGate = false;
                _ = probeTask.ContinueWith(
                    _ => _probeGate.Release(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogWarning("Adobe inline rename text probe timed out; operation rejected.");
                return timeoutResult;
            }

            return await probeTask;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError("Adobe inline rename text probe failed", exception);
            return timeoutResult;
        }
        finally
        {
            if (releaseGate)
                _probeGate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
