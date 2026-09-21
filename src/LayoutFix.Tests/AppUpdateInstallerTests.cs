using LayoutFix.Core.Models;
using LayoutFix.Services;
using System.Diagnostics;
using System.Text.Json;

namespace LayoutFix.Tests;

public sealed class AppUpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.UpdateInstallerTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task RunForTest_ReplacesDirectoryAfterSmokePasses()
    {
        var fixture = CreateFixture();

        var exitCode = await AppUpdateInstaller.RunForTestAsync(
            fixture.InstallDirectory,
            fixture.PayloadDirectory,
            fixture.Version,
            fixture.ResultPath,
            _ => Task.FromResult(true));

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(fixture.InstallDirectory, "old.probe")));
        Assert.True(File.Exists(Path.Combine(fixture.InstallDirectory, "new.probe")));
        Assert.Contains(
            "\"status\":\"success\"",
            File.ReadAllText(fixture.ResultPath),
            StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateDirectories(
            _root,
            ".install.update-backup-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RunForTest_RestoresCompleteOldDirectoryWhenSmokeFails()
    {
        var fixture = CreateFixture();

        var exitCode = await AppUpdateInstaller.RunForTestAsync(
            fixture.InstallDirectory,
            fixture.PayloadDirectory,
            fixture.Version,
            fixture.ResultPath,
            _ => Task.FromResult(false));

        Assert.Equal(71, exitCode);
        Assert.True(File.Exists(Path.Combine(fixture.InstallDirectory, "old.probe")));
        Assert.False(File.Exists(Path.Combine(fixture.InstallDirectory, "new.probe")));
        Assert.Contains(
            "\"status\":\"failed\"",
            File.ReadAllText(fixture.ResultPath),
            StringComparison.Ordinal);
    }

    private Fixture CreateFixture()
    {
        var sourceExecutable = Path.Combine(AppContext.BaseDirectory, "LayoutFix.exe");
        Assert.True(File.Exists(sourceExecutable));
        var productVersion = FileVersionInfo.GetVersionInfo(sourceExecutable).ProductVersion;
        Assert.True(ReleaseVersion.TryParse(productVersion, out var version));

        var installDirectory = Path.Combine(_root, "install");
        var payloadDirectory = Path.Combine(_root, "payload");
        Directory.CreateDirectory(installDirectory);
        Directory.CreateDirectory(payloadDirectory);
        File.Copy(sourceExecutable, Path.Combine(installDirectory, "LayoutFix.exe"));
        File.Copy(sourceExecutable, Path.Combine(payloadDirectory, "LayoutFix.exe"));
        File.WriteAllText(Path.Combine(installDirectory, "old.probe"), "old");
        File.WriteAllText(Path.Combine(payloadDirectory, "new.probe"), "new");
        return new Fixture(
            installDirectory,
            payloadDirectory,
            Path.Combine(_root, "result.json"),
            version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record Fixture(
        string InstallDirectory,
        string PayloadDirectory,
        string ResultPath,
        ReleaseVersion Version);
}
