using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

public sealed class FileLoggerServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.FileLoggerTests.{Guid.NewGuid():N}");

    [Fact]
    public void ConfiguredDirectory_ReceivesDiagnosticLog()
    {
        var destination = Path.Combine(_testDirectory, "shared");
        var fallback = Path.Combine(_testDirectory, "fallback", "layoutfix.log");
        var settings = EnabledSettings(destination);
        using var logger = new FileLoggerService(settings, null, fallback);

        logger.LogInfo("custom-destination-probe");
        logger.Flush();

        var configuredLog = Path.Combine(destination, DiagnosticLogPath.FileName);
        Assert.Contains("custom-destination-probe", File.ReadAllText(configuredLog));
        Assert.False(File.Exists(fallback));
    }

    [Fact]
    public void ChangedDirectory_IsUsedWithoutRestartingLogger()
    {
        var firstDestination = Path.Combine(_testDirectory, "first");
        var secondDestination = Path.Combine(_testDirectory, "second");
        var fallback = Path.Combine(_testDirectory, "fallback", "layoutfix.log");
        var settings = EnabledSettings(firstDestination);
        using var logger = new FileLoggerService(settings, null, fallback);

        logger.LogInfo("first-destination-probe");
        logger.Flush();
        settings.Current.DiagnosticLogDirectory = secondDestination;
        logger.LogInfo("second-destination-probe");
        logger.Flush();

        Assert.Contains(
            "first-destination-probe",
            File.ReadAllText(Path.Combine(firstDestination, DiagnosticLogPath.FileName)));
        Assert.Contains(
            "second-destination-probe",
            File.ReadAllText(Path.Combine(secondDestination, DiagnosticLogPath.FileName)));
    }

    [Fact]
    public void UnavailableConfiguredDirectory_UsesPrivateLocalFallback()
    {
        Directory.CreateDirectory(_testDirectory);
        var blockingFile = Path.Combine(_testDirectory, "not-a-directory");
        File.WriteAllText(blockingFile, "block");
        var destination = Path.Combine(blockingFile, "shared");
        var fallback = Path.Combine(_testDirectory, "fallback", "layoutfix.log");
        var settings = EnabledSettings(destination);
        using var logger = new FileLoggerService(settings, null, fallback);

        logger.LogInfo("fallback-probe");
        logger.Flush();

        var fallbackContents = File.ReadAllText(fallback);
        Assert.Contains("fallback-probe", fallbackContents);
        Assert.Contains("using private local fallback", fallbackContents);
        Assert.DoesNotContain(destination, fallbackContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DestinationChange_RecoversImmediatelyFromPreviousFailureCooldown()
    {
        Directory.CreateDirectory(_testDirectory);
        var blockingFile = Path.Combine(_testDirectory, "not-a-directory");
        File.WriteAllText(blockingFile, "block");
        var unavailableDestination = Path.Combine(blockingFile, "shared");
        var recoveredDestination = Path.Combine(_testDirectory, "recovered");
        var fallback = Path.Combine(_testDirectory, "fallback", "layoutfix.log");
        var settings = EnabledSettings(unavailableDestination);
        using var logger = new FileLoggerService(settings, null, fallback);

        logger.LogInfo("failed-destination-probe");
        logger.Flush();
        settings.Current.DiagnosticLogDirectory = recoveredDestination;
        logger.LogInfo("recovered-destination-probe");
        logger.Flush();

        var recoveredLog = Path.Combine(recoveredDestination, DiagnosticLogPath.FileName);
        Assert.Contains("recovered-destination-probe", File.ReadAllText(recoveredLog));
    }

    [Theory]
    [InlineData(@"relative\logs")]
    [InlineData("   ")]
    public void DirectoryValidation_RejectsRelativePathButAllowsEmptyDefault(string path)
    {
        var valid = DiagnosticLogPath.TryNormalizeDirectory(path, out var normalized);

        Assert.Equal(string.IsNullOrWhiteSpace(path), valid);
        Assert.Equal(string.Empty, normalized);
    }

    private static InMemorySettingsService EnabledSettings(string directory) => new()
    {
        Current = new AppSettings
        {
            LoggingEnabled = true,
            DiagnosticLogDirectory = directory
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }
}
