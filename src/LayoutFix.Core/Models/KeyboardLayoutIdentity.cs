using System;
using System.Globalization;

namespace LayoutFix.Core.Models;

public static class KeyboardLayoutIdentity
{
    private const char Separator = '@';

    public static string Create(string cultureCode, long nativeHandle) =>
        $"{cultureCode}{Separator}{unchecked((uint)nativeHandle):X8}";

    public static string GetCultureCode(string identifierOrCode)
    {
        if (string.IsNullOrWhiteSpace(identifierOrCode))
            return string.Empty;

        var separatorIndex = identifierOrCode.IndexOf(Separator);
        return separatorIndex > 0
            ? identifierOrCode[..separatorIndex]
            : identifierOrCode;
    }

    public static bool TryGetNativeHandle(string identifier, out uint nativeHandle)
    {
        nativeHandle = 0;
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        var separatorIndex = identifier.LastIndexOf(Separator);
        return separatorIndex > 0 &&
               separatorIndex < identifier.Length - 1 &&
               uint.TryParse(
                   identifier.AsSpan(separatorIndex + 1),
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out nativeHandle);
    }

    public static bool SameCulture(string left, string right) =>
        string.Equals(
            GetCultureCode(left),
            GetCultureCode(right),
            StringComparison.OrdinalIgnoreCase);

    public static string GetLanguageCode(string identifierOrCode)
    {
        var cultureCode = GetCultureCode(identifierOrCode);
        var separatorIndex = cultureCode.IndexOfAny(['-', '_']);
        return separatorIndex > 0 ? cultureCode[..separatorIndex] : cultureCode;
    }

    public static bool SameLanguage(string left, string right) =>
        string.Equals(
            GetLanguageCode(left),
            GetLanguageCode(right),
            StringComparison.OrdinalIgnoreCase);
}
