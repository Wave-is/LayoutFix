using System.Collections.Concurrent;
using System.IO;
using System.Collections.Specialized;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

public sealed class ClipboardService : IClipboardService
{
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(3);
    private const int ClipboardRetryAttempts = 10;
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);
    private static readonly HashSet<string> SafeFormats = new(StringComparer.Ordinal)
    {
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.OemText,
        DataFormats.StringFormat,
        DataFormats.Rtf,
        DataFormats.Html,
        DataFormats.CommaSeparatedValue,
        DataFormats.FileDrop,

        // Windows and remote-desktop tools attach small stream metadata even
        // when the user copies plain text. These formats are cloned by value
        // below and must not turn an ordinary text clipboard into a false
        // "complex clipboard" rejection.
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
        "EnterpriseDataProtectionId",
        "ExcludeClipboardContentFromMonitorProcessing",
        "TVClipboard",

        // Chromium attaches two bounded stream values to ordinary copied text.
        // They are clipboard-local provenance metadata, not an OLE payload, and
        // CloneClipboardValue preserves them byte-for-byte without logging data.
        "Chromium internal source RFH token",
        "Chromium internal source URL"
    };
    private readonly BlockingCollection<Action> _operations = new();
    private readonly Thread _staThread;
    private readonly ILoggerService _logger;
    private readonly TimeSpan _operationTimeout;
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private int _workerUnavailable;
    private DataObject? _clipboardLeaseData;
    private List<IDisposable> _clipboardLeaseResources = [];
    private volatile bool _disposed;

    public ClipboardService(ILoggerService logger)
        : this(logger, DefaultOperationTimeout)
    {
    }

    internal ClipboardService(
        ILoggerService logger,
        TimeSpan operationTimeout)
    {
        _logger = logger;
        _operationTimeout = operationTimeout > TimeSpan.Zero
            ? operationTimeout
            : throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        _staThread = new Thread(RunWorker)
        {
            IsBackground = true,
            Name = "LayoutFix Clipboard STA"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    public Task<IClipboardSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<IClipboardSnapshot>(CaptureOnStaThread, cancellationToken);

    public Task RestoreAsync(IClipboardSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is not WindowsClipboardSnapshot windowsSnapshot)
            throw new ArgumentException("The clipboard snapshot was created by another provider.", nameof(snapshot));

        return InvokeAsync(
            () => RestoreOnStaThread(windowsSnapshot),
            cancellationToken);
    }

    public Task<string?> ReadTextAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(
            () => RetryTransientClipboardOperation(ReadUnicodeTextNative),
            cancellationToken);

    public Task SetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return InvokeAsync(
            () => RetryTransientClipboardOperation(() =>
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }),
            cancellationToken);
    }

    public uint GetSequenceNumber() => Win32.GetClipboardSequenceNumber();

    private IClipboardSnapshot CaptureOnStaThread()
        => RetryTransientClipboardOperation(CaptureOnStaThreadOnce);

    private IClipboardSnapshot CaptureOnStaThreadOnce()
    {
        var source = Clipboard.GetDataObject();
        if (source == null)
            return CaptureNativeTextOrEmptyFallback();

        var formats = source.GetFormats(autoConvert: false);
        if (formats.Length == 0)
            return CaptureNativeTextOrEmptyFallback();

        var unsupportedFormats = formats
            .Where(format => !IsSafeFormat(format) && !IsIgnorableFormat(format))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unsupportedFormats.Length > 0)
        {
            _logger.LogWarning(DescribeUnsupportedFormatsForDiagnostics(unsupportedFormats));
            throw new NotSupportedException(
                "The clipboard contains complex data that cannot be preserved safely.");
        }

        var copy = new DataObject();
        var ownedResources = new List<IDisposable>();
        var copiedFormatCount = 0;
        string? unicodeText = null;
        var canRestoreAsUnicodeText = formats.All(format =>
            IsPlainTextFormat(format) || IsIgnorableFormat(format));
        _logger.LogInfo(DescribeFormatsForDiagnostics(formats, canRestoreAsUnicodeText));
        try
        {
            foreach (var format in formats.Distinct(StringComparer.Ordinal))
            {
                if (IsIgnorableFormat(format))
                    continue;

                var isUnicodeText = string.Equals(
                    format,
                    DataFormats.UnicodeText,
                    StringComparison.Ordinal);
                var value = isUnicodeText
                    ? ReadUnicodeTextNative()
                    : source.GetData(format, autoConvert: false);
                if (isUnicodeText && value is not string { Length: > 0 })
                {
                    throw new ExternalException(
                        "Advertised Unicode clipboard text was temporarily unavailable.",
                        ClipboardBusyHResult);
                }
                if (value == null)
                {
                    // Some Windows clipboard owners advertise auxiliary formats such as
                    // "Locale" without making data available for them. There is nothing
                    // to restore for such a format, so it must not abort the transaction.
                    _logger.LogWarning(
                        "An advertised auxiliary clipboard format returned no data and was skipped.");
                    continue;
                }
                if (value is string { Length: 0 })
                {
                    // Clipboard owners sometimes advertise an ANSI companion format
                    // but return an empty string while UnicodeText contains the real
                    // selection. Re-emitting an empty SetText payload is invalid and
                    // must not abort preservation of the non-empty format.
                    _logger.LogWarning(
                        "An advertised auxiliary clipboard format returned empty text and was skipped.");
                    continue;
                }

                var clonedValue = CloneClipboardValue(format, value);
                if (clonedValue is IDisposable disposable)
                    ownedResources.Add(disposable);
                SetClonedData(copy, format, clonedValue);
                if (string.Equals(format, DataFormats.UnicodeText, StringComparison.Ordinal) &&
                    clonedValue is string text)
                {
                    unicodeText = text;
                }
                copiedFormatCount++;
            }

            return new WindowsClipboardSnapshot(
                copiedFormatCount == 0 ? null : copy,
                ownedResources,
                unicodeText,
                canRestoreAsUnicodeText && unicodeText != null);
        }
        catch
        {
            foreach (var resource in ownedResources)
                resource.Dispose();
            throw;
        }
    }

    internal static bool IsSafeFormat(string format) => SafeFormats.Contains(format);

    internal static string DescribeFormatsForDiagnostics(
        IReadOnlyCollection<string> formats,
        bool canRestoreAsUnicodeText)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return $"Clipboard snapshot formats classified. Count: {formats.Count}; " +
            $"PlainTextNative: {canRestoreAsUnicodeText}.";
    }

    internal static string DescribeUnsupportedFormatsForDiagnostics(
        IReadOnlyCollection<string> unsupportedFormats)
    {
        ArgumentNullException.ThrowIfNull(unsupportedFormats);
        return "Text transaction was cancelled because the clipboard contains " +
            $"complex formats. Count: {unsupportedFormats.Count}.";
    }

    internal static bool IsIgnorableFormat(string format) =>
        string.Equals(format, "Locale", StringComparison.Ordinal) ||
        // WinForms derives these legacy single-file aliases from FileDrop. Restoring
        // the canonical string[] lets Windows synthesize them again without cloning
        // redundant shell payloads.
        string.Equals(format, "FileName", StringComparison.Ordinal) ||
        string.Equals(format, "FileNameW", StringComparison.Ordinal) ||
        // OLE can leave only these bookkeeping formats after an otherwise empty
        // clipboard data object has been flushed and its owner exits. They do not
        // contain user-facing payload and must not make the next safe transaction
        // fail forever when Clipboard.GetDataObject() returns null.
        string.Equals(format, "DataObject", StringComparison.Ordinal) ||
        string.Equals(format, "Ole Private Data", StringComparison.Ordinal);

    private static bool IsPlainTextFormat(string format) =>
        string.Equals(format, DataFormats.UnicodeText, StringComparison.Ordinal) ||
        string.Equals(format, DataFormats.Text, StringComparison.Ordinal) ||
        string.Equals(format, DataFormats.OemText, StringComparison.Ordinal) ||
        string.Equals(format, DataFormats.StringFormat, StringComparison.Ordinal);

    private bool RestoreOnStaThread(WindowsClipboardSnapshot snapshot)
    {
        if (snapshot.Data == null)
        {
            _logger.LogInfo("Clipboard restore path: empty.");
            RetryTransientClipboardOperation(() =>
            {
                Clipboard.Clear();
                return true;
            });
        }
        else
        {
            if (snapshot.CanRestoreAsUnicodeText && snapshot.UnicodeText != null)
            {
                _logger.LogInfo("Clipboard restore path: native Unicode text.");
                RetryTransientClipboardOperation(() =>
                {
                    WriteUnicodeTextNative(snapshot.UnicodeText);
                    return true;
                });
            }
            else
            {
                _logger.LogInfo("Clipboard restore path: multi-format OLE.");
                try
                {
                    RetryTransientClipboardOperation(() =>
                    {
                        Clipboard.SetDataObject(snapshot.Data, true, 5, 50);
                        return true;
                    });
                }
                catch (ExternalException exception) when (
                    exception.HResult == ClipboardBusyHResult &&
                    snapshot.UnicodeText != null &&
                    WaitForRestoredText(snapshot.UnicodeText))
                {
                    // OLE can report CLIPBRD_E_CANT_OPEN after SetDataObject already
                    // transferred ownership. Exact native read-back is the authoritative
                    // success condition; without an exact match the exception propagates.
                    _logger.LogWarning(
                        "Clipboard ownership reported a transient error after exact text restore succeeded.");
                }
            }

            if (snapshot.UnicodeText != null && !WaitForRestoredText(snapshot.UnicodeText))
            {
                // SetDataObject already accepted ownership and the retained lease keeps
                // every backing resource alive. Some clipboard monitors delay external
                // read-back beyond this bounded probe; cancelling now would turn an
                // eventually valid restore into a visible missed hotkey.
                _logger.LogWarning(
                    "Clipboard restore ownership succeeded but read-back is still pending.");
            }
        }

        // Custom clipboard formats can be backed by managed streams even when
        // SetDataObject(copy: true) succeeds. OLE may continue reading those
        // streams after this method returns, so retain both the DataObject and
        // its cloned resources until a later restore replaces clipboard ownership.
        var previousResources = _clipboardLeaseResources;
        _clipboardLeaseData = snapshot.Data;
        _clipboardLeaseResources = snapshot.TransferOwnedResources();
        foreach (var resource in previousResources)
            resource.Dispose();

        return true;
    }

    private static bool WaitForRestoredText(string expectedText)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(750);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var restoredText = ReadUnicodeTextNative();
                if (string.Equals(restoredText, expectedText, StringComparison.Ordinal))
                    return true;
            }
            catch (ExternalException exception) when (exception.HResult == ClipboardBusyHResult)
            {
                // A clipboard monitor can briefly race the just-completed ownership
                // change. Keep the same owner/data object and wait; resetting it on
                // every read failure extends the race.
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private static string? ReadUnicodeTextNative()
    {
        if (!Win32.OpenClipboard(IntPtr.Zero))
        {
            throw new ExternalException(
                "The Windows clipboard is temporarily in use.",
                ClipboardBusyHResult);
        }

        try
        {
            if (!Win32.IsClipboardFormatAvailable(Win32.CF_UNICODETEXT))
                return null;

            var memory = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
            if (memory == IntPtr.Zero)
            {
                throw new ExternalException(
                    "Unicode clipboard data is temporarily unavailable.",
                    ClipboardBusyHResult);
            }

            var pointer = Win32.GlobalLock(memory);
            if (pointer == IntPtr.Zero)
            {
                throw new ExternalException(
                    "Unicode clipboard memory is temporarily unavailable.",
                    ClipboardBusyHResult);
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                Win32.GlobalUnlock(memory);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    private static void WriteUnicodeTextNative(string text)
    {
        var byteCount = checked((text.Length + 1) * sizeof(char));
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (UIntPtr)(uint)byteCount);
        if (memory == IntPtr.Zero)
            throw new OutOfMemoryException("Unable to allocate clipboard text memory.");

        var ownershipTransferred = false;
        try
        {
            var pointer = Win32.GlobalLock(memory);
            if (pointer == IntPtr.Zero)
                throw new ExternalException("Unable to lock clipboard text memory.");

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            }
            finally
            {
                Win32.GlobalUnlock(memory);
            }

            if (!Win32.OpenClipboard(IntPtr.Zero))
            {
                throw new ExternalException(
                    "The Windows clipboard is temporarily in use.",
                    ClipboardBusyHResult);
            }

            try
            {
                if (!Win32.EmptyClipboard() ||
                    Win32.SetClipboardData(Win32.CF_UNICODETEXT, memory) == IntPtr.Zero)
                {
                    throw new ExternalException(
                        "Unable to restore Unicode clipboard text.",
                        ClipboardBusyHResult);
                }

                ownershipTransferred = true;
            }
            finally
            {
                Win32.CloseClipboard();
            }

            // Clipboard history, remote-desktop and accessibility monitors can react
            // asynchronously to an ownership change. Require the native payload to
            // remain readable across a short stability window before reporting that
            // the user's text has been restored.
            Thread.Sleep(100);
            if (!string.Equals(ReadUnicodeTextNative(), text, StringComparison.Ordinal))
            {
                throw new ExternalException(
                    "Unicode clipboard text did not remain stable after restore.",
                    ClipboardBusyHResult);
            }
        }
        finally
        {
            if (!ownershipTransferred)
                Win32.GlobalFree(memory);
        }
    }

    internal static T RetryTransientClipboardOperation<T>(
        Func<T> operation,
        Action<TimeSpan>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        delay ??= Thread.Sleep;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (ExternalException exception) when (
                exception.HResult == ClipboardBusyHResult && attempt < ClipboardRetryAttempts)
            {
                delay(TimeSpan.FromMilliseconds(attempt * 20));
            }
        }
    }

    private static object CloneClipboardValue(string format, object value)
    {
        return value switch
        {
            string text => text,
            string[] paths => paths.ToArray(),
            byte[] bytes => bytes.ToArray(),
            MemoryStream memory => CloneStream(memory),
            Stream stream => CloneStream(stream),
            Image image => image.Clone(),
            StringCollection strings => CloneStringCollection(strings),
            ICloneable cloneable => cloneable.Clone()
                ?? throw new InvalidOperationException($"Clipboard format '{format}' could not be cloned."),
            _ when value.GetType().IsPrimitive || value is decimal or DateTime or Guid => value,
            _ => throw new NotSupportedException(
                $"Clipboard format '{format}' uses unsupported data type '{value.GetType().FullName}'.")
        };
    }

    private static WindowsClipboardSnapshot CaptureNativeTextOrEmptyFallback()
    {
        var unicodeText = ReadUnicodeTextNative();
        if (unicodeText != null)
        {
            var data = new DataObject();
            data.SetText(unicodeText, TextDataFormat.UnicodeText);
            return new WindowsClipboardSnapshot(data, [], unicodeText, canRestoreAsUnicodeText: true);
        }

        if (IsClipboardEmptyOrContainsOnlyIgnorableNativeFormats())
            return new WindowsClipboardSnapshot(null, []);

        throw new ExternalException(
            "The clipboard advertises data but its formats are temporarily unavailable.",
            ClipboardBusyHResult);
    }

    private static bool IsClipboardEmptyOrContainsOnlyIgnorableNativeFormats()
    {
        if (!Win32.OpenClipboard(IntPtr.Zero))
        {
            throw new ExternalException(
                "The Windows clipboard is temporarily in use.",
                ClipboardBusyHResult);
        }

        try
        {
            Marshal.SetLastPInvokeError(0);
            var formatCount = Win32.CountClipboardFormats();
            if (formatCount == 0)
                return Marshal.GetLastPInvokeError() == 0;
            if (formatCount < 0)
                return false;

            uint format = 0;
            for (var index = 0; index < formatCount; index++)
            {
                format = Win32.EnumClipboardFormats(format);
                if (format == 0)
                    return false;

                // Registered clipboard formats occupy 0xC000-0xFFFF. Standard
                // numeric formats can carry real payload and remain fail-closed.
                if (format < 0xC000)
                    return false;

                var formatName = new StringBuilder(128);
                if (Win32.GetClipboardFormatName(
                        format,
                        formatName,
                        formatName.Capacity) <= 0 ||
                    !IsIgnorableFormat(formatName.ToString()))
                {
                    return false;
                }
            }

            // If the clipboard changed during enumeration, do not classify the
            // incomplete view as empty.
            Marshal.SetLastPInvokeError(0);
            var nextFormat = Win32.EnumClipboardFormats(format);
            return nextFormat == 0 &&
                Marshal.GetLastPInvokeError() == 0 &&
                Win32.CountClipboardFormats() == formatCount;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    private static void SetClonedData(DataObject target, string format, object value)
    {
        if (value is string text)
        {
            if (string.Equals(format, DataFormats.UnicodeText, StringComparison.Ordinal))
            {
                target.SetText(text, TextDataFormat.UnicodeText);
                return;
            }
            if (string.Equals(format, DataFormats.Text, StringComparison.Ordinal))
            {
                target.SetText(text, TextDataFormat.Text);
                return;
            }
            if (string.Equals(format, DataFormats.Rtf, StringComparison.Ordinal))
            {
                target.SetText(text, TextDataFormat.Rtf);
                return;
            }
            if (string.Equals(format, DataFormats.Html, StringComparison.Ordinal))
            {
                target.SetText(text, TextDataFormat.Html);
                return;
            }
            if (string.Equals(format, DataFormats.CommaSeparatedValue, StringComparison.Ordinal))
            {
                target.SetText(text, TextDataFormat.CommaSeparatedValue);
                return;
            }
        }

        target.SetData(format, autoConvert: false, value);
    }

    private static MemoryStream CloneStream(Stream source)
    {
        var originalPosition = source.CanSeek ? source.Position : 0;
        if (source.CanSeek)
            source.Position = 0;

        var copy = new MemoryStream();
        source.CopyTo(copy);
        copy.Position = 0;

        if (source.CanSeek)
            source.Position = originalPosition;

        return copy;
    }

    private static StringCollection CloneStringCollection(StringCollection source)
    {
        var copy = new StringCollection();
        copy.AddRange(source.Cast<string>().ToArray());
        return copy;
    }

    private async Task InvokeAsync(Action operation, CancellationToken cancellationToken)
    {
        await InvokeAsync(() =>
        {
            operation();
            return true;
        }, cancellationToken);
    }

    internal async Task<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _workerUnavailable) != 0)
        {
            throw new TimeoutException(
                "The clipboard worker is still finishing a timed-out operation.");
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 0 = queued, 1 = started, 2 = abandoned before start,
        // 3 = caller stopped waiting after start, 4 = operation completed.
        // A timed-out queued restore must never run later and overwrite clipboard
        // data the user copied since. If an already-started OLE call stalls, reject
        // subsequent work until that exact call has actually returned.
        var operationState = 0;
        var operationStateGate = new object();
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownCancellation.Token);

        try
        {
            _operations.Add(() =>
            {
                lock (operationStateGate)
                {
                    if (operationState != 0)
                        return;
                    operationState = 1;
                }

                try
                {
                    var result = operation();
                    CompleteWorkerOperation();
                    completion.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    CompleteWorkerOperation();
                    completion.TrySetException(ex);
                }

                void CompleteWorkerOperation()
                {
                    var recovered = false;
                    lock (operationStateGate)
                    {
                        recovered = operationState == 3;
                        operationState = 4;
                        if (recovered)
                            Volatile.Write(ref _workerUnavailable, 0);
                    }

                    if (recovered)
                        _logger.LogInfo("Clipboard worker recovered after a timed-out operation completed.");
                }
            }, waitCancellation.Token);
        }
        catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ClipboardService));
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(ClipboardService));
        }

        try
        {
            return await completion.Task.WaitAsync(
                _operationTimeout,
                waitCancellation.Token);
        }
        catch
        {
            var circuitOpened = false;
            lock (operationStateGate)
            {
                if (operationState == 0)
                {
                    operationState = 2;
                }
                else if (operationState == 1)
                {
                    Volatile.Write(ref _workerUnavailable, 1);
                    operationState = 3;
                    circuitOpened = true;
                }
            }

            if (circuitOpened)
            {
                _logger.LogWarning(
                    "Clipboard worker is busy finishing a timed-out operation; subsequent operations will fail fast.");
            }
            if (_shutdownCancellation.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(ClipboardService));
            throw;
        }
    }

    private void RunWorker()
    {
        try
        {
            foreach (var operation in _operations.GetConsumingEnumerable())
                operation();
        }
        catch (Exception ex)
        {
            _logger.LogError("Clipboard worker stopped unexpectedly", ex);
        }
        finally
        {
            ReleaseClipboardLeaseResources();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdownCancellation.Cancel();
        _operations.CompleteAdding();
        if (Thread.CurrentThread != _staThread)
        {
            if (!_staThread.Join(TimeSpan.FromSeconds(3)))
            {
                _logger.LogWarning(
                    "Clipboard worker did not stop within the shutdown deadline; " +
                    "its owned resources will be released when the active native call returns.");
            }
        }
    }

    private void ReleaseClipboardLeaseResources()
    {
        foreach (var resource in _clipboardLeaseResources)
            resource.Dispose();
        _clipboardLeaseResources.Clear();
        _clipboardLeaseData = null;
    }

    private sealed class WindowsClipboardSnapshot(
        DataObject? data,
        List<IDisposable> ownedResources,
        string? unicodeText = null,
        bool canRestoreAsUnicodeText = false) : IClipboardSnapshot
    {
        private List<IDisposable> _ownedResources = ownedResources;
        public DataObject? Data { get; } = data;
        public string? UnicodeText { get; } = unicodeText;
        public bool CanRestoreAsUnicodeText { get; } = canRestoreAsUnicodeText;

        public List<IDisposable> TransferOwnedResources()
        {
            var transferred = _ownedResources;
            _ownedResources = [];
            return transferred;
        }

        public void Dispose()
        {
            foreach (var resource in _ownedResources)
                resource.Dispose();
            _ownedResources.Clear();
        }
    }
}
