using System.Net;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public class ModelDownloadServiceTests
{
    [Fact]
    public async Task Download_WritesAtomicallyAndReportsCompletion()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "model.gguf");
        var progress = new List<double>();

        try
        {
            await service.DownloadModelAsync(
                descriptor,
                destination,
                progress.Add);

            Assert.Equal(payload.Length, new FileInfo(destination).Length);
            AssertNoTemporaryDownloads(directory);
            Assert.Equal(1.0, progress[^1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_RejectsUnexpectedlySmallResponseWithoutCreatingFile()
    {
        var payload = new byte[100];
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, "model.gguf");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadModelAsync(
                descriptor,
                destination,
                _ => { }));

            Assert.False(File.Exists(destination));
            AssertNoTemporaryDownloads(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_TrustedModel_VerifiesSizeFormatAndSha256BeforeActivation()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);

        try
        {
            await service.DownloadModelAsync(descriptor, destination, _ => { });

            Assert.True(service.IsModelDownloaded(destination, descriptor));
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            AssertNoTemporaryDownloads(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_TrustedModel_HashMismatchLeavesExistingModelUntouched()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload) with { Sha256 = new string('0', 64) };
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);
        var existing = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(destination, existing);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.DownloadModelAsync(descriptor, destination, _ => { }));

            Assert.Equal(existing, await File.ReadAllBytesAsync(destination));
            AssertNoTemporaryDownloads(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_TrustedModel_RejectsNonGgufPayload()
    {
        var payload = new byte[1024 * 1024 + 1];
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.DownloadModelAsync(descriptor, destination, _ => { }));

            Assert.False(File.Exists(destination));
            AssertNoTemporaryDownloads(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_CancellationRemovesPartialFileAndPreservesExistingModel()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new SlowResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);
        var existing = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(destination, existing);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.DownloadModelAsync(descriptor, destination, _ => { }, cancellation.Token));

            Assert.Equal(existing, await File.ReadAllBytesAsync(destination));
            AssertNoTemporaryDownloads(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_ConcurrentRequests_DoNotShareOrDeleteTemporaryFiles()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload);
        using var handler = new BlockingFirstResponseHandler(payload);
        using var client = new HttpClient(handler);
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);
        Task? first = null;

        try
        {
            first = service.DownloadModelAsync(descriptor, destination, _ => { });
            await handler.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = service.DownloadModelAsync(descriptor, destination, _ => { });

            await second;
            handler.ReleaseFirstRead.TrySetResult();
            await first;

            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.GetFiles(directory, "model.gguf.download*"));
        }
        finally
        {
            handler.ReleaseFirstRead.TrySetResult();
            if (first is not null)
            {
                try { await first; } catch { }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_RemovesOnlyStaleOwnedTemporaryFiles()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);
        var staleOwned = destination + $".download-{Guid.NewGuid():N}.tmp";
        var recentOwned = destination + $".download-{Guid.NewGuid():N}.tmp";
        var unrelated = Path.Combine(directory, "other-model.gguf.download-old.tmp");
        await File.WriteAllTextAsync(staleOwned, "stale");
        await File.WriteAllTextAsync(recentOwned, "recent");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        File.SetLastWriteTimeUtc(staleOwned, DateTime.UtcNow - TimeSpan.FromDays(2));

        try
        {
            await service.DownloadModelAsync(descriptor, destination, _ => { });

            Assert.False(File.Exists(staleOwned));
            Assert.True(File.Exists(recentOwned));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Download_ProgressObserverFailureDoesNotTurnActivatedModelIntoFailure()
    {
        var payload = CreateGgufPayload();
        var descriptor = CreateDescriptor(payload);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new ModelDownloadService(client);
        var directory = CreateTemporaryDirectory();
        var destination = Path.Combine(directory, descriptor.FileName);

        try
        {
            await service.DownloadModelAsync(
                descriptor,
                destination,
                _ => throw new InvalidOperationException("disposed progress control"));

            Assert.True(service.IsModelDownloaded(destination, descriptor));
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            AssertNoTemporaryDownloads(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateGgufPayload()
    {
        var payload = new byte[1024 * 1024 + 1];
        "GGUF"u8.CopyTo(payload);
        payload[^1] = 42;
        return payload;
    }

    private static OfflineModelDescriptor CreateDescriptor(byte[] payload) => new(
        "test",
        "model.gguf",
        new Uri("https://models.invalid/model.gguf"),
        payload.LongLength,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant());

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LayoutFix.ModelDownloadTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertNoTemporaryDownloads(string directory) =>
        Assert.Empty(Directory.GetFiles(directory, "model.gguf.download*"));

    private sealed class StaticResponseHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
    }

    private sealed class SlowResponseHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new StreamContent(new SlowReadStream(payload));
            content.Headers.ContentLength = payload.LongLength;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class SlowReadStream(byte[] payload) : MemoryStream(payload, writable: false)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(40, cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class BlockingFirstResponseHandler(byte[] payload) : HttpMessageHandler
    {
        private int _requestCount;
        public TaskCompletionSource FirstReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestCount);
            HttpContent content = requestNumber == 1
                ? new StreamContent(new BlockingFirstReadStream(
                    payload,
                    FirstReadStarted,
                    ReleaseFirstRead))
                : new ByteArrayContent(payload);
            content.Headers.ContentLength = payload.LongLength;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    private sealed class BlockingFirstReadStream(
        byte[] payload,
        TaskCompletionSource firstReadStarted,
        TaskCompletionSource releaseFirstRead) : MemoryStream(payload, writable: false)
    {
        private int _blocked;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                firstReadStarted.TrySetResult();
                await releaseFirstRead.Task.WaitAsync(cancellationToken);
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
