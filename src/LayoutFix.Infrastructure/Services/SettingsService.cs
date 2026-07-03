using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Infrastructure.Services;

[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext
{
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath = "settings.json";
    private AppSettings _current;

    public AppSettings Current => _current;

    public SettingsService()
    {
        _current = Load();
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            _current = new AppSettings();
            Save(_current);
            return _current;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);
            if (loaded != null && loaded.Version < 2)
            {
                _current = new AppSettings { Version = 2 };
                Save(_current);
                return _current;
            }
            _current = loaded ?? new AppSettings();
            return _current;
        }
        catch
        {
            _current = new AppSettings();
            return _current;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings);
            File.WriteAllText(_settingsFilePath, json);
            _current = settings;
        }
        catch
        {
            // Logging will be added in later phases
        }
    }
}
