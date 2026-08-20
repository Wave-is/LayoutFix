using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Services;
using System.Collections.Concurrent;

namespace LayoutFix.IntegrationTests;

public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.Tests.{Guid.NewGuid():N}");

    public SettingsPersistenceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void Save_IsAtomicAndPersistsCurrentSettingsVersion()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "profile", "settings.json");
        var service = new SettingsService(settingsPath);

        service.Current.UiLanguage = "ru";
        service.Current.AutoConversionEnabled = true;
        service.Current.NotificationsEnabled = false;
        service.Current.LoggingEnabled = true;
        service.Save(service.Current);

        var reloaded = new SettingsService(settingsPath);

        Assert.Equal("ru", reloaded.Current.UiLanguage);
        Assert.True(reloaded.Current.AutoConversionEnabled);
        Assert.False(reloaded.Current.NotificationsEnabled);
        Assert.True(reloaded.Current.LoggingEnabled);
        Assert.Equal(AppSettings.CurrentVersion, reloaded.Current.Version);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentSaves_PublishTheSameWinnerInMemoryAndOnDisk()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "concurrent", "settings.json");
        var service = new SettingsService(settingsPath);

        for (var round = 0; round < 8; round++)
        {
            using var startGate = new ManualResetEventSlim(initialState: false);
            var failures = new ConcurrentQueue<Exception>();
            var saves = Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            {
                var marker = $"round-{round:D2}-save-{index:D2}";
                var settings = new AppSettings
                {
                    UiLanguage = marker,
                    UserExceptions = Enumerable.Range(0, 128)
                        .Select(item => $"{marker}-item-{item:D3}")
                        .ToList()
                };
                startGate.Wait();
                try
                {
                    service.Save(settings);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            })).ToArray();

            startGate.Set();
            await Task.WhenAll(saves);

            Assert.Empty(failures);
            var durable = new SettingsService(settingsPath).Current;
            Assert.Equal(service.Current.UiLanguage, durable.UiLanguage);
            Assert.Equal(service.Current.UserExceptions, durable.UserExceptions);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
        }
    }

    [Fact]
    public void CorruptProfile_WithBackup_RestoresPreviousUserRevision()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "recover", "settings.json");
        var service = new SettingsService(settingsPath);

        service.Current.UiLanguage = "uk";
        service.Current.AutoConversionEnabled = true;
        service.Save(service.Current);
        service.Current.UserExceptions.Add("latest-unsaved-by-backup-revision");
        service.Save(service.Current);

        var backupPath = $"{settingsPath}.bak";
        Assert.True(File.Exists(backupPath));
        File.WriteAllText(settingsPath, "{ truncated-profile");

        var recovered = new SettingsService(settingsPath);

        Assert.Equal("uk", recovered.Current.UiLanguage);
        Assert.True(recovered.Current.AutoConversionEnabled);
        Assert.DoesNotContain(
            "latest-unsaved-by-backup-revision",
            recovered.Current.UserExceptions);
        Assert.Equal(AppSettings.CurrentVersion, recovered.Current.Version);
        Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(settingsPath)!,
            "settings.json.corrupt-*"));
        Assert.NotNull(new SettingsService(settingsPath).Current);
    }

    [Fact]
    public void AtomicReplaceFailure_PreservesCurrentProfileAndRemovesTemporaryFile()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "locked", "settings.json");
        var service = new SettingsService(settingsPath);
        service.Current.UiLanguage = "ru";
        service.Save(service.Current);

        using (var lockedProfile = new FileStream(
            settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
        {
            service.Current.UiLanguage = "uk";
            Assert.Throws<IOException>(() => service.Save(service.Current));
        }

        var reloaded = new SettingsService(settingsPath);
        Assert.Equal("ru", reloaded.Current.UiLanguage);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
    }

    [Fact]
    public void LockedBackupReplaceFailure_PreservesBothDurableRevisionsAndRemovesTemporaryFile()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "locked-backup", "settings.json");
        var backupPath = $"{settingsPath}.bak";
        var service = new SettingsService(settingsPath);
        service.Current.UiLanguage = "en";
        service.Save(service.Current);
        service.Current.UiLanguage = "ru";
        service.Save(service.Current);

        using (var lockedBackup = new FileStream(
                   backupPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            service.Current.UiLanguage = "uk";
            Assert.Throws<IOException>(() => service.Save(service.Current));
        }

        Assert.Equal("ru", new SettingsService(settingsPath).Current.UiLanguage);
        Assert.Equal("en", new SettingsService(backupPath).Current.UiLanguage);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
    }

    [Fact]
    public void TemporarilyLockedProfile_UsesBackupWithoutQuarantineOrOverwrite()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "load-locked", "settings.json");
        var service = new SettingsService(settingsPath);
        service.Current.UiLanguage = "ru";
        service.Save(service.Current);
        service.Current.UiLanguage = "uk";
        service.Save(service.Current);
        var primaryJson = File.ReadAllText(settingsPath);

        SettingsService fallback;
        using (var lockedProfile = new FileStream(
            settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
        {
            fallback = new SettingsService(settingsPath);

            Assert.Equal("ru", fallback.Current.UiLanguage);
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(settingsPath)!,
                "settings.json.corrupt-*"));
        }

        fallback.Current.SoundEnabled = !fallback.Current.SoundEnabled;
        Assert.Throws<IOException>(() => fallback.Save(fallback.Current));
        Assert.Equal(primaryJson, File.ReadAllText(settingsPath));
        Assert.Equal("uk", new SettingsService(settingsPath).Current.UiLanguage);
    }

    [Fact]
    public void UnavailableBackupAfterCorruption_BlocksDefaultOverwriteUntilRecoveryReload()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "corrupt-backup-locked", "settings.json");
        var backupPath = $"{settingsPath}.bak";
        var service = new SettingsService(settingsPath);
        service.Current.UiLanguage = "uk";
        service.Current.UserExceptions.Add("recover-me");
        service.Save(service.Current);
        service.Current.UiLanguage = "ru";
        service.Save(service.Current);
        File.WriteAllText(settingsPath, "{ corrupt-active");

        SettingsService unavailableRecovery;
        using (var lockedBackup = new FileStream(
                   backupPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            unavailableRecovery = new SettingsService(settingsPath);
            Assert.Empty(unavailableRecovery.Current.UserExceptions);
        }

        unavailableRecovery.Current.UiLanguage = "en";
        Assert.Throws<IOException>(() => unavailableRecovery.Save(unavailableRecovery.Current));

        Assert.False(File.Exists(settingsPath));
        Assert.Equal("uk", new SettingsService(backupPath).Current.UiLanguage);
        Assert.Contains("recover-me", new SettingsService(backupPath).Current.UserExceptions);

        var recovered = new SettingsService(settingsPath);
        Assert.Equal("uk", recovered.Current.UiLanguage);
        Assert.Contains("recover-me", recovered.Current.UserExceptions);
        Assert.Equal("uk", new SettingsService(settingsPath).Current.UiLanguage);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
    }

    [Fact]
    public void FreshProfile_KeepsAutomaticConversionDisabled()
    {
        var service = new SettingsService(Path.Combine(_temporaryDirectory, "fresh", "settings.json"));

        Assert.False(service.Current.AutoConversionEnabled);
        Assert.Empty(service.Current.BlacklistedProcesses);
        Assert.Contains("Code.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("Antigravity.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("rider64.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("WindowsTerminal.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("pwsh.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("mstsc.exe", service.Current.AutoConversionBlacklistedProcesses);
    }

    [Fact]
    public void Version4Profile_MovesLegacyIdeDefaultsToAutoConversionBlacklist()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v4", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 4,
            BlacklistedProcesses = ["devenv.exe", "Code.exe", "idea64.exe", "mygame.exe"],
            AutoConversionBlacklistedProcesses = []
        };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(oldSettings));

        var service = new SettingsService(settingsPath);

        Assert.Equal(["mygame.exe"], service.Current.BlacklistedProcesses);
        Assert.Contains("devenv.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("Code.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("idea64.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Equal(AppSettings.CurrentVersion, service.Current.Version);
    }

    [Fact]
    public void Version5Profile_AddsUndoShortcutWithoutChangingExistingHotkeys()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v5", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 5,
            HotkeyConfigs =
            [
                new HotkeyConfig
                {
                    Action = nameof(HotkeyAction.FixLayout),
                    Hotkey = "F12",
                    Preset = 1,
                    Enabled = true
                }
            ]
        };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(oldSettings));

        var service = new SettingsService(settingsPath);

        Assert.Contains(service.Current.HotkeyConfigs, config =>
            config.Action == nameof(HotkeyAction.FixLayout) && config.Hotkey == "F12");
        Assert.Contains(service.Current.HotkeyConfigs, config =>
            config.Action == nameof(HotkeyAction.Undo) &&
            config.Hotkey == "Ctrl+Shift+Backspace" &&
            config.Enabled);
    }

    [Fact]
    public void Version7Profile_MigratesShiftLayoutShortcutsToSelectedTextAction()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v7", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 7,
            HotkeyConfigs =
            [
                new HotkeyConfig { Action = "FixLayout", Hotkey = "Pause", Preset = 2, Enabled = true },
                new HotkeyConfig { Action = "FixLayout", Hotkey = "Shift+Pause", Preset = 2, Enabled = true }
            ]
        };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(oldSettings));

        var service = new SettingsService(settingsPath);

        Assert.Contains(service.Current.HotkeyConfigs, config =>
            config.Action == nameof(HotkeyAction.FixLayout) && config.Hotkey == "Pause");
        Assert.Contains(service.Current.HotkeyConfigs, config =>
            config.Action == nameof(HotkeyAction.FixLayoutSelected) && config.Hotkey == "Shift+Pause");
    }

    [Fact]
    public void Version8Profile_AddsAdobeSafetyDefaultsWithoutRemovingUserEntries()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v8", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 8,
            AutoConversionBlacklistedProcesses = ["my-editor.exe", "photoshop.exe"],
            UserExceptions = ["projectname"]
        };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(oldSettings));

        var service = new SettingsService(settingsPath);

        Assert.Contains("my-editor.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("Photoshop.exe", service.Current.AutoConversionBlacklistedProcesses, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AfterFX.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("Adobe Premiere Pro.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Equal(
            1,
            service.Current.AutoConversionBlacklistedProcesses.Count(process =>
                string.Equals(process, "Photoshop.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("projectname", service.Current.UserExceptions);
        Assert.Equal(AppSettings.CurrentVersion, service.Current.Version);
    }

    [Fact]
    public void Version9Profile_AddsCurrentIdeSafetyDefaultsWithoutRemovingUserEntries()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v9", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 9,
            AutoConversionBlacklistedProcesses = ["my-editor.exe", "code.exe"],
            UserExceptions = ["projectname"]
        };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(oldSettings));

        var service = new SettingsService(settingsPath);

        Assert.Contains("my-editor.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("Antigravity.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("Cursor.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("rider64.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Equal(
            1,
            service.Current.AutoConversionBlacklistedProcesses.Count(process =>
                string.Equals(process, "Code.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("projectname", service.Current.UserExceptions);
        Assert.Equal(AppSettings.CurrentVersion, service.Current.Version);
    }

    [Fact]
    public void Version10Profile_AddsTerminalSafetyDefaultsWithoutRemovingUserEntries()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v10", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 10,
            AutoConversionBlacklistedProcesses = ["my-terminal.exe", "PWSH.exe"],
            UserExceptions = ["projectname"]
        };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(oldSettings));

        var service = new SettingsService(settingsPath);

        Assert.Contains("my-terminal.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("WindowsTerminal.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("conhost.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Contains("mstsc.exe", service.Current.AutoConversionBlacklistedProcesses);
        Assert.Equal(
            1,
            service.Current.AutoConversionBlacklistedProcesses.Count(process =>
                string.Equals(process, "pwsh.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("projectname", service.Current.UserExceptions);
        Assert.Equal(AppSettings.CurrentVersion, service.Current.Version);
    }

    [Fact]
    public void LockedVersion10Profile_LoadsSafeMigrationInMemoryWithoutOverwritingDurableFile()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "v10-locked", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var oldSettings = new AppSettings
        {
            Version = 10,
            UiLanguage = "uk",
            AutoConversionBlacklistedProcesses = ["my-terminal.exe"],
            UserExceptions = ["projectname"]
        };
        var durableJson = System.Text.Json.JsonSerializer.Serialize(oldSettings);
        File.WriteAllText(settingsPath, durableJson);

        using (var readableButNotReplaceable = new FileStream(
                   settingsPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            var service = new SettingsService(settingsPath);

            Assert.Equal(AppSettings.CurrentVersion, service.Current.Version);
            Assert.Equal("uk", service.Current.UiLanguage);
            Assert.Contains("my-terminal.exe", service.Current.AutoConversionBlacklistedProcesses);
            Assert.Contains("WindowsTerminal.exe", service.Current.AutoConversionBlacklistedProcesses);
            Assert.Contains("mstsc.exe", service.Current.AutoConversionBlacklistedProcesses);
            Assert.Contains("projectname", service.Current.UserExceptions);
            Assert.Equal(durableJson, File.ReadAllText(settingsPath));
        }

        Assert.Equal(durableJson, File.ReadAllText(settingsPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
    }

    [Fact]
    public void LegacyProfile_IsMigratedWithoutResettingUserChoices()
    {
        var legacyPath = Path.Combine(_temporaryDirectory, "legacy", "settings.json");
        var legacy = new SettingsService(legacyPath);
        legacy.Current.UiLanguage = "uk";
        legacy.Current.SoundEnabled = true;
        legacy.Save(legacy.Current);

        var newPath = Path.Combine(_temporaryDirectory, "local", "settings.json");
        var migrated = new SettingsService(newPath, legacyPath);

        Assert.Equal("uk", migrated.Current.UiLanguage);
        Assert.True(migrated.Current.SoundEnabled);
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public void CorruptProfile_IsQuarantinedAndReplacedWithSafeDefaults()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "corrupt", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{ definitely-not-json }");

        var service = new SettingsService(settingsPath);

        Assert.False(service.Current.AutoConversionEnabled);
        Assert.True(File.Exists(settingsPath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "settings.json.corrupt-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }
}

public sealed class FileLoggerServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.LoggerTests.{Guid.NewGuid():N}");

    [Fact]
    public void Logger_DoesNotCreateAFileWhenDiagnosticsAreDisabled()
    {
        var logPath = Path.Combine(_temporaryDirectory, "layoutfix.log");
        var settings = new InMemorySettingsService { Current = new AppSettings { LoggingEnabled = false } };
        var logger = new FileLoggerService(settings, logPath);

        logger.LogInfo("sensitive text must not be written");

        Assert.False(File.Exists(logPath));
    }

    [Fact]
    public void Logger_WritesOnlyAfterDiagnosticsAreExplicitlyEnabled()
    {
        var logPath = Path.Combine(_temporaryDirectory, "layoutfix.log");
        var settings = new InMemorySettingsService { Current = new AppSettings { LoggingEnabled = true } };
        var logger = new FileLoggerService(settings, logPath);

        logger.LogInfo("operation completed");

        Assert.Contains("operation completed", File.ReadAllText(logPath));
    }

    [Fact]
    public void Logger_DoesNotPersistExceptionMessageOrStackThatMayContainUserText()
    {
        var logPath = Path.Combine(_temporaryDirectory, "layoutfix.log");
        var settings = new InMemorySettingsService { Current = new AppSettings { LoggingEnabled = true } };
        var logger = new FileLoggerService(settings, logPath);

        logger.LogError(
            "translation failed",
            new InvalidOperationException("SECRET clipboard text at C:\\Private\\document.txt"));

        var log = File.ReadAllText(logPath);
        Assert.Contains("translation failed", log);
        Assert.Contains("System.InvalidOperationException", log);
        Assert.Contains("HResult", log);
        Assert.DoesNotContain("SECRET", log);
        Assert.DoesNotContain("document.txt", log);
    }

    [Fact]
    public void Logger_RedactsAbsoluteWindowsPathsFromDirectMessages()
    {
        var logPath = Path.Combine(_temporaryDirectory, "layoutfix.log");
        var settings = new InMemorySettingsService { Current = new AppSettings { LoggingEnabled = true } };
        var logger = new FileLoggerService(settings, logPath);

        logger.LogInfo(
            @"Loading model (C:\Users\Private Person\Sensitive Project\private-model.gguf). " +
            @"Fallback \\private-server\secret-share\customer-name.bin");

        var log = File.ReadAllText(logPath);
        Assert.Contains("Loading model", log);
        Assert.Contains("[absolute-path-redacted]", log);
        Assert.DoesNotContain("Private Person", log);
        Assert.DoesNotContain("Sensitive Project", log);
        Assert.DoesNotContain("private-model.gguf", log);
        Assert.DoesNotContain("private-server", log);
        Assert.DoesNotContain("customer-name.bin", log);
    }

    [Fact]
    public void Logger_RotatesAfterLogGrowsDuringCurrentProcess()
    {
        var logPath = Path.Combine(_temporaryDirectory, "layoutfix.log");
        var backupPath = logPath + ".bak";
        var settings = new InMemorySettingsService { Current = new AppSettings { LoggingEnabled = true } };
        var logger = new FileLoggerService(settings, logPath);

        logger.LogInfo("initialize logger");
        using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            stream.SetLength(5L * 1024 * 1024 + 1);

        logger.LogInfo("written after rotation");

        Assert.True(File.Exists(backupPath));
        Assert.True(new FileInfo(backupPath).Length > 5L * 1024 * 1024);
        var activeLog = File.ReadAllText(logPath);
        Assert.Contains("written after rotation", activeLog);
        Assert.DoesNotContain("initialize logger", activeLog);
    }

    [Fact]
    public void ConcurrentLoggerInstances_DoNotSilentlyDropEntries()
    {
        var logPath = Path.Combine(_temporaryDirectory, "concurrent-layoutfix.log");
        var settings = new InMemorySettingsService { Current = new AppSettings { LoggingEnabled = true } };
        var loggers = Enumerable.Range(0, 4)
            .Select(_ => new FileLoggerService(settings, logPath))
            .ToArray();

        try
        {
            Parallel.For(0, 2_000, index =>
                loggers[index % loggers.Length].LogInfo($"concurrent-marker-{index:D4}"));

            var markers = File.ReadLines(logPath)
                .Where(line => line.Contains("concurrent-marker-", StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf("concurrent-marker-", StringComparison.Ordinal))..])
                .ToArray();
            Assert.Equal(2_000, markers.Length);
            Assert.Equal(2_000, markers.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            foreach (var logger in loggers)
                logger.Dispose();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }
}
