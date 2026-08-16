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
        
        if (!PrintScreen)
        {
            if (!string.IsNullOrEmpty(Key)) parts.Add(Key);
            else parts.Add(VirtualKey.ToString());
        }
        
        return string.Join("+", parts);
    }

    public bool Matches(HotkeyCombo other)
    {
        if (Ctrl != other.Ctrl || Alt != other.Alt || Shift != other.Shift ||
            Win != other.Win || PrintScreen != other.PrintScreen)
        {
            return false;
        }

        if (VirtualKey != 0 && other.VirtualKey != 0)
            return VirtualKey == other.VirtualKey;

        return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
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
            else if (p.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) || p.Equals("PrtScn", StringComparison.OrdinalIgnoreCase))
            {
                combo.PrintScreen = true;
                combo.Key = "printscreen";
                combo.VirtualKey = 0x2C;
            }
            else 
            {
                combo.Key = p;
                var namedVirtualKey = MapStringToVk(p);
                if (namedVirtualKey != 0) combo.VirtualKey = namedVirtualKey;
                else if (int.TryParse(p, out int vk)) combo.VirtualKey = vk;
            }
        }
        return combo;
    }

    public static string GetCanonicalKeyName(int virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z')
            return ((char)virtualKey).ToString().ToLowerInvariant();
        if (virtualKey is >= '0' and <= '9')
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87)
            return "f" + (virtualKey - 0x70 + 1);
        if (virtualKey is >= 0x60 and <= 0x69)
            return "numpad" + (virtualKey - 0x60);

        return virtualKey switch
        {
            0x08 => "backspace",
            0x09 => "tab",
            0x0D => "enter",
            0x13 => "pause",
            0x14 => "capslock",
            0x1B => "esc",
            0x20 => "space",
            0x21 => "pageup",
            0x22 => "pagedown",
            0x23 => "end",
            0x24 => "home",
            0x25 => "left",
            0x26 => "up",
            0x27 => "right",
            0x28 => "down",
            0x2C => "printscreen",
            0x2D => "insert",
            0x2E => "delete",
            0x6A => "multiply",
            0x6B => "add",
            0x6C => "separator",
            0x6D => "subtract",
            0x6E => "decimal",
            0x6F => "divide",
            0x90 => "numlock",
            0x91 => "scroll",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0xE2 => "oem102",
            _ => string.Empty
        };
    }

    private static int MapStringToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                return (int)c;
        }
        var normalized = key.ToLowerInvariant();
        if (normalized.Length is 2 or 3 && normalized[0] == 'f' &&
            int.TryParse(normalized.AsSpan(1), out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            return 0x70 + functionNumber - 1;
        }
        if (normalized.Length == 7 &&
            normalized.StartsWith("numpad", StringComparison.Ordinal) &&
            normalized[6] is >= '0' and <= '9')
        {
            return 0x60 + normalized[6] - '0';
        }

        return normalized switch
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
            "pause" => 0x13,
            "capslock" => 0x14,
            "numlock" => 0x90,
            "scroll" or "scrolllock" => 0x91,
            "insert" => 0x2D,
            "multiply" => 0x6A,
            "add" => 0x6B,
            "separator" => 0x6C,
            "subtract" => 0x6D,
            "decimal" => 0x6E,
            "divide" => 0x6F,
            "`" or "oemtilde" => 0xC0,
            "-" or "oemminus" => 0xBD,
            "=" or "oemplus" => 0xBB,
            "," or "oemcomma" => 0xBC,
            "." or "oemperiod" => 0xBE,
            "/" or "oemquestion" => 0xBF,
            ";" or "oemsemicolon" => 0xBA,
            "'" or "oemquotes" => 0xDE,
            "[" or "oemopenbrackets" => 0xDB,
            "]" or "oemclosebrackets" => 0xDD,
            "\\" or "oempipe" => 0xDC,
            "oem102" or "oembackslash" => 0xE2,
            _ => 0
        };
    }
}
