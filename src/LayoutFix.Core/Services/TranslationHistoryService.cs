using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class TranslationHistoryService : ITranslationHistoryService
{
    private readonly string _historyFilePath;
    private List<TranslationHistoryEntry> _historyCache = new();
    private bool _isLoaded = false;

    public TranslationHistoryService()
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LayoutFix");
        if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
        _historyFilePath = Path.Combine(appData, "translation_history.json");
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;
        if (File.Exists(_historyFilePath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(_historyFilePath);
                _historyCache = JsonSerializer.Deserialize<List<TranslationHistoryEntry>>(json) ?? new List<TranslationHistoryEntry>();
            }
            catch
            {
                _historyCache = new List<TranslationHistoryEntry>();
            }
        }
        _isLoaded = true;
    }

    private async Task SaveAsync()
    {
        string json = JsonSerializer.Serialize(_historyCache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_historyFilePath, json);
    }

    public async Task AddEntryAsync(TranslationHistoryEntry entry)
    {
        await EnsureLoadedAsync();
        
        // Prevent exact duplicates consecutively
        if (_historyCache.Count > 0)
        {
            var last = _historyCache[0];
            if (last.SourceText == entry.SourceText && last.TargetLang == entry.TargetLang)
                return;
        }

        _historyCache.Insert(0, entry);
        if (_historyCache.Count > 50) _historyCache.RemoveAt(_historyCache.Count - 1); // Keep last 50
        
        await SaveAsync();
    }

    public async Task<List<TranslationHistoryEntry>> GetHistoryAsync()
    {
        await EnsureLoadedAsync();
        return new List<TranslationHistoryEntry>(_historyCache);
    }

    public async Task ClearHistoryAsync()
    {
        _historyCache.Clear();
        await SaveAsync();
    }
}
