using System.Text;
using System.Text.RegularExpressions;

namespace LayoutFix.Core.Services;

public static class OfflineTranslationResultGuard
{
    private const int MinimumLengthLimit = 160;
    private const int SourceLengthMultiplier = 6;
    private static readonly string[] TranslationLabels =
    [
        "Translation:",
        "Russian translation:",
        "Ukrainian translation:",
        "English translation:",
        "Spanish translation:",
        "French translation:",
        "German translation:"
    ];
    private static readonly string[] RussianLexicalStems =
    [
        "спасиб",
        "помощ",
        "пожалуйст",
        "привет",
        "здравствуй"
    ];
    private static readonly string[] UkrainianLexicalStems =
    [
        "дяку",
        "допом",
        "привіт",
        "вітаю",
        "ласка"
    ];
    private static readonly HashSet<string> ProperNameStopWords = new(
        [
            "Account", "After", "Application", "April", "Ask", "August", "Before",
            "Browser", "Call", "Close", "Contact", "Dashboard", "Deadline", "December",
            "Deploy", "Development", "Document", "Editor", "Email", "Enable", "English",
            "Feature", "February", "French", "Friday", "German", "Guide", "Hello", "Help",
            "Home", "January", "July", "June", "March", "May", "Meet", "Message",
            "Monday", "November", "October", "Offline", "Open", "Ping", "Please", "Press",
            "Production", "Profile", "Read", "Release", "Remove", "Requirements", "Restart",
            "Run", "Russian", "Saturday", "Save", "See", "September", "Settings", "Spanish",
            "Sunday", "Support", "Team", "Tell", "Thank", "The", "Thursday", "Train",
            "Translation", "Tuesday", "Ukrainian", "Update", "Visit", "Wednesday", "Window"
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PrecedingProperNameCues = new(
        [
            "ask", "call", "contact", "email", "from", "in", "meet", "message",
            "near", "ping", "tell", "visit", "with"
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly Regex TechnicalTokenPattern = new(
        @"`[^`\r\n]+`|" +
        @"https?://[^\s<>()\[\]{}""']+|" +
        @"[A-Za-z]:\\[^\s<>()\[\]{}""']+|" +
        @"(?:Ctrl|Alt|Shift|Win)(?:\+[A-Za-z0-9]+)+|" +
        @"[\p{L}\p{N}_-]+\.[A-Za-z0-9]{1,10}\b|" +
        @"\$?\{[\p{L}\p{N}_.-]+\}|%[A-Za-z]|" +
        @"--[A-Za-z0-9][A-Za-z0-9_-]*|" +
        @"\b[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+\b|" +
        @"\b[A-Z][a-z0-9]+(?:[A-Z][A-Za-z0-9]*)+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex QuantitativeTokenPattern = new(
        @"(?<![\p{L}\p{N}_])[+-]?\d+(?:(?:[.,:/-])\d+)*(?:%|‰)?(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LineStructurePattern = new(
        @"^[ \t]*(?:(?:[-*+][ \t]+(?:\[[ xX]\][ \t]+)?)|(?:\d+[.)][ \t]+)|(?:#{1,6}[ \t]+)|(?:>[ \t]*))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FencedCodeBlockPattern = new(
        @"(?ms)^[ \t]*(?<fence>`{3,}|~{3,})[^\n]*\n.*?^[ \t]*\k<fence>[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownDelimiterPattern = new(
        @"(?<!\\)(?:\*\*|__|~~)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownReferenceUsePattern = new(
        @"!?\[[^\]\r\n]+\]\[(?<reference>[^\]\r\n]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownReferenceDefinitionPattern = new(
        @"(?m)^[ \t]{0,3}\[(?<reference>[^\]\r\n]+)\]:[ \t]*(?<destination><[^>\r\n]+>|[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownAutolinkPattern = new(
        @"<(?<destination>(?:https?://|mailto:)[^<>\s]+)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex MarkdownTableDelimiterPattern = new(
        @"(?m)^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(?:\|[ \t]*:?-{3,}:?[ \t]*)+\|?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LatinProperNamePattern = new(
        @"(?<![\p{L}\p{N}_])[A-Z][a-z]{2,}(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CyrillicWordPattern = new(
        @"[\u0400-\u052F]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishExplicitNegationPattern = new(
        @"(?<![\p{L}])(?:not|never|cannot|can['’]t|don['’]t|doesn['’]t|didn['’]t|" +
        @"won['’]t|wouldn['’]t|shouldn['’]t|mustn['’]t|isn['’]t|aren['’]t|" +
        @"wasn['’]t|weren['’]t|hasn['’]t|haven['’]t|hadn['’]t)(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishNegationResultPattern = new(
        @"(?<![\p{L}])(?:no|not|never|without|cannot|can['’]t|don['’]t|doesn['’]t|" +
        @"didn['’]t|won['’]t|wouldn['’]t|shouldn['’]t|mustn['’]t|isn['’]t|" +
        @"aren['’]t|wasn['’]t|weren['’]t|hasn['’]t|haven['’]t|hadn['’]t)(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CyrillicExplicitNegationPattern = new(
        @"(?<![\p{L}])(?:не|ні|нет|немає|никогда|ніколи)(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CyrillicNegationResultPattern = new(
        @"(?<![\p{L}])(?:не|ні|нет|немає|никогда|ніколи|без)(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex StrongProperNamePredicatePattern = new(
        @"^[ \t]+(?:will[ \t]+(?:call|contact|email|meet|message|visit|write)|(?:lives|works)[ \t]+(?:at|for|in)|(?:asked|replied|said|says)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PreviousLatinWordPattern = new(
        @"([A-Za-z]+)[ \t]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryAccept(
        string sourceText,
        string targetLanguageCode,
        string? rawTranslation,
        out string translation)
    {
        translation = Clean(rawTranslation);
        if (translation.Length == 0 ||
            translation.Length > Math.Max(
                MinimumLengthLimit,
                sourceText.Length * SourceLengthMultiplier) ||
            ContainsModelControlMarker(translation))
        {
            translation = string.Empty;
            return false;
        }

        var sourceComparable = ToComparableText(sourceText);
        var translationComparable = ToComparableText(translation);
        if (translationComparable.Length == 0 ||
            (sourceComparable.Length > 0 &&
                string.Equals(
                    sourceComparable,
                    translationComparable,
                    StringComparison.OrdinalIgnoreCase)))
        {
            translation = string.Empty;
            return false;
        }

        if (!PreservesProtectedTokens(sourceText, translation))
        {
            translation = string.Empty;
            return false;
        }

        if (CountLineBreaks(sourceText) != CountLineBreaks(translation))
        {
            translation = string.Empty;
            return false;
        }

        if (!PreservesLineStructure(sourceText, translation))
        {
            translation = string.Empty;
            return false;
        }

        var naturalLanguageTranslation = RemoveProtectedTokens(
            translation,
            ExtractProtectedTokens(sourceText));

        if (!PreservesExplicitNegation(
            sourceText,
            targetLanguageCode,
            naturalLanguageTranslation))
        {
            translation = string.Empty;
            return false;
        }

        if (targetLanguageCode is "ru" or "uk")
        {
            if (!PreservesLikelyLatinProperNames(sourceText, naturalLanguageTranslation))
            {
                translation = string.Empty;
                return false;
            }

            if (ContainsLatinLetter(sourceText) &&
                (!ContainsCyrillicLetter(naturalLanguageTranslation) ||
                 ContainsLatinLetter(naturalLanguageTranslation)))
            {
                translation = string.Empty;
                return false;
            }

            if (!MatchesRequestedCyrillicLanguage(targetLanguageCode, translation))
            {
                translation = string.Empty;
                return false;
            }
        }
        else if (targetLanguageCode is "en" or "es" or "fr" or "de")
        {
            if (ContainsCyrillicLetter(sourceText) &&
                (!ContainsLatinLetter(naturalLanguageTranslation) ||
                 ContainsCyrillicLetter(naturalLanguageTranslation)))
            {
                translation = string.Empty;
                return false;
            }
        }

        return true;
    }

    private static bool PreservesExplicitNegation(
        string sourceText,
        string targetLanguageCode,
        string naturalLanguageTranslation)
    {
        if (targetLanguageCode is not ("ru" or "uk" or "en"))
            return true;

        var sourceProse = RemoveProtectedTokens(
            sourceText,
            ExtractProtectedTokens(sourceText));
        if (!EnglishExplicitNegationPattern.IsMatch(sourceProse) &&
            !CyrillicExplicitNegationPattern.IsMatch(sourceProse))
        {
            return true;
        }

        return targetLanguageCode == "en"
            ? EnglishNegationResultPattern.IsMatch(naturalLanguageTranslation)
            : CyrillicNegationResultPattern.IsMatch(naturalLanguageTranslation);
    }

    private static bool PreservesLikelyLatinProperNames(
        string sourceText,
        string naturalLanguageTranslation)
    {
        var sourceProse = RemoveProtectedTokens(
            sourceText,
            ExtractProtectedTokens(sourceText));
        var nameMatches = LatinProperNamePattern
            .Matches(sourceProse)
            .Where(match => !ProperNameStopWords.Contains(match.Value))
            .GroupBy(match => match.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (nameMatches.Length == 0)
            return true;
        if (nameMatches.Length == 1 &&
            !HasStrongSingleProperNameContext(sourceProse, nameMatches[0]))
        {
            // A lone capitalized word at a sentence boundary is too ambiguous
            // without a full NER model. Only strong local cues enable this gate.
            return true;
        }

        var translatedNameKeys = CyrillicWordPattern
            .Matches(naturalLanguageTranslation)
            .Select(match => ToLatinPhoneticKey(ReverseTransliterate(match.Value)))
            .Where(key => key.Length >= 3)
            .ToArray();
        return nameMatches.All(nameMatch =>
        {
            var name = nameMatch.Value;
            var sourceKey = ToLatinPhoneticKey(name);
            return sourceKey.Length >= 3 && translatedNameKeys.Any(candidate =>
                candidate.StartsWith(sourceKey, StringComparison.Ordinal) ||
                sourceKey.StartsWith(candidate, StringComparison.Ordinal));
        });
    }

    private static bool HasStrongSingleProperNameContext(
        string sourceProse,
        Match nameMatch)
    {
        var before = sourceProse[..nameMatch.Index];
        var previousWord = PreviousLatinWordPattern.Match(before);
        if (previousWord.Success &&
            PrecedingProperNameCues.Contains(previousWord.Groups[1].Value))
        {
            return true;
        }

        var after = sourceProse[(nameMatch.Index + nameMatch.Length)..];
        return StrongProperNamePredicatePattern.IsMatch(after);
    }

    private static string ReverseTransliterate(string value)
    {
        var result = new StringBuilder(value.Length * 2);
        foreach (var character in value.ToLowerInvariant())
        {
            result.Append(character switch
            {
                'а' => "a", 'б' => "b", 'в' => "v", 'г' or 'ґ' => "g",
                'д' => "d", 'е' or 'ё' or 'э' => "e", 'є' => "ye",
                'ж' => "zh", 'з' => "z", 'и' or 'і' => "i", 'ї' => "yi",
                'й' => "y", 'к' => "k", 'л' => "l", 'м' => "m", 'н' => "n",
                'о' => "o", 'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t",
                'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "ts", 'ч' => "ch",
                'ш' => "sh", 'щ' => "shch", 'ы' => "y", 'ю' => "yu", 'я' => "ya",
                'ъ' or 'ь' => string.Empty,
                _ => character.ToString()
            });
        }

        return result.ToString();
    }

    private static string ToLatinPhoneticKey(string value)
    {
        var lower = value.ToLowerInvariant();
        var result = new StringBuilder(lower.Length * 2);
        for (var index = 0; index < lower.Length; index++)
        {
            var character = lower[index];
            var next = index + 1 < lower.Length ? lower[index + 1] : '\0';
            if (character == 'p' && next == 'h')
            {
                result.Append('f');
                index++;
                continue;
            }
            if (character == 't' && next == 'h')
            {
                result.Append('t');
                index++;
                continue;
            }
            if (character == 'j')
            {
                result.Append("dzh");
                continue;
            }
            if (character == 'x')
            {
                result.Append("ks");
                continue;
            }
            if (character == 'c')
            {
                result.Append(next is 'e' or 'i' or 'y' ? 's' : 'k');
                continue;
            }

            result.Append(character switch
            {
                'q' => 'k',
                'y' => 'i',
                _ => character
            });
        }

        var key = result.ToString().Replace("ii", "i", StringComparison.Ordinal);
        if (key.Length > 3 && key[^1] == 'e')
            key = key[..^1];
        return key;
    }

    private static bool PreservesProtectedTokens(
        string sourceText,
        string translation)
    {
        var sourceTokens = ExtractProtectedTokens(sourceText)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
        var translatedTokens = ExtractProtectedTokens(translation)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
        return sourceTokens.SequenceEqual(translatedTokens, StringComparer.Ordinal);
    }

    private static IEnumerable<string> ExtractProtectedTokens(string value)
    {
        var normalized = value.ReplaceLineEndings("\n");
        var mask = normalized.ToCharArray();
        var tokens = new List<string>();

        foreach (Match match in FencedCodeBlockPattern.Matches(normalized))
        {
            tokens.Add(match.Value);
            MaskRange(mask, match.Index, match.Length);
        }

        foreach (Match match in MarkdownTableDelimiterPattern.Matches(new string(mask)))
        {
            tokens.Add(match.Value);
            MaskRange(mask, match.Index, match.Length);
        }

        foreach (var codeSpan in FindMarkdownCodeSpans(new string(mask)))
        {
            tokens.Add(normalized.Substring(codeSpan.Index, codeSpan.Length));
            MaskRange(mask, codeSpan.Index, codeSpan.Length);
        }

        ExtractNamedMarkdownTokens(
            MarkdownReferenceDefinitionPattern,
            mask,
            tokens,
            "reference",
            "destination");
        ExtractNamedMarkdownTokens(
            MarkdownReferenceUsePattern,
            mask,
            tokens,
            "reference");
        foreach (var link in FindMarkdownInlineLinks(new string(mask)))
        {
            tokens.Add(normalized.Substring(link.DestinationIndex, link.DestinationLength));
            MaskRange(mask, link.DestinationIndex, link.DestinationLength);
        }

        foreach (Match match in MarkdownAutolinkPattern.Matches(new string(mask)))
        {
            tokens.Add(match.Value);
            MaskRange(mask, match.Index, match.Length);
        }

        foreach (Match match in TechnicalTokenPattern.Matches(new string(mask)))
        {
            var token = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (token.Length > 0)
                tokens.Add(token);
            MaskRange(mask, match.Index, match.Length);
        }

        foreach (Match match in QuantitativeTokenPattern.Matches(new string(mask)))
        {
            tokens.Add(match.Value);
            MaskRange(mask, match.Index, match.Length);
        }

        var proseOnly = new string(mask);
        tokens.AddRange(MarkdownDelimiterPattern
            .Matches(proseOnly)
            .Select(match => match.Value));
        return tokens;
    }

    private static void ExtractNamedMarkdownTokens(
        Regex pattern,
        char[] mask,
        ICollection<string> tokens,
        params string[] groupNames)
    {
        foreach (Match match in pattern.Matches(new string(mask)))
        {
            foreach (var groupName in groupNames)
            {
                var group = match.Groups[groupName];
                if (!group.Success || group.Length == 0)
                    continue;

                tokens.Add(group.Value);
                MaskRange(mask, group.Index, group.Length);
            }
        }
    }

    private static string RemoveProtectedTokens(
        string value,
        IEnumerable<string> tokens)
    {
        var result = value.ReplaceLineEndings("\n");
        foreach (var token in tokens
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(token => token.Length))
            result = result.Replace(token, string.Empty, StringComparison.Ordinal);

        return result;
    }

    private static void MaskRange(char[] value, int index, int length)
    {
        var end = Math.Min(value.Length, index + length);
        for (var position = index; position < end; position++)
        {
            if (value[position] != '\n')
                value[position] = ' ';
        }
    }

    private static int CountLineBreaks(string value) =>
        value.ReplaceLineEndings("\n").Count(character => character == '\n');

    private static bool PreservesLineStructure(
        string sourceText,
        string translation)
    {
        var sourceLines = sourceText.ReplaceLineEndings("\n").Split('\n');
        var translatedLines = translation.ReplaceLineEndings("\n").Split('\n');
        if (sourceLines.Length != translatedLines.Length)
            return false;

        for (var index = 0; index < sourceLines.Length; index++)
        {
            var sourcePrefix = LineStructurePattern.Match(sourceLines[index]).Value;
            var translatedPrefix = LineStructurePattern.Match(translatedLines[index]).Value;
            if (!string.Equals(sourcePrefix, translatedPrefix, StringComparison.Ordinal))
                return false;

            if (GetMarkdownLineSignature(sourceLines[index]) !=
                GetMarkdownLineSignature(translatedLines[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static (int PipeCount, bool StartsWithPipe, bool EndsWithPipe,
        int InlineLinks, int InlineImages, int ReferenceUses, int ReferenceImages,
        int ReferenceDefinitions) GetMarkdownLineSignature(string line)
    {
        var trimmed = line.Trim();
        var structuralMask = line.ToCharArray();
        foreach (var codeSpan in FindMarkdownCodeSpans(line))
            MaskRange(structuralMask, codeSpan.Index, codeSpan.Length);
        var structuralLine = new string(structuralMask);
        var inlineLinks = FindMarkdownInlineLinks(structuralLine);
        var referenceUses = MarkdownReferenceUsePattern.Matches(structuralLine);
        return (
            CountUnescapedPipes(line),
            trimmed.StartsWith('|'),
            trimmed.EndsWith('|'),
            inlineLinks.Count,
            inlineLinks.Count(link => link.IsImage),
            referenceUses.Count,
            referenceUses.Count(match => match.Value.StartsWith('!')),
            MarkdownReferenceDefinitionPattern.Matches(structuralLine).Count);
    }

    private static int CountUnescapedPipes(string value)
    {
        var mask = value.ToCharArray();
        foreach (var codeSpan in FindMarkdownCodeSpans(value))
            MaskRange(mask, codeSpan.Index, codeSpan.Length);

        var count = 0;
        var backslashRun = 0;
        foreach (var character in mask)
        {
            if (character == '\\')
            {
                backslashRun++;
                continue;
            }

            if (character == '|' && backslashRun % 2 == 0)
                count++;
            backslashRun = 0;
        }

        return count;
    }

    internal static IReadOnlyList<MarkdownInlineLink> FindMarkdownInlineLinks(string value)
    {
        var links = new List<MarkdownInlineLink>();
        for (var index = 0; index < value.Length; index++)
        {
            var isImage = value[index] == '!' &&
                          index + 1 < value.Length &&
                          value[index + 1] == '[' &&
                          !IsEscaped(value, index);
            var labelStart = isImage ? index + 1 : index;
            if (value[labelStart] != '[' || IsEscaped(value, labelStart))
                continue;

            var labelEnd = FindClosingBracket(value, labelStart);
            if (labelEnd < 0 || labelEnd + 1 >= value.Length || value[labelEnd + 1] != '(')
                continue;

            var destinationStart = labelEnd + 2;
            while (destinationStart < value.Length &&
                   value[destinationStart] is ' ' or '\t')
            {
                destinationStart++;
            }
            if (destinationStart >= value.Length || value[destinationStart] is '\r' or '\n')
                continue;

            var destinationEnd = -1;
            var linkEnd = -1;
            if (value[destinationStart] == '<')
            {
                var angleEnd = FindUnescaped(value, '>', destinationStart + 1);
                if (angleEnd < 0)
                    continue;
                destinationEnd = angleEnd + 1;
                linkEnd = FindUnescaped(value, ')', destinationEnd);
            }
            else
            {
                var depth = 1;
                for (var cursor = destinationStart; cursor < value.Length; cursor++)
                {
                    if (value[cursor] is '\r' or '\n')
                        break;
                    if (value[cursor] == '\\' && cursor + 1 < value.Length)
                    {
                        cursor++;
                        continue;
                    }
                    if (value[cursor] == '(')
                    {
                        depth++;
                        continue;
                    }
                    if (value[cursor] == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            destinationEnd = destinationEnd < 0 ? cursor : destinationEnd;
                            linkEnd = cursor;
                            break;
                        }
                        continue;
                    }
                    if (depth == 1 &&
                        destinationEnd < 0 &&
                        value[cursor] is ' ' or '\t')
                    {
                        destinationEnd = cursor;
                    }
                }
            }

            if (linkEnd < 0 || destinationEnd <= destinationStart)
                continue;

            links.Add(new MarkdownInlineLink(
                destinationStart,
                destinationEnd - destinationStart,
                isImage));
            index = linkEnd;
        }

        return links;
    }

    internal static IReadOnlyList<TextRange> FindMarkdownCodeSpans(string value)
    {
        var spans = new List<TextRange>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '`' || IsEscaped(value, index))
                continue;

            var fenceLength = CountRun(value, index, '`');
            var cursor = index + fenceLength;
            while (cursor < value.Length && value[cursor] is not ('\r' or '\n'))
            {
                if (value[cursor] != '`')
                {
                    cursor++;
                    continue;
                }

                var candidateLength = CountRun(value, cursor, '`');
                if (candidateLength == fenceLength)
                {
                    spans.Add(new TextRange(index, cursor + candidateLength - index));
                    index = cursor + candidateLength - 1;
                    break;
                }
                cursor += candidateLength;
            }
        }

        return spans;
    }

    private static int FindClosingBracket(string value, int openingIndex)
    {
        var depth = 1;
        for (var index = openingIndex + 1; index < value.Length; index++)
        {
            if (value[index] is '\r' or '\n')
                return -1;
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                index++;
                continue;
            }
            if (value[index] == '[')
                depth++;
            else if (value[index] == ']' && --depth == 0)
                return index;
        }

        return -1;
    }

    private static int FindUnescaped(string value, char target, int startIndex)
    {
        for (var index = startIndex; index < value.Length; index++)
        {
            if (value[index] is '\r' or '\n')
                return -1;
            if (value[index] == target && !IsEscaped(value, index))
                return index;
        }
        return -1;
    }

    private static bool IsEscaped(string value, int index)
    {
        var slashCount = 0;
        while (index > 0 && value[--index] == '\\')
            slashCount++;
        return slashCount % 2 != 0;
    }

    private static int CountRun(string value, int startIndex, char character)
    {
        var index = startIndex;
        while (index < value.Length && value[index] == character)
            index++;
        return index - startIndex;
    }

    internal readonly record struct MarkdownInlineLink(
        int DestinationIndex,
        int DestinationLength,
        bool IsImage);

    internal readonly record struct TextRange(int Index, int Length);

    private static bool MatchesRequestedCyrillicLanguage(
        string targetLanguageCode,
        string value)
    {
        var hasRussianOnlyCharacter = value.Any(character => character is 'ы' or 'Ы' or 'э' or 'Э' or 'ъ' or 'Ъ' or 'ё' or 'Ё');
        var hasUkrainianOnlyCharacter = value.Any(character => character is 'і' or 'І' or 'ї' or 'Ї' or 'є' or 'Є' or 'ґ' or 'Ґ');
        var hasArmenianCharacter = value.Any(character => character is >= '\u0530' and <= '\u058F');
        var hasRussianLexicon = ContainsLexicalStem(value, RussianLexicalStems);
        var hasUkrainianLexicon = ContainsLexicalStem(value, UkrainianLexicalStems);

        return targetLanguageCode switch
        {
            "uk" => !hasArmenianCharacter && !hasRussianOnlyCharacter && !hasRussianLexicon,
            "ru" => !hasArmenianCharacter && !hasUkrainianOnlyCharacter && !hasUkrainianLexicon,
            _ => true
        };
    }

    private static bool ContainsLexicalStem(
        string value,
        IReadOnlyList<string> stems)
    {
        var word = new StringBuilder();
        foreach (var character in value.Append(' '))
        {
            if (char.IsLetter(character))
            {
                word.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (word.Length > 0 && stems.Any(stem => word.ToString().StartsWith(stem, StringComparison.Ordinal)))
                return true;

            word.Clear();
        }

        return false;
    }

    private static string Clean(string? value)
    {
        var result = value?.Trim() ?? string.Empty;
        foreach (var label in TranslationLabels)
        {
            if (!result.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                continue;

            result = result[label.Length..].Trim();
            break;
        }

        if (result.Length >= 2 &&
            ((result[0] == '"' && result[^1] == '"') ||
             (result[0] == '“' && result[^1] == '”')))
        {
            result = result[1..^1].Trim();
        }

        return result;
    }

    private static bool ContainsModelControlMarker(string value) =>
        value.Contains("<|", StringComparison.Ordinal) ||
        value.Contains("<start_of_turn>", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("<end_of_turn>", StringComparison.OrdinalIgnoreCase);

    private static string ToComparableText(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                result.Append(char.ToUpperInvariant(character));
        }

        return result.ToString();
    }

    private static bool ContainsCyrillicLetter(string value) =>
        value.Any(character => character is >= '\u0400' and <= '\u052F');

    private static bool ContainsLatinLetter(string value) =>
        value.Any(character =>
            character is >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or
                >= '\u00C0' and <= '\u024F');
}
