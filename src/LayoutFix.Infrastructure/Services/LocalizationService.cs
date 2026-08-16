using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class LocalizationService : ILocalizationService
{
    private readonly string _localesDirectory;
    private Dictionary<string, string> _strings = new();

    public LocalizationService() : this(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales"),
        null)
    {
    }

    internal LocalizationService(string localesDirectory, string? initialCulture)
    {
        _localesDirectory = Path.GetFullPath(localesDirectory);
        LoadLocalization(initialCulture);
    }

    public void SetCulture(string culture)
    {
        LoadLocalization(culture);
    }

    private void LoadLocalization(string? currentCulture = null)
    {
        if (string.IsNullOrEmpty(currentCulture))
        {
            currentCulture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        }

        var result = TryReadLocale(currentCulture, out var localizedStrings);
        if (result == LocaleReadResult.Success)
        {
            _strings = localizedStrings!;
            return;
        }

        // A sharing violation or temporary ACL problem must not replace a
        // working in-memory locale with a different language. Missing,
        // malformed and unsafe culture identifiers use deterministic English.
        if (result == LocaleReadResult.Unavailable ||
            string.Equals(currentCulture, "en", StringComparison.OrdinalIgnoreCase))
            return;

        if (TryReadLocale("en", out localizedStrings) == LocaleReadResult.Success)
            _strings = localizedStrings!;
    }

    private LocaleReadResult TryReadLocale(
        string culture,
        out Dictionary<string, string>? localizedStrings)
    {
        localizedStrings = null;
        if (!TryNormalizeCulture(culture, out var normalizedCulture))
            return LocaleReadResult.MissingOrInvalid;

        var path = Path.Combine(_localesDirectory, normalizedCulture + ".json");
        try
        {
            var json = File.ReadAllText(path);
            localizedStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return localizedStrings is null
                ? LocaleReadResult.MissingOrInvalid
                : LocaleReadResult.Success;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or
                DirectoryNotFoundException or
                JsonException or
                NotSupportedException)
        {
            return LocaleReadResult.MissingOrInvalid;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return LocaleReadResult.Unavailable;
        }
    }

    private static bool TryNormalizeCulture(string culture, out string normalizedCulture)
    {
        normalizedCulture = string.Empty;
        var segments = culture.Split('-');
        if (segments.Length is < 1 or > 3 ||
            segments[0].Length is < 2 or > 3 ||
            !segments[0].All(IsAsciiLetter))
            return false;

        for (var index = 1; index < segments.Length; index++)
        {
            if (segments[index].Length is < 2 or > 8 ||
                !segments[index].All(IsAsciiLetterOrDigit))
                return false;
        }

        normalizedCulture = culture.ToLowerInvariant();
        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char value) =>
        IsAsciiLetter(value) || value is >= '0' and <= '9';

    private enum LocaleReadResult
    {
        Success,
        MissingOrInvalid,
        Unavailable
    }

    public string GetString(string key, string defaultValue)
    {
        if (_strings.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }
        return defaultValue;
    }
}
