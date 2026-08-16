using System.Diagnostics;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.IntegrationTests;

public class AutoConversionServiceTests
{
    [Fact]
    public async Task UsesActualUnicodeTextAndCorrectsSequentially()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var activeWindow = new FakeActiveWindowProvider { LayoutCode = "ru-RU" };
        var memory = new AutoCorrectionMemory();
        using var service = CreateService(
            hook,
            input,
            activeWindow,
            new FakeDictionaryAnalyzer(true),
            correctionMemory: memory);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"), ("l", "д"), ("o", "щ"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(6, input.BackspaceCount);
        Assert.Equal(["hello", " "], input.SentText);
        Assert.Equal("en-US", activeWindow.SwitchedLayout);
        Assert.True(memory.TryPrepareUndo(
            new TextSelection("hello ", activeWindow.CaptureActiveWindow(), true),
            out var undo));
        Assert.Equal("руддщ ", undo.RestoredSelectionText);
    }

    [Fact]
    public async Task DictionaryCorrection_WaitsForTargetLayoutBeforeVisibleInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var activeWindow = new FakeActiveWindowProvider
        {
            LayoutCode = "ru-RU",
            ApplySwitchImmediately = false
        };
        using var service = CreateService(
            hook,
            input,
            activeWindow,
            new FakeDictionaryAnalyzer(true));

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"), ("l", "д"), ("o", "щ"));
        hook.Press("space", " ");
        await activeWindow.SwitchRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);

        activeWindow.CompleteSwitch();
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("en-US", activeWindow.LayoutCode);
        Assert.Equal(6, input.BackspaceCount);
        Assert.Equal(["hello", " "], input.SentText);
    }

    [Fact]
    public async Task DictionaryCorrection_LayoutActivationTimeoutDoesNotEditText()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var activeWindow = new FakeActiveWindowProvider
        {
            LayoutCode = "ru-RU",
            ApplySwitchImmediately = false
        };
        using var service = CreateService(
            hook,
            input,
            activeWindow,
            new FakeDictionaryAnalyzer(true));

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"), ("l", "д"), ("o", "щ"));
        hook.Press("space", " ");
        await activeWindow.SwitchRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(350);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
        Assert.Equal("ru-RU", activeWindow.LayoutCode);
    }

    [Fact]
    public async Task UserAutocorrectTakesPrecedenceWithoutGibberishDetection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["the", " "], input.SentText);
    }

    [Theory]
    [InlineData("yt")]
    [InlineData("jy")]
    [InlineData("nj")]
    public async Task TwoCharacterIdentifiers_DoNotTriggerDictionaryLayoutGuess(string token)
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var dictionary = new FakeDictionaryAnalyzer(true);
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { LayoutCode = "en-US" },
            dictionary);

        hook.Type(token.Select(character =>
            (character.ToString(), character.ToString())).ToArray());
        hook.Press("space", " ");
        await Task.Delay(150);

        Assert.Equal(0, dictionary.CallCount);
        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task TechnicalPunctuation_DoesNotTriggerDictionaryLayoutGuess()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var dictionary = new FakeDictionaryAnalyzer(true);
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { LayoutCode = "en-US" },
            dictionary);

        foreach (var trigger in new[]
                 {
                     "@", "_", "/", "\\", ":", "#",
                     "[", "]", "{", "}", "(", ")"
                 })
        {
            hook.Type(("y", "y"), ("t", "t"), ("n", "n"));
            hook.Press(trigger, trigger);
        }
        await Task.Delay(300);

        Assert.Equal(0, dictionary.CallCount);
        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task ExplicitUserRule_StillAppliesBeforeTechnicalPunctuation()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("_", "_");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(4, input.BackspaceCount);
        Assert.Equal(["the", "_"], input.SentText);
    }

    [Fact]
    public async Task RareShortDictionaryCandidate_DoesNotTriggerAutomaticInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var dictionary = new FakeDictionaryAnalyzer(
            result: true,
            isConfidentForAutomaticCorrection: false);
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { LayoutCode = "en-US" },
            dictionary);

        hook.Type(("y", "y"), ("k", "k"), ("j", "j"));
        hook.Press("space", " ");
        await Task.Delay(150);

        Assert.Equal(1, dictionary.CallCount);
        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task UserAutocorrect_PreservesTitleCaseForLowercaseReplacement()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "T"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["The", " "], input.SentText);
    }

    [Fact]
    public async Task UserAutocorrect_PreservesAllCapsForLowercaseReplacement()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "T"), ("e", "E"), ("h", "H"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["THE", " "], input.SentText);
    }

    [Fact]
    public async Task UserAutocorrect_DoesNotOverrideExplicitReplacementCasing()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["omg"] = "Oh My God";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("o", "O"), ("m", "M"), ("g", "G"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["Oh My God", " "], input.SentText);
    }

    [Fact]
    public async Task UserAutocorrect_PreservesTrailingQuoteOutsideMatchedCore()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "T"), ("e", "e"), ("h", "h"), ("quote", "'"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(5, input.BackspaceCount);
        Assert.Equal(["The'", " "], input.SentText);
    }

    [Fact]
    public async Task FullWordUserException_PreventsTrimmedFallbackCorrection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserExceptions.Add("teh'");
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(true),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"), ("quote", "'"));
        hook.Press("space", " ");
        await Task.Delay(150);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task CoreUserException_PreventsTrimmedFallbackCorrection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserExceptions.Add("teh");
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(true),
            settings);

        hook.Type(("quote", "'"), ("t", "t"), ("e", "e"), ("h", "h"), ("quote", "'"));
        hook.Press("space", " ");
        await Task.Delay(150);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task DeadKeyComposition_ResetsCandidateAndRecoversForNextWord()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        settings.Current.UserAutocorrect["ok"] = "okay";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("dead-key", isDeadKey: true);
        hook.Press("space", " ");
        hook.Type(("o", "o"), ("k", "k"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, input.BackspaceCount);
        Assert.Equal(["okay", " "], input.SentText);
    }

    [Fact]
    public async Task MultiCharacterTextObservation_DeletesEveryInsertedTextElement()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("ime-commit", "te"), ("h", "h"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(4, input.BackspaceCount);
        Assert.Equal(["the", " "], input.SentText);
    }

    [Fact]
    public async Task BackspaceAfterMultiCharacterObservation_RemovesOnlyLastVisibleTextElement()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["th"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("ime-commit", "te"));
        hook.Press("backspace");
        hook.Type(("h", "h"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, input.BackspaceCount);
        Assert.Equal(["the", " "], input.SentText);
    }

    [Fact]
    public async Task CancelsCorrectionIfUserContinuesTyping()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { LayoutCode = "ru-RU" },
            new FakeDictionaryAnalyzer(true));

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"), ("l", "д"), ("o", "щ"));
        hook.Press("space", " ");
        hook.Press("x", "ч");
        await Task.Delay(150);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task HeldModifier_PreventsInjectionAndLaterInputCancelsCorrection()
    {
        var hook = new FakeKeyboardHook();
        var input = new BlockingModifierInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.ModifierWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);

        // KeyboardHook does not publish the modifier-only key-down, but the first
        // modified key still advances the input generation while the worker waits.
        hook.Press("c", "", ctrl: true);
        input.ReleaseModifier();
        await Task.Delay(100);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task ModifierReleaseTimeout_CancelsCorrectionWithoutInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new ModifierTimeoutInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.TimeoutObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(250, input.ObservedTimeoutMilliseconds);
        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Theory]
    [InlineData("Photoshop")]
    [InlineData("Illustrator")]
    [InlineData("InDesign")]
    [InlineData("Acrobat")]
    [InlineData("AcroRd32")]
    [InlineData("AfterFX")]
    [InlineData("Adobe Premiere Pro")]
    [InlineData("Antigravity")]
    [InlineData("Cursor.exe")]
    [InlineData("Code - Insiders.exe")]
    [InlineData("rider64")]
    [InlineData("pycharm64.exe")]
    [InlineData("WindowsTerminal")]
    [InlineData("conhost.exe")]
    [InlineData("pwsh")]
    [InlineData("wezterm-gui.exe")]
    [InlineData("mstsc.exe")]
    [InlineData("wfica32")]
    public async Task SafetyDefaultProcessesAreExcludedFromAutomaticCorrection(
        string processName)
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var dictionary = new FakeDictionaryAnalyzer(true);
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { ProcessName = processName, LayoutCode = "ru-RU" },
            dictionary);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"));
        hook.Press("space", " ");
        await Task.Delay(100);

        Assert.Equal(0, dictionary.CallCount);
        Assert.Equal(0, input.BackspaceCount);
    }

    [Fact]
    public async Task DottedExtensionlessProcessNameMatchesConfiguredExeBlacklist()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var dictionary = new FakeDictionaryAnalyzer(true);
        var settings = CreateSettings();
        settings.Current.AutoConversionBlacklistedProcesses =
            [@"C:\Program Files\LayoutFix\LayoutFix.WindowsE2E.exe"];
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider
            {
                ProcessName = "LayoutFix.WindowsE2E",
                LayoutCode = "ru-RU"
            },
            dictionary,
            settings);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"));
        hook.Press("space", " ");
        await Task.Delay(100);

        Assert.Equal(0, dictionary.CallCount);
        Assert.Equal(0, input.BackspaceCount);
    }

    [Fact]
    public async Task NullBlacklistEntryDoesNotBreakAutomaticCorrection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var dictionary = new FakeDictionaryAnalyzer(true);
        var settings = CreateSettings();
        settings.Current.AutoConversionBlacklistedProcesses = [null!];
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider
            {
                ProcessName = "notepad",
                LayoutCode = "ru-RU"
            },
            dictionary,
            settings);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"));
        hook.Press("space", " ");
        await Task.Delay(100);

        Assert.Equal(1, dictionary.CallCount);
        Assert.True(input.BackspaceCount > 0);
    }

    [Fact]
    public async Task HookCallbackDoesNotWaitForDictionaryAnalysis()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        using var dictionary = new BlockingDictionaryAnalyzer();
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { LayoutCode = "ru-RU" },
            dictionary);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"));
        var stopwatch = Stopwatch.StartNew();
        hook.Press("space", " ");
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        await dictionary.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        dictionary.Release();
    }

    [Fact]
    public async Task BoundedQueue_RecoversAfterInputBurstDuringSlowAnalysis()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var dictionary = new BlockingDictionaryAnalyzer();
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider { LayoutCode = "en-US" },
            dictionary,
            settings);

        hook.Type(("h", "h"), ("e", "e"), ("l", "l"));
        hook.Press("space", " ");
        await dictionary.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        for (var index = 0; index < 2_000; index++)
            hook.Press("a", "a", ctrl: true);

        dictionary.Release();
        await Task.Delay(150);
        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("the", input.SentText);
    }

    [Fact]
    public async Task FailedReplacement_RestoresOriginalWordAndRealEnterTrigger()
    {
        var hook = new FakeKeyboardHook();
        var input = new FailingReplacementInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = new AutoConversionService(
            hook,
            new FakeMouseHook(),
            settings,
            new FakeDictionaryAnalyzer(false),
            input,
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new NullLogger(),
            new NullSoundService(),
            new FakeActiveWindowProvider());

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("enter");
        await input.RollbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(4, input.BackspaceCount);
        Assert.Equal(["the", "teh"], input.TextAttempts);
        Assert.Equal(["enter"], input.KeysSent);
    }

    [Fact]
    public async Task FailedReplacement_FocusChangeSkipsRollbackIntoNewTarget()
    {
        var hook = new FakeKeyboardHook();
        var activeWindow = new FakeActiveWindowProvider();
        var input = new FailingReplacementInputInjector(
            () => activeWindow.IsCurrentWindow = false);
        var logger = new RollbackRecordingLogger();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            activeWindow,
            new FakeDictionaryAnalyzer(false),
            settings,
            logger: logger);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("enter");
        await input.FailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await logger.RollbackSkipped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(4, input.BackspaceCount);
        Assert.Equal(["the"], input.TextAttempts);
        Assert.Empty(input.KeysSent);
    }

    [Fact]
    public async Task FailedTrigger_RemovesReplacementBeforeRestoringOriginalWord()
    {
        var hook = new FakeKeyboardHook();
        var input = new FailingTriggerInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = new AutoConversionService(
            hook,
            new FakeMouseHook(),
            settings,
            new FakeDictionaryAnalyzer(false),
            input,
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            new NullLogger(),
            new NullSoundService(),
            new FakeActiveWindowProvider());

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("enter");
        await input.RollbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([4, 3], input.BackspaceBatches);
        Assert.Equal(["the", "teh"], input.SentText);
        Assert.Equal(["enter", "enter"], input.KeysSent);
    }

    [Fact]
    public async Task PartialDeletion_RestoresOnlyDeletedOriginalSuffix()
    {
        var hook = new FakeKeyboardHook();
        var input = new PartialProgressInputInjector(PartialFailureStage.Deletion, 2);
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.RollbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([4], input.BackspaceBatches);
        Assert.Equal(["h", " "], input.TextAttempts);
    }

    [Fact]
    public async Task PartialReplacement_RemovesAcceptedPrefixBeforeRestoringOriginal()
    {
        var hook = new FakeKeyboardHook();
        var input = new PartialProgressInputInjector(PartialFailureStage.Replacement, 2);
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.RollbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([4, 2], input.BackspaceBatches);
        Assert.Equal(["the", "teh", " "], input.TextAttempts);
    }

    [Fact]
    public async Task PartialTrigger_RemovesAcceptedTriggerAndReplacementBeforeRestore()
    {
        var hook = new FakeKeyboardHook();
        var input = new PartialProgressInputInjector(PartialFailureStage.Trigger, 1);
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.RollbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([4, 1, 3], input.BackspaceBatches);
        Assert.Equal(["the", " ", "teh", " "], input.TextAttempts);
    }

    [Fact]
    public async Task SoundFailureAfterSuccessfulInjectionDoesNotRewriteText()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        settings.Current.SoundEnabled = true;
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings,
            soundService: new ThrowingSoundService());

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await input.CorrectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        Assert.Equal(4, input.BackspaceCount);
        Assert.Equal(["the", " "], input.SentText);
    }

    [Fact]
    public async Task SecureTarget_PreventsAutomaticCorrectionBeforeAnyInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings,
            new DenyTextTargetGuard());

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await Task.Delay(150);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task SecureTarget_IsRecheckedImmediatelyBeforeInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings,
            new AllowThenDenyTextTargetGuard());

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await Task.Delay(150);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task InputDuringTargetRecheck_CancelsBeforeAnyInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        var guard = new AllowThenBlockTextTargetGuard();
        using var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings,
            guard);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await guard.SecondCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        hook.Press("x", "x");
        guard.Release();
        await Task.Delay(100);

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task DictionaryCorrection_InputDuringTargetRecheckRestoresOriginalLayout()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var activeWindow = new FakeActiveWindowProvider { LayoutCode = "ru-RU" };
        var guard = new AllowThenBlockTextTargetGuard();
        using var service = CreateService(
            hook,
            input,
            activeWindow,
            new FakeDictionaryAnalyzer(true),
            targetGuard: guard);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"), ("l", "д"), ("o", "щ"));
        hook.Press("space", " ");
        await guard.SecondCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("en-US", activeWindow.LayoutCode);

        hook.Press("x", "x");
        guard.Release();
        await activeWindow.SecondSwitchRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("ru-RU", activeWindow.LayoutCode);
        Assert.Equal(["en-US", "ru-RU"], activeWindow.SwitchHistory);
        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task DictionaryCorrection_DisposeDuringTargetRecheckRestoresOriginalLayout()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var activeWindow = new FakeActiveWindowProvider { LayoutCode = "ru-RU" };
        var guard = new AllowThenBlockTextTargetGuard();
        var service = CreateService(
            hook,
            input,
            activeWindow,
            new FakeDictionaryAnalyzer(true),
            targetGuard: guard);

        hook.Type(("h", "р"), ("e", "у"), ("l", "д"), ("l", "д"), ("o", "щ"));
        hook.Press("space", " ");
        await guard.SecondCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("en-US", activeWindow.LayoutCode);

        var disposeTask = Task.Run(service.Dispose);
        await guard.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        guard.Release();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("ru-RU", activeWindow.LayoutCode);
        Assert.Equal(["en-US", "ru-RU"], activeWindow.SwitchHistory);
        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
    }

    [Fact]
    public async Task Dispose_DuringTargetRecheckPreventsLateInjection()
    {
        var hook = new FakeKeyboardHook();
        var input = new RecordingInputInjector();
        var settings = CreateSettings();
        settings.Current.UserAutocorrect["teh"] = "the";
        var guard = new AllowThenBlockTextTargetGuard();
        var service = CreateService(
            hook,
            input,
            new FakeActiveWindowProvider(),
            new FakeDictionaryAnalyzer(false),
            settings,
            guard);

        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await guard.SecondCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = Task.Run(service.Dispose);
        await guard.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        guard.Release();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, input.BackspaceCount);
        Assert.Empty(input.SentText);
        hook.Type(("t", "t"), ("e", "e"), ("h", "h"));
        hook.Press("space", " ");
        await Task.Delay(100);
        Assert.Equal(0, input.BackspaceCount);
    }

    private static AutoConversionService CreateService(
        FakeKeyboardHook hook,
        IInputInjector input,
        FakeActiveWindowProvider activeWindow,
        IDictionaryAnalyzer dictionary,
        FakeSettingsService? settings = null,
        ITextTargetGuard? targetGuard = null,
        IAutoCorrectionMemory? correctionMemory = null,
        ILoggerService? logger = null,
        ISoundService? soundService = null) => new(
            hook,
            new FakeMouseHook(),
            settings ?? CreateSettings(),
            dictionary,
            input,
            new FakeKeyboardLayoutManager(),
            new LayoutConverter(),
            logger ?? new NullLogger(),
            soundService ?? new NullSoundService(),
            activeWindow,
            targetGuard,
            correctionMemory);

    private sealed class BlockingModifierInputInjector : IInputInjector
    {
        private readonly TaskCompletionSource _modifierReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ModifierWaitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BackspaceCount { get; private set; }
        public List<string> SentText { get; } = [];

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key) =>
            Task.CompletedTask;

        public Task SendBackspacesAsync(int count)
        {
            BackspaceCount += count;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text)
        {
            SentText.Add(text);
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync() => Task.CompletedTask;

        public async Task WaitForModifiersReleaseAsync(int timeoutMs = 2000)
        {
            ModifierWaitStarted.TrySetResult();
            await _modifierReleased.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }

        public void ReleaseModifier() => _modifierReleased.TrySetResult();
    }

    private sealed class ModifierTimeoutInputInjector : IInputInjector
    {
        public TaskCompletionSource TimeoutObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ObservedTimeoutMilliseconds { get; private set; }
        public int BackspaceCount { get; private set; }
        public List<string> SentText { get; } = [];

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key) =>
            Task.CompletedTask;

        public Task SendBackspacesAsync(int count)
        {
            BackspaceCount += count;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text)
        {
            SentText.Add(text);
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync() => Task.CompletedTask;

        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000)
        {
            ObservedTimeoutMilliseconds = timeoutMs;
            TimeoutObserved.TrySetResult();
            throw new TimeoutException("simulated held modifier");
        }
    }

    private static FakeSettingsService CreateSettings() => new()
    {
        Current = new AppSettings
        {
            AutoConversionEnabled = true,
            BlacklistedProcesses = [],
            LayoutOrder = ["en-US", "ru-RU"]
        }
    };

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

        public void Type(params (string Key, string Text)[] input)
        {
            foreach (var item in input)
                Press(item.Key, item.Text);
        }

        public void Press(
            string key,
            string text = "",
            bool ctrl = false,
            bool isDeadKey = false) => HotkeyPressed?.Invoke(
            this,
            new HotkeyEventArgs(new HotkeyCombo
            {
                Key = key,
                VirtualKey = key.Length == 1 ? char.ToUpperInvariant(key[0]) : 0,
                Ctrl = ctrl
            }, text: text, isDeadKey: isDeadKey));

        public void Start() { }
        public void Stop() { }
        public void Dispose() => HotkeyPressed = null;
    }

    private sealed class FakeMouseHook : IMouseHook
    {
        public event EventHandler? MouseClicked
        {
            add { }
            remove { }
        }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class RecordingInputInjector : IInputInjector
    {
        public int BackspaceCount { get; private set; }
        public List<string> SentText { get; } = [];
        public TaskCompletionSource CorrectionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
        {
            if (key is "enter" or "tab") CorrectionCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SendBackspacesAsync(int count)
        {
            BackspaceCount += count;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text)
        {
            SentText.Add(text);
            if (SentText.Count >= 2) CorrectionCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync() => Task.CompletedTask;
        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => Task.CompletedTask;
    }

    private sealed class FailingReplacementInputInjector(Action? beforeFailure = null) : IInputInjector
    {
        private bool _failFirstText = true;
        public int BackspaceCount { get; private set; }
        public List<string> TextAttempts { get; } = [];
        public List<string> KeysSent { get; } = [];
        public TaskCompletionSource FailureObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RollbackCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
        {
            KeysSent.Add(key);
            RollbackCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SendBackspacesAsync(int count)
        {
            BackspaceCount += count;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text)
        {
            TextAttempts.Add(text);
            if (_failFirstText)
            {
                _failFirstText = false;
                beforeFailure?.Invoke();
                FailureObserved.TrySetResult();
                throw new InvalidOperationException("simulated replacement failure");
            }
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync() => Task.CompletedTask;
        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => Task.CompletedTask;
    }

    private sealed class FailingTriggerInputInjector : IInputInjector
    {
        private bool _failFirstTrigger = true;
        public List<int> BackspaceBatches { get; } = [];
        public List<string> SentText { get; } = [];
        public List<string> KeysSent { get; } = [];
        public TaskCompletionSource RollbackCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
        {
            KeysSent.Add(key);
            if (_failFirstTrigger)
            {
                _failFirstTrigger = false;
                throw new InvalidOperationException("simulated trigger failure");
            }
            RollbackCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SendBackspacesAsync(int count)
        {
            BackspaceBatches.Add(count);
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text)
        {
            SentText.Add(text);
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync() => Task.CompletedTask;
        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => Task.CompletedTask;
    }

    private sealed class PartialProgressInputInjector(
        PartialFailureStage failureStage,
        int affectedUnitCount) : IInputInjector
    {
        private bool _failed;
        public List<int> BackspaceBatches { get; } = [];
        public List<string> TextAttempts { get; } = [];
        public TaskCompletionSource RollbackCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key) =>
            Task.CompletedTask;

        public Task SendBackspacesAsync(int count)
        {
            BackspaceBatches.Add(count);
            if (!_failed && failureStage == PartialFailureStage.Deletion)
            {
                _failed = true;
                throw CreateFailure(InputInjectionOperation.Backspace, count);
            }

            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text)
        {
            TextAttempts.Add(text);
            if (!_failed &&
                ((failureStage == PartialFailureStage.Replacement && text == "the") ||
                 (failureStage == PartialFailureStage.Trigger && text == " ")))
            {
                _failed = true;
                throw CreateFailure(InputInjectionOperation.Text, text.Length);
            }

            if (_failed && text == " ")
                RollbackCompleted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync() => Task.CompletedTask;
        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => Task.CompletedTask;

        private InputInjectionException CreateFailure(
            InputInjectionOperation operation,
            int requestedUnitCount)
        {
            var requestedEventCount = requestedUnitCount * 2;
            var acceptedEventCount = Math.Min(
                requestedEventCount,
                Math.Max(0, affectedUnitCount * 2 - 1));
            return new InputInjectionException(
                operation,
                requestedUnitCount,
                Math.Min(requestedUnitCount, affectedUnitCount),
                requestedEventCount,
                acceptedEventCount);
        }
    }

    private enum PartialFailureStage
    {
        Deletion,
        Replacement,
        Trigger
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class FakeKeyboardLayoutManager : IKeyboardLayoutManager
    {
        private static readonly Layout English = new()
        {
            Code = "en-US",
            Keys = new Dictionary<string, string>
            {
                ["h"] = "h", ["e"] = "e", ["l"] = "l", ["o"] = "o"
            }
        };

        private static readonly Layout Russian = new()
        {
            Code = "ru-RU",
            Keys = new Dictionary<string, string>
            {
                ["h"] = "р", ["e"] = "у", ["l"] = "д", ["o"] = "щ"
            }
        };

        private static readonly IReadOnlyList<Layout> Layouts = [English, Russian];
        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => Layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => Layouts;
    }

    private sealed class FakeDictionaryAnalyzer(
        bool result,
        bool isConfidentForAutomaticCorrection = true) : IDictionaryAnalyzer
    {
        public int CallCount { get; private set; }
        public bool IsGibberish(string word, string currentLayout) =>
            TryGetCorrection(word, currentLayout, out _);

        public bool TryGetCorrection(
            string word,
            string currentLayout,
            out LayoutCorrectionSuggestion suggestion)
        {
            CallCount++;
            suggestion = result
                ? new LayoutCorrectionSuggestion(
                    "hello",
                    "en-US",
                    isConfidentForAutomaticCorrection)
                : default;
            return result;
        }
    }

    private sealed class BlockingDictionaryAnalyzer : IDictionaryAnalyzer, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsGibberish(string word, string currentLayout) =>
            TryGetCorrection(word, currentLayout, out _);

        public bool TryGetCorrection(
            string word,
            string currentLayout,
            out LayoutCorrectionSuggestion suggestion)
        {
            Entered.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(2));
            suggestion = default;
            return false;
        }

        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    private sealed class FakeActiveWindowProvider : IActiveWindowProvider
    {
        private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);
        public string ProcessName { get; set; } = "test-host";
        public string LayoutCode { get; set; } = "en-US";
        public string? SwitchedLayout { get; private set; }
        public List<string> SwitchHistory { get; } = [];
        public bool IsCurrentWindow { get; set; } = true;
        public bool ApplySwitchImmediately { get; set; } = true;
        public TaskCompletionSource SwitchRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondSwitchRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ActiveWindowContext CaptureActiveWindow() => Window;
        public bool IsSameActiveWindow(ActiveWindowContext context) =>
            IsCurrentWindow && context == Window;
        public string GetActiveProcessName() => ProcessName;
        public string GetActiveLayoutCode() => LayoutCode;
        public void SwitchToNextLayout() { }
        public bool TrySwitchToLayout(string layoutCode)
        {
            SwitchedLayout = layoutCode;
            SwitchHistory.Add(layoutCode);
            SwitchRequested.TrySetResult();
            if (SwitchHistory.Count == 2)
                SecondSwitchRequested.TrySetResult();
            if (ApplySwitchImmediately)
                LayoutCode = layoutCode;
            return true;
        }

        public void CompleteSwitch()
        {
            if (SwitchedLayout != null)
                LayoutCode = SwitchedLayout;
        }
    }

    private sealed class NullSoundService : ISoundService
    {
        public void PlaySwitchSound() { }
        public void PlayAutoConvertSound() { }
        public void PlayErrorSound() { }
    }

    private sealed class ThrowingSoundService : ISoundService
    {
        public void PlaySwitchSound() { }
        public void PlayAutoConvertSound() =>
            throw new InvalidOperationException("simulated optional sound failure");
        public void PlayErrorSound() { }
    }

    private sealed class DenyTextTargetGuard : ITextTargetGuard
    {
        public Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class AllowThenDenyTextTargetGuard : ITextTargetGuard
    {
        private int _callCount;

        public Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Interlocked.Increment(ref _callCount) == 1);
    }

    private sealed class AllowThenBlockTextTargetGuard : ITextTargetGuard
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;
        public TaskCompletionSource SecondCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
                return true;

            using var registration = cancellationToken.Register(
                () => CancellationObserved.TrySetResult());
            SecondCheckStarted.TrySetResult();
            await _release.Task;
            return true;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class NullLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    private sealed class RollbackRecordingLogger : ILoggerService
    {
        public TaskCompletionSource RollbackSkipped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void LogInfo(string message) { }

        public void LogWarning(string message)
        {
            if (message.Contains("rollback skipped", StringComparison.OrdinalIgnoreCase))
                RollbackSkipped.TrySetResult();
        }

        public void LogError(string message, Exception? ex = null) { }
    }
}
