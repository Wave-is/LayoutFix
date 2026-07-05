using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class DictionaryAnalyzer : IDictionaryAnalyzer
{
    private readonly Dictionary<string, HashSet<string>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILayoutConverter _layoutConverter;
    private readonly IKeyboardLayoutManager _layoutManager;
    private readonly ISettingsService _settingsService;

    public DictionaryAnalyzer(ILayoutConverter layoutConverter, IKeyboardLayoutManager layoutManager, ISettingsService settingsService)
    {
        _layoutConverter = layoutConverter;
        _layoutManager = layoutManager;
        _settingsService = settingsService;
    }

    private void EnsureDictionaryLoaded(string langCode)
    {
        if (_dictionaries.ContainsKey(langCode)) return;

        var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Exceptions for short words
        if (langCode == "ru" || langCode == "uk")
        {
            var ruExceptions = new[] { "но", "не", "да", "он", "мы", "вы", "ты", "же", "то", "за", "на", "по", "до", "из", "от", "об", "со", "ко", "их", "им" };
            foreach (var w in ruExceptions) hashSet.Add(w);
        }
        else if (langCode == "en")
        {
            var enExceptions = new[] { "hi", "no", "ok", "to", "in", "is", "it", "if", "of", "on", "or", "as", "at", "by", "do", "go", "me", "my", "so", "up", "us", "we", "he", "be", "am", "an" };
            foreach (var w in enExceptions) hashSet.Add(w);
        }

        string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries", $"{langCode}.txt");
        if (File.Exists(file))
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var word = line.Split(' ')[0].Trim(); // Handle cases where there is a frequency number after space
                        if (!string.IsNullOrEmpty(word)) hashSet.Add(word);
                    }
                }
            }
            catch { }
        }

        _dictionaries[langCode] = hashSet;
    }

    public bool IsGibberish(string word, string currentLayout)
    {
        if (word.Length < 2) return false;
        
        string lower = word.ToLower();

        var settings = _settingsService.Current;
        if (settings.UserExceptions.Contains(lower)) return false;

        if (IsValidInLayout(lower, currentLayout)) return false;

        var activeLayouts = _layoutManager.GetLayoutOrder();
        var sourceLayoutObj = activeLayouts.FirstOrDefault(l => l.Code.StartsWith(currentLayout.Substring(0, 2), StringComparison.OrdinalIgnoreCase));
        
        if (sourceLayoutObj == null) return false;

        foreach (var targetLayout in activeLayouts)
        {
            if (targetLayout.Code.Equals(sourceLayoutObj.Code, StringComparison.OrdinalIgnoreCase)) continue;
            
            string converted = _layoutConverter.ConvertTo(lower, targetLayout, sourceLayoutObj);
            
            if (IsValidInLayout(converted, targetLayout.Code))
            {
                return true; 
            }
        }

        return false;
    }

    private bool IsValidInLayout(string word, string layoutCode)
    {
        string lang = layoutCode.Length >= 2 ? layoutCode.Substring(0, 2).ToLower() : layoutCode.ToLower();
        EnsureDictionaryLoaded(lang);
        if (_dictionaries.TryGetValue(lang, out var dict))
        {
            return dict.Contains(word);
        }
        return false;
    }
}
