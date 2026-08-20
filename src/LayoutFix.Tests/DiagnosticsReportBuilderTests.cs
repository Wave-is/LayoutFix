using System.Diagnostics;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

[CollectionDefinition(ProcessHandleIsolationCollection.Name, DisableParallelization = true)]
public sealed class ProcessHandleIsolationCollection
{
    public const string Name = "Process handle isolation";
}

[Collection(ProcessHandleIsolationCollection.Name)]
public class DiagnosticsReportBuilderTests
{
    [Fact]
    public void Build_ContainsCompatibilityMetadataAndConfigurationCounts()
    {
        var settings = new AppSettings
        {
            AutoConversionEnabled = true,
            BlacklistedProcesses = ["one.exe", "two.exe"],
            AutoConversionBlacklistedProcesses = ["three.exe"],
            UserExceptions = ["word"],
            UserAutocorrect = new Dictionary<string, string> { ["from"] = "to" },
            UseOfflineTranslation = true,
            OfflineModelType = "pro",
            NotificationsEnabled = false,
            LoggingEnabled = true
        };

        var report = DiagnosticsReportBuilder.Build(settings, "1.2.3");

        Assert.Contains("LayoutFix diagnostics report", report);
        Assert.Contains("AppVersion=1.2.3", report);
        Assert.Contains($"SettingsSchema={AppSettings.CurrentVersion}", report);
        Assert.Contains("OSVersion=", report);
        Assert.Contains("OSArchitecture=", report);
        Assert.Contains("ProcessArchitecture=", report);
        Assert.Contains("Framework=", report);
        Assert.Contains("Elevated=", report);
        Assert.Contains("ProcessIntegrity=", report);
        Assert.Contains("ElevatedTargetInput=", report);
        Assert.Contains("RemoteSession=", report);
        Assert.Contains("MonitorCount=", report);
        Assert.Contains("SystemDpi=", report);
        Assert.Contains("MonitorScaleMode=", report);
        Assert.Contains("MonitorScaleRange=", report);
        Assert.Contains("DpiAwareness=", report);
        Assert.Contains("InstalledKeyboardLayouts=", report);
        Assert.Contains("GlobalProcessExclusions=2", report);
        Assert.Contains("AutoCorrectionProcessExclusions=1", report);
        Assert.Contains("UserWordExceptions=1", report);
        Assert.Contains("UserReplacements=1", report);
        Assert.Contains("OfflineTranslationEnabled=True", report);
        Assert.Contains("OfflineModel=pro", report);
        Assert.Contains("DiagnosticNotificationsEnabled=False", report);
        Assert.Contains("LoggingEnabled=True", report);
    }

