using System.IO;
using LayoutFix.Core.Models;

namespace LayoutFix.Infrastructure.Services;

public static class DiagnosticLogPath
{
    public const string FileName = "layoutfix.log";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LayoutFix",
        "Logs");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, FileName);

    public static bool TryNormalizeDirectory(
        string? configuredDirectory,
        out string normalizedDirectory)
    {
        normalizedDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredDirectory))
            return true;

        try
        {
            var candidate = configuredDirectory.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(candidate))
                return false;

            normalizedDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(candidate));
            return !string.IsNullOrWhiteSpace(normalizedDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedDirectory = string.Empty;
            return false;
        }
    }

    public static string ResolveFilePath(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return TryNormalizeDirectory(settings.DiagnosticLogDirectory, out var directory) &&
               !string.IsNullOrEmpty(directory)
            ? Path.Combine(directory, FileName)
            : DefaultFilePath;
    }
}
