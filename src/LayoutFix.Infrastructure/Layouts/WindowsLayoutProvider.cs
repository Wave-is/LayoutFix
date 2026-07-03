using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Layouts;

public class WindowsLayoutProvider : IWindowsLayoutProvider
{
    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);
    
    [DllImport("user32.dll")]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    public IReadOnlyList<LayoutFix.Core.Models.Layout> GetInstalledLayouts()
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count == 0) return Array.Empty<LayoutFix.Core.Models.Layout>();

        var list = new IntPtr[count];
        GetKeyboardLayoutList(count, list);

        // Load US layout for base keys mapping
        IntPtr usHkl = LoadKeyboardLayout("00000409", 0);

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
                    DisplayName = culture.NativeName,
                    Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ShiftKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };

                // Letters, numbers, and OEM keys
                var vksToMap = new List<uint>();
                for (uint vk = 0x30; vk <= 0x39; vk++) vksToMap.Add(vk); // 0-9
                for (uint vk = 0x41; vk <= 0x5A; vk++) vksToMap.Add(vk); // A-Z
                uint[] oemVks = { 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE };
                vksToMap.AddRange(oemVks);

                foreach (uint vk in vksToMap)
                {
                    // Get base key from US layout
                    sb.Clear();
                    Array.Clear(state, 0, state.Length);
                    if (ToUnicodeEx(vk, MapVirtualKeyEx(vk, 0, usHkl), state, sb, sb.Capacity, 0, usHkl) > 0)
                    {
                        string baseKey = sb.ToString().ToLower();

                        // Unshifted character in target layout
                        sb.Clear();
                        Array.Clear(state, 0, state.Length);
                        if (ToUnicodeEx(vk, MapVirtualKeyEx(vk, 0, hkl), state, sb, sb.Capacity, 0, hkl) > 0)
                        {
                            layout.Keys[baseKey] = sb.ToString().ToLower();
                        }

                        // Shifted character in target layout
                        sb.Clear();
                        if (ToUnicodeEx(vk, MapVirtualKeyEx(vk, 0, hkl), shiftState, sb, sb.Capacity, 0, hkl) > 0)
                        {
                            layout.ShiftKeys[baseKey] = sb.ToString();
                        }
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
