using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Infrastructure.Layouts;

public class WindowsLayoutProvider : IWindowsLayoutProvider
{
    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);
    
    private const uint ToUnicodeNoStateChange = 0x04;

    private static readonly IReadOnlyDictionary<uint, string> BaseKeys =
        new Dictionary<uint, string>
        {
            [0x30] = "0", [0x31] = "1", [0x32] = "2", [0x33] = "3", [0x34] = "4",
            [0x35] = "5", [0x36] = "6", [0x37] = "7", [0x38] = "8", [0x39] = "9",
            [0x41] = "a", [0x42] = "b", [0x43] = "c", [0x44] = "d", [0x45] = "e",
            [0x46] = "f", [0x47] = "g", [0x48] = "h", [0x49] = "i", [0x4A] = "j",
            [0x4B] = "k", [0x4C] = "l", [0x4D] = "m", [0x4E] = "n", [0x4F] = "o",
            [0x50] = "p", [0x51] = "q", [0x52] = "r", [0x53] = "s", [0x54] = "t",
            [0x55] = "u", [0x56] = "v", [0x57] = "w", [0x58] = "x", [0x59] = "y",
            [0x5A] = "z",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".",
            [0xBF] = "/", [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]",
            [0xDE] = "'"
        };

    public IReadOnlyList<LayoutFix.Core.Models.Layout> GetInstalledLayouts()
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count == 0) return Array.Empty<LayoutFix.Core.Models.Layout>();

        var list = new IntPtr[count];
        GetKeyboardLayoutList(count, list);

        var result = new List<LayoutFix.Core.Models.Layout>();
        byte[] state = new byte[256];
        byte[] shiftState = new byte[256];
        shiftState[0x10] = 0x80; // Shift
        System.Text.StringBuilder sb = new System.Text.StringBuilder(5);

        foreach (var hkl in list)
        {
            int lcid = (int)((long)hkl & 0xFFFF);
            try
            {
                var culture = new System.Globalization.CultureInfo(lcid);
                var layout = new LayoutFix.Core.Models.Layout
                {
                    Code = culture.Name,
                    Identifier = KeyboardLayoutIdentity.Create(culture.Name, hkl.ToInt64()),
                    DisplayName = culture.NativeName,
                    Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ShiftKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };

                foreach (var (vk, baseKey) in BaseKeys)
                {
                    // TOUNICODE_NO_STATE_CHANGE prevents dead keys from altering
                    // the keyboard buffer while the layout table is inspected.
                    sb.Clear();
                    Array.Clear(state, 0, state.Length);
                    if (ToUnicodeEx(
                            vk,
                            MapVirtualKeyEx(vk, 0, hkl),
                            state,
                            sb,
                            sb.Capacity,
                            ToUnicodeNoStateChange,
                            hkl) > 0)
                    {
                        layout.Keys[baseKey] = sb.ToString().ToLowerInvariant();
                    }

                    sb.Clear();
                    if (ToUnicodeEx(
                            vk,
                            MapVirtualKeyEx(vk, 0, hkl),
                            shiftState,
                            sb,
                            sb.Capacity,
                            ToUnicodeNoStateChange,
                            hkl) > 0)
                    {
                        layout.ShiftKeys[baseKey] = sb.ToString();
                    }
                }

                result.Add(layout);
            }
            catch
            {
                // Ignore unknown LCIDs
            }
        }
        return result;
    }
}
