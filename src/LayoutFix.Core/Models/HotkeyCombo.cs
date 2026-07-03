using System;
using System.Collections.Generic;

namespace LayoutFix.Core.Models;

public class HotkeyCombo
{
    public int VirtualKey { get; set; }
    public string Key { get; set; } = string.Empty;
    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }
    public bool PrintScreen { get; set; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        if (PrintScreen) parts.Add("PrintScreen");
        
        if (!string.IsNullOrEmpty(Key)) parts.Add(Key);
        else parts.Add(VirtualKey.ToString());
        
        return string.Join("+", parts);
    }

    public static HotkeyCombo Parse(string value)
    {
        var combo = new HotkeyCombo();
        if (string.IsNullOrWhiteSpace(value)) return combo;

        var parts = value.Split('+');
        foreach(var part in parts)
        {
            var p = part.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) combo.Ctrl = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) combo.Alt = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) combo.Shift = true;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) combo.Win = true;
            else if (p.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) || p.Equals("PrtScn", StringComparison.OrdinalIgnoreCase)) combo.PrintScreen = true;
            else 
            {
                combo.Key = p;
                if (int.TryParse(p, out int vk)) combo.VirtualKey = vk;
                else combo.VirtualKey = MapStringToVk(p);
            }
        }
        return combo;
    }

    private static int MapStringToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                return (int)c;
        }
        return key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" => 0x0D,
            "esc" => 0x1B,
            "tab" => 0x09,
            "backspace" => 0x08,
            "delete" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            _ => 0
        };
    }
}
