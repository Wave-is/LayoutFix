using System;
using System.Collections.Generic;
using System.Linq;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public class KeyboardLayoutManager : IKeyboardLayoutManager
{
    private readonly ISettingsService _settingsService;
    private readonly IWindowsLayoutProvider _windowsLayoutProvider;
    private readonly List<Layout> _layouts = [];

    public KeyboardLayoutManager(ISettingsService settingsService, IWindowsLayoutProvider windowsLayoutProvider)
    {
        _settingsService = settingsService;
        _windowsLayoutProvider = windowsLayoutProvider;
    }

    public void LoadAll()
    {
        _layouts.Clear();
        var installed = _windowsLayoutProvider.GetInstalledLayouts();
        foreach (var layout in installed)
        {
            if (string.IsNullOrWhiteSpace(layout.Code) ||
                _layouts.Any(existing => string.Equals(
                    existing.EffectiveIdentifier,
                    layout.EffectiveIdentifier,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _layouts.Add(layout);
        }
    }

    public IReadOnlyList<Layout> GetInstalledWindowsLayouts()
    {
        return _layouts.ToList();
    }

    public IReadOnlyList<Layout> GetLayoutOrder()
    {
        var settings = _settingsService.Current;
        var order = settings.LayoutOrder ?? new List<string>();
        var disabled = settings.DisabledLanguages ?? [];

        var result = new List<Layout>();
        foreach (var identifierOrCode in order)
        {
            var layout = _layouts.FirstOrDefault(candidate => string.Equals(
                             candidate.EffectiveIdentifier,
                             identifierOrCode,
                             StringComparison.OrdinalIgnoreCase) &&
                             !KeyboardLayoutPreferences.IsDisabled(candidate, disabled)) ??
                         _layouts.FirstOrDefault(candidate => string.Equals(
                             candidate.Code,
                             KeyboardLayoutIdentity.GetCultureCode(identifierOrCode),
                             StringComparison.OrdinalIgnoreCase) &&
                             !KeyboardLayoutPreferences.IsDisabled(candidate, disabled));
            if (layout != null && result.All(existing => !string.Equals(
                    existing.EffectiveIdentifier,
                    layout.EffectiveIdentifier,
                    StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(layout);
            }
        }

        if (settings.UseWindowsLayoutList)
        {
            foreach (var layout in _layouts)
            {
                if (KeyboardLayoutPreferences.IsDisabled(layout, disabled) ||
                    result.Any(existing => KeyboardLayoutIdentity.SameCulture(
                        existing.Code,
                        layout.Code)))
                {
                    continue;
                }

                result.Add(layout);
            }
        }

        return result;
    }

    public IReadOnlyList<Layout> GetLayoutOrder(string? activeLayoutIdentifier)
    {
        var ordered = GetLayoutOrder();
        if (string.IsNullOrWhiteSpace(activeLayoutIdentifier))
            return ordered;

        var active = _layouts.FirstOrDefault(layout => string.Equals(
                         layout.EffectiveIdentifier,
                         activeLayoutIdentifier,
                         StringComparison.OrdinalIgnoreCase)) ??
                     _layouts.FirstOrDefault(layout => KeyboardLayoutIdentity.SameCulture(
                         layout.Code,
                         activeLayoutIdentifier));
        if (active == null)
            return ordered;

        if (KeyboardLayoutPreferences.IsDisabled(
                active,
                _settingsService.Current.DisabledLanguages ?? []))
        {
            return [];
        }

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
