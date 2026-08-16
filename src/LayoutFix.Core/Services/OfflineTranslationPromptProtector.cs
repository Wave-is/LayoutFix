using System.Text;

namespace LayoutFix.Core.Services;

internal static class OfflineTranslationPromptProtector
{
    public static OfflineTranslationPromptProtection Protect(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var ranges = OfflineTranslationResultGuard.FindMarkdownCodeSpans(sourceText)
            .Select(range => new ProtectedRange(range.Index, range.Length))
            .ToList();
        ranges.AddRange(OfflineTranslationResultGuard.FindMarkdownInlineLinks(sourceText)
            .Select(link => new ProtectedRange(link.DestinationIndex, link.DestinationLength))
            .Where(candidate => ranges.All(existing => !Overlaps(existing, candidate))));

        var ordered = ranges
            .OrderBy(range => range.Index)
            .ThenByDescending(range => range.Length)
            .ToArray();
        if (ordered.Length == 0)
            return new OfflineTranslationPromptProtection(sourceText, []);

        var replacements = new List<PromptReplacement>(ordered.Length);
        var result = new StringBuilder(sourceText.Length);
        var sourceIndex = 0;
        var placeholderIndex = 0;
        foreach (var range in ordered)
        {
            if (range.Index < sourceIndex || range.Length <= 0)
                continue;

            result.Append(sourceText, sourceIndex, range.Index - sourceIndex);
            string placeholder;
            do
            {
                placeholder = $"{{LF_PROTECTED_{placeholderIndex++:D4}}}";
            }
            while (sourceText.Contains(placeholder, StringComparison.Ordinal));

            var original = sourceText.Substring(range.Index, range.Length);
            result.Append(placeholder);
            replacements.Add(new PromptReplacement(placeholder, original));
            sourceIndex = range.Index + range.Length;
        }
        result.Append(sourceText, sourceIndex, sourceText.Length - sourceIndex);

        return new OfflineTranslationPromptProtection(result.ToString(), replacements);
    }

    private static bool Overlaps(ProtectedRange left, ProtectedRange right) =>
        left.Index < right.Index + right.Length && right.Index < left.Index + left.Length;

    private readonly record struct ProtectedRange(int Index, int Length);
}

internal sealed class OfflineTranslationPromptProtection(
    string protectedText,
    IReadOnlyList<PromptReplacement> replacements)
{
    public string ProtectedText { get; } = protectedText;

    public bool TryRestore(string? modelOutput, out string restored)
    {
        restored = modelOutput ?? string.Empty;
        if (modelOutput == null)
            return false;

        foreach (var replacement in replacements)
        {
            var first = restored.IndexOf(replacement.Placeholder, StringComparison.Ordinal);
            if (first < 0 ||
                restored.IndexOf(
                    replacement.Placeholder,
                    first + replacement.Placeholder.Length,
                    StringComparison.Ordinal) >= 0)
            {
                restored = string.Empty;
                return false;
            }

            restored = restored.Replace(
                replacement.Placeholder,
                replacement.Original,
                StringComparison.Ordinal);
        }

        return true;
    }
}

internal readonly record struct PromptReplacement(string Placeholder, string Original);
