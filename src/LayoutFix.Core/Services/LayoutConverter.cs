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
        
        var reverseKeys = source.Keys.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
        var reverseShiftKeys = source.ShiftKeys.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

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

    public (string? ConvertedText, Layout? Source, Layout? Target) AutoConvert(string text, IReadOnlyList<Layout> activeLayouts, string? currentLayoutCode = null)
    {
        if (string.IsNullOrWhiteSpace(text) || activeLayouts.Count < 2)
            return (null, null, null);

        var scores = new Dictionary<Layout, int>();
        foreach (var layout in activeLayouts)
        {
            int score = 0;
            var allChars = new HashSet<string>(layout.Keys.Values.Concat(layout.ShiftKeys.Values));
            foreach (var c in text)
            {
                var charStr = c.ToString();
                if (allChars.Contains(charStr) || allChars.Contains(charStr.ToLower()))
                {
                    score++;
                }
            }
            scores[layout] = score;
        }

        int maxScore = scores.Values.Max();
        if (maxScore == 0) return (null, null, null);

        var bestLayouts = scores.Where(kvp => kvp.Value == maxScore).Select(kvp => kvp.Key).ToList();

        Layout sourceLayout;
        if (bestLayouts.Count > 1 && !string.IsNullOrEmpty(currentLayoutCode))
        {
            var matchingCurrent = bestLayouts.FirstOrDefault(l => string.Equals(l.Code, currentLayoutCode, StringComparison.OrdinalIgnoreCase));
            sourceLayout = matchingCurrent ?? bestLayouts.First();
        }
        else
        {
            sourceLayout = bestLayouts.First();
        }

        int index = -1;
        for (int i = 0; i < activeLayouts.Count; i++) {
            if (activeLayouts[i].Code == sourceLayout.Code) { index = i; break; }
        }
        var targetLayout = activeLayouts[(index + 1) % activeLayouts.Count];

        var converted = ConvertTo(text, targetLayout, sourceLayout);
        return (converted, sourceLayout, targetLayout);
    }
}
