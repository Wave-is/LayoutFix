using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

public class ClipboardServiceTests
{
    [Fact]
    public void SafeFormats_AreAcceptedWithoutRenderingComplexClipboardData()
    {
        foreach (var format in new[]
                 {
                     DataFormats.UnicodeText,
                     DataFormats.Text,
                     DataFormats.Rtf,
                     DataFormats.Html,
                     DataFormats.FileDrop
                 })
        {
            Assert.True(ClipboardService.IsSafeFormat(format));
        }
    }

    [Fact]
    public void PlainTextClipboardMetadataStreams_AreAccepted()
    {
        foreach (var format in new[]
                 {
                     "CanIncludeInClipboardHistory",
                     "CanUploadToCloudClipboard",
                     "EnterpriseDataProtectionId",
                     "ExcludeClipboardContentFromMonitorProcessing",
                     "TVClipboard",
                     "Chromium internal source RFH token",
                     "Chromium internal source URL"
                 })
        {
            Assert.True(ClipboardService.IsSafeFormat(format));
        }
    }

    [Fact]
    public void ComplexFormats_AreClassifiedForGuardedValueCloning()
    {
        foreach (var format in new[]
                 {
                     DataFormats.Bitmap,
                     DataFormats.EnhancedMetafile,
                     "Adobe Photoshop Image",
                     "Custom OLE Object"
                 })
        {
            Assert.False(ClipboardService.IsSafeFormat(format));
            Assert.False(ClipboardService.IsIgnorableFormat(format));
        }
    }

    [Fact]
    public void DerivedOrUnavailableFormats_AreIgnored()
    {
        Assert.True(ClipboardService.IsIgnorableFormat("Locale"));
        Assert.True(ClipboardService.IsIgnorableFormat("FileName"));
        Assert.True(ClipboardService.IsIgnorableFormat("FileNameW"));
        Assert.True(ClipboardService.IsIgnorableFormat("DataObject"));
        Assert.True(ClipboardService.IsIgnorableFormat("Ole Private Data"));
        Assert.False(ClipboardService.IsIgnorableFormat("Private Customer Payload"));
    }

    [Fact]
    public void FormatDiagnostics_ExposeCountsButNeverRegisteredNames()
    {
        const string privateFormatName = "PRIVATE_CUSTOMER_CLIPBOARD_FORMAT";
        string[] formats = [DataFormats.UnicodeText, privateFormatName];

        var classification = ClipboardService.DescribeFormatsForDiagnostics(
            formats,
            canRestoreAsUnicodeText: false);
        var rejection = ClipboardService.DescribeUnsupportedFormatsForDiagnostics(
            [privateFormatName]);

        Assert.Contains("Count: 2", classification);
        Assert.Contains("PlainTextNative: False", classification);
        Assert.Contains("Count: 1", rejection);
        Assert.DoesNotContain(privateFormatName, classification);
        Assert.DoesNotContain(privateFormatName, rejection);
    }

    [Fact]
    public void TransientClipboardBusy_IsRetriedWithoutRealDelay()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var result = ClipboardService.RetryTransientClipboardOperation(
            () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new ExternalException("Clipboard busy", unchecked((int)0x800401D0));
                return "ready";
            },
            delays.Add);

        Assert.Equal("ready", result);
        Assert.Equal(3, attempts);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40)],
            delays);
    }

    [Fact]
    public void NonTransientClipboardFailure_IsNotRetried()
    {
        var attempts = 0;

        Assert.Throws<ExternalException>(() =>
            ClipboardService.RetryTransientClipboardOperation<bool>(
                () =>
                {
                    attempts++;
                    throw new ExternalException("Permanent failure", unchecked((int)0x80004005));
                },
                _ => { }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void PersistentClipboardBusy_UsesABoundedRetryBudget()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        Assert.Throws<ExternalException>(() =>
            ClipboardService.RetryTransientClipboardOperation<bool>(
                () =>
                {
                    attempts++;
                    throw new ExternalException("Clipboard busy", unchecked((int)0x800401D0));
                },
                delays.Add));

        Assert.Equal(10, attempts);
        Assert.Equal(9, delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(20), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(180), delays[^1]);
        Assert.Equal(TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(
            delays.Sum(delay => delay.TotalMilliseconds)));
    }

    [Fact]
    public async Task TimedOutStartedOperation_FailsFastUntilWorkerActuallyRecovers()
    {
        using var operationStarted = new ManualResetEventSlim();
        using var releaseOperation = new ManualResetEventSlim();
        using var service = new ClipboardService(
            new NullLogger(),
            TimeSpan.FromMilliseconds(250));
        var queuedOperationRan = false;

        try
        {
            var blocked = service.InvokeAsync(
                () =>
                {
                    operationStarted.Set();
                    releaseOperation.Wait();
                    return 1;
                },
                CancellationToken.None);
            Assert.True(operationStarted.Wait(TimeSpan.FromSeconds(2)));

            var abandoned = service.InvokeAsync(
                () =>
                {
                    queuedOperationRan = true;
                    return 2;
                },
                CancellationToken.None);

            await Assert.ThrowsAsync<TimeoutException>(() => blocked);
            var failFast = Assert.ThrowsAsync<TimeoutException>(() =>
                service.InvokeAsync(() => 3, CancellationToken.None));
            await failFast.WaitAsync(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAsync<TimeoutException>(() => abandoned);

            releaseOperation.Set();
            var recoveryDeadline = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    var result = await service.InvokeAsync(() => 4, CancellationToken.None);
                    Assert.Equal(4, result);
                    break;
                }
                catch (TimeoutException) when (recoveryDeadline.Elapsed < TimeSpan.FromSeconds(2))
                {
                    await Task.Delay(10);
                }
            }

            Assert.False(queuedOperationRan);
        }
        finally
        {
            releaseOperation.Set();
        }
    }

    [Fact]
    public async Task Dispose_AbandonsQueuedWorkWhileActiveNativeCallFinishes()
    {
        using var operationStarted = new ManualResetEventSlim();
        using var releaseOperation = new ManualResetEventSlim();
        var service = new ClipboardService(
            new NullLogger(),
            TimeSpan.FromSeconds(10));
        var queuedOperationRan = false;

        var active = service.InvokeAsync(
            () =>
            {
                operationStarted.Set();
                releaseOperation.Wait();
                return 1;
            },
            CancellationToken.None);
        Assert.True(operationStarted.Wait(TimeSpan.FromSeconds(2)));
        var queued = service.InvokeAsync(
            () =>
            {
                queuedOperationRan = true;
                return 2;
            },
            CancellationToken.None);

        var disposeTask = Task.Run(service.Dispose);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            queued.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            active.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(queuedOperationRan);

        releaseOperation.Set();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(queuedOperationRan);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            service.InvokeAsync(() => 3, CancellationToken.None));
    }

    private sealed class NullLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }
}
