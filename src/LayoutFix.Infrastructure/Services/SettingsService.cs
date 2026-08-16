using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Infrastructure.Services;

[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext
{
}

public class SettingsService : ISettingsService
{
    private enum SettingsFileStatus
    {
        Missing,
        Loaded,
        Invalid,
        Unavailable
    }

    private readonly record struct SettingsFileRead(
        SettingsFileStatus Status,
        AppSettings? Settings = null);

    private readonly string _settingsFilePath;
    private readonly string _backupSettingsFilePath;
    private readonly string? _legacySettingsFilePath;
    private readonly bool _lookForDefaultLegacyFiles;
    private readonly object _persistenceGate = new();
    private AppSettings _current;
    private bool _writesBlockedUntilRecoveryReload;

    public AppSettings Current => _current;

    public SettingsService(string? settingsFilePath = null, string? legacySettingsFilePath = null)
    {
        _lookForDefaultLegacyFiles = string.IsNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = Path.GetFullPath(settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LayoutFix",
            "settings.json"));
        _backupSettingsFilePath = $"{_settingsFilePath}.bak";
        _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
            ? null
            : Path.GetFullPath(legacySettingsFilePath);
        _current = Load();
    }

    public AppSettings Load()
    {
        lock (_persistenceGate)
            return LoadCore();
    }

    private AppSettings LoadCore()
    {
        _writesBlockedUntilRecoveryReload = false;
        var primary = ReadSettingsFile(_settingsFilePath);
        if (primary.Status == SettingsFileStatus.Loaded)
        {
            _current = primary.Settings!;
            if (_current.Version != AppSettings.CurrentVersion)
            {
                _current.Version = AppSettings.CurrentVersion;
                try
                {
                    Save(_current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A readable profile remains usable when an antivirus scan,
                    // another process, or ACL temporarily prevents File.Replace.
                    // Keep the normalized safety migration in memory and leave the
                    // durable previous revision untouched; a later explicit save
                    // can persist it through the normal retry/error path.
                }
            }
            return _current;
        }

        var backup = ReadSettingsFile(_backupSettingsFilePath);
        if (primary.Status == SettingsFileStatus.Unavailable)
        {
            // A sharing violation or ACL problem is not evidence of corruption.
            // Use a known-good backup in memory when possible, but do not rename or
            // overwrite either file while the primary profile is unavailable.
            _writesBlockedUntilRecoveryReload = true;
            _current = CurrentVersion(backup.Settings ?? new AppSettings());
            return _current;
        }

        if (primary.Status == SettingsFileStatus.Missing)
        {
            if (backup.Status == SettingsFileStatus.Unavailable)
            {
                _writesBlockedUntilRecoveryReload = true;
                _current = new AppSettings();
                return _current;
            }

            _current = backup.Settings ?? TryLoadLegacySettings() ?? new AppSettings();
            Save(_current);
            return _current;
        }

        _current = new AppSettings();
        if (!TryQuarantineCorruptSettings())
        {
            _writesBlockedUntilRecoveryReload = true;
            return _current;
        }

        if (backup.Status == SettingsFileStatus.Unavailable)
        {
            _writesBlockedUntilRecoveryReload = true;
            return _current;
        }

        _current = backup.Settings ?? _current;
        Save(_current);
        return _current;
    }

    public void Save(AppSettings settings)
    {
        lock (_persistenceGate)
            SaveCore(settings);
    }

    private void SaveCore(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_writesBlockedUntilRecoveryReload)
        {
            throw new IOException(
                "Settings recovery is pending; reload the durable profile before saving.");
        }

        settings.Version = AppSettings.CurrentVersion;
        var directory = Path.GetDirectoryName(_settingsFilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_settingsFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings);
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(_settingsFilePath))
            {
                // Keep the previous known-good revision while atomically replacing
                // the active profile. If a later write or external tool corrupts
                // settings.json, Load can restore this revision without discarding
                // all user preferences.
                File.Replace(
                    temporaryPath,
                    _settingsFilePath,
                    _backupSettingsFilePath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _settingsFilePath);
            }
            _current = settings;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private AppSettings? TryLoadLegacySettings()
    {
        var candidates = new List<string>();
        if (_legacySettingsFilePath != null)
        {
            candidates.Add(_legacySettingsFilePath);
        }
        else if (_lookForDefaultLegacyFiles)
        {
            candidates.Add(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "settings.json")));
            candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "settings.json")));
        }

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(path, _settingsFilePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                continue;

            var read = ReadSettingsFile(path);
            if (read.Status == SettingsFileStatus.Loaded)
                return read.Settings;
        }

        return null;
    }

    private static SettingsFileRead ReadSettingsFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize(
                json,
                AppSettingsContext.Default.AppSettings);
            return loaded == null
                ? new SettingsFileRead(SettingsFileStatus.Invalid)
                : new SettingsFileRead(SettingsFileStatus.Loaded, Normalize(loaded));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new SettingsFileRead(SettingsFileStatus.Invalid);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SettingsFileRead(SettingsFileStatus.Missing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsFileRead(SettingsFileStatus.Unavailable);
        }
    }

    private static AppSettings CurrentVersion(AppSettings settings)
    {
        settings.Version = AppSettings.CurrentVersion;
        return settings;
    }

    private bool TryQuarantineCorruptSettings()
    {
        try
        {
            var quarantinePath = $"{_settingsFilePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(_settingsFilePath, quarantinePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.HotkeyConfigs ??= defaults.HotkeyConfigs;
        settings.LayoutOrder ??= defaults.LayoutOrder;
        settings.BlacklistedProcesses ??= defaults.BlacklistedProcesses;
        settings.AutoConversionBlacklistedProcesses ??= defaults.AutoConversionBlacklistedProcesses;
        settings.UserExceptions ??= defaults.UserExceptions;
        settings.UserAutocorrect ??= defaults.UserAutocorrect;
        settings.DisabledLanguages ??= defaults.DisabledLanguages;

        if (settings.Version < 5)
        {
            // Earlier versions globally disabled every manual hotkey in common code
            // editors. Keep only automatic correction disabled there; manual actions
            // are explicit and safe to attempt.
            string[] legacyGlobalDefaults = ["devenv.exe", "Code.exe", "idea64.exe"];
            settings.BlacklistedProcesses.RemoveAll(configured => legacyGlobalDefaults.Any(legacy =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(configured),
                    Path.GetFileNameWithoutExtension(legacy),
                    StringComparison.OrdinalIgnoreCase)));

            foreach (var process in legacyGlobalDefaults)
            {
                if (!settings.AutoConversionBlacklistedProcesses.Contains(process, StringComparer.OrdinalIgnoreCase))
                    settings.AutoConversionBlacklistedProcesses.Add(process);
            }
        }

        if (settings.Version < 6 &&
            !settings.HotkeyConfigs.Any(config =>
                string.Equals(config.Action, nameof(HotkeyAction.Undo), StringComparison.OrdinalIgnoreCase)))
        {
            settings.HotkeyConfigs.Add(new HotkeyConfig
            {
                Action = nameof(HotkeyAction.Undo),
                Hotkey = "Ctrl+Shift+Backspace",
                Preset = 1,
                Enabled = true
            });
        }

        if (settings.Version < 8)
        {
            foreach (var config in settings.HotkeyConfigs.Where(config =>
                         string.Equals(config.Action, nameof(HotkeyAction.FixLayout), StringComparison.OrdinalIgnoreCase) &&
                         HotkeyCombo.Parse(config.Hotkey).Shift))
            {
                config.Action = nameof(HotkeyAction.FixLayoutSelected);
            }
        }

        if (settings.Version < 9)
        {
            // Automatic correction in Adobe editors remains an unverified release
            // gate. Older persisted profiles predate this defensive default, so an
            // upgrade must not silently leave them less safe than a fresh install.
            string[] adobeDefaults =
            [
                "Photoshop.exe", "Illustrator.exe", "InDesign.exe", "Acrobat.exe",
                "AcroRd32.exe", "AfterFX.exe", "Adobe Premiere Pro.exe"
            ];
            foreach (var process in adobeDefaults)
            {
                if (!settings.AutoConversionBlacklistedProcesses.Contains(
                        process,
                        StringComparer.OrdinalIgnoreCase))
                {
                    settings.AutoConversionBlacklistedProcesses.Add(process);
                }
            }
        }

        if (settings.Version < 11)
        {
            // Schema-v10 introduced current IDE safety defaults. Schema-v11 adds
            // terminal and remote-session clients: Enter can execute a command
            // before asynchronous analysis completes, so rollback/backspace must
            // never reach the next prompt or a remote transport. Keep all custom
            // entries and exceptions; manual hotkeys remain available.
            foreach (var process in AppSettings.DefaultAutoConversionBlacklistedProcesses)
            {
                if (!settings.AutoConversionBlacklistedProcesses.Contains(
                        process,
                        StringComparer.OrdinalIgnoreCase))
                {
                    settings.AutoConversionBlacklistedProcesses.Add(process);
                }
            }
        }
        return settings;
    }
}