    [Fact]
    public void Build_DoesNotExposeUserContentPathsCredentialsOrCustomModelText()
    {
        const string secret = "DO_NOT_EXPORT_SECRET_DOCUMENT_API_KEY";
        var settings = new AppSettings
        {
            UiLanguage = secret,
            BlacklistedProcesses = [$@"C:\Users\Someone\{secret}.exe"],
            AutoConversionBlacklistedProcesses = [secret],
            UserExceptions = [secret],
            UserAutocorrect = new Dictionary<string, string> { [secret] = secret },
            OfflineModelType = secret
        };

        var report = DiagnosticsReportBuilder.Build(settings, "1.0.12");

        Assert.DoesNotContain(secret, report, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GlobalProcessExclusions=1", report);
        Assert.Contains("AutoCorrectionProcessExclusions=1", report);
        Assert.Contains("UserWordExceptions=1", report);
        Assert.Contains("UserReplacements=1", report);
        Assert.Contains("OfflineModel=custom-or-unknown", report);
    }

    [Fact]
    public void Build_WithMixedDpiSnapshot_ReportsBoundedCompatibilityMetadata()
    {
        var snapshot = new DiagnosticsCompatibilitySnapshot(
            Elevated: false,
            RemoteSession: true,
            ProcessIntegrity: ProcessIntegrityLevel.Medium,
            DpiAwareness: DpiAwarenessLevel.PerMonitorV2,
            MonitorCount: 2,
            SystemDpi: 96,
            MonitorScaleFactors: [100, 150]);

        var report = DiagnosticsReportBuilder.Build(new AppSettings(), "1.0.12", snapshot);

        Assert.Contains("Elevated=False", report);
        Assert.Contains("ProcessIntegrity=medium", report);
        Assert.Contains("ElevatedTargetInput=blocked-when-target-elevated", report);
        Assert.Contains("RemoteSession=True", report);
        Assert.Contains("MonitorCount=2", report);
        Assert.Contains("SystemDpi=96", report);
        Assert.Contains("MonitorScaleMode=mixed", report);
        Assert.Contains("MonitorScaleRange=100-150", report);
        Assert.Contains("DpiAwareness=per-monitor-v2", report);

        var unknownReport = DiagnosticsReportBuilder.Build(
            new AppSettings(),
            "1.0.12",
            snapshot with
            {
                Elevated = null,
                RemoteSession = null,
                MonitorCount = 0,
                SystemDpi = 0,
                MonitorScaleFactors = []
            });
        Assert.Contains("Elevated=unknown", unknownReport);
        Assert.Contains("ElevatedTargetInput=unknown", unknownReport);
        Assert.Contains("RemoteSession=unknown", unknownReport);
        Assert.Contains("MonitorCount=unknown", unknownReport);
        Assert.Contains("SystemDpi=unknown", unknownReport);
        Assert.Contains("MonitorScaleMode=unknown", unknownReport);
        Assert.Contains("MonitorScaleRange=unknown", unknownReport);
    }

    [Fact]
    public void Capture_OnWindows_ReturnsPlausibleDisplayAndIntegrityMetadata()
    {
        var snapshot = WindowsCompatibilityProbe.Capture();

        Assert.InRange(snapshot.MonitorCount, 1, 64);
        Assert.InRange(snapshot.SystemDpi, 48u, 960u);
        Assert.Equal(snapshot.MonitorCount, snapshot.MonitorScaleFactors.Count);
        Assert.All(snapshot.MonitorScaleFactors, scale => Assert.InRange(scale, 100, 500));
        Assert.NotEqual(ProcessIntegrityLevel.Unknown, snapshot.ProcessIntegrity);
        Assert.NotEqual(DpiAwarenessLevel.Unknown, snapshot.DpiAwareness);
        Assert.True(WindowsCompatibilityProbe.CanSendInputToProcess((uint)Environment.ProcessId));
        Assert.False(WindowsCompatibilityProbe.CanSendInputToProcess(4));
    }

    [Theory]
    [InlineData(0x2000u, 0x1000u, true)]
    [InlineData(0x2000u, 0x2000u, true)]
    [InlineData(0x2000u, 0x3000u, false)]
    [InlineData(0x3000u, 0x2000u, true)]
    [InlineData(null, 0x2000u, false)]
    [InlineData(0x2000u, null, false)]
    public void IntegrityPolicy_AllowsOnlyKnownEqualOrLowerTargets(
        uint? currentRid,
        uint? targetRid,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsCompatibilityProbe.IsTargetIntegrityAllowed(currentRid, targetRid));
    }

    [Theory]
    [InlineData(0x2000u, 0x1000u, (int)TargetInputAccess.Allowed)]
    [InlineData(0x2000u, 0x2000u, (int)TargetInputAccess.Allowed)]
    [InlineData(0x2000u, 0x3000u, (int)TargetInputAccess.HigherIntegrity)]
    [InlineData(null, 0x2000u, (int)TargetInputAccess.Unavailable)]
    [InlineData(0x2000u, null, (int)TargetInputAccess.Unavailable)]
    public void IntegrityPolicy_ReportsPrivacySafeFailureReason(
        uint? currentRid,
        uint? targetRid,
        int expectedValue)
    {
        Assert.Equal(
            (TargetInputAccess)expectedValue,
            WindowsCompatibilityProbe.ClassifyTargetInputAccess(currentRid, targetRid));
    }

    [Fact]
    public void IntegrityPreflight_ConcurrentStress_DoesNotLeakProcessHandles()
    {
        const int iterations = 2_048;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var handlesBefore = process.HandleCount;
        var stopwatch = Stopwatch.StartNew();

        Parallel.For(0, iterations, _ =>
        {
            Assert.True(
                WindowsCompatibilityProbe.CanSendInputToProcess((uint)Environment.ProcessId));
        });

        stopwatch.Stop();
        process.Refresh();
        var handlesAfter = process.HandleCount;
        Assert.True(
            handlesAfter <= handlesBefore + 64,
            $"Integrity preflight leaked handles: before={handlesBefore}, after={handlesAfter}.");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Integrity preflight stress was too slow: {stopwatch.Elapsed}.");
    }
}
