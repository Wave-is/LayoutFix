using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.IntegrationTests;

public class TextTransactionServiceTests
{
    [Fact]
    public async Task SecureTarget_IsRejectedBeforeClipboardOrInput()
    {
        var clipboard = new FakeClipboardService("secret");
        var input = new FakeInputInjector(clipboard);
        var windows = new FakeActiveWindowProvider();
        var service = new TextTransactionService(
            input,
            clipboard,
            windows,
            new NullLogger(),
            new DenyTextTargetGuard());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);

        Assert.Null(selection);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal(0, input.SelectWordCount);
        Assert.Null(input.SentText);
    }

    [Fact]
    public async Task EnabledSupportDiagnostics_RecordsTargetAndExactCaptureReasonWithoutUserText()
    {
        var clipboard = new FakeClipboardService("SECRET USER CLIPBOARD TEXT");
        var logger = new RecordingLogger();
        var settings = new InMemorySettingsService
        {
            Current = new AppSettings { LoggingEnabled = true }
        };
        var service = new TextTransactionService(
            new FakeInputInjector(clipboard),
            clipboard,
            new FakeActiveWindowProvider(),
            logger,
            new DenyTextTargetGuard(),
            settingsService: settings);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);

        Assert.Null(selection);
        var log = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("SupportDiagnostic: CaptureId=1", log);
        Assert.Contains("TargetProcess=test-host", log);
        Assert.Contains("Reason=target-safety-check-failed", log);
        Assert.DoesNotContain("SECRET USER CLIPBOARD TEXT", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectAdapter_CapturesAndReplacesWithoutGenericClipboardPath()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard);
        var windows = new FakeActiveWindowProvider();
        var adapter = new FakeDirectTextAdapter(
            DirectTextCaptureResult.Captured(
                "test-direct",
                "ghbdtn",
                allowTargetLayoutActivation: false));
        var service = new TextTransactionService(
            input,
            clipboard,
            windows,
            new NullLogger(),
            directTextAdapter: adapter);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.True(replaced);
        Assert.Equal("ghbdtn", selection!.Text);
        Assert.Equal("test-direct", selection.DirectAdapterId);
        Assert.False(selection.AllowTargetLayoutActivation);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(1, adapter.ReplaceCount);
        Assert.Equal("ghbdtn", adapter.ExpectedText);
        Assert.Equal("привет", adapter.Replacement);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Null(input.SentText);
        Assert.Equal("user clipboard payload", clipboard.Value);
    }

    [Fact]
    public async Task ApplicableDirectAdapterWithoutExactSelection_FailsClosed()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard);
        var adapter = new FakeDirectTextAdapter(
            DirectTextCaptureResult.Rejected("test-direct"));
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            directTextAdapter: adapter);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);

        Assert.Null(selection);
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Equal(0, adapter.ReplaceCount);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal(0, input.SelectWordCount);
        Assert.Equal(0, input.CollapseSelectionCount);
        Assert.Equal("user clipboard payload", clipboard.Value);
    }

    [Fact]
    public async Task AccessibilitySelection_CapturesAndVerifiesWithoutClipboard()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard);
        var guard = new SelectedTextTargetGuard("ghbdtn", "ghbdtn");
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            guard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.True(replaced);
        Assert.Equal("ghbdtn", selection!.Text);
        Assert.Equal("привет", input.SentText);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal("user clipboard payload", clipboard.Value);
    }

    [Fact]
    public async Task VerifiedAccessibilitySelection_ReusesAtomicSafetyProof()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard);
        var guard = new SelectedTextTargetGuard("ghbdtn", "ghbdtn")
        {
            ReturnVerifiedSelection = true
        };
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            guard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.True(replaced);
        Assert.Equal("привет", input.SentText);
        Assert.Equal(2, guard.SelectionReadCount);
        Assert.Equal(0, guard.CanModifyCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task AccessibilitySelection_UsesPreviousWordFallbackWithoutClipboard()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard);
        var guard = new SelectedTextTargetGuard(null, "ghbdtn", "ghbdtn");
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            guard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.True(replaced);
        Assert.True(selection!.WasSelectedByFallback);
        Assert.Equal(1, input.SelectWordCount);
        Assert.Equal("привет", input.SentText);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal("user clipboard payload", clipboard.Value);
    }

    [Fact]
    public async Task ApplicableDirectAdapter_UsesPreviousWordFallbackWithoutClipboard()
    {
        var clipboard = new FakeClipboardService("complex user clipboard payload");
        var input = new FakeInputInjector(clipboard);
        var adapter = new FakeDirectTextAdapter(
            DirectTextCaptureResult.SelectionMissing("test-direct"),
            DirectTextCaptureResult.Captured("test-direct", "ghbdtn"));
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            directTextAdapter: adapter);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);

        Assert.NotNull(selection);
        Assert.Equal("ghbdtn", selection!.Text);
        Assert.True(selection.WasSelectedByFallback);
        Assert.Equal(2, adapter.CaptureCount);
        Assert.Equal(1, input.SelectWordCount);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal("complex user clipboard payload", clipboard.Value);
    }

    [Fact]
    public async Task ProvenEmptySelection_UsesFallbackBeforeAnyClipboardCopy()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard, "ghbdtn");
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            new SelectionAwareTargetGuard(TextSelectionAvailability.None));

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);

        Assert.NotNull(selection);
        Assert.Equal("ghbdtn", selection!.Text);
        Assert.True(selection.WasSelectedByFallback);
        Assert.Equal(1, input.SelectWordCount);
        Assert.Equal(1, input.CopyCount);
        Assert.Equal("user clipboard payload", clipboard.Value);
    }

    [Fact]
    public async Task CaptureAndReplace_RestoreClipboardAndVerifyOriginalSelection()
    {
        var clipboard = new FakeClipboardService("user clipboard payload");
        var input = new FakeInputInjector(clipboard, "ghbdtn", "ghbdtn");
        var windows = new FakeActiveWindowProvider();
        var service = new TextTransactionService(input, clipboard, windows, new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.True(replaced);
        Assert.Equal("ghbdtn", selection!.Text);
        Assert.Equal("привет", input.SentText);
        Assert.Equal("user clipboard payload", clipboard.Value);
        Assert.Equal(2, clipboard.RestoreCount);
        Assert.Equal(0, clipboard.ClearCount);
    }

    [Fact]
    public async Task ModerateReplacement_UsesUnicodeInputAndRestoresOriginalClipboard()
    {
        var originalClipboard = "user clipboard payload";
        var source = new string('x', 447);
        var replacement = new string('я', 447);
        var clipboard = new FakeClipboardService(originalClipboard);
        var input = new FakeInputInjector(clipboard, source, source);
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, replacement);

        Assert.True(replaced);
        Assert.Equal(replacement, input.SentText);
        Assert.Equal(0, input.PasteCount);
        Assert.Null(input.PastedText);
        Assert.Equal(originalClipboard, clipboard.Value);
        Assert.Equal(2, clipboard.RestoreCount);
    }

    [Fact]
    public async Task VeryLargeReplacement_UsesPasteAndRestoresOriginalClipboard()
    {
        var originalClipboard = "user clipboard payload";
        var source = new string('x', 2_048);
        var replacement = new string('я', 2_048);
        var clipboard = new FakeClipboardService(originalClipboard);
        var input = new FakeInputInjector(clipboard, source, source);
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, replacement);

        Assert.True(replaced);
        Assert.Equal(replacement, input.PastedText);
        Assert.Equal(1, input.PasteCount);
        Assert.Null(input.SentText);
        Assert.Equal(originalClipboard, clipboard.Value);
        Assert.Equal(3, clipboard.RestoreCount);
    }

    [Fact]
    public async Task Replace_AbortsWhenSelectionChangedAfterCapture()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "ghbdtn", "another selection");
        var windows = new FakeActiveWindowProvider();
        var service = new TextTransactionService(input, clipboard, windows, new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.False(replaced);
        Assert.Null(input.SentText);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task Replace_AbortsAfterKeyboardInputEvenWhenSelectionTextStillMatches()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "same selection");
        var keyboard = new FakeKeyboardHook();
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            keyboardHook: keyboard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        keyboard.ObservePhysicalInput();
        var replaced = await service.ReplaceAsync(selection!, "translated");

        Assert.False(replaced);
        Assert.Null(input.SentText);
        Assert.Equal(1, clipboard.CaptureCount);
        Assert.Equal(0, selection!.KeyboardInputGeneration);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task Replace_AbortsAfterMouseInputEvenWhenSelectionTextStillMatches()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "same selection");
        var mouse = new FakeMouseHook();
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            mouseHook: mouse);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        mouse.ObservePhysicalInput();
        var replaced = await service.ReplaceAsync(selection!, "translated");

        Assert.False(replaced);
        Assert.Null(input.SentText);
        Assert.Equal(1, clipboard.CaptureCount);
        Assert.Equal(0, selection!.MouseInputGeneration);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task Capture_AbortsWhenInputArrivesDuringTargetValidation()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "selected text");
        var keyboard = new FakeKeyboardHook();
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            new ObserveInputTextTargetGuard(keyboard.ObservePhysicalInput),
            keyboard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);

        Assert.Null(selection);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal(0, input.SelectWordCount);
    }

    [Fact]
    public async Task Replace_AbortsWithoutTouchingClipboardWhenFocusChanged()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "ghbdtn");
        var windows = new FakeActiveWindowProvider();
        var service = new TextTransactionService(input, clipboard, windows, new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        windows.Current = new ActiveWindowContext((nint)10, (nint)20, 30);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.False(replaced);
        Assert.Null(input.SentText);
        Assert.Equal(1, clipboard.CaptureCount);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task PartialReplacement_RemovesAcceptedPrefixAndRestoresSelection()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "ghbdtn", "ghbdtn")
        {
            PartialReplacementAffectedUtf16Length = 2
        };
        var windows = new FakeActiveWindowProvider();
        var service = new TextTransactionService(input, clipboard, windows, new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.False(replaced);
        Assert.Equal([2], input.BackspaceBatches);
        Assert.Equal(["привет", "ghbdtn"], input.SentTexts);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task PartialReplacement_FocusChangeDoesNotRollbackIntoNewTarget()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var windows = new FakeActiveWindowProvider();
        var input = new FakeInputInjector(clipboard, "ghbdtn", "ghbdtn")
        {
            PartialReplacementAffectedUtf16Length = 2,
            OnPartialReplacementFailure = () =>
                windows.Current = new ActiveWindowContext((nint)10, (nint)20, 30)
        };
        var service = new TextTransactionService(input, clipboard, windows, new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.False(replaced);
        Assert.Empty(input.BackspaceBatches);
        Assert.Equal(["привет"], input.SentTexts);
    }

    [Fact]
    public async Task PartialReplacement_NewInputInSameTargetSkipsStaleRollback()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var windows = new FakeActiveWindowProvider();
        var keyboard = new FakeKeyboardHook();
        var input = new FakeInputInjector(clipboard, "ghbdtn", "ghbdtn")
        {
            PartialReplacementAffectedUtf16Length = 2,
            OnPartialReplacementFailure = keyboard.ObservePhysicalInput
        };
        var service = new TextTransactionService(
            input,
            clipboard,
            windows,
            new NullLogger(),
            keyboardHook: keyboard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.False(replaced);
        Assert.Empty(input.BackspaceBatches);
        Assert.Equal(["привет"], input.SentTexts);
        Assert.Equal(1, keyboard.InputGeneration);
    }

    [Fact]
    public async Task PartialReplacement_MouseInputInSameTargetSkipsStaleRollback()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var windows = new FakeActiveWindowProvider();
        var mouse = new FakeMouseHook();
        var input = new FakeInputInjector(clipboard, "ghbdtn", "ghbdtn")
        {
            PartialReplacementAffectedUtf16Length = 2,
            OnPartialReplacementFailure = mouse.ObservePhysicalInput
        };
        var service = new TextTransactionService(
            input,
            clipboard,
            windows,
            new NullLogger(),
            mouseHook: mouse);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);
        var replaced = await service.ReplaceAsync(selection!, "привет");

        Assert.False(replaced);
        Assert.Empty(input.BackspaceBatches);
        Assert.Equal(["привет"], input.SentTexts);
        Assert.Equal(1, mouse.InputGeneration);
    }

    [Fact]
    public async Task FallbackSelection_IsCollapsedWhenConversionProducesNoReplacement()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, null, "word");
        var windows = new FakeActiveWindowProvider();
        var service = new TextTransactionService(input, clipboard, windows, new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);
        await service.CancelFallbackSelectionAsync(selection!);

        Assert.True(selection!.WasSelectedByFallback);
        Assert.Equal(1, input.SelectWordCount);
        Assert.Equal(1, input.CollapseSelectionCount);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task FallbackSelection_NewInputInSameTargetSkipsStaleCaretMove()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, null, "word");
        var keyboard = new FakeKeyboardHook();
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger(),
            keyboardHook: keyboard);

        var selection = await service.CaptureAsync(allowPreviousWordFallback: true);
        keyboard.ObservePhysicalInput();
        await service.CancelFallbackSelectionAsync(selection!);

        Assert.True(selection!.WasSelectedByFallback);
        Assert.Equal(1, input.SelectWordCount);
        Assert.Equal(0, input.CollapseSelectionCount);
        Assert.Equal("original clipboard", clipboard.Value);
    }

    [Fact]
    public async Task Capture_RestoresClipboardWhenCopyFails()
    {
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, "unused") { ThrowOnCopy = true };
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger());

        var selection = await service.CaptureAsync(allowPreviousWordFallback: false);

        Assert.Null(selection);
        Assert.Equal("original clipboard", clipboard.Value);
        Assert.Equal(1, clipboard.RestoreCount);
    }

    [Fact]
    public async Task CancelledFallbackCapture_RestoresClipboardAndCollapsesSelection()
    {
        using var cancellation = new CancellationTokenSource();
        var clipboard = new FakeClipboardService("original clipboard");
        var input = new FakeInputInjector(clipboard, null, "word")
        {
            OnFallbackCopy = cancellation.Cancel
        };
        var service = new TextTransactionService(
            input,
            clipboard,
            new FakeActiveWindowProvider(),
            new NullLogger());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CaptureAsync(
                allowPreviousWordFallback: true,
                cancellation.Token));

        Assert.Equal(1, input.SelectWordCount);
        Assert.Equal(1, input.CollapseSelectionCount);
        Assert.Equal("original clipboard", clipboard.Value);
        Assert.Equal(1, clipboard.RestoreCount);
    }

    private sealed class FakeClipboardService(string? initialValue) : IClipboardService
    {
        private uint _sequence;
        public string? Value { get; private set; } = initialValue;
        public int CaptureCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int ClearCount { get; private set; }

        public Task<IClipboardSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult<IClipboardSnapshot>(new Snapshot(Value));
        }

        public Task RestoreAsync(IClipboardSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Value = ((Snapshot)snapshot).Value;
            RestoreCount++;
            _sequence++;
            return Task.CompletedTask;
        }

        public Task<string?> ReadTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Value = text;
            _sequence++;
            return Task.CompletedTask;
        }
        public uint GetSequenceNumber() => _sequence;

        public void SimulateCopy(string? text)
        {
            Value = text;
            _sequence++;
        }

        public void Dispose() { }

        private sealed class Snapshot(string? value) : IClipboardSnapshot
        {
            public string? Value { get; } = value;
            public void Dispose() { }
        }
    }

    private sealed class FakeInputInjector(
        FakeClipboardService clipboard,
        params string?[] copiedSelections) : IInputInjector
    {
        private readonly Queue<string?> _copiedSelections = new(copiedSelections);
        public bool ThrowOnCopy { get; set; }
        public Action? OnFallbackCopy { get; init; }
        public int? PartialReplacementAffectedUtf16Length { get; set; }
        public Action? OnPartialReplacementFailure { get; init; }
        public string? SentText { get; private set; }
        public List<string> SentTexts { get; } = [];
        public List<int> BackspaceBatches { get; } = [];
        public int SelectWordCount { get; private set; }
        public int CollapseSelectionCount { get; private set; }
        public int PasteCount { get; private set; }
        public int CopyCount { get; private set; }
        public string? PastedText { get; private set; }

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
        {
            if (ctrl && key.Equals("c", StringComparison.OrdinalIgnoreCase))
            {
                CopyCount++;
                if (ThrowOnCopy) throw new InvalidOperationException("copy failed");
                clipboard.SimulateCopy(_copiedSelections.Dequeue());
                if (SelectWordCount > 0)
                    OnFallbackCopy?.Invoke();
            }
            else if (!ctrl && key.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                CollapseSelectionCount++;
            }
            else if (ctrl && key.Equals("v", StringComparison.OrdinalIgnoreCase))
            {
                PasteCount++;
                PastedText = clipboard.Value;
            }

            return Task.CompletedTask;
        }

        public Task SendBackspacesAsync(int count)
        {
            BackspaceBatches.Add(count);
            return Task.CompletedTask;
        }
        public Task SendTextAsync(string text)
        {
            SentTexts.Add(text);
            if (PartialReplacementAffectedUtf16Length is { } affectedLength)
            {
                PartialReplacementAffectedUtf16Length = null;
                OnPartialReplacementFailure?.Invoke();
                var boundedAffectedLength = Math.Clamp(affectedLength, 0, text.Length);
                throw new InputInjectionException(
                    InputInjectionOperation.Text,
                    text.Length,
                    boundedAffectedLength,
                    text.Length * 2,
                    boundedAffectedLength * 2);
            }

            SentText = text;
            return Task.CompletedTask;
        }

        public Task SelectWordLeftAsync()
        {
            SelectWordCount++;
            return Task.CompletedTask;
        }

        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => Task.CompletedTask;
    }

    private sealed class FakeActiveWindowProvider : IActiveWindowProvider
    {
        public ActiveWindowContext Current { get; set; } = new((nint)1, (nint)2, 3);
        public ActiveWindowContext CaptureActiveWindow() => Current;
        public bool IsSameActiveWindow(ActiveWindowContext context) => Current == context;
        public string GetActiveProcessName() => "test-host";
        public string GetActiveLayoutCode() => "en-US";
        public void SwitchToNextLayout() { }
        public bool TrySwitchToLayout(string layoutCode) => true;
    }

    private sealed class SelectionAwareTargetGuard(
        TextSelectionAvailability availability) : ITextTargetGuard
    {
        public Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<TextSelectionAvailability> GetSelectionAvailabilityAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(availability);
    }

    private sealed class SelectedTextTargetGuard(params string?[] selections) : ITextTargetGuard
    {
        private readonly Queue<string?> _selections = new(selections);
        public bool ReturnVerifiedSelection { get; init; }
        public int CanModifyCount { get; private set; }
        public int SelectionReadCount { get; private set; }

        public Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default)
        {
            CanModifyCount++;
            return Task.FromResult(true);
        }

        public Task<TextSelectionReadResult> TryReadSelectedTextAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default)
        {
            SelectionReadCount++;
            var selection = _selections.Count > 1
                ? _selections.Dequeue()
                : _selections.Peek();
            return Task.FromResult(ReturnVerifiedSelection
                ? TextSelectionReadResult.Verified(selection)
                : TextSelectionReadResult.Captured(selection));
        }
    }

    private sealed class FakeDirectTextAdapter(
        params DirectTextCaptureResult[] captureResults) : IDirectTextAdapter
    {
        private readonly Queue<DirectTextCaptureResult> _captureResults = new(captureResults);
        public int CaptureCount { get; private set; }
        public int ReplaceCount { get; private set; }
        public string? ExpectedText { get; private set; }
        public string? Replacement { get; private set; }
        public bool ReplaceResult { get; init; } = true;

        public Task<DirectTextCaptureResult> TryCaptureAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            var result = _captureResults.Count > 1
                ? _captureResults.Dequeue()
                : _captureResults.Peek();
            return Task.FromResult(result);
        }

        public Task<bool> TryReplaceAsync(
            string adapterId,
            ActiveWindowContext context,
            string expectedText,
            string replacement,
            CancellationToken cancellationToken = default)
        {
            ReplaceCount++;
            ExpectedText = expectedText;
            Replacement = replacement;
            return Task.FromResult(ReplaceResult);
        }
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        private long _inputGeneration;
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed
        {
            add { }
            remove { }
        }
        public long InputGeneration => Interlocked.Read(ref _inputGeneration);
        public void ObservePhysicalInput() => Interlocked.Increment(ref _inputGeneration);
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeMouseHook : IMouseHook
    {
        private long _inputGeneration;
        public event EventHandler? MouseClicked
        {
            add { }
            remove { }
        }
        public long InputGeneration => Interlocked.Read(ref _inputGeneration);
        public void ObservePhysicalInput() => Interlocked.Increment(ref _inputGeneration);
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class NullLogger : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) { }
    }

    private sealed class RecordingLogger : ILoggerService
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
        public IReadOnlyCollection<string> Messages => _messages.ToArray();
        public void LogInfo(string message) => _messages.Enqueue(message);
        public void LogWarning(string message) => _messages.Enqueue(message);
        public void LogError(string message, Exception? ex = null) => _messages.Enqueue(message);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class DenyTextTargetGuard : ITextTargetGuard
    {
        public Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class ObserveInputTextTargetGuard(Action observeInput) : ITextTargetGuard
    {
        public Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default)
        {
            observeInput();
            return Task.FromResult(true);
        }
    }
}
