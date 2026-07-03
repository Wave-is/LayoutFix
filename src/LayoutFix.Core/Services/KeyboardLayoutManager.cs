using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public class KeyboardLayoutManager : IKeyboardLayoutManager
{
    private readonly ISettingsService _settingsService;
    private readonly IWindowsLayoutProvider _windowsLayoutProvider;
    private readonly Dictionary<string, Layout> _layouts = new(StringComparer.OrdinalIgnoreCase);

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
            _layouts[layout.Code] = layout;
        }
    }

    public IReadOnlyList<Layout> GetInstalledWindowsLayouts()
    {
        return _layouts.Values.ToList();
    }

    public IReadOnlyList<Layout> GetLayoutOrder()
    {
        var settings = _settingsService.Current;
        var order = settings.LayoutOrder ?? new List<string>();
        
        var result = new List<Layout>();
        foreach (var code in order)
        {
            if (_layouts.TryGetValue(code, out var layout))
            {
                result.Add(layout);
            }
        }
        return result;
    }
}
