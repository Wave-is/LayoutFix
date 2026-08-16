using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class TranslationHistoryService : ITranslationHistoryService
{
    private readonly ISettingsService _settingsService;
    private readonly string _historyFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<TranslationHistoryEntry> _historyCache = new();
    private bool _isLoaded = false;

    public TranslationHistoryService(ISettingsService settingsService, string? historyFilePath = null)
    {
        _settingsService = settingsService;
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayoutFix");
        _historyFilePath = Path.GetFullPath(historyFilePath ?? Path.Combine(appData, "translation_history.json"));
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;
        List<TranslationHistoryEntry> loadedHistory;
        try
        {
            string json = await File.ReadAllTextAsync(_historyFilePath);
            loadedHistory = JsonSerializer.Deserialize<List<TranslationHistoryEntry>>(json) ?? [];
        }
        catch (FileNotFoundException)
        {
            loadedHistory = [];
        }
        catch (DirectoryNotFoundException)
        {
            loadedHistory = [];
        }

        // Publish the cache only after a complete, valid read. A transient I/O
        // failure or malformed JSON must not be mistaken for an empty history,
        // otherwise the next write could replace recoverable user data.
        _historyCache = loadedHistory;
        _isLoaded = true;
    }

    private async Task SaveAsync()
    {
        var directory = Path.GetDirectoryName(_historyFilePath)!;
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(_historyCache, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = Path.Combine(directory, $".translation-history-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json);
            File.Move(temporaryPath, _historyFilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task AddEntryAsync(TranslationHistoryEntry entry)
    {
        if (!_settingsService.Current.TranslationHistoryEnabled) return;
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();

            // Prevent only exact consecutive duplicates. The same source may
            // legitimately produce a different translation after the user
            // changes source language, model or provider.
            if (_historyCache.Count > 0)
            {
                var last = _historyCache[0];
                if (string.Equals(last.SourceText, entry.SourceText, StringComparison.Ordinal) &&
                    string.Equals(last.TranslatedText, entry.TranslatedText, StringComparison.Ordinal) &&
                    string.Equals(last.SourceLang, entry.SourceLang, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(last.TargetLang, entry.TargetLang, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            _historyCache.Insert(0, CloneEntry(entry));
            TranslationHistoryEntry? evictedEntry = null;
            if (_historyCache.Count > 50)
            {
                evictedEntry = _historyCache[^1];
                _historyCache.RemoveAt(_historyCache.Count - 1); // Keep last 50
            }

            try
            {
                await SaveAsync();
            }
            catch
            {
                // Keep the memory view aligned with durable state so retrying
                // the same entry cannot be suppressed as a false duplicate.
                _historyCache.RemoveAt(0);
                if (evictedEntry is not null)
                    _historyCache.Add(evictedEntry);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<TranslationHistoryEntry>> GetHistoryAsync()
    {
        if (!_settingsService.Current.TranslationHistoryEnabled) return [];
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return _historyCache.Select(CloneEntry).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearHistoryAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // File.Delete is idempotent for a missing file, but not for a missing
            // parent directory. Treat both as the same empty durable state. Do not
            // use an existence probe or catch access errors: either could publish
            // an empty cache while a recoverable user file still survives.
            try
            {
                File.Delete(_historyFilePath);
            }
            catch (DirectoryNotFoundException)
            {
            }
            _historyCache.Clear();
            _isLoaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static TranslationHistoryEntry CloneEntry(TranslationHistoryEntry entry) => new()
    {
        Timestamp = entry.Timestamp,
        SourceText = entry.SourceText,
        TranslatedText = entry.TranslatedText,
        SourceLang = entry.SourceLang,
        TargetLang = entry.TargetLang
    };
}
