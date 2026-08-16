using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using LayoutFix.Core.Models;

namespace LayoutFix.Infrastructure.Services;

public static class DiagnosticsReportBuilder
{
    public static string Build(AppSettings settings, string appVersion)
        => Build(settings, appVersion, WindowsCompatibilityProbe.Capture());

    internal static string Build(
        AppSettings settings,
        string appVersion,
        DiagnosticsCompatibilitySnapshot compatibility)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(compatibility);

        var report = new StringBuilder();
        report.AppendLine("LayoutFix diagnostics report");
        Append(report, "GeneratedUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Append(report, "AppVersion", SafeRuntimeValue(appVersion));
        Append(report, "SettingsSchema", settings.Version.ToString(CultureInfo.InvariantCulture));
        Append(report, "OSDescription", SafeRuntimeValue(RuntimeInformation.OSDescription));
        Append(report, "OSVersion", SafeRuntimeValue(Environment.OSVersion.VersionString));
        Append(report, "OSArchitecture", RuntimeInformation.OSArchitecture.ToString());
        Append(report, "ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
        Append(report, "Framework", SafeRuntimeValue(RuntimeInformation.FrameworkDescription));
        Append(report, "Elevated", BoolValue(compatibility.Elevated));
        Append(report, "ProcessIntegrity", EnumValue(compatibility.ProcessIntegrity));
        Append(
            report,
            "ElevatedTargetInput",
            compatibility.Elevated switch
            {
                true => "same-or-lower-integrity",
                false => "blocked-when-target-elevated",
                null => "unknown"
            });
        Append(report, "RemoteSession", BoolValue(compatibility.RemoteSession));
        Append(
            report,
            "MonitorCount",
            compatibility.MonitorCount <= 0
                ? "unknown"
                : compatibility.MonitorCount.ToString(CultureInfo.InvariantCulture));
        Append(report, "SystemDpi", DpiValue(compatibility.SystemDpi));
        Append(report, "MonitorScaleMode", MonitorScaleMode(compatibility));
        Append(report, "MonitorScaleRange", MonitorScaleRange(compatibility));
        Append(report, "DpiAwareness", EnumValue(compatibility.DpiAwareness));
        Append(report, "InstalledKeyboardLayouts", InputLanguage.InstalledInputLanguages.Count.ToString(CultureInfo.InvariantCulture));
        Append(report, "CurrentUiCulture", SafeRuntimeValue(CultureInfo.CurrentUICulture.Name));
        Append(report, "ConfiguredLayoutOrder", Count(settings.LayoutOrder));
        Append(report, "DisabledLayouts", Count(settings.DisabledLanguages));
        Append(report, "AutoConversionEnabled", settings.AutoConversionEnabled.ToString(CultureInfo.InvariantCulture));
        Append(report, "GlobalProcessExclusions", Count(settings.BlacklistedProcesses));
        Append(report, "AutoCorrectionProcessExclusions", Count(settings.AutoConversionBlacklistedProcesses));
        Append(report, "UserWordExceptions", Count(settings.UserExceptions));
        Append(report, "UserReplacements", Count(settings.UserAutocorrect));
        Append(report, "OfflineTranslationEnabled", settings.UseOfflineTranslation.ToString(CultureInfo.InvariantCulture));
        Append(report, "OfflineModel", KnownOfflineModel(settings.OfflineModelType));
        Append(report, "OnlineTranslationEnabled", settings.OnlineTranslationEnabled.ToString(CultureInfo.InvariantCulture));
        Append(report, "TranslationHistoryEnabled", settings.TranslationHistoryEnabled.ToString(CultureInfo.InvariantCulture));
        Append(report, "LoggingEnabled", settings.LoggingEnabled.ToString(CultureInfo.InvariantCulture));
        return report.ToString();
    }

    private static string Count<T>(ICollection<T>? values) =>
        (values?.Count ?? 0).ToString(CultureInfo.InvariantCulture);

    private static string KnownOfflineModel(string? value) => value?.ToLowerInvariant() switch
    {
        "light" => "light",
        "pro" => "pro",
        "alma" => "alma",
        _ => "custom-or-unknown"
    };

    private static string MonitorScaleMode(DiagnosticsCompatibilitySnapshot compatibility)
    {
        if (compatibility.MonitorCount <= 0 ||
            compatibility.MonitorScaleFactors.Count != compatibility.MonitorCount ||
            compatibility.MonitorScaleFactors.Any(scale => scale is < 100 or > 500))
        {
            return "unknown";
        }

        return compatibility.MonitorScaleFactors.Distinct().Skip(1).Any()
            ? "mixed"
            : "uniform";
    }

    private static string MonitorScaleRange(DiagnosticsCompatibilitySnapshot compatibility) =>
        MonitorScaleMode(compatibility) == "unknown"
            ? "unknown"
            : $"{compatibility.MonitorScaleFactors.Min().ToString(CultureInfo.InvariantCulture)}-" +
              compatibility.MonitorScaleFactors.Max().ToString(CultureInfo.InvariantCulture);

    private static string DpiValue(uint value) =>
        value == 0 ? "unknown" : value.ToString(CultureInfo.InvariantCulture);

    private static string BoolValue(bool? value) => value switch
    {
        true => bool.TrueString,
        false => bool.FalseString,
        null => "unknown"
    };

    private static string EnumValue<T>(T value) where T : struct, Enum =>
        value.ToString().Replace("PerMonitorV2", "per-monitor-v2", StringComparison.Ordinal)
            .Replace("PerMonitor", "per-monitor", StringComparison.Ordinal)
            .Replace("UnawareGdiScaled", "unaware-gdi-scaled", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string SafeRuntimeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('=', '-');
    }

    private static void Append(StringBuilder report, string name, string value) =>
        report.Append(name).Append('=').AppendLine(value);
}
