using System.Collections.Generic;

namespace LayoutFix.Core.Models;

public class AppSettings
{
    public int Version { get; set; } = 3;
    public string HotkeyScheme { get; set; } = "PuntoClassic";

    public List<HotkeyConfig> HotkeyConfigs { get; set; } = new()
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
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+`", Preset = 3, Enabled = true }
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
    public string TransliterationTable { get; set; } = "GOST";
    public List<string> BlacklistedProcesses { get; set; } = ["devenv.exe", "Code.exe", "idea64.exe"];
    public List<string> UserExceptions { get; set; } = new();
    public Dictionary<string, string> UserAutocorrect { get; set; } = new();
}
