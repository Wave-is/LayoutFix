using System;
using Microsoft.Win32;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class AutoStartService : IAutoStartService
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LayoutFix";

    public bool IsAutoStartEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                var value = key?.GetValue(AppName) as string;
                return StartupCommandMatches(value, GetExecutablePath());
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey, true);

                if (value)
                {
                    key.SetValue(AppName, BuildStartupCommand(GetExecutablePath()));
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Windows startup registration could not be updated.",
                    exception);
            }
        }
    }

    internal static bool StartupCommandMatches(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(executablePath))
            return false;

        var trimmed = command.Trim();
        if (string.Equals(trimmed, executablePath, StringComparison.OrdinalIgnoreCase))
        {
            // Keep compatibility with old commands only when quoting is not
            // required. An unquoted Run path containing whitespace can be
            // parsed as a different executable and must be repaired.
            return !trimmed.Any(char.IsWhiteSpace);
        }
        if (trimmed.Length < 2 || trimmed[0] != '"')
            return false;

        var closingQuote = trimmed.IndexOf('"', 1);
        return closingQuote > 1 &&
               string.IsNullOrWhiteSpace(trimmed[(closingQuote + 1)..]) &&
               string.Equals(
            trimmed[1..closingQuote],
            executablePath,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildStartupCommand(string executablePath) =>
        $"\"{executablePath.Trim().Trim('"')}\"";

    private static string GetExecutablePath() => Environment.ProcessPath ?? string.Empty;
}
