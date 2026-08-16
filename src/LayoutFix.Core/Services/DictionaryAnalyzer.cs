using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public class DictionaryAnalyzer : IDictionaryAnalyzer
{
    private const long MinimumShortWordFrequency = 1_000;
    private const int MaximumShortWordRankWithoutFrequency = 5_000;
    private const double MinimumRelativeFrequencyMargin = 2.0;
    private readonly ConcurrentDictionary<string, Lazy<DictionaryEntry>> _dictionaries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILayoutConverter _layoutConverter;
    private readonly IKeyboardLayoutManager _layoutManager;
    private readonly ISettingsService _settingsService;
    private readonly string _dictionaryDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _transientFailureRetryDelay;

    public DictionaryAnalyzer(ILayoutConverter layoutConverter, IKeyboardLayoutManager layoutManager, ISettingsService settingsService)
        : this(
            layoutConverter,
            layoutManager,
            settingsService,
            Path.Combine(AppContext.BaseDirectory, "Dictionaries"),
            TimeProvider.System,
            TimeSpan.FromSeconds(1))
    {
    }

    private DictionaryAnalyzer(
        ILayoutConverter layoutConverter,
        IKeyboardLayoutManager layoutManager,
        ISettingsService settingsService,
        string dictionaryDirectory,
        TimeProvider timeProvider,
        TimeSpan transientFailureRetryDelay)
    {
        _layoutConverter = layoutConverter;
        _layoutManager = layoutManager;
        _settingsService = settingsService;
        _dictionaryDirectory = dictionaryDirectory ?? throw new ArgumentNullException(nameof(dictionaryDirectory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (transientFailureRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(transientFailureRetryDelay));
        _transientFailureRetryDelay = transientFailureRetryDelay;
    }

    public static DictionaryAnalyzer CreateForDirectory(
        ILayoutConverter layoutConverter,
        IKeyboardLayoutManager layoutManager,
        ISettingsService settingsService,
        string dictionaryDirectory) =>
        new(
            layoutConverter,
            layoutManager,
            settingsService,
            dictionaryDirectory,
            TimeProvider.System,
            TimeSpan.FromSeconds(1));

    internal static DictionaryAnalyzer CreateForDirectory(
        ILayoutConverter layoutConverter,
        IKeyboardLayoutManager layoutManager,
        ISettingsService settingsService,
        string dictionaryDirectory,
        TimeProvider timeProvider,
        TimeSpan transientFailureRetryDelay) =>
        new(
            layoutConverter,
            layoutManager,
            settingsService,
            dictionaryDirectory,
            timeProvider,
            transientFailureRetryDelay);

    public void WarmUp()
    {
        var languages = _layoutManager
            .GetLayoutOrder()
            .Select(layout => LanguagePart(layout.Code))
            .Where(language => language.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Parallel.ForEach(languages, language => _ = GetDictionary(language));
    }

    private DictionaryEntry GetDictionary(string langCode)
    {
        while (true)
        {
            var lazy = _dictionaries.GetOrAdd(
                langCode,
                code => new Lazy<DictionaryEntry>(
                    () => LoadDictionary(code),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            var dictionary = lazy.Value;
            if (dictionary.RetryAfterUtc is not { } retryAfterUtc ||
                _timeProvider.GetUtcNow() < retryAfterUtc)
            {
                return dictionary;
            }

            var exactEntry = new KeyValuePair<string, Lazy<DictionaryEntry>>(
                langCode,
                lazy);
            if (((ICollection<KeyValuePair<string, Lazy<DictionaryEntry>>>)_dictionaries)
                .Remove(exactEntry))
            {
                continue;
            }
        }
    }

    private DictionaryEntry LoadDictionary(string langCode)
    {
        var hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frequencies = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

        string file = Path.Combine(_dictionaryDirectory, $"{langCode}.txt");
        if (!File.Exists(file))
            return new DictionaryEntry(
                hashSet,
                frequencies,
                ranks,
                0,
                IsAvailable: false,
                RetryAfterUtc: null);

        try
        {
            var fileRank = 0;
            foreach (var line in File.ReadLines(file))
            {
                foreach (var entry in ParseDictionaryLine(line))
                {
                    hashSet.Add(entry.Word);
                    if (entry.CanInferRank)
                    {
                        fileRank++;
                        if (!entry.Frequency.HasValue)
                            ranks.TryAdd(entry.Word, fileRank);
                    }
                    if (entry.Frequency.HasValue &&
                        (!frequencies.TryGetValue(entry.Word, out var previous) ||
                         entry.Frequency.Value > previous))
                    {
                        frequencies[entry.Word] = entry.Frequency.Value;
                    }
                }
            }
        }
        catch (IOException)
        {
            return CreateTransientFailure(hashSet, frequencies, ranks);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateTransientFailure(hashSet, frequencies, ranks);
        }

        return new DictionaryEntry(
            hashSet,
            frequencies,
            ranks,
            frequencies.Values.Sum(),
            IsAvailable: true,
            RetryAfterUtc: null);
    }

    private DictionaryEntry CreateTransientFailure(
        HashSet<string> words,
        Dictionary<string, long> frequencies,
        Dictionary<string, int> ranks) =>
        new(
            words,
            frequencies,
            ranks,
            0,
            IsAvailable: false,
            RetryAfterUtc: _timeProvider.GetUtcNow() + _transientFailureRetryDelay);

    public bool IsGibberish(string word, string currentLayout) =>
        TryGetCorrection(word, currentLayout, out _);

    public bool TryGetCorrection(
        string word,
        string currentLayout,
        out LayoutCorrectionSuggestion suggestion)
    {
        suggestion = default;
        if (word.Length < 2) return false;
        
        string lower = word.ToLowerInvariant();

        var settings = _settingsService.Current;
        if (settings.UserExceptions.Any(exception =>
            string.Equals(exception, lower, StringComparison.OrdinalIgnoreCase))) return false;

        var sourceLanguage = LanguagePart(currentLayout);
        if (string.IsNullOrEmpty(sourceLanguage)) return false;
        var sourceDictionary = GetDictionary(sourceLanguage);
        if (!sourceDictionary.IsAvailable) return false;

        if (sourceDictionary.Words.Contains(lower)) return false;

        var activeLayouts = _layoutManager.GetLayoutOrder(currentLayout);
        var sourceLayoutObj = activeLayouts.FirstOrDefault(layout =>
                                  string.Equals(
                                      layout.EffectiveIdentifier,
                                      currentLayout,
                                      StringComparison.OrdinalIgnoreCase)) ??
                              activeLayouts.FirstOrDefault(layout =>
                                  string.Equals(layout.Code, currentLayout, StringComparison.OrdinalIgnoreCase)) ??
                              activeLayouts.FirstOrDefault(layout =>
                                  string.Equals(LanguagePart(layout.Code), sourceLanguage, StringComparison.OrdinalIgnoreCase));
        
        if (sourceLayoutObj == null) return false;
        if (!IsCompatibleWithSourceLayout(word, sourceLayoutObj)) return false;

        var candidates = new List<CandidateEvidence>();
        foreach (var targetLayout in activeLayouts)
        {
            if (targetLayout.Code.Equals(sourceLayoutObj.Code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(LanguagePart(targetLayout.Code), sourceLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var converted = _layoutConverter.ConvertTo(word, targetLayout, sourceLayoutObj);

            if (TryGetCandidateConfidence(
                    converted,
                    targetLayout.Code,
                    out var isConfidentForAutomaticCorrection,
                    out var relativeFrequency))
            {
                candidates.Add(new CandidateEvidence(
                    new LayoutCorrectionSuggestion(
                        converted,
                        targetLayout.EffectiveIdentifier,
                        isConfidentForAutomaticCorrection),
                    relativeFrequency));
            }
        }

        if (!TryResolveCandidate(candidates, out suggestion)) return false;
        if (suggestion.IsConfidentForAutomaticCorrection &&
            AutomaticCorrectionTokenPolicy.IsProtected(lower))
        {
            suggestion = suggestion with { IsConfidentForAutomaticCorrection = false };
        }
        return true;
    }

    private bool TryGetCandidateConfidence(
        string word,
        string layoutCode,
        out bool isConfidentForAutomaticCorrection,
        out double relativeFrequency)
    {
        isConfidentForAutomaticCorrection = false;
        relativeFrequency = 0;
        string lang = LanguagePart(layoutCode);
        if (string.IsNullOrEmpty(lang)) return false;
        var dictionary = GetDictionary(lang);
        if (!dictionary.IsAvailable || !dictionary.Words.Contains(word))
            return false;

        var hasFrequency = dictionary.Frequencies.TryGetValue(word, out var frequency);
        if (hasFrequency && dictionary.TotalFrequency > 0)
            relativeFrequency = frequency / (double)dictionary.TotalFrequency;

        var letterCount = word.EnumerateRunes().Count(Rune.IsLetter);
        if (letterCount > 5)
        {
            isConfidentForAutomaticCorrection = true;
        }
        else if (hasFrequency)
        {
            isConfidentForAutomaticCorrection = frequency >= MinimumShortWordFrequency;
        }
        else
        {
            // A one-word-per-line compact dictionary retains a useful
            // frequency rank even when its numeric column was stripped.
            // Multi-entry compact files have no trustworthy ordering signal,
            // so their short candidates remain available only to manual fixes.
            isConfidentForAutomaticCorrection =
                dictionary.Ranks.TryGetValue(word, out var rank) &&
                rank <= MaximumShortWordRankWithoutFrequency;
        }
        return true;
    }

    private static bool TryResolveCandidate(
        IReadOnlyList<CandidateEvidence> candidates,
        out LayoutCorrectionSuggestion suggestion)
    {
        suggestion = default;
        if (candidates.Count == 0)
            return false;
        if (candidates.Count == 1)
        {
            suggestion = candidates[0].Suggestion;
            return true;
        }

        // Shared Cyrillic characters produce the same visible correction for
        // several installed layouts. Select a target layout only when numeric
        // dictionary evidence, normalized by corpus size, has a strong margin.
        // Different replacements or rank-only dictionaries remain ambiguous.
        var replacement = candidates[0].Suggestion.Replacement;
        if (candidates.Any(candidate =>
                !string.Equals(
                    candidate.Suggestion.Replacement,
                    replacement,
                    StringComparison.OrdinalIgnoreCase)) ||
            candidates.Any(candidate => candidate.RelativeFrequency <= 0))
        {
            return false;
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.RelativeFrequency)
            .ToArray();
        if (ordered[0].RelativeFrequency <
            ordered[1].RelativeFrequency * MinimumRelativeFrequencyMargin)
        {
            return false;
        }

        suggestion = ordered[0].Suggestion;
        return true;
    }

    private static string LanguagePart(string code) =>
        code.Split(new[] { '-', '_' }, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

    private static bool IsCompatibleWithSourceLayout(string word, Layout sourceLayout)
    {
        var sourceCharacters = new HashSet<string>(
            sourceLayout.Keys.Values.Concat(sourceLayout.ShiftKeys.Values),
            StringComparer.OrdinalIgnoreCase);
        var elements = StringInfo.GetTextElementEnumerator(word);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element.EnumerateRunes().Any(Rune.IsLetter) &&
                !sourceCharacters.Contains(element))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<DictionaryWord> ParseDictionaryLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            yield break;

        var value = line.Trim().TrimStart('\uFEFF');
        if (value.Length == 0 || value.StartsWith('#'))
            yield break;

        // A separator may be part of a malformed frequency token (for example,
        // "1,80m 151"). Recognize strict word-frequency rows first so they fail
        // as one entry instead of leaking a fragment into dictionary evidence.
        var columns = value.Split(
            [' ', '\t'],
            2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var word = columns[0];
        if (columns.Length == 2 &&
            long.TryParse(
                columns[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedFrequency))
        {
            if (IsSingleWord(word))
                yield return new DictionaryWord(word, parsedFrequency, CanInferRank: true);
            yield break;
        }

        // A few upstream language lists are compact comma/semicolon-separated
        // files. Treating those as frequency rows used to load only their first
        // entry, silently reducing an entire dictionary to one word.
        if (value.IndexOfAny([',', ';']) >= 0)
        {
            foreach (var candidate in value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsSingleWord(candidate))
                    yield return new DictionaryWord(candidate, null, CanInferRank: false);
            }
            yield break;
        }

        // Ordered one-word-per-line lists retain rank evidence. Any remaining
        // multi-column row has neither a valid frequency nor a supported compact
        // format and is rejected rather than partially loaded.
        if (columns.Length == 1 && IsSingleWord(word))
            yield return new DictionaryWord(word, null, CanInferRank: true);
    }

    private sealed record DictionaryEntry(
        HashSet<string> Words,
        Dictionary<string, long> Frequencies,
        Dictionary<string, int> Ranks,
        long TotalFrequency,
        bool IsAvailable,
        DateTimeOffset? RetryAfterUtc);

    private readonly record struct CandidateEvidence(
        LayoutCorrectionSuggestion Suggestion,
        double RelativeFrequency);

    private readonly record struct DictionaryWord(
        string Word,
        long? Frequency,
        bool CanInferRank);

    // Dictionary evidence is lexical: punctuation ends the automatic word
    // tracker, while digits/code fragments are intentionally outside manual
    // dictionary correction. Keep the same supported joiners and Unicode marks.
    private static bool IsSingleWord(string value)
    {
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
            return false;

        var hasLetter = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                hasLetter = true;
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is
                    UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or
                    UnicodeCategory.EnclosingMark ||
                rune.Value is '\'' or '’' or '-')
            {
                continue;
            }

            return false;
        }

        return hasLetter;
    }
}
