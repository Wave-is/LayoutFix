using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public class LayoutConverter : ILayoutConverter
{
    public string ConvertTo(string text, Layout target, Layout source)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        var reverseKeys = CreateUnambiguousReverseMap(source.Keys);
        var reverseShiftKeys = CreateUnambiguousReverseMap(source.ShiftKeys);

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var charStr = c.ToString();
            
            if (reverseKeys.TryGetValue(charStr, out var baseKey))
            {
                if (target.Keys.TryGetValue(baseKey, out var targetChar))
                {
                    sb.Append(targetChar);
                    continue;
                }
            }
            
            if (reverseShiftKeys.TryGetValue(charStr, out var baseShiftKey))
            {
                if (target.ShiftKeys.TryGetValue(baseShiftKey, out var targetShiftChar))
                {
                    sb.Append(targetShiftChar);
                    continue;
                }
                if (target.Keys.TryGetValue(baseShiftKey, out var targetUnshiftChar))
                {
                    sb.Append(targetUnshiftChar.ToUpper());
                    continue;
                }
            }
            
            if (char.IsUpper(c))
            {
                var lowerStr = c.ToString().ToLower();
                if (reverseKeys.TryGetValue(lowerStr, out var baseLowerKey))
                {
                    if (target.ShiftKeys.TryGetValue(baseLowerKey, out var targetShiftChar2))
                    {
                        sb.Append(targetShiftChar2);
                        continue;
                    }
                    if (target.Keys.TryGetValue(baseLowerKey, out var targetLowerChar))
                    {
                        sb.Append(targetLowerChar.ToUpper());
                        continue;
                    }
                }
            }
            
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static Dictionary<string, string> CreateUnambiguousReverseMap(
        IEnumerable<KeyValuePair<string, string>> mappings)
    {
        // Custom and OEM Windows layouts can emit the same character from more
        // than one physical key. Guessing which key produced it can corrupt the
        // conversion, while ToDictionary would throw and abort the hotkey path.
        // Keep only mappings whose physical key can be reconstructed safely.
        return mappings
            .Where(mapping => !string.IsNullOrEmpty(mapping.Value))
            .GroupBy(mapping => mapping.Value, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single().Key,
                StringComparer.Ordinal);
    }

    public (string? ConvertedText, Layout? Source, Layout? Target) AutoConvert(string text, IReadOnlyList<Layout> activeLayouts, string? currentLayoutCode = null)
    {
        if (string.IsNullOrWhiteSpace(text) || activeLayouts.Count < 2)
            return (null, null, null);

        var characterSets = activeLayouts.ToDictionary(
            layout => layout,
            layout => new HashSet<string>(
                layout.Keys.Values.Concat(layout.ShiftKeys.Values),
                StringComparer.OrdinalIgnoreCase));

        // Every letter must be compatible with the same source layout. This prevents
        // mixed text such as "ghbdtn привет" from being partially and destructively
        // converted merely because one alphabet happens to win (or tie) by score.
        HashSet<Layout>? sourceCandidates = null;
        var meaningfulCharacterCount = 0;

        foreach (var character in text.Where(char.IsLetter))
        {
            meaningfulCharacterCount++;
            var characterString = character.ToString();
            var matchingLayouts = activeLayouts
                .Where(layout => characterSets[layout].Contains(characterString))
                .ToHashSet();

            if (matchingLayouts.Count == 0)
                return (null, null, null);

            sourceCandidates ??= matchingLayouts;
            sourceCandidates.IntersectWith(matchingLayouts);

            if (sourceCandidates.Count == 0)
                return (null, null, null);
        }

        if (meaningfulCharacterCount == 0 || sourceCandidates is null)
            return (null, null, null);

        var sourceLayout = sourceCandidates.FirstOrDefault(layout => string.Equals(
                               layout.EffectiveIdentifier,
                               currentLayoutCode,
                               StringComparison.OrdinalIgnoreCase)) ??
                           sourceCandidates.FirstOrDefault(layout =>
                               IsSameLayoutCulture(layout, currentLayoutCode)) ??
            activeLayouts.First(sourceCandidates.Contains);

        var index = Enumerable.Range(0, activeLayouts.Count)
            .First(i => ReferenceEquals(activeLayouts[i], sourceLayout));
        foreach (var targetLayout in Enumerable.Range(1, activeLayouts.Count - 1)
                     .Select(offset => activeLayouts[(index + offset) % activeLayouts.Count])
                     .Where(candidate => !KeyboardLayoutIdentity.SameLanguage(
                         candidate.Code,
                         sourceLayout.Code)))
        {
            var converted = ConvertTo(text, targetLayout, sourceLayout);
            if (!string.Equals(converted, text, StringComparison.Ordinal))
                return (converted, sourceLayout, targetLayout);
        }

        // Closely related layouts can produce an identical visible value for
        // many physical-key sequences (for example Russian -> Ukrainian). A
        // manual correction must continue to the next configured layout rather
        // than report a false "no change" after inspecting only that sibling.
        return (null, null, null);
    }

    private static bool IsSameLayoutCulture(Layout layout, string? currentLayoutCode)
    {
        if (string.IsNullOrWhiteSpace(currentLayoutCode))
            return false;

        if (string.Equals(layout.Code, currentLayoutCode, StringComparison.OrdinalIgnoreCase))
            return true;

        return KeyboardLayoutIdentity.SameLanguage(layout.Code, currentLayoutCode);
    }
}
