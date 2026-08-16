using System;
using System.Collections.Generic;
using System.Linq;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface IKeyboardLayoutManager
{
    void LoadAll();
    IReadOnlyList<Layout> GetInstalledWindowsLayouts();
    IReadOnlyList<Layout> GetLayoutOrder();

    IReadOnlyList<Layout> GetLayoutOrder(string? activeLayoutIdentifier)
    {
        var ordered = GetLayoutOrder();
        if (string.IsNullOrWhiteSpace(activeLayoutIdentifier))
            return ordered;

        var installed = GetInstalledWindowsLayouts();
        var active = installed.FirstOrDefault(layout => string.Equals(
                         layout.EffectiveIdentifier,
                         activeLayoutIdentifier,
                         StringComparison.OrdinalIgnoreCase)) ??
                     installed.FirstOrDefault(layout => KeyboardLayoutIdentity.SameCulture(
                         layout.Code,
                         activeLayoutIdentifier));
        if (active == null)
            return ordered;

        var replaceIndex = Enumerable.Range(0, ordered.Count)
            .FirstOrDefault(
                index => KeyboardLayoutIdentity.SameCulture(
                    ordered[index].Code,
                    active.Code),
                -1);
        if (replaceIndex < 0 || string.Equals(
                ordered[replaceIndex].EffectiveIdentifier,
                active.EffectiveIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            return ordered;
        }

        var result = ordered.ToList();
        result[replaceIndex] = active;
        return result;
    }
}
