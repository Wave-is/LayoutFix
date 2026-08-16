using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

public sealed class AdobeInlineRenameTextAdapter : IDirectTextAdapter, IDisposable
{
    internal const string AfterEffectsAdapterId = "after-effects-rename-v1";
    internal const string PremiereAdapterId = "premiere-rename-v1";
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

        if (!await RunBoundedAsync(
            () => ValidateReplacementCore(context, expectedText),
            timeoutResult: false,
            cancellationToken))
        {
            return false;
        }

        // Adobe commits these transient fields when UIA SetValue or Unicode packet
        // input is used. A normal paste preserves the edit transaction, so
        // use the clipboard only after the exact full-field selection was proven
        // and restore every user format unconditionally. ClipboardService provides
        // its own bounded worker; do not return on an outer timeout while restoration
        // is still in flight.
        using var snapshot = await _clipboard.CaptureAsync(cancellationToken);
        try
        {
            await _clipboard.SetTextAsync(replacement, cancellationToken);
            if (!await RunBoundedAsync(
                () => ValidateReplacementCore(context, expectedText),
                timeoutResult: false,
                cancellationToken))
            {
                return false;
            }

            await _input.SendKeyCombinationAsync(true, false, false, "v");
            await Task.Delay(75, cancellationToken);
            if (!_activeWindow.IsSameActiveWindow(context))
                return false;

            return await RunBoundedAsync(
                () => VerifyReplacementCore(context, expectedText, replacement),
                timeoutResult: false,
                cancellationToken);
        }
        finally
        {
            await _clipboard.RestoreAsync(snapshot, CancellationToken.None);
        }
    }

    private DirectTextCaptureResult CaptureCore(
        ActiveWindowContext context,
        string adapterId)
    {
        var element = AutomationElement.FocusedElement;
        if (!TryGetRenamePatterns(context, element, out var value, out var text))
            return DirectTextCaptureResult.Rejected(adapterId);

        var currentValue = value.Current.Value;
        var selections = text.GetSelection();
        if (string.IsNullOrEmpty(currentValue) ||
            selections.Length != 1 ||
            !string.Equals(
                selections[0].GetText(-1),
                currentValue,
                StringComparison.Ordinal))
        {
            return DirectTextCaptureResult.Rejected(adapterId);
        }

        return DirectTextCaptureResult.Captured(adapterId, currentValue);
    }

    private bool ValidateReplacementCore(
        ActiveWindowContext context,
        string expectedText)
    {
        var element = AutomationElement.FocusedElement;
        if (!TryGetRenamePatterns(
            context,
            element,
            out var value,
            out var text,
            expectedAccessibleName: expectedText))
            return false;

        var selections = text.GetSelection();
        if (!string.Equals(value.Current.Value, expectedText, StringComparison.Ordinal) ||
            selections.Length != 1 ||
            !string.Equals(
                selections[0].GetText(-1),
                expectedText,
                StringComparison.Ordinal) ||
            !_activeWindow.IsSameActiveWindow(context))
        {
            return false;
        }

        return true;
    }

    private bool VerifyReplacementCore(
        ActiveWindowContext context,
        string expectedText,
        string replacement)
    {
        var currentElement = AutomationElement.FocusedElement;
        return TryGetRenamePatterns(
                context,
                currentElement,
                out var currentValue,
                out _,
                expectedAccessibleName: expectedText) &&
            string.Equals(
                currentValue.Current.Value,
                replacement,
                StringComparison.Ordinal) &&
            _activeWindow.IsSameActiveWindow(context);
    }

    private static bool TryGetRenamePatterns(
        ActiveWindowContext context,
        AutomationElement? element,
        out ValuePattern value,
        out TextPattern text,
        string? expectedAccessibleName = null)
    {
        value = null!;
        text = null!;
        if (element == null)
            return false;

        var current = element.Current;
        if (current.ProcessId != context.ProcessId ||
            current.NativeWindowHandle != context.FocusedWindow ||
            current.ControlType != ControlType.Edit ||
            !string.Equals(current.ClassName, "Edit", StringComparison.Ordinal) ||
            !string.Equals(current.AutomationId, "1", StringComparison.Ordinal) ||
            !current.IsEnabled ||
            !current.IsKeyboardFocusable ||
            !current.HasKeyboardFocus ||
            current.IsPassword ||
            !element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) ||
            valueObject is not ValuePattern valuePattern ||
            valuePattern.Current.IsReadOnly ||
            !element.TryGetCurrentPattern(TextPattern.Pattern, out var textObject) ||
            textObject is not TextPattern textPattern)
        {
            return false;
        }

        var parent = TreeWalker.ControlViewWalker.GetParent(element);
        if (parent == null ||
            parent.Current.ProcessId != context.ProcessId ||
            parent.Current.ControlType != ControlType.Pane ||
            !string.Equals(
                parent.Current.Name,
                "OS_EditTextContainer",
                StringComparison.Ordinal) ||
            !string.Equals(
                parent.Current.ClassName,
                "DroverLord - Window Class",
                StringComparison.Ordinal) ||
            !IsSupportedAccessibleName(
                current.Name,
                valuePattern.Current.Value,
                expectedAccessibleName))
        {
            return false;
        }

        value = valuePattern;
        text = textPattern;
        return true;
    }

    internal static bool IsSupportedAccessibleName(
        string accessibleName,
        string currentValue,
        string? expectedAccessibleName = null) =>
        string.Equals(accessibleName, "UI_TextEdit", StringComparison.Ordinal) ||
        (!string.IsNullOrEmpty(currentValue) &&
            string.Equals(accessibleName, currentValue, StringComparison.Ordinal)) ||
        (!string.IsNullOrEmpty(expectedAccessibleName) &&
            string.Equals(
                accessibleName,
                expectedAccessibleName,
                StringComparison.Ordinal));

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
            string.Equals(mainClass, "Premiere Pro", StringComparison.Ordinal))
        {
            return PremiereAdapterId;
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
