using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;

namespace LayoutFix.Core.Services;

public class ModelDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public ModelDownloadService()
        : this(new HttpClient { Timeout = TimeSpan.FromHours(2) }, ownsHttpClient: true)
    {
    }

    public ModelDownloadService(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private ModelDownloadService(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public bool IsModelDownloaded(string path, OfflineModelDescriptor descriptor)
        => OfflineModelCatalog.IsInstalled(path, descriptor);

    public async Task DownloadModelAsync(
        OfflineModelDescriptor descriptor,
        string destinationPath,
        Action<double> progressCallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        await DownloadModelCoreAsync(
            descriptor,
            descriptor.DownloadUri,
            destinationPath,
            progressCallback,
            cancellationToken);
    }

    private async Task DownloadModelCoreAsync(
        OfflineModelDescriptor descriptor,
        Uri url,
        string destinationPath,
        Action<double> progressCallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progressCallback);
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Model downloads require an absolute HTTPS URL.", nameof(url));

        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is <= 1024 * 1024)
            throw new InvalidDataException("Model download response is unexpectedly small.");
        if (totalBytes.HasValue && totalBytes.Value != descriptor.FileSize)
            throw new InvalidDataException("Model download size does not match the trusted catalog.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var dir = Path.GetDirectoryName(fullDestinationPath)!;
        Directory.CreateDirectory(dir);

        EnsureSufficientDiskSpace(fullDestinationPath, descriptor.FileSize);
        TryDeleteStaleTemporaryFiles(fullDestinationPath);

        // Each request owns exactly one temp file. A shared ".download" path
        // lets a concurrent request delete or truncate another request's file.
        var temporaryPath = fullDestinationPath + $".download-{Guid.NewGuid():N}.tmp";

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var magic = new byte[4];
            var magicBytesRead = 0;
            await using (var fileStream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                DateTime lastUpdate = DateTime.UtcNow;

                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    hash.AppendData(buffer, 0, bytesRead);
                    if (magicBytesRead < magic.Length)
                    {
                        var copied = Math.Min(magic.Length - magicBytesRead, bytesRead);
                        buffer.AsSpan(0, copied).CopyTo(magic.AsSpan(magicBytesRead));
                        magicBytesRead += copied;
                    }
                    totalRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - lastUpdate).TotalMilliseconds > 100)
                        {
                            TryReportProgress(
                                progressCallback,
                                (double)totalRead / totalBytes.Value);
                            lastUpdate = now;
                        }
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                if (totalRead <= 1024 * 1024)
                    throw new InvalidDataException("Downloaded model file is unexpectedly small.");

                if (totalRead != descriptor.FileSize)
                    throw new InvalidDataException("Downloaded model size does not match the trusted catalog.");
                if (magicBytesRead != magic.Length || !magic.AsSpan().SequenceEqual("GGUF"u8))
                    throw new InvalidDataException("Downloaded file is not a GGUF model.");

                var actualHash = hash.GetHashAndReset();
                var expectedHash = Convert.FromHexString(descriptor.Sha256);
                if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                    throw new InvalidDataException("Downloaded model failed its SHA-256 integrity check.");
            }

            File.Move(temporaryPath, fullDestinationPath, true);
            TryReportProgress(progressCallback, 1.0);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void EnsureSufficientDiskSpace(string destinationPath, long fileSize)
    {
        try
        {
            var fullPath = Path.GetFullPath(destinationPath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root)) return;

            var requiredSpace = checked(fileSize + 256L * 1024 * 1024);
            if (new DriveInfo(root).AvailableFreeSpace < requiredSpace)
                throw new IOException("There is not enough free disk space to download this model safely.");
        }
        catch (ArgumentException)
        {
            // Some virtual paths do not expose DriveInfo. The atomic download
            // will still fail safely if the underlying storage fills up.
        }
    }

    private static void TryReportProgress(Action<double> progressCallback, double progress)
    {
        try
        {
            progressCallback(progress);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
            // A closing Settings window may dispose its ProgressBar between
            // the cancellation check and BeginInvoke. Observer lifetime must
            // not change the durable result of a verified model download.
        }
    }

    private static void TryDeleteStaleTemporaryFiles(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        var pattern = Path.GetFileName(destinationPath) + ".download-*.tmp";
        var staleBefore = DateTime.UtcNow - TimeSpan.FromDays(1);
        try
        {
            foreach (var temporaryPath in Directory.EnumerateFiles(directory, pattern))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(temporaryPath) < staleBefore)
                        File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Another live process may still own the file. Its request
                    // remains responsible for cleanup and activation.
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Stale cleanup is best-effort and must not block a fresh download.
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
