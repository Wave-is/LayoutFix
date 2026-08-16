using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public class OfflineTranslationResultGuardTests
{
    [Theory]
    [InlineData("Привет, мир!")]
    [InlineData("\"Привет, мир!\"")]
    [InlineData("Russian translation: Привет, мир!")]
    public void TryAccept_AcceptsAndCleansCyrillicTranslation(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Hello world.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.True(accepted);
        Assert.Equal("Привет, мир!", translation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hello, world!")]
    [InlineData("Russian translation: Hello world.")]
    [InlineData("<start_of_turn>model\nПривет мир.")]
    public void TryAccept_RejectsEmptyEchoWrongScriptOrControlMarkers(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Hello world.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsRunawayOutputForShortSource()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Hello world.",
            "ru",
            string.Concat(Enumerable.Repeat("Привет мир. ", 20)),
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsCyrillicEchoForLatinTarget()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Привет, мир.",
            "en",
            "Здравствуй, мир.",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AllowsDifferentLatinTranslation()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Hello world.",
            "es",
            "Hola, mundo.",
            out var translation);

        Assert.True(accepted);
        Assert.Equal("Hola, mundo.", translation);
    }

    [Theory]
    [InlineData("Спасибо за помощь.")]
    [InlineData("Дякую за вашу помощь.")]
    [InlineData("Դякую за вашу допомогу.")]
    [InlineData("Пожалуйста, привет!")]
    [InlineData("Текст содержит буквы ё и ы.")]
    public void TryAccept_RejectsRussianForUkrainianTarget(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Thank you for your help.",
            "uk",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsUkrainianForUkrainianTarget()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Thank you for your help.",
            "uk",
            "Дякую за вашу допомогу.",
            out var translation);

        Assert.True(accepted);
        Assert.Equal("Дякую за вашу допомогу.", translation);
    }

    [Theory]
    [InlineData("Дякую за вашу допомогу.")]
    [InlineData("Привіт, світе!")]
    public void TryAccept_RejectsUkrainianForRussianTarget(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Thank you for your help.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsTranslationThatPreservesTechnicalTokens()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Save report.pdf to C:\\Work, press Ctrl+S, then open https://example.com/help and keep {name}.",
            "ru",
            "Сохраните report.pdf в C:\\Work, нажмите Ctrl+S, затем откройте https://example.com/help и сохраните {name}.",
            out var translation);

        Assert.True(accepted);
        Assert.Contains("report.pdf", translation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Сохраните report.docx в C:\\Work, нажмите Ctrl+S, затем откройте https://example.com/help и сохраните {name}.")]
    [InlineData("Сохраните report.pdf в D:\\Work, нажмите Ctrl+S, затем откройте https://example.com/help и сохраните {name}.")]
    [InlineData("Сохраните report.pdf в C:\\Work, нажмите Ctrl+Shift+S, затем откройте https://example.com/help и сохраните {name}.")]
    [InlineData("Сохраните report.pdf в C:\\Work, нажмите Ctrl+S, затем откройте https://example.org/help и сохраните {name}.")]
    [InlineData("Сохраните report.pdf в C:\\Work, нажмите Ctrl+S, затем откройте https://example.com/help и сохраните {имя}.")]
    [InlineData("Сохраните report.pdf и extra.txt в C:\\Work, нажмите Ctrl+S, затем откройте https://example.com/help и сохраните {name}.")]
    public void TryAccept_RejectsChangedTechnicalToken(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Save report.pdf to C:\\Work, press Ctrl+S, then open https://example.com/help and keep {name}.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsUnchangedQuantitativeTokens()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Keep 3 copies until 09:30 on 2026-08-11; progress must reach 100%.",
            "ru",
            "Храните 3 копии до 09:30 2026-08-11; прогресс должен достичь 100%.",
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData("Храните 4 копии до 09:30 2026-08-11; прогресс должен достичь 100%.")]
    [InlineData("Храните 3 копии до 09:45 2026-08-11; прогресс должен достичь 100%.")]
    [InlineData("Храните 3 копии до 09:30 2026/08/11; прогресс должен достичь 100%.")]
    [InlineData("Храните 3 копии до 09:30 2026-08-11; прогресс должен достичь 90%.")]
    [InlineData("Храните 3 копии до 09:30 2026-08-11; прогресс должен достичь 100% за 2 минуты.")]
    public void TryAccept_RejectsChangedOrAddedQuantitativeToken(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Keep 3 copies until 09:30 on 2026-08-11; progress must reach 100%.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData("Do not delete 3 backups.", "Не удаляйте 3 резервные копии.", "ru")]
    [InlineData("Never share the password.", "Никогда не сообщайте пароль.", "ru")]
    [InlineData("Не отправляйте 2 пароля.", "Do not send 2 passwords.", "en")]
    [InlineData("Ніколи не закривайте вікно.", "Never close the window.", "en")]
    public void TryAccept_AcceptsPreservedExplicitNegation(
        string source,
        string rawTranslation,
        string targetLanguage)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            targetLanguage,
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData("Do not delete 3 backups.", "Удалите 3 резервные копии.", "ru")]
    [InlineData("Never share the password.", "Сообщите пароль.", "ru")]
    [InlineData("Не отправляйте 2 пароля.", "Send 2 passwords.", "en")]
    [InlineData("Ніколи не закривайте вікно.", "Close the window.", "en")]
    public void TryAccept_RejectsDroppedExplicitNegation(
        string source,
        string rawTranslation,
        string targetLanguage)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            targetLanguage,
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData(
        "Do not restart the computer until the update finishes.",
        "Не перезавантажуйте комп'ютер до завершення оновлення.",
        "uk")]
    [InlineData(
        "Do not reboot the computer until the update finishes.",
        "Не перезагружайте компьютер до завершения обновления.",
        "ru")]
    [InlineData(
        "Не перезапускайте компьютер до завершения обновления.",
        "Do not restart the computer until the update finishes.",
        "en")]
    public void TryAccept_AcceptsPreservedRestartConcept(
        string source,
        string rawTranslation,
        string targetLanguage)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            targetLanguage,
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData(
        "Do not restart the computer until the update finishes.",
        "Не запускайте комп'ютер до завершення оновлення.",
        "uk")]
    [InlineData(
        "Do not reboot the computer until the update finishes.",
        "Не запускайте компьютер до завершения обновления.",
        "ru")]
    [InlineData(
        "Не перезапускайте компьютер до завершения обновления.",
        "Do not start the computer until the update finishes.",
        "en")]
    public void TryAccept_RejectsLostRestartConcept(
        string source,
        string rawTranslation,
        string targetLanguage)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            targetLanguage,
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_IgnoresNegationInsideProtectedCode()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Run `do not delete` now.",
            "ru",
            "Запустите `do not delete` сейчас.",
            out _);

        Assert.True(accepted);
    }

    [Fact]
    public void TryAccept_RejectsDroppedRepeatedTechnicalToken()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Open report.pdf, copy report.pdf, then press Ctrl+S.",
            "ru",
            "Откройте и скопируйте report.pdf, затем нажмите Ctrl+S.",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsMixedLatinProseButAllowsPreservedTechnicalTokens()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Save report.pdf and press Ctrl+S.",
            "ru",
            "Сохраните файл Save report.pdf и нажмите Ctrl+S.",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsMatchingParagraphBreaks()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Open settings.\r\nRestart the application.",
            "ru",
            "Откройте настройки.\nПерезапустите приложение.",
            out var translation);

        Assert.True(accepted);
        Assert.Contains('\n', translation);
    }

    [Fact]
    public void TryAccept_AcceptsStructuredTranslationWithIdentifiersAndInlineCode()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Update LayoutFix:\n- Open `settings.json`.\n- Enable user_name.\n- Press Ctrl+S.",
            "ru",
            "Обновите LayoutFix:\n- Откройте `settings.json`.\n- Включите user_name.\n- Нажмите Ctrl+S.",
            out var translation);

        Assert.True(accepted);
        Assert.Contains("LayoutFix", translation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Обновите LayoutFix:\nОткройте `settings.json`.\n- Включите user_name.\n- Нажмите Ctrl+S.")]
    [InlineData("Обновите LayoutFix:\n* Откройте `settings.json`.\n- Включите user_name.\n- Нажмите Ctrl+S.")]
    [InlineData("Обновите LayoutFix:\n- Откройте `settings.json`.\n  - Включите user_name.\n- Нажмите Ctrl+S.")]
    public void TryAccept_RejectsChangedListStructure(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Update LayoutFix:\n- Open `settings.json`.\n- Enable user_name.\n- Press Ctrl+S.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsAddedMarkdownStructure()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Open settings.\nRestart the application.",
            "ru",
            "# Откройте настройки.\nПерезапустите приложение.",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsNestedMarkdownAndExactFencedCode()
    {
        const string source =
            "Deploy **LayoutFix**:\n" +
            "1. Open settings.\n" +
            "   - Run this command:\n" +
            "```powershell\n" +
            "layoutfix.exe --safe_mode\n" +
            "```\n" +
            "2. Remove ~~old~~ configuration.";
        const string rawTranslation =
            "Разверните **LayoutFix**:\n" +
            "1. Откройте настройки.\n" +
            "   - Выполните эту команду:\n" +
            "```powershell\n" +
            "layoutfix.exe --safe_mode\n" +
            "```\n" +
            "2. Удалите ~~старую~~ конфигурацию.";

        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.True(accepted);
        Assert.Contains("layoutfix.exe --safe_mode", translation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("```powershell\nlayoutfix.exe --unsafe_mode\n```")]
    [InlineData("```cmd\nlayoutfix.exe --safe_mode\n```")]
    public void TryAccept_RejectsChangedFencedCode(string translatedBlock)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Run this command:\n```powershell\nlayoutfix.exe --safe_mode\n```",
            "ru",
            $"Выполните эту команду:\n{translatedBlock}",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsRemovedStrongMarkdownMarkers()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Open **important** settings.",
            "ru",
            "Откройте важные настройки.",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsMarkdownTableRelativeLinksAndReferences()
    {
        const string source =
            "| Feature | Guide |\n" +
            "| :--- | ---: |\n" +
            "| Offline translation | [Read more](/docs/translate#offline) |\n" +
            "\n" +
            "See [settings][config] and <https://example.com/help>.\n" +
            "\n" +
            "[config]: ./settings.md";
        const string rawTranslation =
            "| Функция | Руководство |\n" +
            "| :--- | ---: |\n" +
            "| Офлайн-перевод | [Подробнее](/docs/translate#offline) |\n" +
            "\n" +
            "Смотрите [настройки][config] и <https://example.com/help>.\n" +
            "\n" +
            "[config]: ./settings.md";

        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.True(accepted);
        Assert.Contains("[Подробнее](/docs/translate#offline)", translation, StringComparison.Ordinal);
        Assert.Contains("[config]: ./settings.md", translation, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAccept_AcceptsInlineCodePipeAndEscapedNestedLink()
    {
        const string source =
            "| Expression | Guide |\n" +
            "| --- | --- |\n" +
            "| ``left | `right` `` | [array \\[index\\]](./docs/setup_(advanced).md) |";
        const string rawTranslation =
            "| Выражение | Руководство |\n" +
            "| --- | --- |\n" +
            "| ``left | `right` `` | [массив \\[индекс\\]](./docs/setup_(advanced).md) |";

        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData(
        "Open [array \\[index\\]](./docs/setup_(advanced).md).",
        "Откройте [массив \\[индекс\\]](./docs/setup_(advanced).txt).")]
    [InlineData(
        "Open [API [advanced]](./docs/setup_(advanced).md).",
        "Откройте [API [расширенный]](./docs/setup_(advanced).txt).")]
    public void TryAccept_RejectsChangedComplexMarkdownLinkDestination(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsChangedMultiBacktickCodeSpan()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Compare ``left | `right` `` now.",
            "ru",
            "Сравните ``left | `wrong` `` сейчас.",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void PromptProtector_RestoresComplexMarkdownTokensExactly()
    {
        const string source =
            "| ``left | `right` `` | [Read \\[advanced\\] guide](./docs/setup_(advanced).md) |";
        var protection = OfflineTranslationPromptProtector.Protect(source);

        Assert.DoesNotContain("``left | `right` ``", protection.ProtectedText, StringComparison.Ordinal);
        Assert.DoesNotContain("./docs/setup_(advanced).md", protection.ProtectedText, StringComparison.Ordinal);
        Assert.Contains("{LF_PROTECTED_0000}", protection.ProtectedText, StringComparison.Ordinal);
        Assert.Contains("{LF_PROTECTED_0001}", protection.ProtectedText, StringComparison.Ordinal);

        var modelOutput = protection.ProtectedText.Replace(
            "Read \\[advanced\\] guide",
            "Читать \\[расширенное\\] руководство",
            StringComparison.Ordinal);
        Assert.True(protection.TryRestore(modelOutput, out var restored));
        Assert.Contains("``left | `right` ``", restored, StringComparison.Ordinal);
        Assert.Contains("(./docs/setup_(advanced).md)", restored, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Перевод без placeholder")]
    [InlineData("{LF_PROTECTED_0000} {LF_PROTECTED_0000}")]
    public void PromptProtector_RejectsMissingOrDuplicatedPlaceholder(string modelOutput)
    {
        var protection = OfflineTranslationPromptProtector.Protect("Open `settings.json` now.");

        Assert.False(protection.TryRestore(modelOutput, out var restored));
        Assert.Empty(restored);
    }

    [Theory]
    [InlineData("Алиса встретится с Бобом в Лондоне завтра.")]
    [InlineData("Алиса завтра встретит Боба в Лондоне.")]
    public void TryAccept_AcceptsPhoneticTransliterationOfMultipleNames(
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Alice will meet Bob in London tomorrow.",
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData("Алла встретится с Бобом в Лондоне завтра.")]
    [InlineData("Алиса встретится с Борисом в Лондоне завтра.")]
    [InlineData("Алиса встретится с Бобом в Ливерпуле завтра.")]
    public void TryAccept_RejectsSubstitutedMultipleNames(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Alice will meet Bob in London tomorrow.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData("Call Alice tomorrow.", "Позвоните Алисе завтра.")]
    [InlineData("Meet Bob tomorrow.", "Встретьте Боба завтра.")]
    [InlineData("Alice will call tomorrow.", "Алиса позвонит завтра.")]
    public void TryAccept_AcceptsSingleNameWithStrongContext(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData("Call Alice tomorrow.", "Позвоните Алле завтра.")]
    [InlineData("Meet Bob tomorrow.", "Встретьте Бориса завтра.")]
    [InlineData("Alice will call tomorrow.", "Алла позвонит завтра.")]
    public void TryAccept_RejectsSubstitutedSingleNameWithStrongContext(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData(
        "Olivia will meet Lucas near Madrid on Friday.",
        "Оливия встретится с Лукасом недалеко от Мадрида в пятницу.")]
    [InlineData(
        "Sophia will call Maria tomorrow.",
        "София позвонит Марии завтра.")]
    public void TryAccept_AcceptsCommonIaAndPhProperNameTransliterations(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData(
        "Olivia will meet Lucas near Madrid on Friday.",
        "Ольга встретится с Лукасом недалеко от Мадрида в пятницу.")]
    [InlineData(
        "Sophia will call Maria tomorrow.",
        "Светлана позвонит Марии завтра.")]
    public void TryAccept_RejectsSubstitutedIaAndPhProperNames(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData("Theodore will call tomorrow.", "Теодор позвонит завтра.")]
    [InlineData("Jennifer will call tomorrow.", "Дженнифер позвонит завтра.")]
    [InlineData("Alex will call tomorrow.", "Алекс позвонит завтра.")]
    public void TryAccept_AcceptsCommonThJAndXProperNameTransliterations(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData("Theodore will call tomorrow.", "Фёдор позвонит завтра.")]
    [InlineData("Jennifer will call tomorrow.", "Жанна позвонит завтра.")]
    [InlineData("Alex will call tomorrow.", "Антон позвонит завтра.")]
    public void TryAccept_RejectsSubstitutedThJAndXProperNames(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData("Train will arrive tomorrow.", "Поезд прибудет завтра.")]
    [InlineData("Contact Support before closing.", "Свяжитесь со службой поддержки перед закрытием.")]
    [InlineData("Open Settings now.", "Откройте настройки сейчас.")]
    [InlineData("Run tests in March.", "Запустите тесты в марте.")]
    [InlineData("Write in English.", "Пишите на английском языке.")]
    public void TryAccept_DoesNotTreatCommonTitleCaseWordsAsNames(
        string source,
        string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData(
        "| Функция | Руководство |\n| :--- | ---: |\n| Перевод | [Подробнее](/docs/translate#online) |")]
    [InlineData(
        "| Функция | Руководство\n| :--- | ---: |\n| Перевод | [Подробнее](/docs/translate#offline) |")]
    [InlineData(
        "| Функция | Руководство |\n| ---: | :--- |\n| Перевод | [Подробнее](/docs/translate#offline) |")]
    public void TryAccept_RejectsChangedMarkdownTableOrLink(string rawTranslation)
    {
        const string source =
            "| Feature | Guide |\n" +
            "| :--- | ---: |\n" +
            "| Translation | [Read more](/docs/translate#offline) |";

        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_RejectsChangedMarkdownReferenceId()
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Open [settings][config].\n\n[config]: ./settings.md",
            "ru",
            "Откройте [настройки][configuration].\n\n[configuration]: ./settings.md",
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Fact]
    public void TryAccept_AcceptsMultipleExactFencedCodeBlocks()
    {
        const string source =
            "Read the configuration:\n~~~json\n{\"safe\": true}\n~~~\n" +
            "Run the command:\n```powershell\nlayoutfix.exe --safe_mode\n```";
        const string rawTranslation =
            "Прочитайте конфигурацию:\n~~~json\n{\"safe\": true}\n~~~\n" +
            "Выполните команду:\n```powershell\nlayoutfix.exe --safe_mode\n```";

        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out _);

        Assert.True(accepted);
    }

    [Fact]
    public void TryAccept_RejectsChangedSecondFencedCodeBlock()
    {
        const string source =
            "Read the configuration:\n~~~json\n{\"safe\": true}\n~~~\n" +
            "Run the command:\n```powershell\nlayoutfix.exe --safe_mode\n```";
        const string rawTranslation =
            "Прочитайте конфигурацию:\n~~~json\n{\"safe\": true}\n~~~\n" +
            "Выполните команду:\n```powershell\nlayoutfix.exe --unsafe_mode\n```";

        var accepted = OfflineTranslationResultGuard.TryAccept(
            source,
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }

    [Theory]
    [InlineData("Откройте настройки. Перезапустите приложение.")]
    [InlineData("Откройте настройки.\n\nПерезапустите приложение.")]
    public void TryAccept_RejectsChangedParagraphBreakCount(string rawTranslation)
    {
        var accepted = OfflineTranslationResultGuard.TryAccept(
            "Open settings.\nRestart the application.",
            "ru",
            rawTranslation,
            out var translation);

        Assert.False(accepted);
        Assert.Empty(translation);
    }
}
