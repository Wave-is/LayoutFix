using System.Collections.Frozen;

namespace LayoutFix.Core.Services;

internal static class AutomaticCorrectionTokenPolicy
{
    private static readonly string[] ResourceNames =
    [
        "LayoutFix.Core.Resources.technical-tokens.txt",
        "LayoutFix.Core.Resources.frequent-source-tokens.txt"
    ];

    private static readonly FrozenSet<string> ProtectedTokens = Load();

    internal static bool IsProtected(string token) =>
        ProtectedTokens.Contains(token);

    private static FrozenSet<string> Load()
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in ResourceNames)
        {
            using var stream = typeof(AutomaticCorrectionTokenPolicy)
                .Assembly
                .GetManifestResourceStream(resourceName) ??
                throw new InvalidOperationException(
                    $"Embedded automatic-correction corpus '{resourceName}' is missing.");
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                var token = line.Trim();
                if (token.Length == 0)
                {
                    throw new InvalidDataException(
                        $"The embedded automatic-correction corpus '{resourceName}' " +
                        "contains an empty entry.");
                }
                if (token.Any(char.IsWhiteSpace) || !tokens.Add(token))
                {
                    throw new InvalidDataException(
                        $"The embedded automatic-correction corpus '{resourceName}' " +
                        "contains an invalid or duplicate entry.");
                }
            }
        }

        if (tokens.Count == 0)
            throw new InvalidDataException("The embedded automatic-correction corpus is empty.");

        return tokens.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
