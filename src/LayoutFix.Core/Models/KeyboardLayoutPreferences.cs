using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LayoutFix.Core.Models;

public static class KeyboardLayoutPreferences
{
    public static bool IsDisabled(Layout layout, IEnumerable<string> disabledEntries)
    {
        var aliases = GetAliases(layout);
        return disabledEntries.Any(disabled => aliases.Contains(disabled));
    }

    public static void Enable(AppSettings settings, Layout layout)
    {
        var aliases = GetAliases(layout);
        settings.DisabledLanguages.RemoveAll(disabled => aliases.Contains(disabled));
    }

    public static void Disable(AppSettings settings, Layout layout)
    {
        if (!settings.DisabledLanguages.Contains(
                layout.EffectiveIdentifier,
                StringComparer.OrdinalIgnoreCase))
        {
            settings.DisabledLanguages.Add(layout.EffectiveIdentifier);
        }
    }

    private static HashSet<string> GetAliases(Layout layout)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            layout.EffectiveIdentifier,
            layout.Code,
            KeyboardLayoutIdentity.GetCultureCode(layout.Code),
            KeyboardLayoutIdentity.GetLanguageCode(layout.Code)
        };

        if (!string.IsNullOrWhiteSpace(layout.DisplayName))
            aliases.Add(layout.DisplayName);

        try
        {
            aliases.Add(CultureInfo.GetCultureInfo(
                KeyboardLayoutIdentity.GetCultureCode(layout.Code)).EnglishName);
        }
        catch (CultureNotFoundException)
        {
        }

        aliases.RemoveWhere(string.IsNullOrWhiteSpace);
        return aliases;
    }
}
