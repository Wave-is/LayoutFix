using LayoutFix.Core.Models;
using System.Diagnostics;
using System.Text.Json;

namespace LayoutFix.Services;

/// <summary>
/// Runs from the already verified staged payload. The old application must
/// exit before its directory can be replaced. A smoke failure restores the
/// complete previous directory and relaunches the old version.
/// </summary>
internal static class AppUpdateInstaller
{
    private const int ParentExitTimeoutMilliseconds = 60_000;
    private const int SmokeTimeoutMilliseconds = 120_000;
    private const int StartupObservationMilliseconds = 3_000;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 7 ||
            !string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(args[1], out var parentProcessId) ||
            !long.TryParse(args[2], out var parentStartTicks) ||
            !ReleaseVersion.TryParse(args[5], out var expectedVersion))
        {
            return 70;
        }

        return await RunCoreAsync(
            parentProcessId,
            parentStartTicks,
            args[3],
            args[4],
            expectedVersion,
            args[6],
            restartAfterUpdate: true,
            smokeTest: null);
    }

    internal static Task<int> RunForTestAsync(
        string installDirectory,
        string payloadDirectory,
        ReleaseVersion expectedVersion,
        string resultPath,
        Func<string, Task<bool>>? smokeTest = null)
        => RunCoreAsync(
            parentProcessId: -1,
            parentStartTicks: 0,
            installDirectory,
            payloadDirectory,
            expectedVersion,
            resultPath,
            restartAfterUpdate: false,
            smokeTest);

    private static async Task<int> RunCoreAsync(
        int parentProcessId,
        long parentStartTicks,
        string requestedInstallDirectory,
        string requestedPayloadDirectory,
        ReleaseVersion expectedVersion,
        string requestedResultPath,
        bool restartAfterUpdate,
        Func<string, Task<bool>>? smokeTest)
    {

        string? backupDirectory = null;
        string? installDirectory = null;
        var installMoved = false;
        try
        {
            installDirectory = ValidateDirectory(requestedInstallDirectory, mustExist: true);
            var payloadDirectory = ValidateDirectory(requestedPayloadDirectory, mustExist: true);
            var resultPath = Path.GetFullPath(requestedResultPath);
            ValidatePathRelationship(installDirectory, payloadDirectory);
            ValidatePayload(payloadDirectory, expectedVersion);
            await WaitForParentExitAsync(parentProcessId, parentStartTicks);

            var installParent = Directory.GetParent(installDirectory)?.FullName
                ?? throw new InvalidDataException("The installation directory has no safe parent.");
            backupDirectory = Path.Combine(
                installParent,
                $".{Path.GetFileName(installDirectory)}.update-backup-{Guid.NewGuid():N}");
            Directory.Move(installDirectory, backupDirectory);
            installMoved = true;
            Directory.CreateDirectory(installDirectory);
            CopyDirectory(payloadDirectory, installDirectory);

            var installedExecutable = Path.Combine(installDirectory, "LayoutFix.exe");
            if (!await (smokeTest ?? RunSmokeTestAsync)(installedExecutable))
                throw new InvalidDataException("The installed update failed its isolated smoke test.");

            if (restartAfterUpdate)
            {
                var application = Process.Start(new ProcessStartInfo(installedExecutable)
                {
                    UseShellExecute = false,
                    WorkingDirectory = installDirectory
                }) ?? throw new InvalidOperationException("The updated application could not start.");
                using (application)
                {
                    await Task.Delay(StartupObservationMilliseconds);
                    if (application.HasExited)
                        throw new InvalidOperationException("The updated application exited during startup.");
                }
            }

            TryDeleteDirectory(backupDirectory);
            WriteResult(resultPath, "success", expectedVersion.ToString(), 0);
            return 0;
        }
        catch (Exception exception)
        {
            if (installMoved && installDirectory != null && backupDirectory != null)
                TryRollbackAndRestart(installDirectory, backupDirectory, restartAfterUpdate);
            TryWriteFailure(requestedResultPath, expectedVersion, exception.HResult);
            return 71;
        }
    }

    private static string ValidateDirectory(string path, bool mustExist)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(fullPath) ||
            string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
            mustExist && !Directory.Exists(fullPath))
        {
            throw new InvalidDataException("An update directory is invalid.");
        }
        return fullPath;
    }

    private static void ValidatePathRelationship(string installDirectory, string payloadDirectory)
    {
        var installPrefix = installDirectory + Path.DirectorySeparatorChar;
        var payloadPrefix = payloadDirectory + Path.DirectorySeparatorChar;
        if (string.Equals(installDirectory, payloadDirectory, StringComparison.OrdinalIgnoreCase) ||
            installDirectory.StartsWith(payloadPrefix, StringComparison.OrdinalIgnoreCase) ||
            payloadDirectory.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update payload overlaps the installation directory.");
        }
    }

    private static void ValidatePayload(string payloadDirectory, ReleaseVersion expectedVersion)
    {
        var executable = Path.Combine(payloadDirectory, "LayoutFix.exe");
        if (!File.Exists(executable))
            throw new InvalidDataException("The staged application executable is missing.");

        var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
        if (!ReleaseVersion.TryParse(productVersion, out var actualVersion) ||
            actualVersion.CompareTo(expectedVersion) != 0)
        {
            throw new InvalidDataException("The staged application version is invalid.");
        }
    }

    private static async Task WaitForParentExitAsync(int processId, long expectedStartTicks)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime().Ticks != expectedStartTicks)
                throw new InvalidDataException("The update parent process identity changed.");
            await process.WaitForExitAsync().WaitAsync(
                TimeSpan.FromMilliseconds(ParentExitTimeoutMilliseconds));
        }
        catch (ArgumentException)
        {
            // The parent already exited between launching the updater and this
            // process opening its handle.
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static async Task<bool> RunSmokeTestAsync(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            ArgumentList = { "--smoke-test" }
        });
        if (process == null) return false;
        try
        {
            await process.WaitForExitAsync().WaitAsync(
                TimeSpan.FromMilliseconds(SmokeTimeoutMilliseconds));
            return process.ExitCode == 0;
        }
        catch (TimeoutException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return false;
        }
    }

    private static void TryRollbackAndRestart(
        string installDirectory,
        string backupDirectory,
        bool restartApplication)
    {
        try
        {
            TryDeleteDirectory(installDirectory);
            if (!Directory.Exists(installDirectory) && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, installDirectory);
                var executable = Path.Combine(installDirectory, "LayoutFix.exe");
                if (restartApplication && File.Exists(executable))
                {
                    Process.Start(new ProcessStartInfo(executable)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = installDirectory
                    })?.Dispose();
                }
            }
        }
        catch
        {
            // Keep the complete backup directory for manual recovery if a
            // filesystem or antivirus lock also prevents automatic rollback.
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

    private static void TryWriteFailure(string? resultPath, ReleaseVersion version, int hResult)
    {
        if (string.IsNullOrWhiteSpace(resultPath)) return;
        try
        {
            WriteResult(Path.GetFullPath(resultPath), "failed", version.ToString(), hResult);
        }
        catch
        {
        }
    }

    private static void WriteResult(string path, string status, string version, int hResult)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new
        {
            status,
            version,
            hresult = $"0x{hResult:X8}",
            completedUtc = DateTimeOffset.UtcNow
        });
        File.WriteAllText(path, json);
    }
}
