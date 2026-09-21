using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Services;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LayoutFix.Tests;

public sealed class AppUpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.UpdateTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Check_WhenLatestIsOlder_DoesNotDownload()
    {
        var package = CreatePortableArchive();
        var handler = new ReleaseHandler("1.0.22", package, omitAssets: true);
        using var client = new HttpClient(handler);
        using var service = CreateService(client, new ReleaseVersion(1, 0, 23));

        await service.CheckAndDownloadAsync(force: true);

        Assert.Equal(AppUpdateStatus.UpToDate, service.State.Status);
        Assert.Equal(1, handler.ReleaseRequests);
        Assert.Equal(0, handler.PackageRequests);
    }

    [Fact]
    public async Task Check_DownloadsVerifiesAndStagesNewerPortableRelease()
    {
        var package = CreatePortableArchive();
        var handler = new ReleaseHandler("1.0.24", package);
        using var client = new HttpClient(handler);
        using var service = CreateService(client, new ReleaseVersion(1, 0, 23));

        await service.CheckAndDownloadAsync(force: true);

        Assert.Equal(AppUpdateStatus.ReadyToInstall, service.State.Status);
        Assert.Equal("1.0.24", service.State.Version);
        Assert.Equal(1, handler.PackageRequests);
        var stagedExecutable = Assert.Single(
            Directory.EnumerateFiles(
                Path.Combine(_root, "updates"),
                "LayoutFix.exe",
                SearchOption.AllDirectories));
        Assert.True(File.Exists(stagedExecutable));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.download", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.pdb", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Check_RejectsPackageWhoseDigestDoesNotMatch()
    {
        var package = CreatePortableArchive();
        var handler = new ReleaseHandler("1.0.24", package, corruptDigest: true);
        using var client = new HttpClient(handler);
        using var service = CreateService(client, new ReleaseVersion(1, 0, 23));

        await service.CheckAndDownloadAsync(force: true);

        Assert.Equal(AppUpdateStatus.Failed, service.State.Status);
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(_root, "updates"),
            "LayoutFix.exe",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Check_RejectsArchiveTraversalWithoutWritingOutsideStage()
    {
        var package = CreatePortableArchive("../escape.txt");
        var handler = new ReleaseHandler("1.0.24", package);
        using var client = new HttpClient(handler);
        using var service = CreateService(client, new ReleaseVersion(1, 0, 23));
        var escapedPath = Path.Combine(_root, "escape.txt");

        await service.CheckAndDownloadAsync(force: true);

        Assert.Equal(AppUpdateStatus.Failed, service.State.Status);
        Assert.False(File.Exists(escapedPath));
    }

    [Fact]
    public async Task Check_WhenAutomaticUpdatesAreDisabled_RequiresForce()
    {
        var package = CreatePortableArchive();
        var handler = new ReleaseHandler("1.0.24", package);
        using var client = new HttpClient(handler);
        var settings = new InMemorySettingsService();
        settings.Current.AutoUpdateEnabled = false;
        using var service = CreateService(
            client,
            new ReleaseVersion(1, 0, 23),
            settings);

        await service.CheckAndDownloadAsync();

        Assert.Equal(AppUpdateStatus.Idle, service.State.Status);
        Assert.Equal(0, handler.ReleaseRequests);

        await service.CheckAndDownloadAsync(force: true);
        Assert.Equal(AppUpdateStatus.ReadyToInstall, service.State.Status);
    }

    [Fact]
    public async Task Constructor_RestoresPreviouslyVerifiedReadyPackage()
    {
        var package = CreatePortableArchive();
        var handler = new ReleaseHandler("1.0.24", package);
        using var client = new HttpClient(handler);
        using (var first = CreateService(client, new ReleaseVersion(1, 0, 23)))
            await first.CheckAndDownloadAsync(force: true);

        using var secondClient = new HttpClient(new ReleaseHandler("1.0.24", package));
        using var restored = CreateService(secondClient, new ReleaseVersion(1, 0, 23));

        Assert.Equal(AppUpdateStatus.ReadyToInstall, restored.State.Status);
        Assert.Equal("1.0.24", restored.State.Version);
    }

    [Fact]
    public async Task InstalledCopy_SelectsVerifiedInstallerAsset()
    {
        var installer = CreatePortableArchive();
        var handler = new ReleaseHandler(
            "1.0.24",
            installer,
            assetName: AppUpdateService.InstallerAssetName);
        using var client = new HttpClient(handler);
        using var service = CreateService(
            client,
            new ReleaseVersion(1, 0, 23),
            installed: true);

        await service.CheckAndDownloadAsync(force: true);

        Assert.Equal(AppUpdateStatus.ReadyToInstall, service.State.Status);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(_root, "updates"),
            AppUpdateService.InstallerAssetName,
            SearchOption.AllDirectories));
    }

    private AppUpdateService CreateService(
        HttpClient client,
        ReleaseVersion currentVersion,
        InMemorySettingsService? settings = null,
        bool installed = false)
    {
        Directory.CreateDirectory(_root);
        var executablePath = Path.Combine(_root, "current", "LayoutFix.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllBytes(executablePath, [0x4D, 0x5A]);
        if (installed)
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(executablePath)!, "unins000.exe"), "fixture");
        return new AppUpdateService(
            settings ?? new InMemorySettingsService(),
            new RecordingLogger(),
            client,
            currentVersion,
            Path.Combine(_root, "updates"),
            executablePath,
            _ => new ReleaseVersion(1, 0, 24));
    }

    private static byte[] CreatePortableArchive(string? unsafeEntry = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "LayoutFix.exe", "MZ-layoutfix");
            AddEntry(archive, "LayoutFix.Core.dll", "core");
            AddEntry(archive, "LayoutFix.Infrastructure.dll", "infrastructure");
            AddEntry(
                archive,
                "translation-worker/LayoutFix.TranslationWorker.dll",
                "worker");
            AddEntry(archive, "Dictionaries/en.txt", "hello 100\nworld 90");
            AddEntry(archive, "locales/en.json", "{}");
            if (unsafeEntry != null)
                AddEntry(archive, unsafeEntry, "must-not-escape");

            var padding = archive.CreateEntry("Resources/update-padding.bin", CompressionLevel.NoCompression);
            using var paddingStream = padding.Open();
            var random = new byte[128 * 1024];
            for (var block = 0; block < 10; block++)
            {
                RandomNumberGenerator.Fill(random);
                paddingStream.Write(random);
            }
        }
        return output.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string contents)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class ReleaseHandler(
        string version,
        byte[] package,
        bool corruptDigest = false,
        bool omitAssets = false,
        string assetName = AppUpdateService.PortableAssetName) : HttpMessageHandler
    {
        public int ReleaseRequests { get; private set; }
        public int PackageRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                ReleaseRequests++;
                var digest = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
                if (corruptDigest)
                    digest = new string('0', 64);
                var json = JsonSerializer.Serialize(new
                {
                    tag_name = $"v{version}",
                    html_url = $"https://github.com/Wave-is/LayoutFix/releases/tag/v{version}",
                    draft = false,
                    prerelease = false,
                    assets = omitAssets
                        ? []
                        : new[]
                        {
                            new
                            {
                                name = assetName,
                                size = package.LongLength,
                                digest = $"sha256:{digest}",
                                browser_download_url =
                                    $"https://github.com/Wave-is/LayoutFix/releases/download/v{version}/" +
                                    assetName
                            }
                        }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            PackageRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(package)
            });
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class RecordingLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }
}
