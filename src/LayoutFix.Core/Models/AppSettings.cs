using System.Collections.Generic;

namespace LayoutFix.Core.Models;

public class AppSettings
{
    public const int CurrentVersion = 11;
    public static IReadOnlyList<string> DefaultAutoConversionBlacklistedProcesses { get; } =
    [
        "devenv.exe", "Code.exe", "Code - Insiders.exe", "VSCodium.exe",
        "Cursor.exe", "Windsurf.exe", "Antigravity.exe",
        "idea64.exe", "rider64.exe", "pycharm64.exe", "webstorm64.exe",
        "clion64.exe", "goland64.exe", "phpstorm64.exe", "rubymine64.exe",
        "datagrip64.exe", "fleet.exe", "studio64.exe", "eclipse.exe", "netbeans.exe",
        "WindowsTerminal.exe", "OpenConsole.exe", "conhost.exe", "cmd.exe", "wt.exe",
        "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wsl.exe", "bash.exe",
        "mintty.exe", "wezterm-gui.exe", "alacritty.exe", "Tabby.exe", "Hyper.exe",
        "ConEmu.exe", "ConEmu64.exe", "Cmder.exe", "kitty.exe", "putty.exe",
        "SecureCRT.exe", "MobaXterm.exe", "Termius.exe",
        "mstsc.exe", "msrdc.exe", "msrdcw.exe", "RemoteDesktop.exe", "wfica32.exe",
        "Photoshop.exe", "Illustrator.exe", "InDesign.exe", "Acrobat.exe",
        "AcroRd32.exe", "AfterFX.exe", "Adobe Premiere Pro.exe"
    ];

    public int Version { get; set; } = CurrentVersion;
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
    
    public string OfflineModelType { get; set; } = "light"; // "light", "alma", or "pro"

    public List<HotkeyConfig> HotkeyConfigs { get; set; } = new List<HotkeyConfig>
    {
        // Set 1 (ScrollLock)
        new() { Action = "FixLayout", Hotkey = "Scroll", Preset = 1, Enabled = true },
        new() { Action = "FixLayoutSelected", Hotkey = "Shift+Scroll", Preset = 1, Enabled = true },
        new() { Action = "ChangeCase", Hotkey = "Alt+Scroll", Preset = 1, Enabled = true },
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+Scroll", Preset = 1, Enabled = true },
        // Set 2 (Pause)
        new() { Action = "FixLayout", Hotkey = "Pause", Preset = 2, Enabled = true },
        new() { Action = "FixLayoutSelected", Hotkey = "Shift+Pause", Preset = 2, Enabled = true },
        new() { Action = "ChangeCase", Hotkey = "Alt+Pause", Preset = 2, Enabled = true },
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+Pause", Preset = 2, Enabled = true },
        // Set 3 (Tilde `~`)
        new() { Action = "FixLayout", Hotkey = "Ctrl+`", Preset = 3, Enabled = true },
        new() { Action = "FixLayoutSelected", Hotkey = "Ctrl+Shift+`", Preset = 3, Enabled = true },
        new() { Action = "ChangeCase", Hotkey = "Alt+`", Preset = 3, Enabled = true },
        new() { Action = "Transliterate", Hotkey = "Ctrl+Alt+`", Preset = 3, Enabled = true },
        new() { Action = "ConvertToUkrainian", Hotkey = "Ctrl+F8", Preset = 1, Enabled = false },
        new() { Action = "Translate1", Hotkey = "Alt+Shift+T", Preset = 1, Enabled = true },
        new() { Action = "Translate2", Hotkey = "Alt+T", Preset = 1, Enabled = true },
        new() { Action = "Translate3", Hotkey = "Ctrl+Alt+T", Preset = 1, Enabled = true },
        new() { Action = "OpenTranslator", Hotkey = "Ctrl+Shift+T", Preset = 1, Enabled = true },
        new() { Action = "Undo", Hotkey = "Ctrl+Shift+Backspace", Preset = 1, Enabled = true }
    };

    public List<string> LayoutOrder { get; set; } = ["en-US", "ru-RU", "uk-UA"];
    public bool UseWindowsLayoutList { get; set; } = true;
    public string ScrollLockMode { get; set; } = "Smart";
    public bool SoundEnabled { get; set; } = false;
    public bool NotificationsEnabled { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public bool UseFlagIcons { get; set; } = true;
    // Automatic correction modifies text while the user is typing. Keep it opt-in
    // until the current application and text target can be proven safe.
    public bool AutoConversionEnabled { get; set; } = false;
    public bool LoggingEnabled { get; set; } = false;
    public bool UseOfflineTranslation { get; set; } = false;
    public bool OnlineTranslationEnabled { get; set; } = false;
    public bool TranslationHistoryEnabled { get; set; } = false;
    public string TransliterationTable { get; set; } = "GOST";
    public List<string> BlacklistedProcesses { get; set; } = [];
    public List<string> AutoConversionBlacklistedProcesses { get; set; } =
        new(DefaultAutoConversionBlacklistedProcesses);
    public List<string> UserExceptions { get; set; } = new();
    public Dictionary<string, string> UserAutocorrect { get; set; } = new();
    public List<string> DisabledLanguages { get; set; } = new();
}
