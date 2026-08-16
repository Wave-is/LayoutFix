using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Infrastructure.Services;

public sealed class AutoConversionService : IDisposable
{
    private const int MaximumWordLength = 128;
    private const int MinimumAutomaticLayoutWordLength = 3;
    private const int ModifierReleaseTimeoutMilliseconds = 250;
    private const int LayoutActivationTimeoutMilliseconds = 250;
    private readonly IKeyboardHook _keyboardHook;
    private readonly IMouseHook _mouseHook;
    private readonly ISettingsService _settingsService;
    private readonly IDictionaryAnalyzer _dictionaryAnalyzer;
    private readonly IInputInjector _inputInjector;
    private readonly ILoggerService _logger;
    private readonly ISoundService _soundService;
    private readonly IActiveWindowProvider _activeWindowProvider;
    private readonly ITextTargetGuard? _targetGuard;
    private readonly IAutoCorrectionMemory? _correctionMemory;
    private readonly Channel<InputObservation> _inputQueue;
    private readonly Task _inputProcessor;
    private readonly List<string> _typedUnits = [];
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly object _lifecycleGate = new();

    private ActiveWindowContext _wordWindow;
    private string _wordLayout = string.Empty;
    private long _inputGeneration;
    private long _lastProcessedGeneration;
    private volatile bool _disposed;

    public AutoConversionService(
        IKeyboardHook keyboardHook,
        IMouseHook mouseHook,
        ISettingsService settingsService,
        IDictionaryAnalyzer dictionaryAnalyzer,
        IInputInjector inputInjector,
        IKeyboardLayoutManager layoutManager,
        ILayoutConverter layoutConverter,
        ILoggerService logger,
        ISoundService soundService,
        IActiveWindowProvider activeWindowProvider,
        ITextTargetGuard? targetGuard = null,
        IAutoCorrectionMemory? correctionMemory = null)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _settingsService = settingsService;
        _dictionaryAnalyzer = dictionaryAnalyzer;
        _inputInjector = inputInjector;
        ArgumentNullException.ThrowIfNull(layoutManager);
        ArgumentNullException.ThrowIfNull(layoutConverter);
        _logger = logger;
        _soundService = soundService;
        _activeWindowProvider = activeWindowProvider;
        _targetGuard = targetGuard;
        _correctionMemory = correctionMemory;

        _inputQueue = Channel.CreateBounded<InputObservation>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
        _inputProcessor = Task.Run(ProcessInputQueueAsync);

