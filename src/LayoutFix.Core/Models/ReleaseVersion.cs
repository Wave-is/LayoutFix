namespace LayoutFix.Core.Models;

/// <summary>
/// Small dependency-free SemVer 2.0 value used by the update client. Build
/// metadata is intentionally ignored for precedence while prerelease labels
/// follow the numeric/alphanumeric ordering rules from the specification.
/// </summary>
public readonly record struct ReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease = null) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
            candidate = candidate[1..];

        var metadataIndex = candidate.IndexOf('+');
        if (metadataIndex >= 0)
        {
            var metadata = candidate[(metadataIndex + 1)..];
            if (!AreValidIdentifiers(metadata, rejectNumericLeadingZero: false))
                return false;
            candidate = candidate[..metadataIndex];
        }

        string? prerelease = null;
        var prereleaseIndex = candidate.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            prerelease = candidate[(prereleaseIndex + 1)..];
            candidate = candidate[..prereleaseIndex];
            if (!IsValidPrerelease(prerelease))
                return false;
        }

        var parts = candidate.Split('.');
        if (parts.Length != 3 ||
            !TryParseCoreNumber(parts[0], out var major) ||
            !TryParseCoreNumber(parts[1], out var minor) ||
            !TryParseCoreNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (Prerelease == null && other.Prerelease == null) return 0;
        if (Prerelease == null) return 1;
        if (other.Prerelease == null) return -1;

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var leftNumeric = left[index].All(char.IsAsciiDigit);
            var rightNumeric = right[index].All(char.IsAsciiDigit);
            if (leftNumeric && rightNumeric)
            {
                result = left[index].Length.CompareTo(right[index].Length);
                if (result == 0)
                    result = string.CompareOrdinal(left[index], right[index]);
            }
            else if (leftNumeric)
                result = -1;
            else if (rightNumeric)
                result = 1;
            else
                result = string.CompareOrdinal(left[index], right[index]);

            if (result != 0) return result;
        }

        return left.Length.CompareTo(right.Length);
    }

    public override string ToString()
        => $"{Major}.{Minor}.{Patch}" + (Prerelease == null ? string.Empty : $"-{Prerelease}");

    private static bool TryParseCoreNumber(string value, out int number)
    {
        number = 0;
        if (value.Length == 0 || (value.Length > 1 && value[0] == '0'))
            return false;
        return int.TryParse(value, out number) && number >= 0;
    }

    private static bool IsValidPrerelease(string value)
        => AreValidIdentifiers(value, rejectNumericLeadingZero: true);

    private static bool AreValidIdentifiers(string value, bool rejectNumericLeadingZero)
    {
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || identifier.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }

            if (rejectNumericLeadingZero && identifier.Length > 1 && identifier[0] == '0' &&
                identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }
}
