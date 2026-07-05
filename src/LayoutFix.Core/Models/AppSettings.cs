using System.Collections.Generic;

namespace LayoutFix.Core.Models;

public class AppSettings
{
    public int Version { get; set; } = 3;
    public string HotkeyScheme { get; set; } = "PuntoClassic";

    public string AppTheme { get; set; } = "Dark";
    public string UiLanguage { get; set; } = "en";
    public string ConvertKey { get; set; } = "Pause";
    public string SwitchLayoutKey { get; set; } = "Shift+Pause";
    public string ChangeCaseKey { get; set; } = "Alt+Pause";
    public string TransliterateKey { get; set; } = "Ctrl+Alt+P";

    // Auto-translate settings
    public string TranslateLang1 { get; set; } = "en";
    public string TranslateLang2 { get; set; } = "ru";
    public string TranslateLang3 { get; set; } = "uk";
    
    public string OfflineModelType { get; set; } = "light"; // "light" or "pro"

    public List<HotkeyConfig> HotkeyConfigs { get; set; } = new List<HotkeyConfig>
    {
        // Set 1 (ScrollLock)
        new() { Action = "FixLayout", Hotkey = "Scroll", Preset = 1, Enabled = true },
        new() { Action = "FixLayout", Hotkey = "Shift+Scroll", Preset = 1, Enabled = true },
        new() { Action = "ChangeCase", Hotkey = "Alt+Scroll", Preset = 1, Enabled = true },
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+Scroll", Preset = 1, Enabled = true },
        // Set 2 (Pause)
        new() { Action = "FixLayout", Hotkey = "Pause", Preset = 2, Enabled = true },
        new() { Action = "FixLayout", Hotkey = "Shift+Pause", Preset = 2, Enabled = true },
        new() { Action = "ChangeCase", Hotkey = "Alt+Pause", Preset = 2, Enabled = true },
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+Pause", Preset = 2, Enabled = true },
        // Set 3 (Tilde `~`)
        new() { Action = "FixLayout", Hotkey = "Ctrl+`", Preset = 3, Enabled = true },
        new() { Action = "FixLayout", Hotkey = "Ctrl+Shift+`", Preset = 3, Enabled = true },
        new() { Action = "ChangeCase", Hotkey = "Alt+`", Preset = 3, Enabled = true },
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+`", Preset = 3, Enabled = true },
        new() { Action = "ConvertToUkrainian", Hotkey = "Ctrl+F8", Preset = 1, Enabled = false },
        new() { Action = "Translate1", Hotkey = "Alt+Shift+T", Preset = 1, Enabled = true },
        new() { Action = "Translate2", Hotkey = "Alt+T", Preset = 1, Enabled = true },
        new() { Action = "Translate3", Hotkey = "Ctrl+Alt+T", Preset = 1, Enabled = true },
        new() { Action = "OpenTranslator", Hotkey = "Ctrl+Shift+T", Preset = 1, Enabled = true }
    };

    public List<string> LayoutOrder { get; set; } = ["en-US", "ru-RU", "uk-UA"];
    public bool UseWindowsLayoutList { get; set; } = true;
    public string ScrollLockMode { get; set; } = "Smart";
    public bool SoundEnabled { get; set; } = false;
    public bool NotificationsEnabled { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public bool UseFlagIcons { get; set; } = true;
    public bool AutoConversionEnabled { get; set; } = true;
    public bool LoggingEnabled { get; set; } = false;
    public bool UseOfflineTranslation { get; set; } = false;
    public string TransliterationTable { get; set; } = "GOST";
    public List<string> BlacklistedProcesses { get; set; } = ["devenv.exe", "Code.exe", "idea64.exe"];
    public List<string> UserExceptions { get; set; } = new();
    public Dictionary<string, string> UserAutocorrect { get; set; } = new();
    public List<string> DisabledLanguages { get; set; } = new();
}