        _keyboardHook.HotkeyPressed += OnKeyPressed;
        _mouseHook.MouseClicked += OnMouseClicked;
    }

    private void OnMouseClicked(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var generation = Interlocked.Increment(ref _inputGeneration);
        _inputQueue.Writer.TryWrite(InputObservation.Reset(generation));
    }

    private void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        if (_disposed) return;
        var generation = Interlocked.Increment(ref _inputGeneration);
        if (e.Handled || !_settingsService.Current.AutoConversionEnabled)
        {
            _inputQueue.Writer.TryWrite(InputObservation.Reset(generation));
            return;
        }

        _inputQueue.Writer.TryWrite(InputObservation.KeyPress(
            e.Combo.Key,
            e.Text,
            e.Combo.VirtualKey,
            e.Combo.Ctrl,
            e.Combo.Alt,
            e.Combo.Win,
            e.IsDeadKey,
            generation));
    }

    private async Task ProcessInputQueueAsync()
    {
        try
        {
            await foreach (var observation in _inputQueue.Reader.ReadAllAsync(
                _shutdownCancellation.Token))
            {
                try
                {
                    if (_lastProcessedGeneration != 0 && observation.Generation != _lastProcessedGeneration + 1)
                        ResetWord();
                    _lastProcessedGeneration = observation.Generation;

                    if (observation.ShouldReset || !_settingsService.Current.AutoConversionEnabled)
                    {
                        ResetWord();
                        continue;
                    }

                    await ProcessKeyAsync(observation, _shutdownCancellation.Token);
                }
                catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
                {
                    ResetWord();
                    break;
                }
                catch (Exception ex)
                {
                    ResetWord();
                    _logger.LogError("Automatic conversion input processing failed", ex);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
        {
            ResetWord();
        }
    }

    private async Task ProcessKeyAsync(
        InputObservation observation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observation.Ctrl || observation.Alt || observation.Win)
        {
            ResetWord();
            return;
        }

        if (observation.IsDeadKey)
        {
            ResetWord();
            _logger.LogInfo("Automatic correction skipped an uncommitted dead-key composition.");
            return;
        }

        if (observation.Key.Equals("backspace", StringComparison.OrdinalIgnoreCase))
        {
            if (_typedUnits.Count > 0)
                _typedUnits.RemoveAt(_typedUnits.Count - 1);
            if (_typedUnits.Count == 0)
                ResetWord();
            return;
        }

        if (IsWordText(observation.Text))
        {
            var currentWindow = _activeWindowProvider.CaptureActiveWindow();
            if (!currentWindow.IsValid)
            {
                ResetWord();
                return;
            }

            if (_typedUnits.Count == 0 || currentWindow != _wordWindow)
            {
                ResetWord();
                if (IsAutoConversionBlacklisted())
                    return;
                if (_targetGuard != null &&
                    !await _targetGuard.CanModifyAsync(currentWindow, cancellationToken))
                    return;
                cancellationToken.ThrowIfCancellationRequested();

                _wordWindow = currentWindow;
                _wordLayout = _activeWindowProvider.GetActiveLayoutCode();
            }

            _typedUnits.AddRange(EnumerateTextElements(observation.Text));
            if (_typedUnits.Count > MaximumWordLength)
                ResetWord();
            return;
        }

        if (!TryGetTrigger(observation, out var trigger))
        {
            ResetWord();
            return;
        }

        if (_typedUnits.Count == 0)
            return;

        var word = string.Concat(_typedUnits);
        var wordTextElementCount = CountTextElements(word);
        var wordWindow = _wordWindow;
        var wordLayout = _wordLayout;
        ResetWord();

        await TryCorrectWordAsync(
            word,
            wordTextElementCount,
            wordWindow,
            wordLayout,
            trigger,
            observation.Generation,
            cancellationToken);
    }

    private async Task TryCorrectWordAsync(
        string word,
        int wordTextElementCount,
        ActiveWindowContext window,
        string currentLayout,
        Trigger trigger,
        long boundaryGeneration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (word.Length < 2 || string.IsNullOrWhiteSpace(currentLayout) ||
            IsLanguageDisabled(currentLayout) || IsAutoConversionBlacklisted())
        {
            return;
        }

        var replacement = FindUserAutocorrect(word);
        string? targetLayoutCode = null;

        if (replacement == null)
        {
            var hasTrimmedCore = TryTrimEdgeJoiners(
                word,
                out var core,
                out var prefix,
                out var suffix);
            if (IsUserException(word) ||
                (hasTrimmedCore && IsUserException(core)))
                return;

            if (trigger.AllowsDictionaryCorrection &&
                CanUseDictionaryAutomaticCorrection(word) &&
                _dictionaryAnalyzer.TryGetCorrection(word, currentLayout, out var suggestion) &&
                suggestion.IsConfidentForAutomaticCorrection)
            {
                replacement = suggestion.Replacement;
                targetLayoutCode = suggestion.TargetLayoutCode;
            }
            else if (hasTrimmedCore)
            {
                replacement = FindUserAutocorrect(core);
                if (replacement == null)
                {
                    if (!trigger.AllowsDictionaryCorrection ||
                        !CanUseDictionaryAutomaticCorrection(core) ||
                        !_dictionaryAnalyzer.TryGetCorrection(core, currentLayout, out suggestion) ||
                        !suggestion.IsConfidentForAutomaticCorrection)
                        return;

                    replacement = suggestion.Replacement;
                    targetLayoutCode = suggestion.TargetLayoutCode;
                }

                replacement = prefix + replacement + suffix;
            }
            else
            {
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInfo($"Automatic conversion prepared. Source length: {word.Length}, result length: {replacement.Length}");
        await Task.Delay(35, cancellationToken);

        try
        {
            // A modifier-only key-down is intentionally not published as a text
            // observation by KeyboardHook. Never inject Backspace while a physical
            // modifier is held: Ctrl+Backspace, Alt+Backspace and Win+Backspace have
            // application-specific destructive meanings.
            await _inputInjector.WaitForModifiersReleaseAsync(
                ModifierReleaseTimeoutMilliseconds);
        }
        catch (TimeoutException)
        {
            _logger.LogInfo(
                "Automatic conversion cancelled because modifier keys remained pressed.");
            return;
        }

        if (Interlocked.Read(ref _inputGeneration) != boundaryGeneration)
        {
            _logger.LogInfo("Automatic conversion cancelled because input changed.");
            return;
        }
        if (!_activeWindowProvider.IsSameActiveWindow(window))
        {
            _logger.LogInfo("Automatic conversion cancelled because focus changed.");
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var layoutBeforeActivation = _activeWindowProvider.GetActiveLayoutCode();
        var layoutSwitchRequested = targetLayoutCode != null &&
            !LayoutsMatch(layoutBeforeActivation, targetLayoutCode);
        bool targetLayoutReady;
        try
        {
            targetLayoutReady = targetLayoutCode == null ||
                await ActivateTargetLayoutAsync(targetLayoutCode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            throw;
        }
        if (!targetLayoutReady)
        {
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            return;
        }
        if (Interlocked.Read(ref _inputGeneration) != boundaryGeneration ||
            !_activeWindowProvider.IsSameActiveWindow(window))
        {
            _logger.LogInfo(
                "Automatic conversion cancelled because input or focus changed during layout activation.");
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            return;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            cancellationToken.ThrowIfCancellationRequested();
        }

        bool targetStillSafe;
        try
        {
            targetStillSafe = _targetGuard == null ||
                await _targetGuard.CanModifyAsync(window, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            throw;
        }
        if (!targetStillSafe)
        {
            _logger.LogInfo("Automatic conversion cancelled because the target is no longer safe.");
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            return;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (Interlocked.Read(ref _inputGeneration) != boundaryGeneration ||
            !_activeWindowProvider.IsSameActiveWindow(window))
        {
            _logger.LogInfo(
                "Automatic conversion cancelled because input or focus changed during the target safety check.");
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            return;
        }

        bool correctionStopped;
        lock (_lifecycleGate)
            correctionStopped = _disposed || cancellationToken.IsCancellationRequested;
        if (correctionStopped)
        {
            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            return;
        }

        var requestedDeletionCount = wordTextElementCount + 1;
        var deletedUnitCount = 0;
        var replacementAffectedUtf16Length = 0;
        var triggerAffectedTextElementCount = 0;
        var injectionStage = AutoInjectionStage.Deletion;
        try
        {
            await _inputInjector.SendBackspacesAsync(requestedDeletionCount);
            deletedUnitCount = requestedDeletionCount;
            injectionStage = AutoInjectionStage.Replacement;
            await _inputInjector.SendTextAsync(replacement);
            replacementAffectedUtf16Length = replacement.Length;
            injectionStage = AutoInjectionStage.Trigger;
            await SendTriggerAsync(trigger);
        }
        catch (Exception ex)
        {
            if (ex is InputInjectionException injectionException)
            {
                switch (injectionStage)
                {
                    case AutoInjectionStage.Deletion
                        when injectionException.Operation == InputInjectionOperation.Backspace:
                        deletedUnitCount = Math.Min(
                            requestedDeletionCount,
                            injectionException.AffectedUnitCount);
                        break;
                    case AutoInjectionStage.Replacement
                        when injectionException.Operation == InputInjectionOperation.Text:
                        replacementAffectedUtf16Length = Math.Min(
                            replacement.Length,
                            injectionException.AffectedUnitCount);
                        break;
                    case AutoInjectionStage.Trigger:
                        triggerAffectedTextElementCount =
                            injectionException.AffectedUnitCount == 0
                                ? 0
                                : injectionException.Operation == InputInjectionOperation.Text
                                    ? CountTextElements(trigger.TextFallback[..Math.Min(
                                        trigger.TextFallback.Length,
                                        injectionException.AffectedUnitCount)])
                                    : 1;
                        break;
                }

                _logger.LogWarning(
                    $"Automatic conversion injection made partial progress at {injectionStage}: " +
                    $"affected {injectionException.AffectedUnitCount} of " +
                    $"{injectionException.RequestedUnitCount} units.");
            }

            _logger.LogError("Automatic conversion injection failed", ex);
            if (deletedUnitCount > 0)
            {
                try
                {
                    if (await CanRollbackAsync(window, boundaryGeneration))
                    {
                        if (triggerAffectedTextElementCount > 0)
                        {
                            await _inputInjector.SendBackspacesAsync(
                                triggerAffectedTextElementCount);
                        }

                        if (replacementAffectedUtf16Length > 0)
                        {
                            await _inputInjector.SendBackspacesAsync(CountTextElements(
                                replacement[..replacementAffectedUtf16Length]));
                        }

                        await RestoreDeletedOriginalSuffixAsync(
                            word,
                            wordTextElementCount,
                            trigger,
                            deletedUnitCount);
                    }
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError("Automatic conversion rollback failed", rollbackException);
                }
            }

            if (layoutSwitchRequested)
                await RestoreLayoutAfterFailedCorrectionAsync(layoutBeforeActivation, window);
            return;
        }

        try
        {
            if (targetLayoutCode != null)
            {
                _correctionMemory?.Record(
                    word,
                    replacement,
                    trigger.TextFallback,
                    window);
            }

            if (_settingsService.Current.SoundEnabled)
                _soundService.PlayAutoConvertSound();
        }
        catch (Exception ex)
        {
            // The visible transaction has already completed. Optional feedback or
            // learning failures must never rewrite the user's text a second time.
            _logger.LogError("Automatic conversion post-injection feedback failed", ex);
        }
    }

    private async Task RestoreDeletedOriginalSuffixAsync(
        string word,
        int wordTextElementCount,
        Trigger trigger,
        int deletedUnitCount)
    {
        var boundedDeletedUnitCount = Math.Clamp(
            deletedUnitCount,
            0,
            wordTextElementCount + 1);
        if (boundedDeletedUnitCount == 0)
            return;

        var deletedWordElementCount = Math.Max(0, boundedDeletedUnitCount - 1);
        if (deletedWordElementCount > 0)
        {
            var elements = EnumerateTextElements(word).ToArray();
            await _inputInjector.SendTextAsync(string.Concat(
                elements.Skip(elements.Length - deletedWordElementCount)));
        }

        await SendTriggerAsync(trigger);
    }

    private async Task<bool> CanRollbackAsync(
        ActiveWindowContext window,
        long boundaryGeneration)
    {
        if (Interlocked.Read(ref _inputGeneration) != boundaryGeneration ||
            !_activeWindowProvider.IsSameActiveWindow(window))
        {
            _logger.LogWarning(
                "Automatic conversion rollback skipped because input or focus changed after the injection failure.");
            return false;
        }

        try
        {
            await _inputInjector.WaitForModifiersReleaseAsync(
                ModifierReleaseTimeoutMilliseconds);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Automatic conversion rollback skipped because modifier keys remained pressed.");
            return false;
        }

        if (_targetGuard != null &&
            !await _targetGuard.CanModifyAsync(window, CancellationToken.None))
        {
            _logger.LogWarning(
                "Automatic conversion rollback skipped because the original target is no longer safe.");
            return false;
        }

        if (Interlocked.Read(ref _inputGeneration) != boundaryGeneration ||
            !_activeWindowProvider.IsSameActiveWindow(window))
        {
            _logger.LogWarning(
                "Automatic conversion rollback skipped because input or focus changed during rollback validation.");
            return false;
        }

        return true;
    }

    private async Task RestoreLayoutAfterFailedCorrectionAsync(
        string previousLayoutCode,
        ActiveWindowContext window)
    {
        if (string.IsNullOrWhiteSpace(previousLayoutCode) ||
            !_activeWindowProvider.IsSameActiveWindow(window) ||
            LayoutsMatch(_activeWindowProvider.GetActiveLayoutCode(), previousLayoutCode))
        {
            return;
        }

        try
        {
            if (!await ActivateTargetLayoutAsync(previousLayoutCode, CancellationToken.None))
            {
                _logger.LogWarning(
                    $"Automatic conversion could not restore layout {previousLayoutCode} after cancellation or failure.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                $"Automatic conversion layout restoration failed for {previousLayoutCode}",
                ex);
        }
    }

    private async Task<bool> ActivateTargetLayoutAsync(
        string targetLayoutCode,
        CancellationToken cancellationToken)
    {
        if (LayoutsMatch(_activeWindowProvider.GetActiveLayoutCode(), targetLayoutCode))
            return true;

        if (!_activeWindowProvider.TrySwitchToLayout(targetLayoutCode))
        {
            _logger.LogWarning(
                $"Automatic conversion target layout {targetLayoutCode} is unavailable.");
            return false;
        }

        var deadline = Environment.TickCount64 + LayoutActivationTimeoutMilliseconds;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LayoutsMatch(_activeWindowProvider.GetActiveLayoutCode(), targetLayoutCode))
                return true;
            await Task.Delay(10, cancellationToken);
        }
        while (Environment.TickCount64 < deadline);

        _logger.LogWarning(
            $"Automatic conversion target layout {targetLayoutCode} did not activate in time.");
        return false;
    }

    private static bool LayoutsMatch(string current, string target)
    {
        if (KeyboardLayoutIdentity.TryGetNativeHandle(current, out var currentHandle) &&
            KeyboardLayoutIdentity.TryGetNativeHandle(target, out var targetHandle))
        {
            return currentHandle == targetHandle;
        }

        return KeyboardLayoutIdentity.SameCulture(current, target);
    }

    private string? FindUserAutocorrect(string word)
    {
        foreach (var pair in _settingsService.Current.UserAutocorrect)
        {
            if (string.Equals(pair.Key, word, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(pair.Value))
            {
                return ApplySourceCasing(word, pair.Value);
            }
        }

        return null;
    }

    private static string ApplySourceCasing(string source, string replacement)
    {
        var replacementLetters = replacement.EnumerateRunes()
            .Where(Rune.IsLetter)
            .ToArray();
        if (replacementLetters.Length == 0 || replacementLetters.Any(letter => !Rune.IsLower(letter)))
            return replacement;

        var sourceLetters = source.EnumerateRunes()
            .Where(Rune.IsLetter)
            .ToArray();
        if (sourceLetters.Length == 0)
            return replacement;

        if (sourceLetters.All(Rune.IsUpper))
            return replacement.ToUpperInvariant();

        if (Rune.IsUpper(sourceLetters[0]) && sourceLetters.Skip(1).All(Rune.IsLower))
            return UppercaseFirstLetter(replacement);

        return replacement;
    }

    private static string UppercaseFirstLetter(string text)
    {
        var elements = EnumerateTextElements(text).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            if (!elements[index].EnumerateRunes().Any(Rune.IsLetter))
                continue;

            elements[index] = elements[index].ToUpperInvariant();
            break;
        }

        return string.Concat(elements);
    }

    private bool IsUserException(string word) =>
        _settingsService.Current.UserExceptions.Any(exception =>
            string.Equals(exception, word, StringComparison.OrdinalIgnoreCase));

    private static bool TryTrimEdgeJoiners(
        string word,
        out string core,
        out string prefix,
        out string suffix)
    {
        var elements = EnumerateTextElements(word).ToArray();
        var start = 0;
        while (start < elements.Length && IsWordJoiner(elements[start]))
            start++;

        var end = elements.Length - 1;
        while (end >= start && IsWordJoiner(elements[end]))
            end--;

        if ((start == 0 && end == elements.Length - 1) || end < start)
        {
            core = prefix = suffix = string.Empty;
            return false;
        }

        prefix = string.Concat(elements.Take(start));
        core = string.Concat(elements.Skip(start).Take(end - start + 1));
        suffix = string.Concat(elements.Skip(end + 1));
        return true;
    }

    private static bool IsWordJoiner(string textElement) =>
        textElement is "'" or "’" or "-";

    private bool IsLanguageDisabled(string layoutCode)
    {
        var language = LanguagePart(layoutCode);
        var cultureCode = KeyboardLayoutIdentity.GetCultureCode(layoutCode);
        string? englishName = null;
        try
        {
            englishName = CultureInfo.GetCultureInfo(cultureCode).EnglishName;
        }
        catch (CultureNotFoundException)
        {
        }

        return _settingsService.Current.DisabledLanguages.Any(disabled =>
            string.Equals(disabled, layoutCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(disabled, cultureCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(disabled, language, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(disabled, englishName, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAutoConversionBlacklisted()
    {
        var process = _activeWindowProvider.GetActiveProcessName();
        if (string.IsNullOrWhiteSpace(process))
            return false;

        return _settingsService.Current.BlacklistedProcesses
            .Concat(_settingsService.Current.AutoConversionBlacklistedProcesses)
            .Any(blocked => ProcessNamesEqual(blocked, process));
    }

    private static bool ProcessNamesEqual(string? configured, string? actual) =>
        string.Equals(
            NormalizeProcessName(configured),
            NormalizeProcessName(actual),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var fileName = Path.GetFileName(value.Trim().Trim('"'));
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static string LanguagePart(string code) =>
        code.Split(new[] { '-', '_' }, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

    private static int CountTextElements(string text) =>
        StringInfo.ParseCombiningCharacters(text).Length;

    private static bool CanUseDictionaryAutomaticCorrection(string text) =>
        CountTextElements(text) >= MinimumAutomaticLayoutWordLength;

    private static bool IsWordText(string text) =>
        !string.IsNullOrEmpty(text) &&
        EnumerateTextElements(text).All(IsWordTextElement);

    private static bool IsWordTextElement(string textElement)
    {
        if (IsWordJoiner(textElement))
            return true;

        if (!char.IsLetterOrDigit(textElement, 0))
            return false;

        var index = char.IsSurrogatePair(textElement, 0) ? 2 : 1;
        while (index < textElement.Length)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(textElement, index);
            if (category is not (
                UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark))
            {
                return false;
            }

            index += char.IsSurrogatePair(textElement, index) ? 2 : 1;
        }

        return true;
    }

    private static IEnumerable<string> EnumerateTextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            yield return enumerator.GetTextElement();
    }

    private static bool TryGetTrigger(InputObservation observation, out Trigger trigger)
    {
        if (observation.Key.Equals("space", StringComparison.OrdinalIgnoreCase))
        {
            trigger = new Trigger(" ", null);
            return true;
        }
        if (observation.Key.Equals("enter", StringComparison.OrdinalIgnoreCase))
        {
            trigger = new Trigger(Environment.NewLine, "enter");
            return true;
        }
        if (observation.Key.Equals("tab", StringComparison.OrdinalIgnoreCase))
        {
            trigger = new Trigger("\t", "tab");
            return true;
        }
        if (!string.IsNullOrEmpty(observation.Text) && observation.Text.Any(char.IsPunctuation))
        {
            trigger = new Trigger(
                observation.Text,
                null,
                AllowsDictionaryCorrection: !IsTechnicalPunctuation(observation.Text));
            return true;
        }

        trigger = default;
        return false;
    }

    private static bool IsTechnicalPunctuation(string text) =>
        text.IndexOfAny(['@', '_', '/', '\\', ':', '#', '[', ']', '{', '}', '(', ')']) >= 0;

    private Task SendTriggerAsync(Trigger trigger) =>
        trigger.Key == null
            ? _inputInjector.SendTextAsync(trigger.TextFallback)
            : _inputInjector.SendKeyCombinationAsync(false, false, false, trigger.Key);

    private void ResetWord()
    {
        _typedUnits.Clear();
        _wordWindow = default;
        _wordLayout = string.Empty;
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _keyboardHook.HotkeyPressed -= OnKeyPressed;
        _mouseHook.MouseClicked -= OnMouseClicked;
        _inputQueue.Writer.TryComplete();
        _shutdownCancellation.Cancel();
        var processorStopped = false;
        try
        {
            processorStopped = _inputProcessor.Wait(TimeSpan.FromSeconds(3));
            if (!processorStopped)
                _logger.LogWarning("Automatic correction worker did not stop within the shutdown deadline.");
        }
        catch (AggregateException exception)
        {
            _logger.LogError("Automatic correction worker failed during shutdown", exception.Flatten());
        }
        if (processorStopped)
            _shutdownCancellation.Dispose();
    }

    private readonly record struct Trigger(
        string TextFallback,
        string? Key,
        bool AllowsDictionaryCorrection = true);

    private enum AutoInjectionStage
    {
        Deletion,
        Replacement,
        Trigger
    }

    private readonly record struct InputObservation(
        string Key,
        string Text,
        int VirtualKey,
        bool Ctrl,
        bool Alt,
        bool Win,
        bool IsDeadKey,
        long Generation,
        bool ShouldReset)
    {
        public static InputObservation KeyPress(
            string key,
            string text,
            int virtualKey,
            bool ctrl,
            bool alt,
            bool win,
            bool isDeadKey,
            long generation) => new(
                key,
                text,
                virtualKey,
                ctrl,
                alt,
                win,
                isDeadKey,
                generation,
                false);

        public static InputObservation Reset(long generation) =>
            new(string.Empty, string.Empty, 0, false, false, false, false, generation, true);
    }
}
