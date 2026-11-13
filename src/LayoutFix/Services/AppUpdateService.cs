using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace LayoutFix.Services;

public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    public const string PortableAssetName = "LayoutFix_Portable_win-x64.zip";
    public const string InstallerAssetName = "LayoutFix_Setup.exe";
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/Wave-is/LayoutFix/releases/latest");
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private const long MaximumExtractedBytes = 1536L * 1024 * 1024;
    private const int MaximumArchiveEntries = 20_000;

    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ReleaseVersion _currentVersion;
    private readonly string _updateRoot;
    private readonly string _executablePath;
    private readonly Func<string, ReleaseVersion?> _payloadVersionReader;
    private readonly bool _installedMode;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _stateGate = new();
    private AppUpdateState _state = new(AppUpdateStatus.Idle);
    private string? _readyPayloadPath;
    private string? _readyInstallerPath;
    private byte[]? _readyInstallerHash;
    private string? _readyMarkerPath;
    private UpdatePackageKind? _readyPackageKind;
    private ReleaseVersion? _readyVersion;
    private int _automaticCheckStarted;
    private bool _disposed;

    public AppUpdateService(ISettingsService settings, ILoggerService logger)
        : this(
            settings,
            logger,
            CreateHttpClient(),
            ownsHttpClient: true,
            ReadCurrentVersion(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LayoutFix",
                "Updates"),
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LayoutFix.exe"),
            ReadPayloadVersion)
    {
    }

    public AppUpdateService(
        ISettingsService settings,
        ILoggerService logger,
        string updateRoot,
        string executablePath)
        : this(
            settings,
            logger,
            CreateHttpClient(),
            ownsHttpClient: true,
            ReadCurrentVersion(),
            updateRoot,
            executablePath,
            ReadPayloadVersion)
    {
    }

    public AppUpdateService(
        ISettingsService settings,
        ILoggerService logger,
        HttpClient httpClient,
        ReleaseVersion currentVersion,
        string updateRoot,
        string executablePath,
        Func<string, ReleaseVersion?>? payloadVersionReader = null)
        : this(
            settings,
            logger,
            httpClient,
            ownsHttpClient: false,
            currentVersion,
            updateRoot,
            executablePath,
            payloadVersionReader ?? ReadPayloadVersion)
    {
    }

    private AppUpdateService(
        ISettingsService settings,
        ILoggerService logger,
        HttpClient httpClient,
        bool ownsHttpClient,
        ReleaseVersion currentVersion,
        string updateRoot,
        string executablePath,
        Func<string, ReleaseVersion?> payloadVersionReader)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _currentVersion = currentVersion;
        _updateRoot = Path.GetFullPath(updateRoot);
        _executablePath = Path.GetFullPath(executablePath);
        _payloadVersionReader = payloadVersionReader;
        _installedMode = File.Exists(Path.Combine(
            Path.GetDirectoryName(_executablePath) ?? string.Empty,
            "unins000.exe"));
        TryRestoreReadyPackage();
    }

    public AppUpdateState State
    {
        get
        {
            lock (_stateGate)
                return _state;
        }
    }

    public event EventHandler<AppUpdateState>? StateChanged;
    public bool HasAttemptedInstall => _readyMarkerPath != null &&
        File.Exists(_readyMarkerPath + ".attempted");

    public void StartAutomaticCheck()
    {
        if (!_settings.Current.AutoUpdateEnabled ||
            Interlocked.Exchange(ref _automaticCheckStarted, 1) != 0)
        {
            return;
        }

        var lifetimeToken = _lifetimeCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), lifetimeToken);
                while (!lifetimeToken.IsCancellationRequested)
                {
                    if (_settings.Current.AutoUpdateEnabled && State.Status is not
                        (AppUpdateStatus.ReadyToInstall or AppUpdateStatus.Installing))
                        await CheckAndDownloadAsync(cancellationToken: lifetimeToken);
                    await Task.Delay(TimeSpan.FromHours(6), lifetimeToken);
                }
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
            }
        });
    }

    public async Task CheckAndDownloadAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!force && !_settings.Current.AutoUpdateEnabled)
            return;

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        if (!await _operationGate.WaitAsync(0, linkedCancellation.Token))
            return;

        try
        {
            SetState(new AppUpdateState(AppUpdateStatus.Checking));
            var release = await GetLatestReleaseAsync(linkedCancellation.Token);
            if (release.Version.CompareTo(_currentVersion) <= 0)
            {
                SetState(new AppUpdateState(
                    AppUpdateStatus.UpToDate,
                    _currentVersion.ToString(),
                    ReleasePageUrl: release.ReleasePageUrl));
                return;
            }

            SetState(new AppUpdateState(
                AppUpdateStatus.Downloading,
                release.Version.ToString(),
                ReleasePageUrl: release.ReleasePageUrl));
            var stagedPackage = await DownloadAndStageAsync(release, linkedCancellation.Token);
            _readyPackageKind = stagedPackage.Kind;
            _readyPayloadPath = stagedPackage.Kind == UpdatePackageKind.Portable
                ? stagedPackage.Path
                : null;
            _readyInstallerPath = stagedPackage.Kind == UpdatePackageKind.Installer
                ? stagedPackage.Path
                : null;
            _readyVersion = release.Version;
            _readyInstallerHash = release.ExpectedHash;
            _readyMarkerPath = WriteReadyMarker(stagedPackage, release.Version, release.ExpectedHash);
            SetState(new AppUpdateState(
                AppUpdateStatus.ReadyToInstall,
                release.Version.ToString(),
                100,
                release.ReleasePageUrl));
            _logger.LogInfo(
                $"Update package is ready. Version={release.Version}; Integrity=sha256; Payload=verified.");
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            SetState(new AppUpdateState(AppUpdateStatus.Idle));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Update check failed. " +
                $"ExceptionType={exception.GetType().FullName}; HResult=0x{exception.HResult:X8}.");
            SetState(new AppUpdateState(AppUpdateStatus.Failed));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public bool TryStartInstaller()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var version = _readyVersion;
        if (State.Status != AppUpdateStatus.ReadyToInstall ||
            version == null ||
            _readyPackageKind == null)
        {
            return false;
        }

        try
        {
            var installDirectory = Path.GetDirectoryName(_executablePath)
                ?? throw new InvalidOperationException("The installation directory is unavailable.");
            if (_readyPackageKind == UpdatePackageKind.Installer)
            {
                if (_readyInstallerPath == null || !File.Exists(_readyInstallerPath))
                    return false;
                using var verifiedFile = File.OpenRead(_readyInstallerPath);
                if (_readyInstallerHash == null ||
                    !CryptographicOperations.FixedTimeEquals(SHA256.HashData(verifiedFile), _readyInstallerHash))
                    throw new InvalidDataException("The staged installer changed after download.");
                var installerStartInfo = new ProcessStartInfo(_readyInstallerPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(_readyInstallerPath)!
                };
                installerStartInfo.ArgumentList.Add("/VERYSILENT");
                installerStartInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
                installerStartInfo.ArgumentList.Add("/NORESTART");
                installerStartInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
                installerStartInfo.ArgumentList.Add("/LAUNCHAFTERUPDATE");
                installerStartInfo.ArgumentList.Add($"/LOG={Path.Combine(_updateRoot, "last-installer.log")}");
                installerStartInfo.ArgumentList.Add($"/DIR={installDirectory}");
                File.WriteAllText(_readyMarkerPath! + ".attempted", version.Value.ToString());
                using var installerProcess = Process.Start(installerStartInfo)
                    ?? throw new InvalidOperationException("The verified update installer could not start.");
                SetState(new AppUpdateState(AppUpdateStatus.Installing, version.Value.ToString(), 100));
                _logger.LogInfo($"Verified update installer started. Version={version.Value}.");
                return true;
            }

            var payloadPath = _readyPayloadPath;
            if (payloadPath == null)
                return false;
            var stagedExecutable = Path.Combine(payloadPath, "LayoutFix.exe");
            if (!File.Exists(stagedExecutable))
                return false;
            Directory.CreateDirectory(_updateRoot);
            var resultPath = Path.Combine(_updateRoot, "last-update-result.json");
            using var currentProcess = Process.GetCurrentProcess();
            var startInfo = new ProcessStartInfo(stagedExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = payloadPath
            };
            startInfo.ArgumentList.Add("--apply-update");
            startInfo.ArgumentList.Add(currentProcess.Id.ToString());
            startInfo.ArgumentList.Add(currentProcess.StartTime.ToUniversalTime().Ticks.ToString());
            startInfo.ArgumentList.Add(installDirectory);
            startInfo.ArgumentList.Add(payloadPath);
            startInfo.ArgumentList.Add(version.Value.ToString());
            startInfo.ArgumentList.Add(resultPath);

            using var updateProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The portable update installer could not start.");
            TryDeleteFile(_readyMarkerPath);
            SetState(new AppUpdateState(AppUpdateStatus.Installing, version.Value.ToString(), 100));
            _logger.LogInfo($"Update installer started. Version={version.Value}.");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Update installer could not start. " +
                $"ExceptionType={exception.GetType().FullName}; HResult=0x{exception.HResult:X8}.");
            SetState(new AppUpdateState(AppUpdateStatus.Failed, version.Value.ToString()));
            return false;
        }
    }

    private async Task<ReleaseDescriptor> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        using var checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkCancellation.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            checkCancellation.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(checkCancellation.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: checkCancellation.Token);
        var root = document.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
        {
            throw new InvalidDataException("The latest release endpoint returned a non-stable release.");
        }

        var tag = root.GetProperty("tag_name").GetString();
        if (!ReleaseVersion.TryParse(tag, out var version) || version.Prerelease != null)
            throw new InvalidDataException("The release tag is not a stable semantic version.");

        var releasePageUrl = root.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString()
            : null;
        if (version.CompareTo(_currentVersion) <= 0)
        {
            return new ReleaseDescriptor(
                version,
                null,
                0,
                [],
                releasePageUrl,
                _installedMode ? UpdatePackageKind.Installer : UpdatePackageKind.Portable);
        }

        var expectedAssetName = _installedMode ? InstallerAssetName : PortableAssetName;
        JsonElement? matchingAsset = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(
                    asset.GetProperty("name").GetString(),
                    expectedAssetName,
                    StringComparison.Ordinal))
            {
                matchingAsset = asset;
                break;
            }
        }

        if (matchingAsset == null)
            throw new InvalidDataException("The stable release does not contain an update package.");

        var selectedAsset = matchingAsset.Value;
        var size = selectedAsset.GetProperty("size").GetInt64();
        if (size <= 1024 * 1024 || size > MaximumDownloadBytes)
            throw new InvalidDataException("The update package size is outside the safe range.");

        var digest = selectedAsset.GetProperty("digest").GetString();
        if (!TryParseSha256Digest(digest, out var expectedHash))
            throw new InvalidDataException("The update package has no valid SHA-256 digest.");

        var downloadUrl = selectedAsset.GetProperty("browser_download_url").GetString();
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.AbsolutePath.StartsWith(
                "/Wave-is/LayoutFix/releases/download/",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update package URL is not trusted.");
        }

        return new ReleaseDescriptor(
            version,
            downloadUri,
            size,
            expectedHash,
            releasePageUrl,
            _installedMode ? UpdatePackageKind.Installer : UpdatePackageKind.Portable);
    }

    private async Task<StagedPackage> DownloadAndStageAsync(
        ReleaseDescriptor release,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_updateRoot);
        var stageDirectory = Path.Combine(
            _updateRoot,
            $"{release.Version}-{Guid.NewGuid():N}");
        var packagePath = Path.Combine(
            stageDirectory,
            release.Kind == UpdatePackageKind.Installer ? InstallerAssetName : "package.zip");
        var temporaryArchivePath = packagePath + ".download";
        var payloadPath = Path.Combine(stageDirectory, "payload");
        Directory.CreateDirectory(stageDirectory);

        try
        {
            using var response = await _httpClient.GetAsync(
                release.DownloadUri ?? throw new InvalidDataException(
                    "The update download URL is missing."),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength != release.Size)
            {
                throw new InvalidDataException("The downloaded update size does not match its release metadata.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryArchivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long totalRead = 0;
            var lastProgress = -1;
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) != 0)
            {
                totalRead = checked(totalRead + bytesRead);
                if (totalRead > MaximumDownloadBytes || totalRead > release.Size)
                    throw new InvalidDataException("The update package exceeded its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                hash.AppendData(buffer, 0, bytesRead);

                var progress = (int)Math.Clamp(totalRead * 100L / release.Size, 0, 99);
                if (progress >= lastProgress + 2)
                {
                    lastProgress = progress;
                    SetState(new AppUpdateState(
                        AppUpdateStatus.Downloading,
                        release.Version.ToString(),
                        progress,
                        release.ReleasePageUrl));
                }
            }

            await output.FlushAsync(cancellationToken);
            if (totalRead != release.Size)
                throw new InvalidDataException("The downloaded update is incomplete.");

            var actualHash = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualHash, release.ExpectedHash))
                throw new InvalidDataException("The downloaded update failed its SHA-256 integrity check.");

            output.Close();
            File.Move(temporaryArchivePath, packagePath);
            if (release.Kind == UpdatePackageKind.Installer)
            {
                var installerVersion = _payloadVersionReader(packagePath);
                if (installerVersion == null ||
                    installerVersion.Value.CompareTo(release.Version) != 0)
                {
                    throw new InvalidDataException(
                        "The update installer version does not match the release tag.");
                }
                return new StagedPackage(UpdatePackageKind.Installer, packagePath);
            }

            ExtractVerifiedArchive(packagePath, payloadPath);
            ValidatePayload(payloadPath, release.Version);
            File.Delete(packagePath);
            return new StagedPackage(UpdatePackageKind.Portable, payloadPath);
        }
        catch
        {
            TryDeleteDirectory(stageDirectory);
            throw;
        }
    }

    private void ValidatePayload(string payloadPath, ReleaseVersion expectedVersion)
    {
        string[] requiredFiles =
        [
            "LayoutFix.exe",
            "LayoutFix.Core.dll",
            "LayoutFix.Infrastructure.dll",
            Path.Combine("translation-worker", "LayoutFix.TranslationWorker.dll"),
            Path.Combine("Dictionaries", "en.txt"),
            Path.Combine("locales", "en.json")
        ];
        if (requiredFiles.Any(relative => !File.Exists(Path.Combine(payloadPath, relative))))
            throw new InvalidDataException("The update package is missing required application files.");

        var payloadVersion = _payloadVersionReader(Path.Combine(payloadPath, "LayoutFix.exe"));
        if (payloadVersion == null || payloadVersion.Value.CompareTo(expectedVersion) != 0)
            throw new InvalidDataException("The update payload version does not match the release tag.");
    }

    private static void ExtractVerifiedArchive(string archivePath, string payloadPath)
    {
        Directory.CreateDirectory(payloadPath);
        var payloadRoot = Path.GetFullPath(payloadPath) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidDataException("The update archive entry count is outside the safe range.");

        long extractedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.IndexOf('\0') >= 0 || IsPrivatePayloadFile(entry.FullName))
                throw new InvalidDataException("The update archive contains a forbidden file.");

            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
                throw new InvalidDataException("The update archive contains a symbolic link.");

            var destination = Path.GetFullPath(Path.Combine(payloadPath, entry.FullName));
            if (!destination.StartsWith(payloadRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update archive contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > MaximumExtractedBytes)
                throw new InvalidDataException("The expanded update exceeds the safe size limit.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static bool IsPrivatePayloadFile(string archiveName)
    {
        var name = Path.GetFileName(archiveName);
        return name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("translation_history.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("layoutfix.log", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
    }

    private void SetState(AppUpdateState state)
    {
        lock (_stateGate)
            _state = state;
        try
        {
            StateChanged?.Invoke(this, state);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Update observer failed. " +
                $"ExceptionType={exception.GetType().FullName}; HResult=0x{exception.HResult:X8}.");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LayoutFix", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static ReleaseVersion ReadCurrentVersion()
    {
        var informationalVersion = typeof(AppUpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!ReleaseVersion.TryParse(informationalVersion, out var version))
            throw new InvalidOperationException("The running application version is invalid.");
        return version;
    }

    private static ReleaseVersion? ReadPayloadVersion(string executablePath)
    {
        var productVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
        return ReleaseVersion.TryParse(productVersion, out var version) ? version : null;
    }

    private static bool TryParseSha256Digest(string? digest, out byte[] hash)
    {
        hash = [];
        if (digest == null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return false;
        var hex = digest[7..];
        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
            return false;
        hash = Convert.FromHexString(hex);
        return true;
    }

    private string WriteReadyMarker(StagedPackage package, ReleaseVersion version, byte[] hash)
    {
        var stageDirectory = package.Kind == UpdatePackageKind.Portable
            ? Directory.GetParent(package.Path)?.FullName
            : Path.GetDirectoryName(package.Path);
        if (stageDirectory == null)
            throw new InvalidDataException("The update staging directory is invalid.");
        var markerPath = Path.Combine(stageDirectory, "ready.json");
        var relativePackagePath = Path.GetRelativePath(stageDirectory, package.Path);
        var json = JsonSerializer.Serialize(new ReadyMarker(
            version.ToString(),
            package.Kind.ToString(),
            relativePackagePath, "sha256:" + Convert.ToHexString(hash)));
        File.WriteAllText(markerPath, json);
        return markerPath;
    }

    private void TryRestoreReadyPackage()
    {
        try
        {
            if (!Directory.Exists(_updateRoot)) return;
            foreach (var markerPath in Directory
                         .EnumerateFiles(_updateRoot, "ready.json", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var marker = JsonSerializer.Deserialize<ReadyMarker>(
                        File.ReadAllText(markerPath));
                    if (marker == null ||
                        !ReleaseVersion.TryParse(marker.Version, out var version) ||
                        version.CompareTo(_currentVersion) <= 0 ||
                        !Enum.TryParse<UpdatePackageKind>(marker.Kind, out var kind) ||
                        kind == UpdatePackageKind.Installer != _installedMode)
                    {
                        continue;
                    }

                    var stageDirectory = Path.GetDirectoryName(markerPath)!;
                    var stagePrefix = Path.GetFullPath(stageDirectory) + Path.DirectorySeparatorChar;
                    var packagePath = Path.GetFullPath(Path.Combine(
                        stageDirectory,
                        marker.RelativePackagePath));
                    if (!packagePath.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (kind == UpdatePackageKind.Portable)
                        ValidatePayload(packagePath, version);
                    else
                    {
                        if (!TryParseSha256Digest(marker.Digest, out var expectedHash))
                            continue;
                        if (!File.Exists(packagePath) ||
                            _payloadVersionReader(packagePath) is not { } installerVersion ||
                            installerVersion.CompareTo(version) != 0)
                        {
                            continue;
                        }
                        _readyInstallerHash = expectedHash;
                    }

                    _readyPackageKind = kind;
                    _readyVersion = version;
                    _readyPayloadPath = kind == UpdatePackageKind.Portable ? packagePath : null;
                    _readyInstallerPath = kind == UpdatePackageKind.Installer ? packagePath : null;
                    _readyMarkerPath = markerPath;
                    _state = new AppUpdateState(
                        AppUpdateStatus.ReadyToInstall,
                        version.ToString(),
                        100);
                    return;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException or
                        InvalidDataException)
                {
                    // Ignore incomplete or stale staging records. A fresh check
                    // can safely download another isolated package.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        // A background request may still be unwinding and releasing this gate.
        // Process shutdown is imminent; disposing it here would turn a normal
        // cancellation into an ObjectDisposedException on that worker.
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed record ReleaseDescriptor(
        ReleaseVersion Version,
        Uri? DownloadUri,
        long Size,
        byte[] ExpectedHash,
        string? ReleasePageUrl,
        UpdatePackageKind Kind);

    private sealed record StagedPackage(UpdatePackageKind Kind, string Path);

    private sealed record ReadyMarker(string Version, string Kind, string RelativePackagePath, string? Digest = null);

    private enum UpdatePackageKind
    {
        Portable,
        Installer
    }
}
