namespace LayoutFix.Core.Models;

public class HotkeyConfig
{
    public string Action { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public int Preset { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}
