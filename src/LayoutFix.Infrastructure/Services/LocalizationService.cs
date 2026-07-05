using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class LocalizationService : ILocalizationService
{
    private Dictionary<string, string> _strings = new();

    public LocalizationService()
    {
        LoadLocalization();
    }

    public void SetCulture(string culture)
    {
        LoadLocalization(culture);
    }

    private void LoadLocalization(string? currentCulture = null)
    {
        if (string.IsNullOrEmpty(currentCulture))
        {
            currentCulture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        }
        string localesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales");
        
        // Try to load current culture, fallback to 'en', then fallback to whatever exists
        string path = Path.Combine(localesDir, $"{currentCulture}.json");
        if (!File.Exists(path)) path = Path.Combine(localesDir, "en.json");
        if (!File.Exists(path))
        {
            var files = Directory.Exists(localesDir) ? Directory.GetFiles(localesDir, "*.json") : Array.Empty<string>();
            if (files.Length > 0) path = files[0];
        }

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    _strings = dict;
                }
            }
            catch { }
        }
    }

    public string GetString(string key, string defaultValue)
    {
        if (_strings.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }
        return defaultValue;
    }
}
