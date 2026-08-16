using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData("en", "Custom replacements", "Copy", "Cancel", "Detect language")]
    [InlineData("ru", "Свои автозамены", "Копировать", "Отменить", "Определить язык")]
    [InlineData("uk", "Власні автозаміни", "Копіювати", "Скасувати", "Визначити мову")]
    public void LocaleFiles_AreValidAndContainNewAutocorrectStrings(
        string culture,
        string expected,
        string expectedCopy,
        string expectedCancel,
        string expectedDetectLanguage)
    {
        var service = new LocalizationService();

        service.SetCulture(culture);

        Assert.Equal(expected, service.GetString("Settings_CustomReplacements", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_HotkeyConflictMessage", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_AppExceptionsDescription", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_AllActionsExclusions", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_AutoCorrectionExclusions", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_AutoCorrectionExclusionsDescription", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_RestoreSafetyDefaults", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_DiagnosticsReport", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_DiagnosticsPrivacy", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_CopyDiagnostics", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_DiagnosticsCopied", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_DiagnosticsCopyError", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_Logging", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_Theme", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_ThemeAuto", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_ThemeLight", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_ThemeDark", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_InterfaceLanguage", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_RestartRequired", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_RestartRequiredMessage", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_SaveErrorTitle", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_SaveErrorMessage", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_KeyboardPrefix", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_Active", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_BuiltWith", "missing"));
        Assert.NotEqual("missing", service.GetString("Settings_AboutTagline", "missing"));
        Assert.Equal(expectedCopy, service.GetString("Translator_Copy", "missing"));
        Assert.Equal(expectedCancel, service.GetString("Translator_Cancel", "missing"));
        Assert.Equal(
            expectedDetectLanguage,
            service.GetString("Translator_DetectLanguage", "missing"));
        Assert.NotEqual("missing", service.GetString("Translator_HistoryCollapsed", "missing"));
        Assert.NotEqual("missing", service.GetString("Translator_HistoryExpanded", "missing"));
        Assert.NotEqual("missing", service.GetString("Translator_TranslatingOnline", "missing"));
        Assert.NotEqual("missing", service.GetString("Translator_TranslatingOffline", "missing"));
        Assert.NotEqual("missing", service.GetString("Translator_ErrorPrefix", "missing"));
    }

    [Fact]
    public void SetCulture_DoesNotReadJsonOutsideLocaleDirectory()
    {
        var fileNameWithoutExtension = $"layoutfix-locale-escape-{Guid.NewGuid():N}";
        var outsidePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            fileNameWithoutExtension + ".json");
        File.WriteAllText(outsidePath, "{\"LocalizationProbe\":\"outside-locale-directory\"}");

        try
        {
            var service = new LocalizationService();

            service.SetCulture("..\\" + fileNameWithoutExtension);

            Assert.Equal("safe-default", service.GetString("LocalizationProbe", "safe-default"));
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public void SetCulture_MalformedLocaleFallsBackToEnglishInsteadOfPreviousCulture()
    {
        var malformedPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "locales",
            "zq.json");
        File.WriteAllText(malformedPath, "{ definitely-not-json }");

        try
        {
            var service = new LocalizationService();
            service.SetCulture("ru");
            Assert.Equal("Общие", service.GetString("Settings_General", "missing"));

            service.SetCulture("zq");

            Assert.Equal("General", service.GetString("Settings_General", "missing"));
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    [Fact]
    public void MissingEnglishLocale_DoesNotSelectArbitraryLanguageFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.LocalizationTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "fr.json"),
            "{\"LocalizationProbe\":\"francais\"}");

        try
        {
            var service = new LocalizationService(directory, "zq");

            Assert.Equal("safe-default", service.GetString("LocalizationProbe", "safe-default"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TemporarilyUnavailableLocale_PreservesCurrentInMemoryLanguage()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.LocalizationTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "en.json"), "{\"Settings_General\":\"General\"}");
        File.WriteAllText(Path.Combine(directory, "ru.json"), "{\"Settings_General\":\"Общие\"}");
        var unavailablePath = Path.Combine(directory, "zq.json");
        File.WriteAllText(unavailablePath, "{\"Settings_General\":\"Temporary\"}");

        try
        {
            var service = new LocalizationService(directory, "ru");
            using var fileLock = new FileStream(
                unavailablePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            service.SetCulture("zq");

            Assert.Equal("Общие", service.GetString("Settings_General", "missing"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
