using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Input;
using LayoutFix.Infrastructure.Layouts;
using LayoutFix.Infrastructure.Native;
using LayoutFix.Infrastructure.Services;
using LayoutFix.Services;
using LayoutFix.UI;
using LayoutFix.UI.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Specialized;
using System.Globalization;
using System.IO.Pipes;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace LayoutFix.WindowsE2E;

internal static class Program
{
    private const string ClipboardSentinel = "LAYOUTFIX_CLIPBOARD_SENTINEL";
    private static readonly ManualCorrectionCase[] ManualCorrectionCases =
    [
        new("lowercase", "ghbdtn", "привет"),
        new("title-case", "Ghbdtn", "Привет"),
        new("uppercase", "GHBDTN", "ПРИВЕТ"),
        new("phrase", "ghbdtn vbh", "привет мир"),
        new("reverse-phrase", "руддщ цщкдв", "hello world"),
        new("punctuation", "ghbdtn? vbh!", "привет, мир!"),
        new("numbers", "ntcn 123", "тест 123"),
        new("multiline", "ghbdtn\r\nvbh", "привет\r\nмир"),
        new("tab", "ghbdtn\tvbh", "привет\tмир"),
        new("emoji", "🙂 ghbdtn", "🙂 привет"),
        new("unicode-wrappers", "«ghbdtn» — vbh", "«привет» — мир"),
        new(
            "long-selection",
            string.Join(' ', Enumerable.Repeat("ghbdtn", 64)),
            string.Join(' ', Enumerable.Repeat("привет", 64)))
    ];
    private static int _exitCode = 1;
    private static readonly string ResultPath = Path.Combine(
        AppContext.BaseDirectory,
        "windows-e2e-result.txt");

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length >= 4 && args[0] == "--edit-host")
            return RunExternalEditHost(args[1], args[2], args[3]);
        if (args.Length >= 6 && args[0] == "--logger-write-host" &&
            int.TryParse(args[3], out var loggerWriteCount))
        {
            return RunLoggerWriteHost(
                args[1],
                args[2],
                loggerWriteCount,
                args[4],
                args[5]);
        }
        if (args.Length >= 2 && args[0] == "--settings-snapshot")
            return RunSettingsSnapshot(
                args[1],
                args.Length >= 3 ? args[2] : "Dictionary",
                args.Length >= 4 ? args[3] : null);
        if (args.Length >= 1 && args[0] == "--autostart-registry-test")
            return RunAutoStartRegistryTest();
        if (args.Length >= 1 && args[0] == "--settings-clean-close-test")
            return RunSettingsCleanCloseTest();
        if (args.Length >= 1 && args[0] == "--settings-diagnostic-test")
            return RunSettingsDiagnosticTest();
        if (args.Length >= 1 && args[0] == "--settings-registry-diagnostic-test")
            return RunSettingsRegistryDiagnosticTest();
        if (args.Length >= 1 && args[0] == "--settings-migration-lock-test")
            return RunSettingsMigrationLockTest();
        if (args.Length >= 1 && args[0] == "--settings-recovery-barrier-test")
            return RunSettingsRecoveryBarrierTest();
        if (args.Length >= 1 && args[0] == "--settings-concurrency-test")
            return RunSettingsConcurrencyTestAsync().GetAwaiter().GetResult();
        if (args.Length >= 1 && args[0] == "--translation-history-durability-test")
            return RunTranslationHistoryDurabilityTestAsync().GetAwaiter().GetResult();
        if (args.Length >= 1 && args[0] == "--logger-concurrency-test")
            return RunLoggerConcurrencyTestAsync().GetAwaiter().GetResult();
        if (args.Length >= 1 && args[0] == "--compatibility-probe-test")
            return RunCompatibilityProbeTest();
        if (args.Length >= 1 && args[0] == "--dictionary-performance-test")
            return RunDictionaryPerformanceTest();
        if (args.Length >= 2 && args[0] == "--startup-lifecycle-test")
            return RunStartupLifecycleTestWithRetriesAsync(
                    args[1],
                    expectHookFailureRecovery: false,
                    expectSessionRecovery: false)
                .GetAwaiter()
                .GetResult();
        if (args.Length >= 2 && args[0] == "--startup-recovery-test")
            return RunStartupLifecycleTestWithRetriesAsync(
                    args[1],
                    expectHookFailureRecovery: true,
                    expectSessionRecovery: false)
                .GetAwaiter()
                .GetResult();
        if (args.Length >= 2 && args[0] == "--session-recovery-test")
            return RunStartupLifecycleTestWithRetriesAsync(
                    args[1],
                    expectHookFailureRecovery: false,
                    expectSessionRecovery: true)
                .GetAwaiter()
                .GetResult();
        if (args.Length >= 2 && args[0] == "--worker-isolation-test")
            return RunWorkerIsolationTestAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0] == "--worker-model-switch-test")
            return RunWorkerModelSwitchTestAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0] == "--worker-startup-timeout-test")
            return RunWorkerStartupTimeoutTestAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0] == "--worker-translation-test")
            return RunWorkerTranslationTestAsync(
                    args[1],
                    args.Length >= 3 ? args[2] : OfflineModelCatalog.Light.Id)
                .GetAwaiter()
                .GetResult();
        if (args.Length >= 4 && args[0] == "--worker-translation-case")
            return RunWorkerTranslationCaseAsync(args[1], args[2], args[3])
                .GetAwaiter()
                .GetResult();
        if (args.Length >= 2 && args[0] == "--worker-translation-matrix")
            return RunWorkerTranslationMatrixAsync(
                    args[1],
                    args.Length >= 3 ? args[2] : OfflineModelCatalog.Light.Id)
                .GetAwaiter()
                .GetResult();
        if (args.Length >= 2 && args[0] == "--download-model")
            return DownloadModelAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length >= 1 && args[0] == "--secure-target-test")
            return RunSecureTargetTest();
        if (args.Length >= 1 && args[0] == "--noneditable-pipeline-test")
            return RunNonEditablePipelineTestAsync().GetAwaiter().GetResult();
        if (args.Length >= 1 && args[0] == "--noneditable-exit-race-test")
            return RunNonEditableExitRaceTestAsync().GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0] == "--noneditable-target-test" &&
            long.TryParse(args[1], out var nonEditableTargetHandle) &&
            nonEditableTargetHandle != 0)
        {
            return RunNonEditableTargetTestAsync(new IntPtr(nonEditableTargetHandle))
                .GetAwaiter()
                .GetResult();
        }
        if (args.Length >= 1 && args[0] == "--translator-behavior-test")
            return RunTranslatorBehaviorTest();
        if (args.Length >= 1 && args[0] == "--translator-localization-test")
            return RunTranslatorLocalizationTest();
        if (args.Length >= 1 && args[0] == "--notification-focus-test")
            return RunNotificationFocusTest();
        if (args.Length >= 1 && args[0] == "--clipboard-formats-test")
            return RunClipboardFormatsTest();
        if (args.Length >= 1 && args[0] == "--empty-ole-clipboard-host")
            return RunEmptyOleClipboardHost();
        if (args.Length >= 2 && args[0] == "--blacklisted-process-host")
            return RunBlacklistedProcessHost(args[1]);
        if (args.Length >= 1 && args[0] == "--auto-correction-test")
        {
            var soakIterations = 0;
            if (args.Length >= 2 &&
                (!int.TryParse(args[1], out soakIterations) ||
                 soakIterations is < 0 or > 1_000))
            {
                return 64;
            }

            return RunAutoCorrectionTest(soakIterations);
        }
        if (args.Length >= 1 && args[0] == "--selection-ownership-test")
            return RunSelectionOwnershipTest();

        var physicalIterations = 1;
        var configuredHotkey = "Ctrl+F12";
        ushort configuredHotkeyVirtualKey = 0x7B;
        string? externalEditKind = null;
        var existingTextAppWindow = IntPtr.Zero;
        var existingTextControlWindow = IntPtr.Zero;
        var afterEffectsRenameTest = false;
        BrowserCompatibilityHost? browserHost = null;
        string? browserKind = null;
        string? browserTargetKind = null;
        var partialReplacementTest = false;
        var partialReplacementInputRaceTest = false;
        var partialReplacementMouseRaceTest = false;
        var partialReplacementXButtonRaceTest = false;
        var manualCorrectionMatrix = false;
        if (args.Length >= 1 && args[0] == "--physical-soak")
        {
            if (args.Length < 2 || !int.TryParse(args[1], out physicalIterations) ||
                physicalIterations is < 1 or > 10_000)
            {
                return 64;
            }
        }
        else if (args.Length >= 1 && args[0] == "--manual-correction-matrix")
        {
            manualCorrectionMatrix = true;
            physicalIterations = ManualCorrectionCases.Length;
        }
        else if (args.Length >= 3 && args[0] == "--hotkey-vk-test")
        {
            configuredHotkey = args[1];
            if (!ushort.TryParse(args[2], out configuredHotkeyVirtualKey) ||
                configuredHotkeyVirtualKey == 0)
            {
                return 64;
            }
        }
        else if (args.Length >= 2 && args[0] == "--external-edit-test")
        {
            externalEditKind = args[1].ToLowerInvariant();
            if (externalEditKind is not ("edit" or "richedit"))
                return 64;
        }
        else if (args.Length >= 2 &&
            args[0] is "--existing-text-app-test" or "--notepad-test")
        {
            if (!long.TryParse(args[1], out var textAppHandle) || textAppHandle == 0)
                return 64;
            existingTextAppWindow = new IntPtr(textAppHandle);
            if (args.Length >= 3)
            {
                if (!long.TryParse(args[2], out var textControlHandle) ||
                    textControlHandle == 0)
                {
                    return 64;
                }
                existingTextControlWindow = new IntPtr(textControlHandle);
            }
            if (args.Length >= 5)
            {
                configuredHotkey = args[3];
                if (!ushort.TryParse(args[4], out configuredHotkeyVirtualKey) ||
                    configuredHotkeyVirtualKey == 0)
                {
                    return 64;
                }
            }
        }
        else if (args.Length >= 2 && args[0] == "--aftereffects-rename-test")
        {
            if (!long.TryParse(args[1], out var textAppHandle) || textAppHandle == 0)
                return 64;
            existingTextAppWindow = new IntPtr(textAppHandle);
            afterEffectsRenameTest = true;
        }
        else if (args.Length >= 2 &&
            args[0] is "--edge-test" or "--chrome-test")
        {
            browserKind = args[0] == "--chrome-test" ? "chrome" : "edge";
            browserTargetKind = args[1].ToLowerInvariant();
            if (browserTargetKind is not ("input" or "textarea" or "contenteditable"))
                return 64;
            browserHost = BrowserCompatibilityHost.Start(browserKind, browserTargetKind);
            existingTextAppWindow = browserHost.MainWindowHandle;
        }
        else if (args.Length >= 1 &&
            args[0] is "--partial-replacement-test" or
                "--partial-replacement-input-race-test" or
                "--partial-replacement-mouse-race-test" or
                "--partial-replacement-xbutton-race-test")
        {
            partialReplacementTest = true;
            partialReplacementInputRaceTest =
                args[0] == "--partial-replacement-input-race-test";
            partialReplacementMouseRaceTest =
                args[0] == "--partial-replacement-mouse-race-test";
            partialReplacementXButtonRaceTest =
                args[0] == "--partial-replacement-xbutton-race-test";
        }

        using var ownedBrowserHost = browserHost;

        var hasExternalTarget = externalEditKind != null || existingTextAppWindow != IntPtr.Zero;

        File.WriteAllText(ResultPath, $"start {DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        if (browserTargetKind != null)
            AppendResult(
                $"browser-host={browserKind};target={browserTargetKind};isolatedProfile=True");
        AppendResult(
            $"physical-hotkey:configured={configuredHotkey};vk=0x{configuredHotkeyVirtualKey:X2}");

        var testDirectory = Path.Combine(Path.GetTempPath(), $"LayoutFix.E2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
        settings.Current.LayoutOrder = ["en-US", "ru-RU", "uk-UA"];
        settings.Current.HotkeyConfigs =
        [
            new HotkeyConfig
            {
                Action = nameof(HotkeyAction.FixLayoutSelected),
                Hotkey = configuredHotkey,
                Enabled = true
            }
        ];
        settings.Current.AutoConversionEnabled = false;
        var diagnosticsForced = string.Equals(
            Environment.GetEnvironmentVariable("LAYOUTFIX_E2E_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal);
        settings.Current.LoggingEnabled =
            physicalIterations == 1 || diagnosticsForced || manualCorrectionMatrix;
        settings.Current.BlacklistedProcesses = [];
        settings.Save(settings.Current);

        var logPath = Path.Combine(testDirectory, "e2e.log");
        var logger = new FileLoggerService(settings, logPath);
        using var keyboardHook = new KeyboardHook(logger);
        using var mouseHook = new MouseHook(logger);
        using var clipboard = new ClipboardService(logger);
        var nativeInput = new InputInjector();
        var partialInput = partialReplacementTest
            ? new PartialTextFailureInputInjector(
                nativeInput,
                affectedUtf16Length: 2)
            : null;
        IInputInjector input = partialInput is null ? nativeInput : partialInput;
        var activeWindow = new ActiveWindowProvider();
        var layoutManager = new KeyboardLayoutManager(settings, new WindowsLayoutProvider());
        var layoutConverter = new LayoutConverter();
        var dictionaryAnalyzer = new DictionaryAnalyzer(layoutConverter, layoutManager, settings);
        using var targetGuard = new WindowsTextTargetGuard(activeWindow, logger, settings);
        using var directTextAdapter = new AdobeInlineRenameTextAdapter(
            activeWindow,
            nativeInput,
            clipboard,
            logger);
        var textTransaction = new TextTransactionService(
            input,
            clipboard,
            activeWindow,
            logger,
            targetGuard,
            keyboardHook,
            mouseHook,
            directTextAdapter,
            settings);
        using var translation = new NullTranslationCoordinator();
        using var coordinator = new HotkeyCoordinator(
            keyboardHook,
            textTransaction,
            settings,
            layoutManager,
            layoutConverter,
            new TextTransformer(),
            new TransliterationService(),
            new NumberToTextConverter(),
            logger,
            activeWindow,
            new NullSoundService(),
            translation,
            new NullTranslatorWindowProvider(),
            dictionaryAnalyzer: dictionaryAnalyzer);

        using var form = new Form
        {
            Text = hasExternalTarget ? "LayoutFix E2E Controller" : "LayoutFix Windows E2E",
            Width = 700,
            Height = 250,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = !hasExternalTarget,
            ShowInTaskbar = !hasExternalTarget
        };
        var editor = new TextBox
        {
            Name = "Editor",
            Dock = DockStyle.Fill,
            Multiline = true,
            Font = new Font("Segoe UI", 18),
            Text = manualCorrectionMatrix ? ManualCorrectionCases[0].Input : "ghbdtn"
        };
        form.Controls.Add(editor);
        if (partialReplacementInputRaceTest)
        {
            partialInput!.SetAfterPartialText(async () =>
            {
                SendExternalKeys((ushort)'X');
                await Task.Delay(100);
            });
        }
        else if (partialReplacementMouseRaceTest || partialReplacementXButtonRaceTest)
        {
            partialInput!.SetAfterPartialText(async () =>
            {
                var clickCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                form.BeginInvoke(() =>
                {
                    NativeFocus.GetCursorPos(out var originalCursor);
                    var clickPoint = editor.PointToScreen(new Point(
                        Math.Max(4, editor.ClientSize.Width / 2),
                        Math.Max(4, editor.ClientSize.Height / 2)));
                    NativeFocus.SetCursorPos(clickPoint.X, clickPoint.Y);
                    NativeFocus.MouseEvent(
                        partialReplacementXButtonRaceTest
                            ? NativeFocus.MouseEventXDown
                            : NativeFocus.MouseEventLeftDown,
                        0,
                        0,
                        partialReplacementXButtonRaceTest ? NativeFocus.XButton1 : 0,
                        UIntPtr.Zero);
                    NativeFocus.MouseEvent(
                        partialReplacementXButtonRaceTest
                            ? NativeFocus.MouseEventXUp
                            : NativeFocus.MouseEventLeftUp,
                        0,
                        0,
                        partialReplacementXButtonRaceTest ? NativeFocus.XButton1 : 0,
                        UIntPtr.Zero);
                    NativeFocus.SetCursorPos(originalCursor.X, originalCursor.Y);
                    clickCompleted.TrySetResult();
                });
                await clickCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await Task.Delay(100);
            });
        }

        IClipboardSnapshot? originalClipboard = null;
        Process? externalEditProcess = null;
        var externalReadyPath = Path.Combine(testDirectory, "external-edit.ready");
        var externalStatePath = Path.Combine(testDirectory, "external-edit.state");
        var existingTargetModified = false;
        form.Shown += async (_, _) =>
        {
            try
            {
                AppendResult("form:shown");
                var targetWindow = form.Handle;
                if (externalEditKind != null)
                {
                    externalEditProcess = StartExternalEditHost(
                        externalEditKind,
                        externalReadyPath,
                        externalStatePath);
                    var readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                    while (DateTime.UtcNow < readyDeadline && !File.Exists(externalReadyPath))
                        await Task.Delay(50);
                    if (!File.Exists(externalReadyPath) ||
                        !long.TryParse(File.ReadAllText(externalReadyPath), out var targetHandleValue))
                    {
                        throw new InvalidOperationException("The external Edit host did not become ready.");
                    }
                    targetWindow = new IntPtr(targetHandleValue);
                }
                else if (existingTextAppWindow != IntPtr.Zero)
                {
                    targetWindow = existingTextAppWindow;
                }

                var foregroundClaimed = TryClaimForeground(targetWindow);
                if (!foregroundClaimed && existingTextAppWindow == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "The E2E window could not safely claim foreground focus; no input was sent.");

                if (existingTextAppWindow != IntPtr.Zero &&
                    Win32.GetForegroundWindow() != existingTextAppWindow)
                {
                    if (!TryClaimForeground(existingTextAppWindow))
                    {
                        throw new InvalidOperationException(
                            "The compatibility target did not retain foreground focus; no text input was sent.");
                    }
                }
                if (afterEffectsRenameTest)
                {
                    existingTextControlWindow = await PrepareAfterEffectsRenameTargetAsync(
                        existingTextAppWindow);
                    if (existingTextControlWindow == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "The isolated After Effects rename target could not be prepared; no text input was sent.");
                    }
                    AppendResult(
                        $"aftereffects-rename:prepared=True;control=0x{existingTextControlWindow.ToInt64():X}");
                }
                if (existingTextControlWindow != IntPtr.Zero &&
                    !afterEffectsRenameTest &&
                    !await TryFocusAndSelectExternalTextAsync(
                        existingTextAppWindow,
                        existingTextControlWindow))
                {
                    throw new InvalidOperationException(
                        "The exact compatibility text control could not be focused; no text input was sent.");
                }

                if (!hasExternalTarget)
                    editor.Focus();
                else if (existingTextAppWindow == IntPtr.Zero)
                    SendExternalSelectAll();
                await Task.Delay(300);
                AppendResult(
                    $"focus:external={hasExternalTarget};editor={editor.Focused};" +
                    $"foregroundMatches={Win32.GetForegroundWindow() == targetWindow}");
                if (hasExternalTarget)
                    AppendFocusedElementDiagnostics(activeWindow.CaptureActiveWindow());

                originalClipboard = await clipboard.CaptureAsync();
                AppendResult("clipboard:captured");
                if (existingTextAppWindow != IntPtr.Zero)
                {
                    // Browser contenteditable controls may retain the real DOM focus
                    // while UIA reports no focused Edit/Document (or reports the
                    // omnibox as focused too). Probe the current focus read-only first;
                    // exact TEST is the only authority that permits replacement.
                    SendExternalSelectAll();
                    await Task.Delay(100);
                    var originalTargetSelection = await textTransaction.CaptureAsync(
                        allowPreviousWordFallback: false);
                    for (var focusAttempt = 1;
                         focusAttempt <= 4 &&
                         !IsExpectedCompatibilitySentinel(originalTargetSelection);
                         focusAttempt++)
                    {
                        AppendResult(
                            $"focus-probe:currentSelectionMatch=False;" +
                            $"length={originalTargetSelection?.Text.Length ?? 0};" +
                            $"attempt={focusAttempt}");
                        _ = await TryFocusAndSelectExternalTextAsync(
                            existingTextAppWindow,
                            existingTextControlWindow);

                        SendExternalSelectAll();
                        // A cold Chromium accessibility provider may finish its
                        // first bounded safety probe after the DOM field has
                        // already received focus. Wait for that probe gate to
                        // recover, then require the exact sentinel before any
                        // replacement is permitted.
                        await Task.Delay(500);
                        originalTargetSelection = await textTransaction.CaptureAsync(
                            allowPreviousWordFallback: false);
                    }

                    if (!string.Equals(
                        originalTargetSelection?.Text.TrimEnd('\r', '\n'),
                        "TEST",
                        StringComparison.Ordinal))
                    {
                        var observed = originalTargetSelection?.Text ?? string.Empty;
                        AppendResult(
                            $"compatibility-target:unexpected-length={observed.Length}");
                        throw new InvalidOperationException(
                            "The compatibility target no longer contains the expected TEST sentinel.");
                    }
                    AppendResult("focus-probe:exactSentinel=True");

                    existingTargetModified = true;
                    if (!await textTransaction.ReplaceAsync(
                        originalTargetSelection!,
                        "ghbdtn"))
                    {
                        throw new InvalidOperationException(
                            "The compatibility sentinel could not be replaced through the production transaction.");
                    }
                    SendExternalSelectAll();
                    await Task.Delay(100);
                }
                var completedIterations = 0;
                for (var iteration = 1; iteration <= physicalIterations; iteration++)
                {
                    var manualCase = manualCorrectionMatrix
                        ? ManualCorrectionCases[iteration - 1]
                        : new ManualCorrectionCase("default", "ghbdtn", "привет");
                    if (manualCorrectionMatrix)
                    {
                        AppendResult(
                            $"manual-case:id={manualCase.Id};sourceLength={manualCase.Input.Length};" +
                            $"expectedLength={manualCase.Expected.Length}");
                    }
                    if (!TryClaimForeground(targetWindow))
                        throw new InvalidOperationException(
                            $"The E2E window lost foreground focus before iteration {iteration}; no input was sent.");
                    if (existingTextControlWindow != IntPtr.Zero &&
                        !await TryFocusAndSelectExternalTextAsync(
                            existingTextAppWindow,
                            existingTextControlWindow))
                    {
                        throw new InvalidOperationException(
                            $"The exact compatibility text control lost focus before iteration {iteration}.");
                    }

                    if (!hasExternalTarget)
                    {
                        editor.Text = manualCase.Input;
                        editor.Focus();
                        editor.SelectAll();
                    }
                    else if (existingTextAppWindow == IntPtr.Zero)
                    {
                        SendExternalSelectAll();
                        await Task.Delay(100);
                    }
                    await SetClipboardSentinelAsync();

                    SendExternalHotkey(configuredHotkey, configuredHotkeyVirtualKey);
                    if (partialInput != null)
                    {
                        await partialInput.FailureObserved.Task.WaitAsync(
                            TimeSpan.FromSeconds(8));
                        if (partialReplacementInputRaceTest ||
                            partialReplacementMouseRaceTest ||
                            partialReplacementXButtonRaceTest)
                        {
                            await partialInput.PhysicalInputObserved.Task.WaitAsync(
                                TimeSpan.FromSeconds(8));
                            await Task.Delay(250);
                            if (partialInput.RollbackTextObserved.Task.IsCompleted)
                            {
                                throw new InvalidOperationException(
                                    "Stale partial replacement rollback ran after new physical input.");
                            }
                            AppendResult(
                                partialReplacementXButtonRaceTest
                                    ? "partial-replacement-xbutton-race:inputObserved=True;rollbackSkipped=True"
                                : partialReplacementMouseRaceTest
                                    ? "partial-replacement-mouse-race:inputObserved=True;rollbackSkipped=True"
                                    : "partial-replacement-race:inputObserved=True;rollbackSkipped=True");
                        }
                        else
                        {
                            await partialInput.RollbackTextObserved.Task.WaitAsync(
                                TimeSpan.FromSeconds(8));
                            AppendResult(
                                "partial-replacement:failureObserved=True;rollbackObserved=True");
                        }
                    }
                    string targetText;
                    if (existingTextAppWindow != IntPtr.Zero)
                    {
                        await Task.Delay(5_000);
                        if (existingTextControlWindow != IntPtr.Zero &&
                            !await TryFocusAndSelectExternalTextAsync(
                                existingTextAppWindow,
                                existingTextControlWindow))
                        {
                            throw new InvalidOperationException(
                                "The exact compatibility text control could not be refocused for verification.");
                        }
                        SendExternalSelectAll();
                        await Task.Delay(100);
                        var observedTargetSelection = await textTransaction.CaptureAsync(
                            allowPreviousWordFallback: false);
                        targetText = observedTargetSelection?.Text ?? string.Empty;
                        if (observedTargetSelection == null ||
                            !await textTransaction.ReplaceAsync(
                                observedTargetSelection,
                                "TEST"))
                        {
                            throw new InvalidOperationException(
                                "The compatibility target could not be restored through the production transaction.");
                        }
                        SendExternalSelectAll();
                        await Task.Delay(100);
                        var restoredTargetSelection = await textTransaction.CaptureAsync(
                            allowPreviousWordFallback: false);
                        if (!string.Equals(
                            restoredTargetSelection?.Text.TrimEnd('\r', '\n'),
                            "TEST",
                            StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "The compatibility target TEST sentinel could not be restored exactly.");
                        }
                        existingTargetModified = false;
                    }
                    else
                    {
                        var expectedTargetText = partialReplacementTest
                                ? "ghbdtn"
                                : manualCase.Expected;
                        var verificationSeconds = expectedTargetText.Length <= 128
                            ? 8
                            : Math.Min(20, 8 + expectedTargetText.Length / 40d);
                        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(verificationSeconds);
                        while (DateTime.UtcNow < deadline)
                        {
                            var observedText = ReadTargetText(
                                externalEditKind,
                                externalStatePath,
                                editor);
                            if (partialReplacementInputRaceTest
                                ? observedText.StartsWith("пр", StringComparison.Ordinal) &&
                                  observedText.Length > 2
                                : partialReplacementMouseRaceTest || partialReplacementXButtonRaceTest
                                    ? observedText == "пр"
                                : observedText == expectedTargetText)
                            {
                                break;
                            }
                            await Task.Delay(25);
                        }
                        targetText = ReadTargetText(externalEditKind, externalStatePath, editor);
                    }

                    // Do not inspect the clipboard while LayoutFix owns its transaction:
                    // polling OpenClipboard here creates artificial contention with the
                    // production STA worker and makes the E2E itself flaky.
                    await Task.Delay(physicalIterations == 1 ? 200 : 25);
                    var clipboardPreserved = false;
                    for (var attempt = 0; attempt < 3 && !clipboardPreserved; attempt++)
                    {
                        clipboardPreserved = IsClipboardSentinelRestored();
                        if (!clipboardPreserved)
                            await Task.Delay(100);
                    }

                    var workerClipboardText = await clipboard.ReadTextAsync();
                    var workerClipboard = workerClipboardText == ClipboardSentinel
                        ? "sentinel"
                        : workerClipboardText == null
                            ? "non-text"
                            : $"other-text-length-{workerClipboardText.Length}";
                    var expectedText = partialReplacementTest
                            ? "ghbdtn"
                            : manualCase.Expected;
                    var targetTextMatches = partialReplacementInputRaceTest
                        ? targetText.StartsWith("пр", StringComparison.Ordinal) &&
                          targetText.Length > 2
                        : partialReplacementMouseRaceTest || partialReplacementXButtonRaceTest
                            ? targetText == "пр"
                        : targetText == expectedText;
                    var passed = targetTextMatches &&
                        clipboardPreserved && workerClipboardText == ClipboardSentinel;
                    if (!passed)
                    {
                        _exitCode = 2;
                        AppendResult(
                            $"verify:iteration={iteration};text={targetText};" +
                            $"clipboardPreserved={clipboardPreserved};clipboard={DescribeClipboard()};" +
                            $"workerClipboard={workerClipboard};exit={_exitCode}");
                        break;
                    }

                    completedIterations = iteration;
                    if (physicalIterations == 1 || iteration % 100 == 0)
                        AppendResult($"progress:completed={iteration}");

                    // The visible replacement and restored clipboard become observable
                    // just before the coordinator releases its single-flight hotkey slot.
                    // Keep soak iterations sequential instead of accidentally testing the
                    // intentional busy-hotkey rejection window between those two events.
                    if (iteration < physicalIterations)
                        await Task.Delay(100);
                }

                if (completedIterations == physicalIterations)
                    _exitCode = 0;
                AppendResult(
                    $"verify:completed={completedIterations};requested={physicalIterations};exit={_exitCode}");
            }
            catch (Exception exception)
            {
                _exitCode = 3;
                AppendResult($"error:{exception}");
            }
            finally
            {
                if (existingTargetModified && existingTextAppWindow != IntPtr.Zero)
                {
                    try
                    {
                        if (!TryClaimForeground(existingTextAppWindow) ||
                            !await TryFocusAndSelectExternalTextAsync(
                                existingTextAppWindow,
                                existingTextControlWindow))
                            throw new InvalidOperationException(
                                "The compatibility target could not be focused for fail-safe restoration.");

                        SendExternalSelectAll();
                        await Task.Delay(100);
                        await input.SendTextAsync("TEST");
                    }
                    catch (Exception exception)
                    {
                        AppendResult($"restore-error:{exception}");
                        _exitCode = 5;
                    }
                }
                if (originalClipboard != null)
                {
                    try { await clipboard.RestoreAsync(originalClipboard); } catch { _exitCode = 4; }
                    originalClipboard.Dispose();
                }
                if (externalEditProcess != null)
                {
                    try
                    {
                        externalEditProcess.CloseMainWindow();
                        if (!externalEditProcess.WaitForExit(2_000))
                            externalEditProcess.Kill(entireProcessTree: true);
                    }
                    catch { }
                    externalEditProcess.Dispose();
                }
                AppendResult($"finish:{_exitCode}");
                form.Close();
            }
        };

        coordinator.Initialize();
        AppendResult("coordinator:initialized");
        var observedHotkeys = 0;
        keyboardHook.HotkeyPressed += (_, args) =>
        {
            observedHotkeys++;
            if (physicalIterations == 1 || observedHotkeys % 100 == 0)
            {
                AppendResult(
                    $"hook:count={observedHotkeys};combo={args.Combo};" +
                    $"repeat={args.IsRepeat};handled={args.Handled}");
            }
        };
        keyboardHook.Start();
        mouseHook.Start();
        AppendResult("hook:started");
        Application.Run(form);
        mouseHook.Stop();
        keyboardHook.Stop();

        if (File.Exists(logPath))
        {
            var logText = File.ReadAllText(logPath);
            AppendResult("log:begin");
            foreach (var line in logText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                AppendResult(line);
            AppendResult("log:end");

            if (settings.Current.LoggingEnabled)
            {
                var captureDiagnostic = logText.Contains(
                    "SupportDiagnostic: CaptureId=",
                    StringComparison.Ordinal);
                var targetDiagnostic = logText.Contains(
                    "Phase=target-probe",
                    StringComparison.Ordinal);
                var targetProcessDiagnostic = logText.Contains(
                    "TargetProcess=",
                    StringComparison.Ordinal);
                var privateContentAbsent =
                    !ManualCorrectionCases.Any(testCase =>
                        logText.Contains(testCase.Input, StringComparison.Ordinal) ||
                        logText.Contains(testCase.Expected, StringComparison.Ordinal)) &&
                    !logText.Contains(ClipboardSentinel, StringComparison.Ordinal);
                AppendResult(
                    $"diagnostics:capture={captureDiagnostic};target={targetDiagnostic};" +
                    $"process={targetProcessDiagnostic};privacy={privateContentAbsent};" +
                    $"forced={diagnosticsForced}");
                if (_exitCode == 0 &&
                    (!captureDiagnostic || !targetDiagnostic ||
                     !targetProcessDiagnostic || !privateContentAbsent))
                {
                    _exitCode = 87;
                }
            }
        }

        try { Directory.Delete(testDirectory, recursive: true); } catch { }
        return _exitCode;
    }

    private static bool IsClipboardSentinelRestored()
    {
        try
        {
            return Clipboard.ContainsText(TextDataFormat.UnicodeText) &&
                Clipboard.GetText(TextDataFormat.UnicodeText) == ClipboardSentinel;
        }
        catch (ExternalException)
        {
            // Another process may hold the clipboard briefly while the transaction completes.
            return false;
        }
    }

    private static int RunExternalEditHost(string kind, string readyPath, string statePath)
    {
        using var form = new Form
        {
            Text = $"LayoutFix External {kind} E2E",
            Width = 700,
            Height = 250,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        Control editor = kind switch
        {
            "edit" => new TextBox
            {
                Multiline = true,
                AcceptsReturn = true
            },
            "richedit" => new RichTextBox(),
            "button" => new Button(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        editor.Name = "ExternalEditor";
        editor.Dock = DockStyle.Fill;
        editor.Font = new Font("Segoe UI", 18);
        editor.Text = "ghbdtn";
        form.Controls.Add(editor);

        void PersistState()
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    File.WriteAllText(statePath, editor.Text);
                    return;
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(5);
                }
            }
        }

        editor.TextChanged += (_, _) => PersistState();
        form.Shown += (_, _) =>
        {
            editor.Focus();
            editor.Select();
            if (editor is TextBoxBase textBox)
                textBox.SelectAll();
            PersistState();
            File.WriteAllText(readyPath, form.Handle.ToInt64().ToString());
        };
        Application.Run(form);
        return 0;
    }

    private static Process StartExternalEditHost(string kind, string readyPath, string statePath)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve the Windows E2E executable path.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add("--edit-host");
        startInfo.ArgumentList.Add(kind);
        startInfo.ArgumentList.Add(readyPath);
        startInfo.ArgumentList.Add(statePath);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to launch the external Edit host.");
    }

    private sealed class BrowserCompatibilityHost : IDisposable
    {
        private readonly Process _process;
        private readonly string _profileDirectory;
        private readonly string _ownedProfilePrefix;

        private BrowserCompatibilityHost(
            Process process,
            string profileDirectory,
            string ownedProfilePrefix)
        {
            _process = process;
            _profileDirectory = profileDirectory;
            _ownedProfilePrefix = ownedProfilePrefix;
        }

        public IntPtr MainWindowHandle
        {
            get
            {
                _process.Refresh();
                return _process.MainWindowHandle;
            }
        }

        public static BrowserCompatibilityHost Start(string browserKind, string targetKind)
        {
            var browser = browserKind switch
            {
                "edge" => new
                {
                    VendorName = "Microsoft",
                    DisplayName = "Edge",
                    ExecutablePaths = new[]
                    {
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            "Microsoft", "Edge", "Application", "msedge.exe"),
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            "Microsoft", "Edge", "Application", "msedge.exe")
                    }
                },
                "chrome" => new
                {
                    VendorName = "Google",
                    DisplayName = "Chrome",
                    ExecutablePaths = new[]
                    {
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            "Google", "Chrome", "Application", "chrome.exe"),
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            "Google", "Chrome", "Application", "chrome.exe"),
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Google", "Chrome", "Application", "chrome.exe")
                    }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(browserKind))
            };
            var browserPath = browser.ExecutablePaths.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException(
                    $"{browser.VendorName} {browser.DisplayName} is not installed in a supported location.");

            var systemTemp = Path.GetFullPath(Path.GetTempPath());
            var profilePrefix = $"LayoutFix.{browser.DisplayName}E2E.";
            var ownedProfilePrefix = Path.Combine(systemTemp, profilePrefix);
            var profileDirectory = Path.GetFullPath(Path.Combine(
                systemTemp,
                $"{profilePrefix}{Guid.NewGuid():N}"));
            if (!profileDirectory.StartsWith(
                    ownedProfilePrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Resolved {browser.DisplayName} test profile is outside the owned temporary prefix.");
            }

            Directory.CreateDirectory(profileDirectory);
            Process? process = null;
            try
            {
                var fixturePath = Path.Combine(profileDirectory, "text-compatibility.html");
                File.WriteAllText(fixturePath, CreateFixture(browser.DisplayName, targetKind));

                var startInfo = new ProcessStartInfo(browserPath)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add($"--user-data-dir={profileDirectory}");
                startInfo.ArgumentList.Add("--no-first-run");
                startInfo.ArgumentList.Add("--no-default-browser-check");
                startInfo.ArgumentList.Add("--disable-sync");
                startInfo.ArgumentList.Add("--disable-background-mode");
                startInfo.ArgumentList.Add($"--app={new Uri(fixturePath).AbsoluteUri}");
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Unable to launch Microsoft Edge.");
                // GUI processes can emit URLs, tokens, profile paths, or unrelated
                // diagnostics. Drain both streams without persisting or displaying
                // their contents so compatibility tests cannot leak them into CI logs.
                process.OutputDataReceived += static (_, _) => { };
                process.ErrorDataReceived += static (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero &&
                        process.MainWindowTitle.Contains(
                            $"LayoutFix {browser.DisplayName} E2E {targetKind}",
                            StringComparison.Ordinal))
                    {
                        // MainWindowTitle is populated from the local document only
                        // after navigation commits. Give Chromium one additional
                        // rendering turn so the fixture's load/focus handler runs.
                        Thread.Sleep(750);
                        return new BrowserCompatibilityHost(
                            process,
                            profileDirectory,
                            ownedProfilePrefix);
                    }
                    Thread.Sleep(100);
                }

                throw new InvalidOperationException(
                    $"The isolated {browser.VendorName} {browser.DisplayName} test document did not finish loading.");
            }
            catch
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                        process.WaitForExit(3_000);
                    }
                    catch { }
                    process.Dispose();
                }
                TryDeleteOwnedProfile(profileDirectory, ownedProfilePrefix);
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.CloseMainWindow();
                    if (!_process.WaitForExit(3_000))
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(3_000);
                    }
                }
            }
            catch { }
            finally
            {
                _process.Dispose();
                TryDeleteOwnedProfile(_profileDirectory, _ownedProfilePrefix);
            }
        }

        private static string CreateFixture(string browserName, string targetKind)
        {
            var target = targetKind switch
            {
                "input" => "<input id=\"target\" aria-label=\"LayoutFix input target\" autofocus value=\"TEST\">",
                "textarea" => "<textarea id=\"target\" aria-label=\"LayoutFix textarea target\" autofocus>TEST</textarea>",
                "contenteditable" => "<div id=\"target\" role=\"textbox\" aria-label=\"LayoutFix contenteditable target\" contenteditable=\"true\">TEST</div>",
                _ => throw new ArgumentOutOfRangeException(nameof(targetKind))
            };
            var selectScript = targetKind == "contenteditable"
                ? "const r=document.createRange();r.selectNodeContents(t);const s=getSelection();s.removeAllRanges();s.addRange(r);"
                : "t.select();";
            return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <title>LayoutFix {{browserName}} E2E {{targetKind}}</title>
                  <style>body{font:24px Segoe UI;padding:40px}#target{display:block;width:600px;min-height:48px;font:24px Segoe UI}</style>
                </head>
                <body>
                  <label>LayoutFix compatibility target</label>
                  {{target}}
                  <script>
                    addEventListener('load',()=>{
                      const t=document.getElementById('target');
                      const activate=()=>{t.focus();{{selectScript}}};
                      activate();
                      addEventListener('focus',()=>setTimeout(activate,0));
                      setTimeout(activate,250);
                      setTimeout(activate,1000);
                    });
                  </script>
                </body>
                </html>
                """;
        }

        private static void TryDeleteOwnedProfile(
            string profileDirectory,
            string ownedProfilePrefix)
        {
            var fullPath = Path.GetFullPath(profileDirectory);
            if (!fullPath.StartsWith(ownedProfilePrefix, StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                if (Directory.Exists(fullPath))
                    Directory.Delete(fullPath, recursive: true);
            }
            catch { }
        }
    }

    private static async Task<IntPtr> PrepareAfterEffectsRenameTargetAsync(
        IntPtr mainWindow)
    {
        try
        {
            var mainThread = Win32.GetWindowThreadProcessId(mainWindow, out var processId);
            if (mainThread == 0 || processId == 0)
            {
                AppendResult("aftereffects-rename:precondition=window-thread");
                return IntPtr.Zero;
            }

            using var process = Process.GetProcessById(checked((int)processId));
            process.Refresh();
            var executableName = Path.GetFileName(process.MainModule?.FileName);
            var mainClass = new System.Text.StringBuilder(256);
            var bounds = new Win32.RECT();
            var classRead = Win32.GetClassName(mainWindow, mainClass, mainClass.Capacity) > 0;
            var boundsRead = NativeFocus.GetWindowRect(mainWindow, ref bounds);
            var executableMatches = string.Equals(
                executableName,
                "AfterFX.exe",
                StringComparison.OrdinalIgnoreCase);
            var handleMatches = process.MainWindowHandle == mainWindow;
            var titleMatches = process.MainWindowTitle.Contains(
                "Adobe After Effects",
                StringComparison.Ordinal);
            var classMatches = classRead &&
                mainClass.ToString().StartsWith("AE_CApplication_", StringComparison.Ordinal);
            if (!executableMatches || !handleMatches || !process.Responding ||
                !titleMatches || !classMatches || !boundsRead)
            {
                AppendResult(
                    $"aftereffects-rename:precondition=identity;exe={executableMatches};" +
                    $"handle={handleMatches};responding={process.Responding};" +
                    $"title={titleMatches};class={classMatches};bounds={boundsRead}");
                return IntPtr.Zero;
            }

            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (Win32.GetForegroundWindow() != mainWindow)
            {
                if (!TryClaimForeground(mainWindow))
                {
                    AppendResult("aftereffects-rename:precondition=foreground-claim");
                    return IntPtr.Zero;
                }
                await Task.Delay(100);
            }
            if (width < 1200 || height < 700 || Win32.GetForegroundWindow() != mainWindow)
            {
                AppendResult(
                    $"aftereffects-rename:precondition=surface;geometry={width}x{height};" +
                    $"foreground={Win32.GetForegroundWindow() == mainWindow}");
                return IntPtr.Zero;
            }

            // The fixture opens the composition in the standard workspace. This
            // bounded click lands on its only timeline layer; the process/window,
            // geometry and resulting exact Adobe rename identity are all verified
            // before any value is changed.
            SendExternalKeys(0x1B); // Escape any stale transient Adobe edit mode.
            await Task.Delay(150);
            var restoreCursor = NativeFocus.GetCursorPos(out var originalCursor);
            try
            {
                var layerNameX = bounds.Left + (int)Math.Round(width * 0.114);
                var layerNameY = bounds.Top + (int)Math.Round(height * 0.792);
                AppendResult(
                    $"aftereffects-rename:geometry={width}x{height};" +
                    $"click={layerNameX},{layerNameY}");
                if (!NativeFocus.SetCursorPos(layerNameX, layerNameY))
                    return IntPtr.Zero;
                NativeFocus.MouseEvent(
                    NativeFocus.MouseEventLeftDown,
                    0,
                    0,
                    0,
                    UIntPtr.Zero);
                NativeFocus.MouseEvent(
                    NativeFocus.MouseEventLeftUp,
                    0,
                    0,
                    0,
                    UIntPtr.Zero);
            }
            finally
            {
                if (restoreCursor)
                    NativeFocus.SetCursorPos(originalCursor.X, originalCursor.Y);
            }

            await Task.Delay(150);
            SendExternalKeys(0x0D); // Enter opens rename for the selected fixture layer.

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
                var focused = AutomationElement.FocusedElement;
                if (focused == null)
                    continue;

                var current = focused.Current;
                var controlWindow = new IntPtr(current.NativeWindowHandle);
                if (current.ProcessId != processId ||
                    controlWindow == IntPtr.Zero ||
                    !NativeFocus.IsChild(mainWindow, controlWindow) ||
                    current.ControlType != ControlType.Edit ||
                    !string.Equals(current.ClassName, "Edit", StringComparison.Ordinal) ||
                    !string.Equals(current.AutomationId, "1", StringComparison.Ordinal) ||
                    !current.IsEnabled || !current.IsKeyboardFocusable ||
                    !current.HasKeyboardFocus || current.IsPassword ||
                    !focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) ||
                    valueObject is not ValuePattern valuePattern || valuePattern.Current.IsReadOnly ||
                    !focused.TryGetCurrentPattern(TextPattern.Pattern, out var textObject) ||
                    textObject is not TextPattern textPattern)
                {
                    continue;
                }

                var currentValue = valuePattern.Current.Value;
                var parent = TreeWalker.ControlViewWalker.GetParent(focused);
                if (parent == null ||
                    parent.Current.ProcessId != processId ||
                    parent.Current.ControlType != ControlType.Pane ||
                    !string.Equals(
                        parent.Current.Name,
                        "OS_EditTextContainer",
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        parent.Current.ClassName,
                        "DroverLord - Window Class",
                        StringComparison.Ordinal) ||
                    !(string.Equals(
                            current.Name,
                            "UI_TextEdit",
                            StringComparison.Ordinal) ||
                        (!string.IsNullOrEmpty(currentValue) &&
                            string.Equals(
                                current.Name,
                                currentValue,
                                StringComparison.Ordinal))) ||
                    !string.Equals(currentValue, "TEST", StringComparison.Ordinal))
                {
                    return IntPtr.Zero;
                }
                SendExternalSelectAll();
                await Task.Delay(100);
                var selection = textPattern.GetSelection();
                if (selection.Length == 1 &&
                    string.Equals(selection[0].GetText(-1), "TEST", StringComparison.Ordinal) &&
                    Win32.GetForegroundWindow() == mainWindow &&
                    AutomationElement.FocusedElement.Current.NativeWindowHandle == controlWindow)
                {
                    return controlWindow;
                }

                return IntPtr.Zero;
            }

            try
            {
                var observed = AutomationElement.FocusedElement?.Current;
                if (observed != null)
                {
                    AppendResult(
                        $"aftereffects-rename:focus-miss-name={observed.Value.Name};" +
                        $"class={observed.Value.ClassName};id={observed.Value.AutomationId};" +
                        $"handle=0x{observed.Value.NativeWindowHandle:X}");
                }
            }
            catch (Exception exception)
            {
                AppendResult(
                    $"aftereffects-rename:focus-miss-error={exception.GetType().FullName}");
            }
        }
        catch (Exception exception)
        {
            AppendResult(
                $"aftereffects-rename:prepareError={exception.GetType().FullName};" +
                $"hresult=0x{exception.HResult:X8}");
        }

        return IntPtr.Zero;
    }

    private static async Task<bool> TryFocusAndSelectExternalTextAsync(
        IntPtr window,
        IntPtr preferredControl = default)
    {
        if (preferredControl != IntPtr.Zero)
        {
            try
            {
                if (!NativeFocus.IsChild(window, preferredControl))
                    return false;

                var className = new System.Text.StringBuilder(256);
                if (Win32.GetClassName(preferredControl, className, className.Capacity) <= 0 ||
                    !className.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var mainThread = Win32.GetWindowThreadProcessId(window, out var mainProcessId);
                var controlThread = Win32.GetWindowThreadProcessId(
                    preferredControl,
                    out var controlProcessId);
                if (mainThread == 0 || controlThread == 0 ||
                    mainProcessId == 0 || mainProcessId != controlProcessId)
                {
                    return false;
                }

                var element = AutomationElement.FromHandle(preferredControl);
                var current = element.Current;
                if (current.ProcessId != mainProcessId ||
                    current.NativeWindowHandle != preferredControl ||
                    current.ControlType != ControlType.Edit ||
                    !current.IsEnabled || current.IsOffscreen || current.IsPassword)
                {
                    return false;
                }

                var nativeFocused = TrySetNativeChildFocus(window, preferredControl);
                try
                {
                    element.SetFocus();
                }
                catch when (nativeFocused)
                {
                }

                for (var attempt = 0; attempt < 10; attempt++)
                {
                    var focused = AutomationElement.FocusedElement;
                    if (focused.Current.ProcessId == mainProcessId &&
                        focused.Current.NativeWindowHandle == preferredControl)
                    {
                        AppendResult("focus-probe:path=exact-control;focused=True");
                        return true;
                    }
                    await Task.Delay(50);
                }

                return false;
            }
            catch (Exception exception)
            {
                AppendResult(
                    $"focus-probe:exactControlError={exception.GetType().FullName};" +
                    $"hresult=0x{exception.HResult:X8}");
                return false;
            }
        }

        var nativeEdit = FindNativeEditDescendant(window);
        if (nativeEdit != IntPtr.Zero)
        {
            var focused = TrySetNativeChildFocus(window, nativeEdit);
            AppendResult($"focus-probe:path=native-edit;focused={focused}");
            return focused;
        }

        try
        {
            return await Task.Run(() =>
            {
                var root = AutomationElement.FromHandle(window);
                var condition = new OrCondition(
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Document),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Edit));
                var candidates = root.FindAll(TreeScope.Descendants, condition);
                AppendResult($"focus-probe:candidates={candidates.Count}");
                var orderedCandidates = new List<(AutomationElement Element, int Rank)>();
                foreach (AutomationElement candidate in candidates)
                {
                    try
                    {
                        var current = candidate.Current;
                        if (!current.IsEnabled || current.IsOffscreen || current.IsPassword)
                            continue;

                        var isEdit = current.ControlType == ControlType.Edit;
                        var isInsideDocument = HasDocumentAncestor(candidate, root);
                        var rank = (isInsideDocument, current.HasKeyboardFocus, isEdit) switch
                        {
                            (true, true, _) => 0,
                            (true, false, true) => 1,
                            (false, true, true) => 2,
                            (false, false, true) => 3,
                            _ => 4
                        };
                        orderedCandidates.Add((candidate, rank));
                    }
                    catch (Exception exception)
                    {
                        AppendResult(
                            $"focus-probe:candidateInspectError={exception.GetType().FullName};" +
                            $"hresult=0x{exception.HResult:X8}");
                    }
                }

                foreach (var item in orderedCandidates.OrderBy(candidate => candidate.Rank))
                {
                    var candidate = item.Element;
                    try
                    {
                        var current = candidate.Current;
                        var nativeHandle = new IntPtr(current.NativeWindowHandle);
                        var nativeFocused = nativeHandle != IntPtr.Zero &&
                            TrySetNativeChildFocus(window, nativeHandle);
                        try
                        {
                            candidate.SetFocus();
                        }
                        catch when (nativeFocused)
                        {
                            // Native GUI-thread focus is authoritative for
                            // classic providers that reject UIA SetFocus.
                        }

                        // The subsequent exact TEST capture is the authoritative
                        // focus proof. Some providers lag HasKeyboardFocus even
                        // after SetFocus succeeds.
                        return true;
                    }
                    catch (Exception exception)
                    {
                        AppendResult(
                            $"focus-probe:candidateError={exception.GetType().FullName};" +
                            $"hresult=0x{exception.HResult:X8}");
                        if (TryClickAutomationElement(window, candidate))
                        {
                            AppendResult("focus-probe:path=uia-bounded-click;focused=True");
                            return true;
                        }
                    }
                }

                return false;
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            AppendResult(
                $"focus-probe:error={exception.GetType().FullName};hresult=0x{exception.HResult:X8}");
            return false;
        }
    }

    private static bool TryClickAutomationElement(
        IntPtr topLevelWindow,
        AutomationElement candidate)
    {
        try
        {
            if (Win32.GetForegroundWindow() != topLevelWindow)
                return false;

            var bounds = candidate.Current.BoundingRectangle;
            var windowBounds = new Win32.RECT();
            if (bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4 ||
                !NativeFocus.GetWindowRect(topLevelWindow, ref windowBounds))
            {
                return false;
            }

            var x = checked((int)Math.Round(bounds.Left + (bounds.Width / 2)));
            var y = checked((int)Math.Round(bounds.Top + (bounds.Height / 2)));
            if (x < windowBounds.Left || x >= windowBounds.Right ||
                y < windowBounds.Top || y >= windowBounds.Bottom)
            {
                return false;
            }

            var restoreCursor = NativeFocus.GetCursorPos(out var originalCursor);
            try
            {
                if (!NativeFocus.SetCursorPos(x, y))
                    return false;
                NativeFocus.MouseEvent(NativeFocus.MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                NativeFocus.MouseEvent(NativeFocus.MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(50);
                return true;
            }
            finally
            {
                if (restoreCursor)
                    NativeFocus.SetCursorPos(originalCursor.X, originalCursor.Y);
            }
        }
        catch (Exception exception)
        {
            AppendResult(
                $"focus-probe:clickError={exception.GetType().FullName};" +
                $"hresult=0x{exception.HResult:X8}");
            return false;
        }
    }

    private static bool HasDocumentAncestor(
        AutomationElement candidate,
        AutomationElement root)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var parent = walker.GetParent(candidate);
            while (parent != null && !parent.Equals(root))
            {
                if (parent.Current.ControlType == ControlType.Document)
                    return true;
                parent = walker.GetParent(parent);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        return false;
    }

    private static bool IsExpectedCompatibilitySentinel(TextSelection? selection) =>
        string.Equals(
            selection?.Text.TrimEnd('\r', '\n'),
            "TEST",
            StringComparison.Ordinal);

    private static void AppendFocusedElementDiagnostics(ActiveWindowContext context)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null)
            {
                AppendResult("focus-uia:element=null");
                return;
            }

            var current = element.Current;
            AppendResult(
                $"focus-uia:type={current.ControlType?.ProgrammaticName ?? "unknown"};" +
                $"processMatch={current.ProcessId == context.ProcessId};" +
                $"focusable={current.IsKeyboardFocusable};focused={current.HasKeyboardFocus};" +
                $"password={current.IsPassword};" +
                $"value={element.TryGetCurrentPattern(ValuePattern.Pattern, out _)};" +
                $"text={element.TryGetCurrentPattern(TextPattern.Pattern, out _)}");
        }
        catch (Exception exception)
        {
            AppendResult(
                $"focus-uia:error={exception.GetType().FullName};hresult=0x{exception.HResult:X8}");
        }
    }

    private static IntPtr FindNativeEditDescendant(IntPtr topLevelWindow)
    {
        var result = IntPtr.Zero;
        var fallback = IntPtr.Zero;
        long resultArea = -1;
        long fallbackArea = -1;
        NativeFocus.EnumChildWindows(
            topLevelWindow,
            (window, _) =>
            {
                if (!NativeFocus.IsWindowVisible(window) || !NativeFocus.IsWindowEnabled(window))
                    return true;

                var className = new System.Text.StringBuilder(256);
                if (Win32.GetClassName(window, className, className.Capacity) <= 0)
                    return true;

                var value = className.ToString();
                var bounds = new Win32.RECT();
                if (!NativeFocus.GetWindowRect(window, ref bounds))
                    return true;
                var area = Math.Max(0L, bounds.Right - bounds.Left) *
                    Math.Max(0L, bounds.Bottom - bounds.Top);
                if (value.Contains("RichEdit", StringComparison.OrdinalIgnoreCase))
                {
                    if (area > resultArea)
                    {
                        result = window;
                        resultArea = area;
                    }
                    return true;
                }
                if (value.Contains("Edit", StringComparison.OrdinalIgnoreCase) &&
                    area > fallbackArea)
                {
                    fallback = window;
                    fallbackArea = area;
                }

                return true;
            },
            IntPtr.Zero);
        return result != IntPtr.Zero ? result : fallback;
    }

    private static bool TrySetNativeChildFocus(IntPtr topLevelWindow, IntPtr childWindow)
    {
        var currentThread = NativeFocus.GetCurrentThreadId();
        var targetThread = Win32.GetWindowThreadProcessId(childWindow, out _);
        var attached = targetThread != 0 && targetThread != currentThread &&
            NativeFocus.AttachThreadInput(currentThread, targetThread, attach: true);

        try
        {
            NativeFocus.BringWindowToTop(topLevelWindow);
            NativeFocus.SetForegroundWindow(topLevelWindow);
            NativeFocus.SetFocus(childWindow);
            var guiInfo = new Win32.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<Win32.GUITHREADINFO>()
            };
            return Win32.GetGUIThreadInfo(targetThread, ref guiInfo) &&
                guiInfo.hwndFocus == childWindow;
        }
        finally
        {
            if (attached)
                NativeFocus.AttachThreadInput(currentThread, targetThread, attach: false);
        }
    }

    private static string ReadTargetText(string? externalKind, string statePath, TextBox editor)
    {
        if (externalKind == null)
            return editor.Text;

        try
        {
            return File.Exists(statePath) ? File.ReadAllText(statePath) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static int RunClipboardFormatsTest()
    {
        File.WriteAllText(ResultPath, $"clipboard-formats:start {DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        var testDirectory = Path.Combine(Path.GetTempPath(), $"LayoutFix.ClipboardE2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var filePath = Path.Combine(testDirectory, "file-drop-sentinel.txt");
        File.WriteAllText(filePath, "LayoutFix clipboard format test");

        var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
        settings.Current.LoggingEnabled = true;
        settings.Save(settings.Current);
        var logPath = Path.Combine(testDirectory, "clipboard-e2e.log");
        var logger = new FileLoggerService(settings, logPath);
        using var clipboard = new ClipboardService(logger);
        IClipboardSnapshot? original = null;
        var result = 1;

        try
        {
            original = clipboard.CaptureAsync().GetAwaiter().GetResult();
            const string unicode = "LAYOUTFIX_FORMAT_SENTINEL";
            const string rtf = @"{\rtf1\ansi LayoutFix format sentinel}";
            const string html = "<html><body><!--StartFragment-->LayoutFix format sentinel<!--EndFragment--></body></html>";
            byte[] metadata = [1, 3, 3, 7];
            byte[] chromiumToken = [2, 4, 6, 8, 10, 12];
            byte[] chromiumUrl = [11, 9, 7, 5, 3, 1];

            var data = new DataObject();
            data.SetText(unicode, TextDataFormat.UnicodeText);
            data.SetText(rtf, TextDataFormat.Rtf);
            data.SetText(html, TextDataFormat.Html);
            var files = new StringCollection { filePath };
            data.SetFileDropList(files);
            data.SetData("CanUploadToCloudClipboard", autoConvert: false, new MemoryStream(metadata));
            data.SetData(
                "Chromium internal source RFH token",
                autoConvert: false,
                new MemoryStream(chromiumToken));
            data.SetData(
                "Chromium internal source URL",
                autoConvert: false,
                new MemoryStream(chromiumUrl));
            Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 50);

            using (var snapshot = clipboard.CaptureAsync().GetAwaiter().GetResult())
            {
                Clipboard.SetText("temporary overwrite", TextDataFormat.UnicodeText);
                clipboard.RestoreAsync(snapshot).GetAwaiter().GetResult();
            }

            var restored = Clipboard.GetDataObject()
                ?? throw new InvalidOperationException("Restored clipboard has no data object.");
            var restoredUnicode = restored.GetData(DataFormats.UnicodeText, autoConvert: false) as string;
            var restoredRtf = restored.GetData(DataFormats.Rtf, autoConvert: false) as string;
            var restoredHtml = restored.GetData(DataFormats.Html, autoConvert: false) as string;
            var restoredFiles = restored.GetData(DataFormats.FileDrop, autoConvert: false) as string[];
            var restoredMetadata = ReadStreamBytes(
                restored.GetData("CanUploadToCloudClipboard", autoConvert: false));
            var restoredChromiumToken = ReadStreamBytes(
                restored.GetData("Chromium internal source RFH token", autoConvert: false));
            var restoredChromiumUrl = ReadStreamBytes(
                restored.GetData("Chromium internal source URL", autoConvert: false));
            var formatsPassed = restoredUnicode == unicode && restoredRtf == rtf &&
                restoredHtml == html && restoredFiles?.SequenceEqual([filePath]) == true &&
                restoredMetadata.SequenceEqual(metadata) &&
                restoredChromiumToken.SequenceEqual(chromiumToken) &&
                restoredChromiumUrl.SequenceEqual(chromiumUrl);
            AppendResult(
                $"clipboard-formats:restore={formatsPassed};" +
                $"unicode={restoredUnicode == unicode};rtf={restoredRtf == rtf};" +
                $"html={restoredHtml == html};fileDrop={restoredFiles?.SequenceEqual([filePath]) == true};" +
                $"metadata={restoredMetadata.SequenceEqual(metadata)};" +
                $"chromiumToken={restoredChromiumToken.SequenceEqual(chromiumToken)};" +
                $"chromiumUrl={restoredChromiumUrl.SequenceEqual(chromiumUrl)}");

            using var bitmap = new Bitmap(2, 2);
            const string privateFormatName = "PRIVATE_CLIPBOARD_FORMAT_SENTINEL";
            var complex = new DataObject();
            complex.SetText("COMPLEX_SENTINEL", TextDataFormat.UnicodeText);
            complex.SetImage(bitmap);
            complex.SetData(
                privateFormatName,
                autoConvert: false,
                new MemoryStream([9, 8, 7, 6]));
            Clipboard.SetDataObject(complex, copy: true, retryTimes: 5, retryDelay: 50);
            var rejected = false;
            try
            {
                using var unexpected = clipboard.CaptureAsync().GetAwaiter().GetResult();
            }
            catch (NotSupportedException)
            {
                rejected = true;
            }

            var complexUntouched = Clipboard.ContainsText(TextDataFormat.UnicodeText) &&
                Clipboard.GetText(TextDataFormat.UnicodeText) == "COMPLEX_SENTINEL" &&
                Clipboard.ContainsImage();
            var privateFormatNameRedacted = File.Exists(logPath) &&
                !File.ReadAllText(logPath).Contains(privateFormatName, StringComparison.Ordinal);
            AppendResult(
                $"clipboard-formats:complexRejected={rejected};untouched={complexUntouched};" +
                $"privateFormatNameRedacted={privateFormatNameRedacted}");

            var hostStart = new ProcessStartInfo(
                Environment.ProcessPath
                    ?? throw new InvalidOperationException("Current E2E executable path is unavailable."))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            hostStart.ArgumentList.Add("--empty-ole-clipboard-host");
            using var emptyOleHost = Process.Start(hostStart)
                ?? throw new InvalidOperationException("Empty OLE clipboard host could not start.");
            if (!emptyOleHost.WaitForExit(5_000) || emptyOleHost.ExitCode != 0)
                throw new InvalidOperationException("Empty OLE clipboard host failed.");

            var nativeFormatCount = Win32.CountClipboardFormats();
            var oleData = Clipboard.GetDataObject();
            var advertisedFormatCount = oleData?.GetFormats(autoConvert: false).Length ?? 0;
            var emptyOleFallbackObserved = nativeFormatCount > 0 &&
                advertisedFormatCount == 0 &&
                !Clipboard.ContainsText(TextDataFormat.UnicodeText);
            using (var emptySnapshot = clipboard.CaptureAsync().GetAwaiter().GetResult())
            {
                Clipboard.SetText("temporary overwrite", TextDataFormat.UnicodeText);
                clipboard.RestoreAsync(emptySnapshot).GetAwaiter().GetResult();
            }
            var restoredEmptyData = Clipboard.GetDataObject();
            var restoredAdvertisedFormatCount =
                restoredEmptyData?.GetFormats(autoConvert: false).Length ?? 0;
            var emptyRestored = restoredAdvertisedFormatCount == 0 &&
                !Clipboard.ContainsText(TextDataFormat.UnicodeText);
            AppendResult(
                $"clipboard-formats:emptyOleFallback={emptyOleFallbackObserved};" +
                $"emptyRestored={emptyRestored};nativeFormatCount={nativeFormatCount};" +
                $"advertisedFormatCount={advertisedFormatCount};" +
                $"restoredAdvertisedFormatCount={restoredAdvertisedFormatCount}");

            result = formatsPassed && rejected && complexUntouched &&
                privateFormatNameRedacted && emptyOleFallbackObserved && emptyRestored
                    ? 0
                    : 2;
        }
        catch (Exception exception)
        {
            AppendResult($"clipboard-formats:error={exception.GetType().FullName};hresult=0x{exception.HResult:X8}");
            result = 3;
        }
        finally
        {
            if (original != null)
            {
                try
                {
                    clipboard.RestoreAsync(original).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    result = 4;
                    AppendResult(
                        $"clipboard-formats:originalRestoreError={exception.GetType().FullName};" +
                        $"hresult=0x{exception.HResult:X8}");
                }
                original.Dispose();
            }

            if (File.Exists(logPath))
            {
                foreach (var line in File.ReadLines(logPath))
                    AppendResult($"clipboard-formats:log={line}");
            }
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
            AppendResult("clipboard-formats:finish");
        }

        return result;
    }

    private static int RunEmptyOleClipboardHost()
    {
        try
        {
            Clipboard.SetDataObject(new DataObject(), copy: true, retryTimes: 5, retryDelay: 50);
            return 0;
        }
        catch
        {
            return 111;
        }
    }

    private static byte[] ReadStreamBytes(object? value)
    {
        if (value is not Stream stream)
            return [];

        var position = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        if (stream.CanSeek)
            stream.Position = position;
        return copy.ToArray();
    }

    private static async Task SetClipboardSentinelAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetText(ClipboardSentinel, TextDataFormat.UnicodeText);
                return;
            }
            catch (ExternalException) when (attempt < 10)
            {
                await Task.Delay(attempt * 20);
            }
        }
    }

    private static string DescribeClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                return "non-text";

            var text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (text == ClipboardSentinel)
                return "sentinel";
            if (text == "ghbdtn")
                return "captured-selection";
            return $"other-text-length-{text.Length}";
        }
        catch (ExternalException)
        {
            return "busy";
        }
    }

    private static bool TryClaimForeground(IntPtr targetWindow)
    {
        var foregroundWindow = Win32.GetForegroundWindow();
        if (foregroundWindow == targetWindow)
            return true;

        var currentThread = NativeFocus.GetCurrentThreadId();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : Win32.GetWindowThreadProcessId(foregroundWindow, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeFocus.AttachThreadInput(currentThread, foregroundThread, attach: true);

        try
        {
            NativeFocus.BringWindowToTop(targetWindow);
            NativeFocus.SetForegroundWindow(targetWindow);
            return Win32.GetForegroundWindow() == targetWindow;
        }
        finally
        {
            if (attached)
                NativeFocus.AttachThreadInput(currentThread, foregroundThread, attach: false);
        }
    }

    private static int RunAutoStartRegistryTest()
    {
        const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "LayoutFix";
        var executablePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executablePath) || !executablePath.Any(char.IsWhiteSpace))
        {
            Console.Error.WriteLine("Autostart registry E2E requires a harness path containing whitespace.");
            return 71;
        }

        object? previousValue = null;
        RegistryValueKind? previousKind = null;
        var hadPreviousValue = false;
        var result = 0;

        try
        {
            using (var existingKey = Registry.CurrentUser.OpenSubKey(registryPath, writable: false))
            {
                hadPreviousValue = existingKey?.GetValueNames().Contains(valueName) == true;
                if (hadPreviousValue)
                {
                    previousValue = existingKey!.GetValue(
                        valueName,
                        null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    previousKind = existingKey.GetValueKind(valueName);
                }
            }

            using (var writableKey = Registry.CurrentUser.CreateSubKey(registryPath, writable: true))
            {
                writableKey.SetValue(valueName, executablePath, RegistryValueKind.String);
            }

            var service = new AutoStartService();
            if (service.IsAutoStartEnabled)
            {
                Console.Error.WriteLine("Unsafe unquoted autostart command was accepted.");
                result = 72;
            }
            else
            {
                service.IsAutoStartEnabled = true;
                using var repairedKey = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
                var repairedValue = repairedKey?.GetValue(valueName) as string;
                var expectedValue = $"\"{executablePath}\"";
                if (!string.Equals(repairedValue, expectedValue, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Autostart repair did not produce the canonical quoted command.");
                    result = 73;
                }
                else if (!service.IsAutoStartEnabled)
                {
                    Console.Error.WriteLine("Canonical autostart command was not recognized after repair.");
                    result = 74;
                }
                else
                {
                    Console.WriteLine("autostart_registry=pass unsafe_unquoted=reject canonical_repair=pass");
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Autostart registry E2E failed: {exception.GetType().Name}.");
            result = 75;
        }
        finally
        {
            try
            {
                using var restoreKey = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
                if (hadPreviousValue && previousValue is not null && previousKind.HasValue)
                    restoreKey.SetValue(valueName, previousValue, previousKind.Value);
                else
                    restoreKey.DeleteValue(valueName, throwOnMissingValue: false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Autostart registry restore failed: {exception.GetType().Name}.");
                result = 76;
            }
        }

        return result;
    }

    private static int RunSettingsDiagnosticTest()
    {
        const string privateSentinel = "PRIVATE_SETTINGS_DIAGNOSTIC_PATH";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var settingsPath = Path.Combine(testDirectory, "settings.json");
            var logPath = Path.Combine(testDirectory, "layoutfix.log");
            var settings = new SettingsService(settingsPath);
            settings.Current.LoggingEnabled = true;
            settings.Save(settings.Current);
            var logger = new FileLoggerService(settings, logPath);
            logger.LogInfo("diagnostic bootstrap");
            using (var stream = new FileStream(
                       logPath,
                       FileMode.Open,
                       FileAccess.Write,
                       FileShare.Read))
            {
                stream.SetLength(5L * 1024 * 1024 + 1);
            }

            var coordinator = new SettingsPersistenceCoordinator(
                settings,
                new CountingAutoStartService(settings.Current.AutoStart),
                settings.Current.AutoStart);
            settings.Current.SoundEnabled = !settings.Current.SoundEnabled;

            SettingsPersistenceException? persistenceException = null;
            using (var fileLock = new FileStream(
                       settingsPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                try
                {
                    coordinator.Save(settings.Current);
                }
                catch (SettingsPersistenceException exception)
                {
                    persistenceException = exception;
                    logger.LogError(exception.SafeLogMessage, exception);
                }
            }

            if (persistenceException is not
                {
                    Stage: SettingsPersistenceStage.SettingsFile,
                    DiagnosticCode: "LF-ST-001"
                })
            {
                Console.Error.WriteLine("Locked settings file did not produce LF-ST-001.");
                return 80;
            }
            if (!coordinator.HasPendingChanges)
            {
                Console.Error.WriteLine("Locked settings failure did not remain pending.");
                return 81;
            }

            var backupPath = logPath + ".bak";
            if (!File.Exists(backupPath) || new FileInfo(backupPath).Length <= 5L * 1024 * 1024)
            {
                Console.Error.WriteLine("Settings diagnostic did not rotate the oversized log.");
                return 82;
            }

            var activeLog = File.ReadAllText(logPath);
            var requiredFields = new[]
            {
                "DiagnosticCode: LF-ST-001",
                "Action: settings-save",
                "Stage: settings-file",
                "Outcome: failed",
                "ExceptionType: LayoutFix.Services.SettingsPersistenceException",
                "HResult:"
            };
            if (requiredFields.Any(field => !activeLog.Contains(field, StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("Settings diagnostic log is missing stable fields.");
                return 83;
            }
            if (activeLog.Contains(privateSentinel, StringComparison.Ordinal) ||
                activeLog.Contains(testDirectory, StringComparison.OrdinalIgnoreCase) ||
                activeLog.Contains("settings.json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Settings diagnostic log exposed private path data.");
                return 84;
            }

            Console.WriteLine(
                "settings_diagnostic=pass code=LF-ST-001 rotation=pass privacy=pass pending_retry=pass");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Settings diagnostic E2E failed: {exception.GetType().Name}.");
            return 85;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunSettingsCleanCloseTest()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.SettingsCleanClose.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var innerSettings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
            var settings = new CountingSettingsService(innerSettings);
            var autoStart = new CountingAutoStartService(innerSettings.Current.AutoStart);
            var logger = new FileLoggerService(settings, Path.Combine(testDirectory, "clean-close.log"));
            using var modelDownloadService = new ModelDownloadService();
            var historyService = new TranslationHistoryService(
                settings,
                Path.Combine(testDirectory, "translation-history.json"));
            using var form = new SettingsForm(
                settings,
                autoStart,
                new PassthroughLocalizationService(),
                logger,
                modelDownloadService,
                historyService,
                new InMemoryTranslationCredentialStore());

            form.Shown += (_, _) => form.Close();
            Application.Run(form);

            if (settings.SaveCount != 0)
            {
                Console.Error.WriteLine("Clean Settings close rewrote settings.json.");
                return 77;
            }
            if (autoStart.WriteCount != 0)
            {
                Console.Error.WriteLine("Clean Settings close rewrote the autostart registration.");
                return 78;
            }

            Console.WriteLine("settings_clean_close=pass settings_writes=0 autostart_writes=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Settings clean-close E2E failed: {exception.GetType().Name}.");
            return 79;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunSettingsRegistryDiagnosticTest()
    {
        const string privateSentinel = "PRIVATE_SETTINGS_REGISTRY_DIAGNOSTIC";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var settingsPath = Path.Combine(testDirectory, "settings.json");
            var innerSettings = new SettingsService(settingsPath);
            innerSettings.Current.LoggingEnabled = true;
            innerSettings.Current.AutoStart = false;
            innerSettings.Save(innerSettings.Current);

            var settings = new CountingSettingsService(innerSettings);
            var autoStart = new CountingAutoStartService(initialValue: false)
            {
                FailWritesRemaining = 1,
                FailureMessage = privateSentinel
            };
            var logger = new FileLoggerService(
                settings,
                Path.Combine(testDirectory, "registry-diagnostic.log"));
            var coordinator = new SettingsPersistenceCoordinator(
                settings,
                autoStart,
                initialAutoStart: false);
            settings.Current.AutoStart = true;

            SettingsPersistenceException? persistenceException = null;
            try
            {
                coordinator.Save(settings.Current);
            }
            catch (SettingsPersistenceException exception)
            {
                persistenceException = exception;
                logger.LogError(exception.SafeLogMessage, exception);
            }

            if (persistenceException is not
                {
                    Stage: SettingsPersistenceStage.AutoStartRegistry,
                    DiagnosticCode: "LF-ST-002"
                })
            {
                Console.Error.WriteLine("Autostart failure did not produce LF-ST-002.");
                return 86;
            }
            if (!coordinator.HasPendingChanges ||
                settings.SaveCount != 1 ||
                autoStart.WriteCount != 1 ||
                autoStart.IsAutoStartEnabled)
            {
                Console.Error.WriteLine("Autostart failure did not preserve the expected pending state.");
                return 87;
            }

            var durableSettings = new SettingsService(settingsPath).Current;
            if (!durableSettings.AutoStart)
            {
                Console.Error.WriteLine("Settings file was not durable before the registry retry.");
                return 88;
            }

            coordinator.RetryPending(settings.Current);
            if (coordinator.HasPendingChanges ||
                settings.SaveCount != 1 ||
                autoStart.WriteCount != 2 ||
                !autoStart.IsAutoStartEnabled)
            {
                Console.Error.WriteLine("Registry-only retry rewrote settings or did not converge.");
                return 89;
            }

            var activeLog = File.ReadAllText(Path.Combine(testDirectory, "registry-diagnostic.log"));
            var requiredFields = new[]
            {
                "DiagnosticCode: LF-ST-002",
                "Action: settings-save",
                "Stage: autostart-registry",
                "Outcome: failed",
                "ExceptionType: LayoutFix.Services.SettingsPersistenceException",
                "HResult:"
            };
            if (requiredFields.Any(field => !activeLog.Contains(field, StringComparison.Ordinal)) ||
                activeLog.Contains(privateSentinel, StringComparison.Ordinal) ||
                activeLog.Contains(testDirectory, StringComparison.OrdinalIgnoreCase) ||
                activeLog.Contains("settings.json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Registry diagnostic log failed stable-field or privacy checks.");
                return 90;
            }

            Console.WriteLine(
                "settings_registry_diagnostic=pass code=LF-ST-002 file_writes=1 registry_writes=2 privacy=pass retry=pass");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Settings registry diagnostic E2E failed: {exception.GetType().Name}.");
            return 91;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunSettingsMigrationLockTest()
    {
        const string privateSentinel = "PRIVATE_SETTINGS_MIGRATION_LOCK";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var settingsPath = Path.Combine(testDirectory, "settings.json");
            var oldSettings = new AppSettings
            {
                Version = 10,
                UiLanguage = "uk",
                AutoConversionBlacklistedProcesses = ["private-terminal.exe"],
                UserExceptions = ["private-project"]
            };
            var durableJson = JsonSerializer.Serialize(oldSettings);
            File.WriteAllText(settingsPath, durableJson);

            using (var readableButNotReplaceable = new FileStream(
                       settingsPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                var settings = new SettingsService(settingsPath);
                if (settings.Current.Version != AppSettings.CurrentVersion ||
                    settings.Current.UiLanguage != "uk" ||
                    !settings.Current.AutoConversionBlacklistedProcesses.Contains(
                        "private-terminal.exe",
                        StringComparer.OrdinalIgnoreCase) ||
                    !settings.Current.AutoConversionBlacklistedProcesses.Contains(
                        "WindowsTerminal.exe",
                        StringComparer.OrdinalIgnoreCase) ||
                    !settings.Current.AutoConversionBlacklistedProcesses.Contains(
                        "mstsc.exe",
                        StringComparer.OrdinalIgnoreCase) ||
                    !settings.Current.UserExceptions.Contains("private-project", StringComparer.Ordinal) ||
                    File.ReadAllText(settingsPath) != durableJson)
                {
                    Console.Error.WriteLine(
                        "Locked settings migration lost data, safety defaults, or durable JSON.");
                    return 92;
                }
            }

            if (File.ReadAllText(settingsPath) != durableJson ||
                Directory.GetFiles(testDirectory, "*.tmp").Length != 0)
            {
                Console.Error.WriteLine("Locked settings migration changed durable files.");
                return 93;
            }

            Console.WriteLine(
                "settings_migration_lock=pass startup=pass in_memory_migration=pass durable_json=unchanged temp_files=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Settings migration lock E2E failed: {exception.GetType().Name}.");
            return 94;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunSettingsRecoveryBarrierTest()
    {
        const string privateSentinel = "PRIVATE_SETTINGS_RECOVERY_BARRIER";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var settingsPath = Path.Combine(testDirectory, "settings.json");
            var backupPath = $"{settingsPath}.bak";
            var settings = new SettingsService(settingsPath);
            settings.Current.UiLanguage = "uk";
            settings.Current.UserExceptions.Add("recover-me");
            settings.Save(settings.Current);
            settings.Current.UiLanguage = "ru";
            settings.Save(settings.Current);
            File.WriteAllText(settingsPath, "{ corrupt-active");

            SettingsService unavailableRecovery;
            using (var lockedBackup = new FileStream(
                       backupPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.None))
            {
                unavailableRecovery = new SettingsService(settingsPath);
            }

            unavailableRecovery.Current.UiLanguage = "en";
            try
            {
                unavailableRecovery.Save(unavailableRecovery.Current);
                Console.Error.WriteLine("Recovery barrier allowed a default-profile overwrite.");
                return 95;
            }
            catch (IOException)
            {
            }

            var durableBackup = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(backupPath));
            if (File.Exists(settingsPath) ||
                durableBackup?.UiLanguage != "uk" ||
                !durableBackup.UserExceptions.Contains("recover-me", StringComparer.Ordinal))
            {
                Console.Error.WriteLine("Recovery barrier did not preserve the durable backup.");
                return 96;
            }

            var recovered = new SettingsService(settingsPath);
            if (recovered.Current.UiLanguage != "uk" ||
                !recovered.Current.UserExceptions.Contains("recover-me", StringComparer.Ordinal) ||
                Directory.GetFiles(testDirectory, "*.tmp").Length != 0)
            {
                Console.Error.WriteLine("Recovery reload did not restore the durable profile.");
                return 97;
            }

            Console.WriteLine(
                "settings_recovery_barrier=pass overwrite=blocked backup=preserved reload=recovered temp_files=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Settings recovery barrier E2E failed: {exception.GetType().Name}.");
            return 98;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunSettingsConcurrencyTestAsync()
    {
        const string privateSentinel = "PRIVATE_SETTINGS_CONCURRENCY";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var settingsPath = Path.Combine(testDirectory, "settings.json");
            var settings = new SettingsService(settingsPath);
            using var startGate = new ManualResetEventSlim(initialState: false);
            var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var saves = Enumerable.Range(0, 128).Select(index => Task.Run(() =>
            {
                var marker = $"save-{index:D3}";
                var candidate = new AppSettings
                {
                    UiLanguage = marker,
                    UserExceptions = Enumerable.Range(0, 32)
                        .Select(item => $"{marker}-item-{item:D2}")
                        .ToList()
                };
                startGate.Wait();
                try
                {
                    settings.Save(candidate);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            })).ToArray();

            startGate.Set();
            await Task.WhenAll(saves);

            var durable = new SettingsService(settingsPath).Current;
            if (!failures.IsEmpty ||
                settings.Current.UiLanguage != durable.UiLanguage ||
                !settings.Current.UserExceptions.SequenceEqual(durable.UserExceptions) ||
                Directory.GetFiles(testDirectory, "*.tmp").Length != 0)
            {
                Console.Error.WriteLine(
                    "Concurrent settings saves failed or published a non-durable winner.");
                return 99;
            }

            Console.WriteLine(
                "settings_concurrency=pass saves=128 failures=0 memory_disk_match=pass temp_files=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Settings concurrency E2E failed: {exception.GetType().Name}.");
            return 100;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunTranslationHistoryDurabilityTestAsync()
    {
        const string privateSentinel = "PRIVATE_TRANSLATION_HISTORY_DURABILITY";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");

        try
        {
            var historyPath = Path.Combine(testDirectory, "nested", "translation-history.json");
            var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
            settings.Current.TranslationHistoryEnabled = true;
            var history = new TranslationHistoryService(settings, historyPath);

            await history.ClearHistoryAsync();
            var submitted = new TranslationHistoryEntry
            {
                Timestamp = new DateTime(2026, 8, 13, 14, 0, 0, DateTimeKind.Utc),
                SourceText = "original source",
                TranslatedText = "исходный перевод",
                SourceLang = "en",
                TargetLang = "ru"
            };
            await history.AddEntryAsync(submitted);
            submitted.SourceText = privateSentinel;
            submitted.TranslatedText = privateSentinel;
            var returned = await history.GetHistoryAsync();
            returned[0].SourceText = privateSentinel;
            returned[0].TranslatedText = privateSentinel;

            var inMemory = await history.GetHistoryAsync();
            var durable = await new TranslationHistoryService(settings, historyPath).GetHistoryAsync();
            if (inMemory.Count != 1 || durable.Count != 1 ||
                inMemory[0].SourceText != "original source" ||
                inMemory[0].TranslatedText != "исходный перевод" ||
                durable[0].SourceText != inMemory[0].SourceText ||
                durable[0].TranslatedText != inMemory[0].TranslatedText ||
                File.ReadAllText(historyPath).Contains(privateSentinel, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Translation history snapshots aliased mutable caller data.");
                return 101;
            }

            var operations = Enumerable.Range(0, 120).Select(index => index % 17 == 0
                ? history.ClearHistoryAsync()
                : history.AddEntryAsync(new TranslationHistoryEntry
                {
                    Timestamp = new DateTime(2026, 8, 13, 14, 1, 0, DateTimeKind.Utc)
                        .AddSeconds(index),
                    SourceText = $"source-{index:D3}",
                    TranslatedText = $"translated-{index:D3}",
                    SourceLang = "en",
                    TargetLang = "uk"
                }));
            await Task.WhenAll(operations);

            inMemory = await history.GetHistoryAsync();
            durable = await new TranslationHistoryService(settings, historyPath).GetHistoryAsync();
            var Identity = (TranslationHistoryEntry entry) =>
                $"{entry.Timestamp:O}|{entry.SourceText}|{entry.TranslatedText}|{entry.SourceLang}|{entry.TargetLang}";
            if (inMemory.Count > 50 ||
                !inMemory.Select(Identity).SequenceEqual(durable.Select(Identity)) ||
                Directory.GetFiles(testDirectory, "*.tmp", SearchOption.AllDirectories).Length != 0)
            {
                Console.Error.WriteLine("Concurrent translation history memory/durable state diverged.");
                return 102;
            }

            Console.WriteLine(
                "translation_history_durability=pass snapshots=isolated fresh_clear=pass concurrent_ops=120 memory_disk_match=pass temp_files=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Translation history durability E2E failed: {exception.GetType().Name}.");
            return 103;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunLoggerWriteHost(
        string logPath,
        string markerPrefix,
        int count,
        string readyPath,
        string goPath)
    {
        try
        {
            if (count is < 1 or > 10_000)
                return 104;

            var settings = new SettingsService($"{readyPath}.settings.json");
            settings.Current.LoggingEnabled = true;
            using var logger = new FileLoggerService(settings, logPath);
            File.WriteAllText(readyPath, "ready");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!File.Exists(goPath))
            {
                if (DateTime.UtcNow >= deadline)
                    return 105;
                Thread.Sleep(10);
            }

            for (var index = 0; index < count; index++)
                logger.LogInfo($"{markerPrefix}-{index:D4}");
            return 0;
        }
        catch
        {
            return 106;
        }
    }

    private static int RunCompatibilityProbeTest()
    {
        const string privateSentinel = "PRIVATE_COMPATIBILITY_REPORT_SENTINEL";
        try
        {
            var settings = new AppSettings
            {
                BlacklistedProcesses = [privateSentinel],
                AutoConversionBlacklistedProcesses = [privateSentinel],
                UserExceptions = [privateSentinel],
                UserAutocorrect = new Dictionary<string, string>
                {
                    [privateSentinel] = privateSentinel
                },
                OfflineModelType = privateSentinel
            };
            var report = DiagnosticsReportBuilder.Build(settings, "1.0.12-e2e");
            if (report.Contains(privateSentinel, StringComparison.Ordinal))
                return 112;

            var values = report.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
            string Required(string name) => values.TryGetValue(name, out var value)
                ? value
                : throw new InvalidDataException($"Compatibility report field is missing: {name}.");

            var elevated = Required("Elevated");
            var integrity = Required("ProcessIntegrity");
            var targetInput = Required("ElevatedTargetInput");
            var remote = Required("RemoteSession");
            var scaleMode = Required("MonitorScaleMode");
            var scaleRange = Required("MonitorScaleRange");
            var awareness = Required("DpiAwareness");
            if (!int.TryParse(Required("MonitorCount"), out var monitors) || monitors < 1 ||
                !uint.TryParse(Required("SystemDpi"), out var systemDpi) || systemDpi is < 96 or > 480 ||
                elevated is not ("True" or "False") ||
                remote is not ("True" or "False") ||
                integrity is "unknown" or "protected" ||
                scaleMode is not ("uniform" or "mixed") ||
                awareness != "per-monitor-v2" ||
                (elevated == "True" && targetInput != "same-or-lower-integrity") ||
                (elevated == "False" && targetInput != "blocked-when-target-elevated"))
            {
                return 113;
            }

            var scaleParts = scaleRange.Split('-', 2);
            if (scaleParts.Length != 2 ||
                !int.TryParse(scaleParts[0], out var minimumScale) ||
                !int.TryParse(scaleParts[1], out var maximumScale) ||
                minimumScale is < 100 or > 500 ||
                maximumScale < minimumScale || maximumScale > 500)
            {
                return 114;
            }

            Console.WriteLine(
                $"compatibility_probe=pass elevated={elevated.ToLowerInvariant()} " +
                $"integrity={integrity} remote={remote.ToLowerInvariant()} monitors={monitors} " +
                $"system_dpi={systemDpi} scale_mode={scaleMode} scale_range={scaleRange} " +
                $"dpi_awareness={awareness} privacy=pass");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Compatibility probe E2E failed: {exception.GetType().Name}.");
            return 115;
        }
    }

    private static int RunDictionaryPerformanceTest()
    {
        const int warmIterations = 10_000;
        const int concurrentWorkers = 64;
        const int concurrentIterationsPerWorker = 100;
        const long maximumWarmUpMilliseconds = 3_000;
        const double maximumReadyFirstMilliseconds = 250;
        const long maximumWarmMilliseconds = 3_000;
        const long maximumConcurrentMilliseconds = 3_000;
        try
        {
            var dictionaryDirectory = Path.Combine(AppContext.BaseDirectory, "Dictionaries");
            var converter = new LayoutConverter();
            var layouts = new DictionaryPerformanceLayoutManager();
            var settings = new DictionaryPerformanceSettingsService();

            var analyzer = DictionaryAnalyzer.CreateForDirectory(
                converter,
                layouts,
                settings,
                dictionaryDirectory);
            var stopwatch = Stopwatch.StartNew();
            analyzer.WarmUp();
            stopwatch.Stop();
            var warmUpMilliseconds = stopwatch.ElapsedMilliseconds;

            stopwatch.Restart();
            if (!HasExpectedProductionCorrection(analyzer))
                return 116;
            stopwatch.Stop();
            var readyFirstMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

            stopwatch.Restart();
            for (var iteration = 0; iteration < warmIterations; iteration++)
            {
                if (!HasExpectedProductionCorrection(analyzer))
                    return 117;
            }
            stopwatch.Stop();
            var warmMilliseconds = stopwatch.ElapsedMilliseconds;

            var concurrentAnalyzer = DictionaryAnalyzer.CreateForDirectory(
                converter,
                layouts,
                settings,
                dictionaryDirectory);
            stopwatch.Restart();
            var concurrentResults = Task.WhenAll(
                    Enumerable.Range(0, concurrentWorkers).Select(_ => Task.Run(() =>
                    {
                        for (var iteration = 0;
                             iteration < concurrentIterationsPerWorker;
                             iteration++)
                        {
                            if (!HasExpectedProductionCorrection(concurrentAnalyzer))
                                return false;
                        }
                        return true;
                    })))
                .GetAwaiter()
                .GetResult();
            stopwatch.Stop();
            var concurrentMilliseconds = stopwatch.ElapsedMilliseconds;

            if (concurrentResults.Any(result => !result) ||
                warmUpMilliseconds > maximumWarmUpMilliseconds ||
                readyFirstMilliseconds > maximumReadyFirstMilliseconds ||
                warmMilliseconds > maximumWarmMilliseconds ||
                concurrentMilliseconds > maximumConcurrentMilliseconds)
            {
                var readyFirst = readyFirstMilliseconds.ToString(
                    "F3",
                    CultureInfo.InvariantCulture);
                Console.Error.WriteLine(
                    $"dictionary_performance=fail warmup_ms={warmUpMilliseconds} " +
                    $"ready_first_ms={readyFirst} " +
                    $"warm_ms={warmMilliseconds} concurrent_ms={concurrentMilliseconds}");
                return 118;
            }

            var readyFirstResult = readyFirstMilliseconds.ToString(
                "F3",
                CultureInfo.InvariantCulture);
            Console.WriteLine(
                $"dictionary_performance=pass warmup_ms={warmUpMilliseconds} " +
                $"ready_first_ms={readyFirstResult} " +
                $"warm_ms={warmMilliseconds} warm_requests={warmIterations} " +
                $"concurrent_ms={concurrentMilliseconds} " +
                $"concurrent_requests={concurrentWorkers * concurrentIterationsPerWorker} " +
                "languages=en-ru-uk result=stable");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Dictionary performance E2E failed: {exception.GetType().Name}.");
            return 119;
        }
    }

    private static bool HasExpectedProductionCorrection(DictionaryAnalyzer analyzer) =>
        analyzer.TryGetCorrection("ytn", "en-US", out var suggestion) &&
        suggestion.Replacement == "нет" &&
        suggestion.TargetLayoutCode == "ru-RU" &&
        suggestion.IsConfidentForAutomaticCorrection;

    private static async Task<int> RunStartupLifecycleTestWithRetriesAsync(
        string applicationPath,
        bool expectHookFailureRecovery,
        bool expectSessionRecovery)
    {
        const int clipboardSequenceChanged = 123;
        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var result = await RunStartupLifecycleTestAsync(
                applicationPath,
                expectHookFailureRecovery,
                expectSessionRecovery);
            if (result != clipboardSequenceChanged || attempt >= maximumAttempts)
                return result;

            Console.Error.WriteLine(
                $"Clipboard sequence changed concurrently; retrying lifecycle ({attempt}/{maximumAttempts}).");
            await Task.Delay(250);
        }
    }

    private static async Task<int> RunStartupLifecycleTestAsync(
        string applicationPath,
        bool expectHookFailureRecovery,
        bool expectSessionRecovery)
    {
        const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "LayoutFix";
        if (Process.GetProcessesByName("LayoutFix").Length != 0)
        {
            Console.Error.WriteLine(
                "Startup lifecycle E2E refuses to interfere with an existing LayoutFix session.");
            return 120;
        }

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.StartupE2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var statePath = Path.Combine(testDirectory, "startup.state");
        var loggerSettings = new DictionaryPerformanceSettingsService();
        using var logger = new FileLoggerService(
            loggerSettings,
            Path.Combine(testDirectory, "clipboard.log"));
        using var clipboard = new ClipboardService(logger);
        Process? application = null;
        var stage = "initialize";

        try
        {
            stage = "capture-profile";
            var profileBefore = CaptureUserProfileState();
            stage = "capture-registry";
            var registryBefore = CaptureRegistryState(registryPath, valueName);
            stage = "capture-clipboard-sequence";
            var clipboardSequence = clipboard.GetSequenceNumber();

            stage = "start-application";
            var startInfo = new ProcessStartInfo(applicationPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(
                expectHookFailureRecovery
                    ? "--startup-recovery-test"
                    : expectSessionRecovery
                        ? "--session-recovery-test"
                    : "--startup-lifecycle-test");
            startInfo.ArgumentList.Add(statePath);
            application = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start production application.");
            stage = "wait-application";
            await application.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));

            stage = "read-state";
            if (application.ExitCode != 0 || !File.Exists(statePath))
                return 121;
            var state = File.ReadAllText(statePath);
            var requiredState = new[]
            {
                "startup_lifecycle=pass",
                "tray=ready",
                "message_loop=active",
                "hooks=operational",
                "dictionaries=warmed",
                "profile=isolated",
                "autostart=untouched",
                "diagnostics=verified"
            };
            if (requiredState.Any(token =>
                    !state.Split(' ').Contains(token, StringComparer.Ordinal)))
            {
                Console.Error.WriteLine("Startup lifecycle state was incomplete.");
                return 122;
            }
            var attemptsToken = state.Split(' ')
                .SingleOrDefault(token => token.StartsWith("attempts=", StringComparison.Ordinal));
            var attemptsValid = attemptsToken != null &&
                int.TryParse(attemptsToken.AsSpan("attempts=".Length), out var attempts) &&
                attempts == (expectHookFailureRecovery ? 6 : expectSessionRecovery ? 2 : 1);
            var failuresToken = state.Split(' ')
                .SingleOrDefault(token => token.StartsWith("failures=", StringComparison.Ordinal));
            var failuresValid = failuresToken != null &&
                int.TryParse(failuresToken.AsSpan("failures=".Length), out var failures) &&
                failures == (expectHookFailureRecovery ? 5 : 0);
            var suppressedToken = state.Split(' ')
                .SingleOrDefault(token => token.StartsWith("suppressed=", StringComparison.Ordinal));
            var suppressedValid = suppressedToken != null &&
                int.TryParse(suppressedToken.AsSpan("suppressed=".Length), out var suppressed) &&
                suppressed == (expectHookFailureRecovery ? 1 : 0);
            var degradationToken = state.Split(' ')
                .SingleOrDefault(token => token.StartsWith("degradation_ms=", StringComparison.Ordinal));
            var degradationValid = degradationToken != null &&
                long.TryParse(
                    degradationToken.AsSpan("degradation_ms=".Length),
                    out var degradationMilliseconds) &&
                (expectHookFailureRecovery
                    ? degradationMilliseconds >= 20_000
                    : degradationMilliseconds == 0);
            var requestsToken = state.Split(' ')
                .SingleOrDefault(token => token.StartsWith("requests=", StringComparison.Ordinal));
            var requestsValid = requestsToken != null &&
                int.TryParse(requestsToken.AsSpan("requests=".Length), out var requests) &&
                requests == (expectSessionRecovery ? 5 : 1);
            var recoveryEvidenceValid = expectHookFailureRecovery
                ? state.Contains("recovery=retry", StringComparison.Ordinal) &&
                  state.Contains("first_attempt=failed", StringComparison.Ordinal)
                : expectSessionRecovery
                    ? state.Contains("recovery=retry", StringComparison.Ordinal) &&
                      state.Contains("first_attempt=succeeded", StringComparison.Ordinal) &&
                      state.Contains("session=coalesced", StringComparison.Ordinal)
                    : state.Contains("recovery=initial", StringComparison.Ordinal) &&
                      state.Contains("first_attempt=succeeded", StringComparison.Ordinal) &&
                      state.Contains("session=none", StringComparison.Ordinal);
            if (!attemptsValid || !failuresValid || !suppressedValid ||
                !degradationValid || !requestsValid || !recoveryEvidenceValid)
            {
                Console.Error.WriteLine("Startup recovery evidence was incomplete.");
                return 122;
            }
            if (clipboard.GetSequenceNumber() != clipboardSequence)
            {
                Console.Error.WriteLine(
                    "Clipboard sequence changed during production startup lifecycle.");
                return 123;
            }
            stage = "verify-user-state";
            if (!profileBefore.SequenceEqual(CaptureUserProfileState()) ||
                !string.Equals(
                    registryBefore,
                    CaptureRegistryState(registryPath, valueName),
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Production startup changed user state.");
                return 124;
            }

            Console.WriteLine(
                "startup_lifecycle=pass tray=ready message_loop=active hooks=operational " +
                "dictionaries=warmed profile=isolated autostart=unchanged clipboard=unchanged " +
                $"recovery={(expectHookFailureRecovery ? "retry" : expectSessionRecovery ? "session" : "ready")}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Startup lifecycle E2E failed: {exception.GetType().Name}; stage={stage}.");
            return 125;
        }
        finally
        {
            if (application is { HasExited: false })
            {
                application.Kill(entireProcessTree: true);
                await application.WaitForExitAsync();
            }
            application?.Dispose();
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static SortedDictionary<string, string> CaptureUserProfileState()
    {
        var profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LayoutFix");
        var state = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in new[]
        {
            "settings.json",
            "settings.json.bak",
            "translation_history.json",
            Path.Combine("Logs", "layoutfix.log")
        })
        {
            var path = Path.Combine(profileDirectory, relativePath);
            if (!File.Exists(path))
            {
                state[relativePath] = "<missing>";
                continue;
            }

            using var stream = File.OpenRead(path);
            var hash = System.Security.Cryptography.SHA256.HashData(stream);
            state[relativePath] = $"{stream.Length}:{Convert.ToHexString(hash)}";
        }
        return state;
    }

    private static string CaptureRegistryState(string registryPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        if (key?.GetValueNames().Contains(valueName, StringComparer.Ordinal) != true)
            return "<missing>";

        var kind = key.GetValueKind(valueName);
        var value = key.GetValue(
            valueName,
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        var encoded = value switch
        {
            byte[] bytes => Convert.ToHexString(bytes),
            string[] strings => string.Join("\u0000", strings),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>"
        };
        return $"{kind}:{encoded}";
    }

    private static async Task<int> RunLoggerConcurrencyTestAsync()
    {
        const string privateSentinel = "PRIVATE_LOGGER_CONCURRENCY";
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.{privateSentinel}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);

        Process? first = null;
        Process? second = null;
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Current E2E executable path is unavailable.");
            var logPath = Path.Combine(testDirectory, "layoutfix.log");
            var backupPath = logPath + ".bak";
            var goPath = Path.Combine(testDirectory, "go");
            var firstReady = Path.Combine(testDirectory, "first.ready");
            var secondReady = Path.Combine(testDirectory, "second.ready");
            using (var stream = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                stream.SetLength(5L * 1024 * 1024 - 32);

            Process StartHost(string prefix, string readyPath)
            {
                var startInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.ArgumentList.Add("--logger-write-host");
                startInfo.ArgumentList.Add(logPath);
                startInfo.ArgumentList.Add(prefix);
                startInfo.ArgumentList.Add("1000");
                startInfo.ArgumentList.Add(readyPath);
                startInfo.ArgumentList.Add(goPath);
                return Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Logger write host could not start.");
            }

            first = StartHost("process-a-marker", firstReady);
            second = StartHost("process-b-marker", secondReady);
            var readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while ((!File.Exists(firstReady) || !File.Exists(secondReady)) &&
                   DateTime.UtcNow < readyDeadline &&
                   !first.HasExited &&
                   !second.HasExited)
            {
                await Task.Delay(20);
            }
            if (!File.Exists(firstReady) || !File.Exists(secondReady))
            {
                Console.Error.WriteLine("Logger write hosts did not reach the barrier.");
                return 107;
            }

            File.WriteAllText(goPath, "go");
            await Task.WhenAll(
                first.WaitForExitAsync(),
                second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(20));
            if (first.ExitCode != 0 || second.ExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"Logger write host failed: first={first.ExitCode} second={second.ExitCode}.");
                return 108;
            }

            var markers = File.ReadLines(logPath)
                .Where(line => line.Contains("process-", StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf("process-", StringComparison.Ordinal))..])
                .ToArray();
            if (markers.Length != 2_000 ||
                markers.Distinct(StringComparer.Ordinal).Count() != 2_000 ||
                !File.Exists(backupPath) ||
                new FileInfo(logPath).Length > 5L * 1024 * 1024 ||
                Directory.GetFiles(testDirectory, "*.tmp", SearchOption.AllDirectories).Length != 0)
            {
                Console.Error.WriteLine(
                    $"Cross-process logger durability failed: observed={markers.Length} " +
                    $"backup={File.Exists(backupPath)} active_bytes={new FileInfo(logPath).Length}.");
                return 109;
            }

            Console.WriteLine(
                "logger_concurrency=pass processes=2 entries=2000 lost=0 duplicates=0 " +
                "rotation=pass temp_files=0");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Logger concurrency E2E failed: {exception.GetType().Name}.");
            return 110;
        }
        finally
        {
            foreach (var process in new[] { first, second })
            {
                if (process is null)
                    continue;
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                process.Dispose();
            }
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunSettingsSnapshot(string outputPath, string tabName, string? culture)
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"LayoutFix.SettingsSnapshot.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var settingsPath = Path.Combine(testDirectory, "settings.json");
        var settings = new SettingsService(settingsPath);
        settings.Current.AppTheme = "Dark";
        settings.Current.BlacklistedProcesses = ["blocked-app.exe"];
        settings.Current.AutoConversionBlacklistedProcesses = ["auto-only-app.exe"];
        settings.Current.UserExceptions = ["codex", "openai"];
        settings.Current.UserAutocorrect = new Dictionary<string, string>
        {
            ["teh"] = "the",
            ["омг"] = "О мой Бог"
        };
        const string diagnosticsSentinel = "DO_NOT_EXPORT_TYPED_TEXT_OR_API_KEY";
        if (tabName.Equals("About", StringComparison.OrdinalIgnoreCase))
        {
            settings.Current.BlacklistedProcesses = [$@"C:\Users\Someone\{diagnosticsSentinel}.exe"];
            settings.Current.AutoConversionBlacklistedProcesses = [diagnosticsSentinel];
            settings.Current.UserExceptions = [diagnosticsSentinel];
            settings.Current.UserAutocorrect = new Dictionary<string, string>
            {
                [diagnosticsSentinel] = diagnosticsSentinel
            };
            settings.Current.OfflineModelType = diagnosticsSentinel;
        }
        settings.Save(settings.Current);

        var logger = new FileLoggerService(settings, Path.Combine(testDirectory, "snapshot.log"));
        using var modelDownloadService = new ModelDownloadService();
        var historyService = new TranslationHistoryService(
            settings,
            Path.Combine(testDirectory, "translation-history.json"));
        ILocalizationService localization = new PassthroughLocalizationService();
        if (!string.IsNullOrWhiteSpace(culture))
        {
            var localized = new LocalizationService();
            localized.SetCulture(culture);
            localization = localized;
        }
        using var form = new SettingsForm(
            settings,
            new NullAutoStartService(),
            localization,
            logger,
            modelDownloadService,
            historyService,
            new InMemoryTranslationCredentialStore());
        var result = 1;
        form.Shown += async (_, _) =>
        {
            try
            {
                var navigationName = tabName.ToUpperInvariant() switch
                {
                    "GENERAL" => localization.GetString("Settings_General", "Settings"),
                    "HOTKEYS" => localization.GetString("Settings_Hotkeys", "Hotkeys"),
                    "EXCEPTIONS" => localization.GetString("Settings_Exceptions", "Exceptions"),
                    "TRANSLATE" => localization.GetString("Settings_Translate", "Auto-Translate"),
                    "DICTIONARY" => localization.GetString("Settings_Dict", "Dictionary"),
                    "LANGUAGES" => localization.GetString("Settings_Languages", "Languages"),
                    "ABOUT" => localization.GetString("Settings_About", "About"),
                    _ => tabName
                };
                var tabButton = Descendants(form)
                    .OfType<Button>()
                    .FirstOrDefault(button => button.Text.Contains(
                        navigationName,
                        StringComparison.OrdinalIgnoreCase));
                if (tabButton == null)
                {
                    throw new InvalidOperationException(
                        $"Settings navigation button '{navigationName}' is missing.");
                }
                tabButton.PerformClick();
                await Task.Delay(150);

                var sidebarButtons = Descendants(form)
                    .OfType<Button>()
                    .Where(button => button.Visible && button.Parent != null &&
                        form.PointToClient(button.Parent.PointToScreen(Point.Empty)).X == 0 &&
                        button.Width == 220)
                    .OrderBy(button => button.PointToScreen(Point.Empty).Y)
                    .ToArray();
                if (sidebarButtons.Length != 7)
                    throw new InvalidOperationException("The settings sidebar does not contain seven visible tabs.");
                var sidebarBounds = sidebarButtons
                    .Select(button => form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle)))
                    .ToArray();
                if (sidebarBounds[0].Top < 40 || sidebarBounds.Any(bounds => bounds.Left < 0) ||
                    sidebarBounds.Zip(sidebarBounds.Skip(1), (first, second) => first.Bottom > second.Top).Any(overlap => overlap))
                {
                    throw new InvalidOperationException("Settings sidebar tabs overlap or extend beneath the top bar.");
                }

                var expectedTitle = tabName.ToUpperInvariant() switch
                {
                    "GENERAL" => localization.GetString("Settings_General", "App Settings"),
                    "HOTKEYS" => "Global Shortcuts",
                    "EXCEPTIONS" => localization.GetString("Settings_Exceptions", "App Exceptions"),
                    "TRANSLATE" => localization.GetString("Settings_Translate", "Auto-Translate"),
                    "DICTIONARY" => localization.GetString("Settings_Dict", "Dictionary"),
                    "LANGUAGES" => localization.GetString(
                        "Settings_Languages",
                        "Language & Keyboard Layouts"),
                    "ABOUT" => localization.GetString("Settings_About", "About LayoutFix"),
                    _ => null
                };
                if (expectedTitle != null)
                {
                    var title = Descendants(form)
                        .OfType<Label>()
                        .First(label => label.Text.Equals(expectedTitle, StringComparison.Ordinal));
                    var titlePosition = form.PointToClient(title.PointToScreen(Point.Empty));
                    if (titlePosition.X < 220 || titlePosition.Y < 50)
                    {
                        throw new InvalidOperationException(
                            $"{tabName} title overlaps the settings chrome at {titlePosition}.");
                    }
                }

                if (tabName.Equals("General", StringComparison.OrdinalIgnoreCase))
                {
                    var expectedToggleLabels = new[]
                    {
                        localization.GetString("Settings_AutoConv", "Enable automatic correction while typing"),
                        localization.GetString("Settings_AutoStart", "Start with Windows"),
                        localization.GetString("Settings_Sound", "Enable sound notifications"),
                        localization.GetString("Settings_Notifications", "Show diagnostic popup messages (LF-... codes)"),
                        localization.GetString("Settings_Flags", "Use country flags in tray"),
                        localization.GetString("Settings_Logging", "Diagnostic logs for testing (include application name)")
                    };
                    foreach (var expectedLabel in expectedToggleLabels)
                    {
                        var label = Descendants(form)
                            .OfType<Label>()
                            .SingleOrDefault(control => control.Visible && control.Text.Equals(
                                expectedLabel,
                                StringComparison.Ordinal));
                        var toggle = label?.Parent?.Controls.OfType<ToggleSwitch>().SingleOrDefault();
                        if (label == null || toggle == null)
                            throw new InvalidOperationException($"General setting '{expectedLabel}' is missing.");

                        var labelBounds = label.RectangleToScreen(label.ClientRectangle);
                        var toggleBounds = toggle.RectangleToScreen(toggle.ClientRectangle);
                        if (labelBounds.Right + 12 > toggleBounds.Left)
                        {
                            throw new InvalidOperationException(
                                $"General setting '{expectedLabel}' overlaps its toggle.");
                        }
                    }

                    var themeLabel = localization.GetString("Settings_Theme", "Color theme");
                    var themePanel = Descendants(form)
                        .OfType<Label>()
                        .Single(label => label.Visible && label.Text.Equals(themeLabel, StringComparison.Ordinal))
                        .Parent!;
                    var themeCombo = themePanel.Controls.OfType<ComboBox>().Single();
                    var expectedThemes = new[]
                    {
                        localization.GetString("Settings_ThemeAuto", "Follow Windows"),
                        localization.GetString("Settings_ThemeLight", "Light"),
                        localization.GetString("Settings_ThemeDark", "Dark")
                    };
                    if (!themeCombo.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(expectedThemes))
                        throw new InvalidOperationException("Localized theme choices are incomplete or out of order.");

                    var interfaceLanguage = localization.GetString(
                        "Settings_InterfaceLanguage",
                        "Interface language");
                    if (!Descendants(form).OfType<Label>().Any(label =>
                        label.Visible && label.Text.Equals(interfaceLanguage, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException("The localized interface language label is missing.");
                    }
                }

                if (tabName.Equals("Languages", StringComparison.OrdinalIgnoreCase))
                {
                    var expectedLayoutSource = localization.GetString(
                        "Settings_UseWindowsLayouts",
                        "Use installed Windows keyboard layouts");
                    var useWindowsLayouts = Descendants(form)
                        .OfType<Label>()
                        .FirstOrDefault(label => label.Visible && label.Text.Equals(
                            expectedLayoutSource,
                            StringComparison.Ordinal));
                    if (useWindowsLayouts == null)
                        throw new InvalidOperationException("Windows layout source toggle is missing.");

                    var toggle = useWindowsLayouts.Parent?.Controls
                        .OfType<ToggleSwitch>()
                        .SingleOrDefault();
                    if (toggle is not { Checked: true })
                        throw new InvalidOperationException("Windows layout source toggle has an invalid default.");

                    var firstLayoutCard = Descendants(form)
                        .OfType<CardPanel>()
                        .FirstOrDefault(card => card.Visible && card.Controls
                            .OfType<ToggleSwitch>()
                            .Any());
                    var firstLayoutToggle = firstLayoutCard?.Controls
                        .OfType<ToggleSwitch>()
                        .SingleOrDefault();
                    if (firstLayoutToggle == null)
                        throw new InvalidOperationException("Installed keyboard layout cards are missing.");

                    var visibleLayoutLabels = Descendants(firstLayoutCard!)
                        .OfType<Label>()
                        .Where(label => label.Visible)
                        .Select(label => label.Text)
                        .ToArray();
                    var keyboardPrefix = localization.GetString("Settings_KeyboardPrefix", "Keyboard:");
                    var activeLabel = localization.GetString("Settings_Active", "Active");
                    if (!visibleLayoutLabels.Any(text => text.StartsWith(keyboardPrefix + " ", StringComparison.Ordinal)) ||
                        !visibleLayoutLabels.Contains(activeLabel, StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Localized keyboard prefix or active-layout label is missing.");
                    }

                    firstLayoutToggle.Checked = false;
                    if (!settings.Current.DisabledLanguages.Any(value => value.Contains('@')))
                        throw new InvalidOperationException("Disabling a layout did not persist its exact HKL identity.");

                    firstLayoutToggle.Checked = true;
                    if (settings.Current.DisabledLanguages.Any(value => value.Contains('@')))
                        throw new InvalidOperationException("Re-enabling a layout did not clear its exact HKL identity.");
                }

                if (tabName.Equals("Exceptions", StringComparison.OrdinalIgnoreCase))
                {
                    var visibleLabels = Descendants(form)
                        .OfType<Label>()
                        .Where(label => label.Visible)
                        .Select(label => label.Text)
                        .ToArray();
                    var expectedGlobalTitle = localization.GetString(
                        "Settings_AllActionsExclusions",
                        "All LayoutFix actions");
                    var expectedAutoTitle = localization.GetString(
                        "Settings_AutoCorrectionExclusions",
                        "Automatic correction only");
                    if (!visibleLabels.Contains(expectedGlobalTitle, StringComparer.Ordinal) ||
                        !visibleLabels.Contains(expectedAutoTitle, StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Global and automatic-only process exclusion sections are not both visible.");
                    }

                    var controlsByAccessibleName = Descendants(form)
                        .Where(control => !string.IsNullOrWhiteSpace(control.AccessibleName))
                        .ToDictionary(control => control.AccessibleName!, StringComparer.Ordinal);
                    var globalInput = (TextBox)controlsByAccessibleName["GlobalProcessExclusions.Input"];
                    var globalAdd = (Button)controlsByAccessibleName["GlobalProcessExclusions.Add"];
                    var autoInput = (TextBox)controlsByAccessibleName["AutoCorrectionProcessExclusions.Input"];
                    var autoAdd = (Button)controlsByAccessibleName["AutoCorrectionProcessExclusions.Add"];
                    var autoList = (ListBox)controlsByAccessibleName["AutoCorrectionProcessExclusions.List"];
                    var autoRemove = (Button)controlsByAccessibleName["AutoCorrectionProcessExclusions.Remove"];
                    var restoreDefaults = (Button)controlsByAccessibleName["AutoCorrectionProcessExclusions.RestoreDefaults"];

                    globalInput.Text = "manual-and-auto.exe";
                    globalAdd.PerformClick();
                    if (!settings.Current.BlacklistedProcesses.Contains("manual-and-auto.exe") ||
                        settings.Current.AutoConversionBlacklistedProcesses.Contains("manual-and-auto.exe"))
                    {
                        throw new InvalidOperationException(
                            "Global process exclusion did not remain isolated from automatic-only exclusions.");
                    }

                    autoInput.Text = "custom-editor.exe";
                    autoAdd.PerformClick();
                    if (!settings.Current.AutoConversionBlacklistedProcesses.Contains("custom-editor.exe") ||
                        settings.Current.BlacklistedProcesses.Contains("custom-editor.exe"))
                    {
                        throw new InvalidOperationException(
                            "Automatic-only process exclusion did not remain isolated from global exclusions.");
                    }

                    autoList.SelectedItem = "custom-editor.exe";
                    autoRemove.PerformClick();
                    if (settings.Current.AutoConversionBlacklistedProcesses.Contains("custom-editor.exe"))
                        throw new InvalidOperationException("Automatic-only process exclusion could not be removed.");

                    restoreDefaults.PerformClick();
                    if (!settings.Current.AutoConversionBlacklistedProcesses.Contains("Code.exe") ||
                        !settings.Current.AutoConversionBlacklistedProcesses.Contains("Photoshop.exe"))
                    {
                        throw new InvalidOperationException(
                            "Restoring the automatic correction safety defaults did not restore IDE and Adobe exclusions.");
                    }

                    var persistedSettings = new SettingsService(settingsPath).Current;
                    if (!persistedSettings.BlacklistedProcesses.Contains("manual-and-auto.exe") ||
                        !persistedSettings.AutoConversionBlacklistedProcesses.Contains("Code.exe") ||
                        persistedSettings.AutoConversionBlacklistedProcesses.Contains("custom-editor.exe"))
                    {
                        throw new InvalidOperationException("Process exclusion edits were not persisted.");
                    }
                }

                if (tabName.Equals("About", StringComparison.OrdinalIgnoreCase))
                {
                    var visibleAboutText = Descendants(form)
                        .OfType<Label>()
                        .Where(label => label.Visible)
                        .Select(label => label.Text)
                        .ToArray();
                    var builtWith = localization.GetString("Settings_BuiltWith", "Built with .NET 8.");
                    var tagline = localization.GetString(
                        "Settings_AboutTagline",
                        "Automatic keyboard layout correction and translation.");
                    if (!visibleAboutText.Any(text => text.Contains(builtWith, StringComparison.Ordinal)) ||
                        !visibleAboutText.Any(text => text.Contains(tagline, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException("The localized About description is missing.");
                    }

                    var report = Descendants(form)
                        .OfType<TextBox>()
                        .SingleOrDefault(control => control.Visible && control.AccessibleName == "DiagnosticsReport.Preview");
                    var copyButton = Descendants(form)
                        .OfType<Button>()
                        .SingleOrDefault(control => control.Visible && control.AccessibleName == "DiagnosticsReport.Copy");
                    if (report == null || copyButton == null)
                        throw new InvalidOperationException("The safe diagnostics preview or copy action is missing.");
                    if (!report.ReadOnly || report.Text.Contains(diagnosticsSentinel, StringComparison.Ordinal))
                        throw new InvalidOperationException("The diagnostics preview exposed seeded user content.");

                    var requiredFields = new[]
                    {
                        "AppVersion=",
                        "SettingsSchema=",
                        "OSVersion=",
                        "ProcessArchitecture=",
                        "ProcessIntegrity=",
                        "ElevatedTargetInput=",
                        "RemoteSession=",
                        "MonitorCount=",
                        "SystemDpi=",
                        "MonitorScaleMode=",
                        "MonitorScaleRange=",
                        "DpiAwareness=",
                        "InstalledKeyboardLayouts=",
                        "GlobalProcessExclusions=1",
                        "AutoCorrectionProcessExclusions=1",
                        "UserWordExceptions=1",
                        "UserReplacements=1",
                        "OfflineModel=custom-or-unknown"
                    };
                    var missingFields = requiredFields
                        .Where(field => !report.Text.Contains(field, StringComparison.Ordinal))
                        .ToArray();
                    if (missingFields.Length > 0)
                    {
                        throw new InvalidOperationException(
                            $"The diagnostics preview is missing fields: {string.Join(", ", missingFields)}.");
                    }

                }


                if (tabName.Equals("Translate", StringComparison.OrdinalIgnoreCase))
                {
                    var visibleCombos = Descendants(form)
                        .OfType<ComboBox>()
                        .Where(combo => combo.Visible)
                        .OrderBy(combo => combo.PointToScreen(Point.Empty).Y)
                        .ToArray();
                    var selectedLanguages = visibleCombos
                        .Skip(1)
                        .Select(combo => (combo.SelectedItem as SettingsForm.LangItem)?.Code)
                        .ToArray();
                    if (!selectedLanguages.SequenceEqual(new[] { "en", "ru", "uk" }))
                    {
                        throw new InvalidOperationException(
                            $"Translation language selections are invalid: {string.Join(",", selectedLanguages)}.");
                    }

                    var capabilityLabel = Descendants(form)
                        .OfType<Label>()
                        .SingleOrDefault(label => label.Visible && label.Text.StartsWith(
                            "Validated offline targets:",
                            StringComparison.Ordinal));
                    if (capabilityLabel?.Text != "Validated offline targets: EN, RU, FR, ES.")
                        throw new InvalidOperationException("Light model capability summary is missing.");

                    var modelCombo = visibleCombos[0];
                    modelCombo.SelectedIndex = 2;
                    Application.DoEvents();
                    if (capabilityLabel.Text != "Validated offline targets: EN, RU, UK, FR, ES.")
                        throw new InvalidOperationException("Balanced model capability summary did not update.");

                    modelCombo.SelectedIndex = 1;
                    Application.DoEvents();
                    if (capabilityLabel.Text != "Validated offline targets: EN, RU, UK, DE, FR, ES.")
                        throw new InvalidOperationException("ALMA model capability summary did not update.");

                    modelCombo.SelectedIndex = 0;
                    Application.DoEvents();
                }

                var fullOutputPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var deviceContext = graphics.GetHdc();
                    try
                    {
                        if (!NativeFocus.PrintWindow(form.Handle, deviceContext, flags: 2))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "PrintWindow failed.");
                    }
                    finally
                    {
                        graphics.ReleaseHdc(deviceContext);
                    }
                }
                bitmap.Save(fullOutputPath, System.Drawing.Imaging.ImageFormat.Png);
                var unexpectedTopBarPixels = 0;
                for (var y = 2; y < Math.Min(38, bitmap.Height); y++)
                {
                    for (var x = 220; x < bitmap.Width - 60; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        if (pixel.A > 0 && pixel.R + pixel.G + pixel.B > 480)
                            unexpectedTopBarPixels++;
                    }
                }
                if (unexpectedTopBarPixels > 16)
                {
                    throw new InvalidOperationException(
                        $"{tabName} content overlaps the settings top bar " +
                        $"({unexpectedTopBarPixels} unexpected bright pixels).");
                }
                result = 0;
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.ChangeExtension(outputPath, ".error.txt"), exception.ToString());
                result = 2;
            }
            finally
            {
                form.Close();
            }
        };

        Application.Run(form);
        try { Directory.Delete(testDirectory, recursive: true); } catch { }
        return result;
    }

    private static async Task<int> RunWorkerIsolationTestAsync(string applicationPath)
    {
        var fullApplicationPath = Path.GetFullPath(applicationPath);
        var dependencyManifest = Path.ChangeExtension(fullApplicationPath, ".deps.json");
        if (!File.Exists(dependencyManifest) ||
            File.ReadAllText(dependencyManifest).Contains("LLamaSharp", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        var capabilityTestDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.OfflineCapability.{Guid.NewGuid():N}");
        Directory.CreateDirectory(capabilityTestDirectory);
        try
        {
            var capabilitySettings = new SettingsService(
                Path.Combine(capabilityTestDirectory, "settings.json"));
            capabilitySettings.Current.OfflineModelType = OfflineModelCatalog.Light.Id;
            capabilitySettings.Save(capabilitySettings.Current);
            var capabilityLogger = new FileLoggerService(
                capabilitySettings,
                Path.Combine(capabilityTestDirectory, "capability.log"));
            using var capabilityClient = new LayoutFix.Services.OfflineTranslationWorkerClient(
                capabilitySettings,
                capabilityLogger);
            try
            {
                await capabilityClient.TranslateAsync("Hello.", " DE ", "en");
                return 22;
            }
            catch (NotSupportedException exception) when (
                exception.Message.Contains("quality gate", StringComparison.Ordinal) &&
                exception.Message.Contains("online translation", StringComparison.Ordinal))
            {
            }
        }
        finally
        {
            try { Directory.Delete(capabilityTestDirectory, recursive: true); } catch { }
        }

        var workerAssembly = Path.Combine(
            Path.GetDirectoryName(fullApplicationPath)!,
            "translation-worker",
            "LayoutFix.TranslationWorker.dll");
        if (!File.Exists(workerAssembly)) return 21;

        var pipeName = $"LayoutFix-E2E-Worker-{Environment.ProcessId}-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var worker = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fullApplicationPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        worker.StartInfo.ArgumentList.Add("--translation-worker");
        worker.StartInfo.ArgumentList.Add(pipeName);
        worker.StartInfo.ArgumentList.Add("light");

        try
        {
            if (!worker.Start()) return 10;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(timeout.Token);
            var reader = new StreamReader(pipe, leaveOpen: true);
            var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            var request = JsonSerializer.Serialize(new
            {
                Text = new string('x', 3_001),
                TargetLanguage = "uk",
                SourceLanguage = "en"
            });
            await writer.WriteLineAsync(request);
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line == null) return 11;
            using var response = JsonDocument.Parse(line);
            if (!response.RootElement.TryGetProperty("Success", out var success) || success.GetBoolean())
                return 12;

            writer.Dispose();
            reader.Dispose();
            await pipe.DisposeAsync();
            if (!worker.WaitForExit(5_000)) return 13;
            return worker.ExitCode == 0 ? 0 : 14;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "worker-isolation-error.txt"),
                    $"WorkerExit={TryGetExitCode(worker)}{Environment.NewLine}{exception}");
            }
            catch
            {
            }
            return 15;
        }
        finally
        {
            try
            {
                if (!worker.HasExited)
                    worker.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    private static async Task<int> RunWorkerModelSwitchTestAsync(string applicationPath)
    {
        var light = OfflineModelCatalog.Light;
        var pro = OfflineModelCatalog.Pro;
        if (!OfflineModelCatalog.IsInstalled(OfflineModelLocator.GetModelPath(light.Id), light) ||
            !OfflineModelCatalog.IsInstalled(OfflineModelLocator.GetModelPath(pro.Id), pro))
        {
            Console.Error.WriteLine("worker-model-switch: required light/pro models are missing");
            return 90;
        }

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.WorkerModelSwitch.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        try
        {
            var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
            settings.Current.OfflineModelType = light.Id;
            settings.Save(settings.Current);
            var logger = new FileLoggerService(
                settings,
                Path.Combine(testDirectory, "worker-model-switch.log"));
            using var client = new LayoutFix.Services.OfflineTranslationWorkerClient(
                settings,
                logger,
                applicationPath);

            var lightResult = await client.TranslateAsync("Hello.", "ru", "en");
            var lightWorker = client.GetWorkerStateForTesting();

            settings.Current.OfflineModelType = pro.Id;
            settings.Save(settings.Current);
            var proResult = await client.TranslateAsync("Hello.", "uk", "en");
            var proWorker = client.GetWorkerStateForTesting();

            Console.WriteLine(
                $"worker-model-switch:light_pid={lightWorker.ProcessId};" +
                $"light_model={lightWorker.ModelId};pro_pid={proWorker.ProcessId};" +
                $"pro_model={proWorker.ModelId};light_length={lightResult.Length};" +
                $"pro_length={proResult.Length}");

            return lightWorker is { ProcessId: not null, ModelId: "light" } &&
                   proWorker is { ProcessId: not null, ModelId: "pro" } &&
                   lightWorker.ProcessId != proWorker.ProcessId &&
                   !string.IsNullOrWhiteSpace(lightResult) &&
                   !string.IsNullOrWhiteSpace(proResult)
                ? 0
                : 91;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunWorkerStartupTimeoutTestAsync(string applicationPath)
    {
        var light = OfflineModelCatalog.Light;
        if (!OfflineModelCatalog.IsInstalled(OfflineModelLocator.GetModelPath(light.Id), light))
            return 92;

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.WorkerStartupTimeout.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        try
        {
            var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
            settings.Current.OfflineModelType = light.Id;
            settings.Save(settings.Current);
            using var client = new LayoutFix.Services.OfflineTranslationWorkerClient(
                settings,
                new E2ENullLogger(),
                applicationPath);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await client.TranslateAsync("Hello.", "ru", "en");
                return 93;
            }
            catch (TimeoutException exception)
            {
                stopwatch.Stop();
                Console.WriteLine(
                    $"worker-startup-timeout:elapsed_ms={stopwatch.ElapsedMilliseconds};" +
                    $"message={exception.Message}");
                return exception.Message.Contains("did not connect within 10 seconds", StringComparison.Ordinal) &&
                       stopwatch.Elapsed >= TimeSpan.FromSeconds(9) &&
                       stopwatch.Elapsed < TimeSpan.FromSeconds(20)
                    ? 0
                    : 94;
            }
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunWorkerTranslationTestAsync(
        string applicationPath,
        string modelType) =>
        await RunWorkerTranslationCasesAsync(
            applicationPath,
            modelType,
            [TranslationQualityCases[0]]);

    private static async Task<int> RunWorkerTranslationCaseAsync(
        string applicationPath,
        string modelType,
        string caseId)
    {
        if (modelType is not ("light" or "alma" or "pro"))
            return 29;

        var qualityCase = TranslationQualityCases.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, caseId, StringComparison.Ordinal));
        if (qualityCase == null)
            return 38;
        if (GetExpectation(qualityCase, modelType) == TranslationCaseExpectation.Excluded)
            return 39;

        return await RunWorkerTranslationCasesAsync(
            applicationPath,
            modelType,
            [qualityCase]);
    }

    private static async Task<int> RunWorkerTranslationMatrixAsync(
        string applicationPath,
        string modelType)
    {
        var cases = TranslationQualityCases
            .Where(qualityCase =>
                GetExpectation(qualityCase, modelType) != TranslationCaseExpectation.Excluded)
            .ToArray();
        return await RunWorkerTranslationCasesAsync(
            applicationPath,
            modelType,
            cases);
    }

    private static async Task<int> RunWorkerTranslationCasesAsync(
        string applicationPath,
        string modelType,
        IReadOnlyList<TranslationQualityCase> cases)
    {
        if (modelType is not ("light" or "alma" or "pro"))
            return 29;

        var fullApplicationPath = Path.GetFullPath(applicationPath);
        var model = OfflineModelCatalog.Get(modelType);
        if (!OfflineModelCatalog.IsInstalled(OfflineModelLocator.GetModelPath(model.Id), model))
            return 30;

        var pipeName = $"LayoutFix-E2E-Translation-{Environment.ProcessId}-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var worker = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fullApplicationPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        worker.StartInfo.ArgumentList.Add("--translation-worker");
        worker.StartInfo.ArgumentList.Add(pipeName);
        worker.StartInfo.ArgumentList.Add(model.Id);

        try
        {
            if (!worker.Start()) return 31;
            using var timeout = new CancellationTokenSource(
                cases.Count == 1 ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(8));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var firstFailure = 0;
            foreach (var qualityCase in cases)
            {
                var request = JsonSerializer.Serialize(new
                {
                    Text = qualityCase.SourceText,
                    TargetLanguage = qualityCase.TargetLanguage,
                    SourceLanguage = qualityCase.SourceLanguage
                });
                await writer.WriteLineAsync(request.AsMemory(), timeout.Token);
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line == null) return 32;

                Console.WriteLine(
                    $"worker-translation-response:model={model.Id} case={qualityCase.Id} json={line}");
                using var response = JsonDocument.Parse(line);
                var expectation = GetExpectation(qualityCase, model.Id);
                if (!response.RootElement.TryGetProperty("Success", out var success) || !success.GetBoolean())
                {
                    if (expectation is TranslationCaseExpectation.SafeRejection or
                        TranslationCaseExpectation.ValidOrSafeRejection)
                    {
                        Console.WriteLine(
                            $"worker-translation:model={model.Id} case={qualityCase.Id} " +
                            "result=pass-safe-rejection");
                        continue;
                    }

                    firstFailure = firstFailure == 0 ? 33 : firstFailure;
                    Console.WriteLine(
                        $"worker-translation:model={model.Id} case={qualityCase.Id} result=fail-response");
                    continue;
                }
                if (!response.RootElement.TryGetProperty("Translation", out var translation))
                {
                    firstFailure = firstFailure == 0 ? 34 : firstFailure;
                    Console.WriteLine(
                        $"worker-translation:model={model.Id} case={qualityCase.Id} result=fail-missing");
                    continue;
                }

                var translationText = translation.GetString();
                Console.WriteLine(
                    $"worker-translation-output:model={model.Id} case={qualityCase.Id} " +
                    $"text={JsonSerializer.Serialize(translationText)}");
                if (expectation == TranslationCaseExpectation.SafeRejection)
                {
                    firstFailure = firstFailure == 0 ? 37 : firstFailure;
                    Console.WriteLine(
                        $"worker-translation:model={model.Id} case={qualityCase.Id} " +
                        "result=fail-unsafe-acceptance");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(translationText) ||
                    string.Equals(
                        translationText,
                        qualityCase.SourceText,
                        StringComparison.OrdinalIgnoreCase))
                {
                    firstFailure = firstFailure == 0 ? 34 : firstFailure;
                    Console.WriteLine(
                        $"worker-translation:model={model.Id} case={qualityCase.Id} result=fail-empty-or-echo");
                    continue;
                }

                if (!IsExpectedTranslation(qualityCase, translationText))
                {
                    firstFailure = firstFailure == 0 ? 36 : firstFailure;
                    Console.WriteLine(
                        $"worker-translation:model={model.Id} case={qualityCase.Id} result=fail-semantic");
                    continue;
                }

                Console.WriteLine(
                    $"worker-translation:model={model.Id} case={qualityCase.Id} " +
                    $"chars={translationText.Length} result=pass");
            }

            Console.WriteLine(
                $"worker-translation-matrix:model={model.Id} cases={cases.Count} " +
                $"result={(firstFailure == 0 ? "pass" : "fail")}");
            return firstFailure;
        }
        catch
        {
            return 35;
        }
        finally
        {
            try
            {
                if (!worker.HasExited)
                    worker.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    private static TranslationCaseExpectation GetExpectation(
        TranslationQualityCase qualityCase,
        string modelType) => modelType switch
        {
            "light" => qualityCase.LightExpectation,
            "pro" => qualityCase.ProExpectation,
            "alma" => qualityCase.AlmaExpectation,
            _ => TranslationCaseExpectation.Excluded
        };

    private static bool IsExpectedTranslation(
        TranslationQualityCase qualityCase,
        string translation)
    {
        var requiredExactTokens = qualityCase.RequiredExactTokens ?? [];
        if (requiredExactTokens.Any(token =>
            !translation.Contains(token, StringComparison.Ordinal)))
        {
            return false;
        }

        var naturalLanguageText = translation;
        foreach (var token in requiredExactTokens)
            naturalLanguageText = naturalLanguageText.Replace(token, string.Empty, StringComparison.Ordinal);

        if (translation.Length > qualityCase.MaximumLength ||
            (qualityCase.CyrillicOnly && naturalLanguageText.Any(character =>
                character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')))
        {
            return false;
        }

        var normalized = RemoveDiacritics(translation).ToLowerInvariant();
        return qualityCase.RequiredTermGroups.All(group =>
            group.Any(term => normalized.Contains(
                RemoveDiacritics(term).ToLowerInvariant(),
                StringComparison.Ordinal)));
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(character);
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly TranslationQualityCase[] TranslationQualityCases =
    [
        new(
            "en-ru-hello",
            "Hello world.",
            "en",
            "ru",
            80,
            true,
            [["привет", "здравств"], ["мир"]]),
        new(
            "ru-en-train",
            "Поезд прибывает завтра утром.",
            "ru",
            "en",
            120,
            false,
            [["train"], ["tomorrow"], ["morning"]]),
        new(
            "en-uk-help",
            "Thank you for your help.",
            "en",
            "uk",
            120,
            true,
            [["дяку"], ["допом"]],
            LightExpectation: TranslationCaseExpectation.SafeRejection),
        new(
            "en-de-door",
            "Please open the red door.",
            "en",
            "de",
            120,
            false,
            [["tur"], ["rot"]],
            LightExpectation: TranslationCaseExpectation.SafeRejection,
            ProExpectation: TranslationCaseExpectation.SafeRejection),
        new(
            "en-fr-door",
            "Please open the red door.",
            "en",
            "fr",
            120,
            false,
            [["porte"], ["rouge"]]),
        new(
            "en-es-door",
            "Please open the red door.",
            "en",
            "es",
            120,
            false,
            [["puerta"], ["roj"]]),
        new(
            "en-ru-technical",
            "Save report.pdf to C:\\Work, then press Ctrl+S and open https://example.com/help.",
            "en",
            "ru",
            260,
            true,
            [["сохран"], ["нажм"], ["отк"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.SafeRejection,
            RequiredExactTokens: ["report.pdf", "C:\\Work", "Ctrl+S", "https://example.com/help"]),
        new(
            "ru-en-technical",
            "Откройте config.json и нажмите Ctrl+Enter, затем проверьте https://example.com/status.",
            "ru",
            "en",
            260,
            false,
            [["open"], ["press"], ["check"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            RequiredExactTokens: ["config.json", "Ctrl+Enter", "https://example.com/status"]),
        new(
            "en-ru-multiline",
            "Open settings.\nRestart the application.",
            "en",
            "ru",
            180,
            true,
            [["настрой"], ["перезапуст"], ["прилож"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            ProExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.SafeRejection,
            RequiredExactTokens: ["\n"]),
        new(
            "en-ru-long",
            "Before installing the update, save the document, close the editor, and make sure the backup was created.",
            "en",
            "ru",
            360,
            true,
            [["обнов"], ["сохран"], ["документ"], ["закр"], ["редактор"], ["резерв", "коп"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            ProExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-ru-structured",
            "Update LayoutFix:\n- Open `settings.json`.\n- Enable user_name.\n- Press Ctrl+S.",
            "en",
            "ru",
            400,
            true,
            [["обнов"], ["откр"], ["включ", "установ"], ["нажм"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.SafeRejection,
            RequiredExactTokens: ["LayoutFix", "`settings.json`", "user_name", "Ctrl+S"]),
        new(
            "en-ru-fenced-code",
            "Update **LayoutFix**:\n1. Open settings.\n2. Run this command:\n```powershell\nlayoutfix.exe --safe_mode\n```",
            "en",
            "ru",
            500,
            true,
            [["обнов"], ["откр"], ["запуст", "выполн"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.SafeRejection,
            RequiredExactTokens:
            [
                "LayoutFix",
                "```powershell\nlayoutfix.exe --safe_mode\n```"
            ]),
        new(
            "en-ru-markdown-table-links",
            "| Feature | Guide |\n| :--- | ---: |\n| Offline translation | [Read more](/docs/translate#offline) |",
            "en",
            "ru",
            420,
            true,
            [["функц", "возможност"], ["руковод", "справ"], ["офлайн", "оффлайн", "автоном"], ["подроб", "читать"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.SafeRejection,
            RequiredExactTokens:
            [
                "| :--- | ---: |",
                "/docs/translate#offline"
            ]),
        new(
            "en-ru-markdown-complex-link-code-pipe",
            "| Feature | Guide |\n| --- | --- |\n| ``left | `right` `` | [Read \\[advanced\\] guide](./docs/setup_(advanced).md) |",
            "en",
            "ru",
            420,
            true,
            [["функц", "возможност", "свойств"], ["руковод", "справ"], ["расшир", "продвин"], ["чита"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.SafeRejection,
            RequiredExactTokens:
            [
                "| --- | --- |",
                "``left | `right` ``",
                "./docs/setup_(advanced).md"
            ]),
        new(
            "en-ru-proper-names",
            "Alice will meet Bob in London tomorrow.",
            "en",
            "ru",
            240,
            true,
            [["алис"], ["боб"], ["лондон"], ["завтра"], ["встрет"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            ProExpectation: TranslationCaseExpectation.SafeRejection),
        new(
            "en-ru-single-proper-name",
            "Alice will call tomorrow.",
            "en",
            "ru",
            180,
            true,
            [["алис"], ["позвон"], ["завтра"]],
            LightExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-ru-proper-names-ia",
            "Olivia will meet Lucas near Madrid on Friday.",
            "en",
            "ru",
            240,
            true,
            [["олив"], ["лукас"], ["мадрид"], ["пятниц"], ["встрет"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-ru-single-proper-name-th",
            "Theodore will call tomorrow.",
            "en",
            "ru",
            180,
            true,
            [["теодор"], ["позвон"], ["завтра"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            ProExpectation: TranslationCaseExpectation.SafeRejection,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-uk-single-proper-name-j",
            "Jennifer will call tomorrow.",
            "en",
            "uk",
            220,
            true,
            [["дженн"], ["зателефон", "подзвон"], ["завтра"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            ProExpectation: TranslationCaseExpectation.SafeRejection,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-ru-negation-quantity",
            "Do not close the window before the upload reaches 100 percent.",
            "en",
            "ru",
            260,
            true,
            [["не"], ["закр"], ["окн"], ["загруз"], ["100"], ["достиг", "достич"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-uk-negation-temporal",
            "Do not restart the computer until the update finishes.",
            "en",
            "uk",
            260,
            true,
            [["не"], ["перезап", "перезавантаж"], ["комп"], ["оновлен"], ["заверш"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            ProExpectation: TranslationCaseExpectation.ValidOrSafeRejection,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "ru-en-negation-count",
            "Не удаляйте 3 резервные копии, пока проверка не завершится.",
            "ru",
            "en",
            280,
            false,
            [["do not", "don't", "never"], ["delet", "remov"], ["3"], ["backup"], ["cop"], ["until"], ["complet", "finish"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "en-ru-after-sequence",
            "After the report is saved, close the editor.",
            "en",
            "ru",
            240,
            true,
            [["после"], ["отчет", "отчёт"], ["сохран"], ["закр"], ["редактор"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "ru-en-before-sequence",
            "Сохраните документ перед тем, как закрыть редактор.",
            "ru",
            "en",
            240,
            false,
            [["save"], ["document"], ["before"], ["clos"], ["editor"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "uk-en-conditional-negation-temporal",
            "Якщо перевірку завершено, не видаляйте архів до створення копії.",
            "uk",
            "en",
            300,
            false,
            [
                ["if"],
                ["check", "verif"],
                ["complet", "finish"],
                ["do not", "don't", "never"],
                ["delet", "remov"],
                ["archive"],
                ["until", "before"],
                ["cop", "backup"]
            ],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.Excluded),
        new(
            "de-en-weather",
            "Morgen wird das Wetter besser.",
            "de",
            "en",
            180,
            false,
            [["tomorrow"], ["weather"], ["better", "improv"]],
            LightExpectation: TranslationCaseExpectation.Excluded),
        new(
            "fr-en-meeting",
            "La réunion commence à neuf heures.",
            "fr",
            "en",
            180,
            false,
            [["meeting"], ["begin", "start"], ["nine", "9"]],
            LightExpectation: TranslationCaseExpectation.Excluded,
            AlmaExpectation: TranslationCaseExpectation.ValidOrSafeRejection),
        new(
            "es-en-document",
            "Guarda el documento antes de cerrar la ventana.",
            "es",
            "en",
            220,
            false,
            [["save"], ["document"], ["before"], ["clos"], ["window"]],
            LightExpectation: TranslationCaseExpectation.Excluded)
    ];

    private sealed record TranslationQualityCase(
        string Id,
        string SourceText,
        string SourceLanguage,
        string TargetLanguage,
        int MaximumLength,
        bool CyrillicOnly,
        string[][] RequiredTermGroups,
        TranslationCaseExpectation LightExpectation = TranslationCaseExpectation.Success,
        TranslationCaseExpectation ProExpectation = TranslationCaseExpectation.Success,
        TranslationCaseExpectation AlmaExpectation = TranslationCaseExpectation.Success,
        string[]? RequiredExactTokens = null);

    private enum TranslationCaseExpectation
    {
        Excluded,
        Success,
        SafeRejection,
        ValidOrSafeRejection
    }

    private static async Task<int> DownloadModelAsync(string modelType)
    {
        var model = OfflineModelCatalog.Get(modelType);
        var path = OfflineModelLocator.GetModelPath(model.Id);
        using var service = new ModelDownloadService();
        if (service.IsModelDownloaded(path, model))
            return 0;

        var lastReported = -1;
        try
        {
            await service.DownloadModelAsync(model, path, progress =>
            {
                var percentage = Math.Clamp((int)(progress * 100), 0, 100);
                if (percentage / 10 == lastReported / 10) return;
                lastReported = percentage;
                Console.WriteLine($"model-download:{percentage}%");
            });
            return service.IsModelDownloaded(path, model) ? 0 : 41;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"model-download-failed:{exception.GetType().Name}");
            return 42;
        }
    }

    private static int RunSelectionOwnershipTest()
    {
        const string duplicateText = "ghbdtn ghbdtn";
        var result = 91;
        using var form = new Form
        {
            Text = "LayoutFix Selection Ownership E2E",
            Width = 560,
            Height = 220,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        var editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18),
            Multiline = true,
            Text = duplicateText
        };
        form.Controls.Add(editor);

        var logger = new E2ENullLogger();
        var activeWindow = new ActiveWindowProvider();
        using var keyboardHook = new KeyboardHook(logger);
        using var mouseHook = new MouseHook(logger);
        using var clipboard = new ClipboardService(logger);
        using var guard = new WindowsTextTargetGuard(activeWindow, logger);
        var transaction = new TextTransactionService(
            new InputInjector(),
            clipboard,
            activeWindow,
            logger,
            guard,
            keyboardHook,
            mouseHook);
        var hookRecovery = new HookRecoveryCoordinator(
            keyboardHook,
            mouseHook,
            logger);
        IClipboardSnapshot? originalClipboard = null;

        form.Shown += async (_, _) =>
        {
            try
            {
                if (!TryClaimForeground(form.Handle))
                {
                    Console.Error.WriteLine(
                        "selection_ownership=fail stage=foreground-claim");
                    result = 96;
                    return;
                }
                editor.Focus();
                await Task.Delay(150);
                var focusedContext = activeWindow.CaptureActiveWindow();
                if (focusedContext.ForegroundWindow != form.Handle ||
                    focusedContext.FocusedWindow != editor.Handle)
                {
                    Console.Error.WriteLine(
                        $"selection_ownership=fail stage=exact-focus" +
                        $" foreground_match={focusedContext.ForegroundWindow == form.Handle}" +
                        $" control_match={focusedContext.FocusedWindow == editor.Handle}");
                    result = 97;
                    return;
                }
                originalClipboard = await clipboard.CaptureAsync();
                await SetClipboardSentinelAsync();
                keyboardHook.Start();
                mouseHook.Start();

                editor.Select(0, 6);
                var keyboardSelection = await transaction.CaptureAsync(
                    allowPreviousWordFallback: false);
                if (keyboardSelection == null)
                {
                    Console.Error.WriteLine(
                        $"selection_ownership=fail stage=keyboard-capture" +
                        $" keyboard_generation={keyboardHook.InputGeneration}" +
                        $" mouse_generation={mouseHook.InputGeneration}");
                    result = 92;
                    return;
                }

                SendExternalKeys(0x27); // VK_RIGHT: real hook input changes selection ownership.
                await Task.Delay(100);
                editor.Select(7, 6); // Same text in a different range of the same HWND.
                var keyboardReplaced = await transaction.ReplaceAsync(
                    keyboardSelection,
                    "привет");
                var keyboardSafe = !keyboardReplaced && editor.Text == duplicateText;

                editor.Text = duplicateText;
                editor.Select(0, 6);
                var mouseSelection = await transaction.CaptureAsync(
                    allowPreviousWordFallback: false);
                if (mouseSelection == null)
                {
                    Console.Error.WriteLine(
                        $"selection_ownership=fail stage=mouse-capture" +
                        $" keyboard_generation={keyboardHook.InputGeneration}" +
                        $" mouse_generation={mouseHook.InputGeneration}");
                    result = 93;
                    return;
                }

                NativeFocus.GetCursorPos(out var originalCursor);
                var clickPoint = editor.PointToScreen(new Point(
                    Math.Max(4, editor.ClientSize.Width / 2),
                    Math.Max(4, editor.ClientSize.Height / 2)));
                NativeFocus.SetCursorPos(clickPoint.X, clickPoint.Y);
                NativeFocus.MouseEvent(
                    NativeFocus.MouseEventLeftDown,
                    0,
                    0,
                    0,
                    UIntPtr.Zero);
                NativeFocus.MouseEvent(
                    NativeFocus.MouseEventLeftUp,
                    0,
                    0,
                    0,
                    UIntPtr.Zero);
                NativeFocus.SetCursorPos(originalCursor.X, originalCursor.Y);
                await Task.Delay(100);
                editor.Select(7, 6);
                var mouseReplaced = await transaction.ReplaceAsync(
                    mouseSelection,
                    "привет");
                var mouseSafe = !mouseReplaced && editor.Text == duplicateText;

                var keyboardGenerationBeforeRecovery = keyboardHook.InputGeneration;
                var mouseGenerationBeforeRecovery = mouseHook.InputGeneration;
                hookRecovery.RequestRecovery();
                var hooksRecovered = hookRecovery.RecoverIfRequested();
                var recoveryInvalidatedSelections =
                    keyboardHook.InputGeneration > keyboardGenerationBeforeRecovery &&
                    mouseHook.InputGeneration > mouseGenerationBeforeRecovery;

                var keyboardGenerationAfterRecovery = keyboardHook.InputGeneration;
                var mouseGenerationAfterRecovery = mouseHook.InputGeneration;
                SendExternalKeys(0x87); // VK_F24
                NativeFocus.MouseEvent(
                    NativeFocus.MouseEventXDown,
                    0,
                    0,
                    NativeFocus.XButton1,
                    UIntPtr.Zero);
                NativeFocus.MouseEvent(
                    NativeFocus.MouseEventXUp,
                    0,
                    0,
                    NativeFocus.XButton1,
                    UIntPtr.Zero);
                await Task.Delay(100);
                var recoveredHooksObserveInput =
                    keyboardHook.InputGeneration > keyboardGenerationAfterRecovery &&
                    mouseHook.InputGeneration > mouseGenerationAfterRecovery;
                var hookRecoverySafe = hooksRecovered &&
                    recoveryInvalidatedSelections &&
                    recoveredHooksObserveInput;

                var clipboardText = await clipboard.ReadTextAsync();
                var clipboardSafe = clipboardText == ClipboardSentinel;
                var passed = keyboardSafe && mouseSafe && hookRecoverySafe && clipboardSafe;
                Console.WriteLine(
                    $"selection_ownership={(passed ? "pass" : "fail")}" +
                    $" keyboard_safe={keyboardSafe}" +
                    $" mouse_safe={mouseSafe} duplicate_text=safe" +
                    $" hook_recovery={hookRecoverySafe}" +
                    $" clipboard_preserved={clipboardSafe}");
                result = passed ? 0 : 94;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"selection_ownership=fail type={exception.GetType().Name}");
                result = 95;
            }
            finally
            {
                mouseHook.Stop();
                keyboardHook.Stop();
                if (originalClipboard != null)
                {
                    try
                    {
                        await clipboard.RestoreAsync(
                            originalClipboard,
                            CancellationToken.None);
                    }
                    catch { }
                    originalClipboard.Dispose();
                }
                form.Close();
            }
        };

        Application.Run(form);
        return result;
    }

    private static int RunSecureTargetTest()
    {
        var result = 20;
        using var form = new Form
        {
            Text = "LayoutFix Secure Target E2E",
            Width = 500,
            Height = 280,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        var normal = new TextBox { Left = 20, Top = 25, Width = 430, Text = "ordinary text" };
        var password = new TextBox
        {
            Left = 20,
            Top = 80,
            Width = 430,
            Text = "secret",
            UseSystemPasswordChar = true
        };
        var nonText = new Button
        {
            Left = 20,
            Top = 190,
            Width = 180,
            Text = "Non-text control"
        };
        var readOnly = new TextBox
        {
            Left = 20,
            Top = 135,
            Width = 430,
            Text = "read only",
            ReadOnly = true
        };
        form.Controls.Add(normal);
        form.Controls.Add(password);
        form.Controls.Add(readOnly);
        form.Controls.Add(nonText);

        var activeWindow = new E2EFixedActiveWindowProvider();
        using var guard = new WindowsTextTargetGuard(activeWindow, new E2ENullLogger());
        form.Shown += async (_, _) =>
        {
            try
            {
                password.Focus();
                await Task.Delay(150);
                activeWindow.Current = new ActiveWindowContext(
                    form.Handle,
                    password.Handle,
                    (uint)Environment.ProcessId);
                var passwordAllowed = await guard.CanModifyAsync(activeWindow.Current);

                normal.Focus();
                await Task.Delay(150);
                activeWindow.Current = new ActiveWindowContext(
                    form.Handle,
                    normal.Handle,
                    (uint)Environment.ProcessId);
                var normalAllowed = await guard.CanModifyAsync(activeWindow.Current);

                readOnly.Focus();
                await Task.Delay(150);
                activeWindow.Current = new ActiveWindowContext(
                    form.Handle,
                    readOnly.Handle,
                    (uint)Environment.ProcessId);
                var readOnlyAllowed = await guard.CanModifyAsync(activeWindow.Current);

                nonText.Focus();
                await Task.Delay(150);
                activeWindow.Current = new ActiveWindowContext(
                    form.Handle,
                    nonText.Handle,
                    (uint)Environment.ProcessId);
                var nonTextAllowed = await guard.CanModifyAsync(activeWindow.Current);
                var unexpectedInput = new FailOnUseInputInjector();
                using var unexpectedClipboard = new FailOnUseClipboardService();
                var transaction = new TextTransactionService(
                    unexpectedInput,
                    unexpectedClipboard,
                    activeWindow,
                    new E2ENullLogger(),
                    guard);
                var nonTextSelection = await transaction.CaptureAsync(
                    allowPreviousWordFallback: true);
                var nonTextPipelineSafe = nonTextSelection == null &&
                    unexpectedInput.CallCount == 0 &&
                    unexpectedClipboard.CallCount == 0;
                result = passwordAllowed ? 23 :
                    !normalAllowed ? 24 :
                    nonTextAllowed ? 25 :
                    readOnlyAllowed ? 26 :
                    !nonTextPipelineSafe ? 27 :
                    0;
            }
            catch
            {
                result = 22;
            }
            finally
            {
                form.Close();
            }
        };

        Application.Run(form);
        return result;
    }

    private static async Task<int> RunNonEditableTargetTestAsync(
        IntPtr targetWindow,
        Action? beforeProcessHealthCheck = null)
    {
        var activeWindow = new ActiveWindowProvider();
        var context = default(ActiveWindowContext);
        var foregroundClaimed = false;
        var focusDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        do
        {
            foregroundClaimed |= TryClaimForeground(targetWindow);
            await Task.Delay(100);
            context = activeWindow.CaptureActiveWindow();
            if (context.IsValid && context.ForegroundWindow == targetWindow)
            {
                foregroundClaimed = true;
                break;
            }
        }
        while (DateTime.UtcNow < focusDeadline);

        if (!foregroundClaimed)
        {
            Console.Error.WriteLine(
                $"noneditable_target=fail stage=foreground-claim" +
                $" target=0x{targetWindow.ToInt64():X}" +
                $" foreground=0x{Win32.GetForegroundWindow().ToInt64():X}");
            return 81;
        }

        if (!context.IsValid || context.ForegroundWindow != targetWindow)
        {
            Console.Error.WriteLine(
                $"noneditable_target=fail stage=context-capture" +
                $" target=0x{targetWindow.ToInt64():X}" +
                $" foreground=0x{context.ForegroundWindow.ToInt64():X}" +
                $" valid={context.IsValid}");
            return 82;
        }

        using var guard = new WindowsTextTargetGuard(activeWindow, new E2ENullLogger());
        var stopwatch = Stopwatch.StartNew();
        var allowed = await guard.CanModifyAsync(context);
        var unexpectedInput = new FailOnUseInputInjector();
        using var unexpectedClipboard = new FailOnUseClipboardService();
        var transaction = new TextTransactionService(
            unexpectedInput,
            unexpectedClipboard,
            activeWindow,
            new E2ENullLogger(),
            guard);
        var selection = await transaction.CaptureAsync(allowPreviousWordFallback: true);
        stopwatch.Stop();
        var pipelineSafe = selection == null &&
            unexpectedInput.CallCount == 0 &&
            unexpectedClipboard.CallCount == 0;
        beforeProcessHealthCheck?.Invoke();
        var targetResponding = false;
        try
        {
            using var targetProcess = Process.GetProcessById(checked((int)context.ProcessId));
            targetProcess.Refresh();
            targetResponding = !targetProcess.HasExited && targetProcess.Responding;
        }
        catch (ArgumentException)
        {
            // The target can close after context capture. Report a deterministic gate
            // failure instead of crashing the E2E runner with an unhandled race.
        }
        catch (InvalidOperationException)
        {
            // Treat a process that becomes unavailable while refreshing as exited.
        }
        Console.WriteLine(
            $"noneditable_target=\"{(allowed ? "unexpectedly-allowed" : "rejected")}" +
            $" elapsed_ms={stopwatch.ElapsedMilliseconds} process_id={context.ProcessId}" +
            $" pipeline_safe={pipelineSafe} responding={targetResponding}");

        if (allowed)
            return 83;
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(1))
            return 84;
        if (!pipelineSafe)
            return 85;
        return targetResponding ? 0 : 86;
    }

    private static Task<int> RunNonEditablePipelineTestAsync()
    {
        return RunNonEditableHostTestAsync(exitBeforeHealthCheck: false);
    }

    private static Task<int> RunNonEditableExitRaceTestAsync()
    {
        return RunNonEditableHostTestAsync(exitBeforeHealthCheck: true);
    }

    private static async Task<int> RunNonEditableHostTestAsync(bool exitBeforeHealthCheck)
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.NonEditable{(exitBeforeHealthCheck ? "ExitRace" : string.Empty)}E2E." +
            $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var readyPath = Path.Combine(testDirectory, "host.ready");
        var statePath = Path.Combine(testDirectory, "host.state");
        Process? host = null;
        try
        {
            host = StartExternalEditHost("button", readyPath, statePath);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline &&
                   !host.HasExited &&
                   !File.Exists(readyPath))
            {
                await Task.Delay(50);
            }

            if (host.HasExited ||
                !File.Exists(readyPath) ||
                !long.TryParse(File.ReadAllText(readyPath), out var handleValue) ||
                handleValue == 0)
            {
                return 87;
            }

            Action? beforeProcessHealthCheck = null;
            if (exitBeforeHealthCheck)
            {
                beforeProcessHealthCheck = () =>
                {
                    host.Refresh();
                    if (host.HasExited)
                        throw new InvalidOperationException(
                            "The exit-race host ended before the controlled health-check boundary.");

                    host.Kill(entireProcessTree: true);
                    if (!host.WaitForExit(3_000))
                        throw new InvalidOperationException(
                            "The exit-race host did not terminate at the controlled boundary.");
                };
            }

            var result = await RunNonEditableTargetTestAsync(
                new IntPtr(handleValue),
                beforeProcessHealthCheck);
            if (!exitBeforeHealthCheck)
                return result;

            Console.WriteLine(
                $"noneditable_exit_race result={(result == 86 ? "pass" : "fail")}" +
                $" observed_code={result}");
            return result == 86 ? 0 : 88;
        }
        finally
        {
            if (host != null)
            {
                try
                {
                    if (!host.HasExited)
                    {
                        host.CloseMainWindow();
                        if (!host.WaitForExit(3_000))
                        {
                            host.Kill(entireProcessTree: true);
                            host.WaitForExit(3_000);
                        }
                    }
                }
                catch { }
                host.Dispose();
            }

            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunBlacklistedProcessHost(string resultPath)
    {
        try
        {
            var fullResultPath = Path.GetFullPath(resultPath);
            var temporaryRoot = Path.TrimEndingDirectorySeparator(
                                    Path.GetFullPath(Path.GetTempPath())) +
                                Path.DirectorySeparatorChar;
            if (!fullResultPath.StartsWith(
                    temporaryRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 88;
            }

            using var form = new Form
            {
                Text = "LayoutFix protected-process host",
                Width = 480,
                Height = 160,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true
            };
            var editor = new TextBox
            {
                Left = 20,
                Top = 40,
                Width = 420,
                Font = new Font("Segoe UI", 16)
            };
            form.Controls.Add(editor);
            var result = 89;
            form.Shown += async (_, _) =>
            {
                try
                {
                    editor.Focus();
                    File.WriteAllText(
                        fullResultPath + ".ready",
                        Process.GetCurrentProcess().ProcessName);
                    await Task.Delay(1_200);
                    File.WriteAllText(fullResultPath, editor.Text);
                    result = 0;
                }
                finally
                {
                    form.Close();
                }
            };

            Application.Run(form);
            return result;
        }
        catch
        {
            return 90;
        }
    }

    private static int RunAutoCorrectionTest(int soakIterations)
    {
        var originalInputLanguage = InputLanguage.CurrentInputLanguage;
        var englishInputLanguage = InputLanguage.InstalledInputLanguages
            .Cast<InputLanguage>()
            .FirstOrDefault(language => string.Equals(
                language.Culture.Name,
                "en-US",
                StringComparison.OrdinalIgnoreCase));
        if (englishInputLanguage == null)
            return 70;

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.AutoCorrectionE2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var blacklistedHostExecutable = Path.Combine(
            AppContext.BaseDirectory,
            "pwsh.exe");
        try { File.Delete(blacklistedHostExecutable); } catch { }
        var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
        settings.Current.AutoConversionEnabled = true;
        settings.Current.LoggingEnabled = true;
        settings.Current.BlacklistedProcesses = [];
        settings.Current.AutoConversionBlacklistedProcesses = [];
        settings.Current.LayoutOrder = ["en-US", "ru-RU"];
        settings.Current.UseWindowsLayoutList = false;
        settings.Current.UserAutocorrect = new Dictionary<string, string>
        {
            ["teh"] = "the"
        };
        settings.Save(settings.Current);

        var logger = new FileLoggerService(settings, Path.Combine(testDirectory, "auto-correction.log"));
        using var keyboardHook = new KeyboardHook(logger);
        using var mouseHook = new MouseHook(logger);
        var activeWindow = new ActiveWindowProvider();
        var windowsLayoutManager = new KeyboardLayoutManager(
            settings,
            new WindowsLayoutProvider());
        windowsLayoutManager.LoadAll();
        IKeyboardLayoutManager layoutManager = new AutoCorrectionE2ELayoutManager(
            windowsLayoutManager);
        var layoutConverter = new LayoutConverter();
        var dictionaryAnalyzer = new DictionaryAnalyzer(layoutConverter, layoutManager, settings);
        using var targetGuard = new WindowsTextTargetGuard(activeWindow, logger);
        var pausingTargetGuard = new PausingTextTargetGuard(targetGuard);
        var inputInjector = new FaultInjectingInputInjector(new InputInjector());
        using var service = new AutoConversionService(
            keyboardHook,
            mouseHook,
            settings,
            dictionaryAnalyzer,
            inputInjector,
            layoutManager,
            layoutConverter,
            logger,
            new NullSoundService(),
            activeWindow,
            pausingTargetGuard);
        var russianPhysicalKeyObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        keyboardHook.HotkeyPressed += (_, observation) =>
        {
            Console.WriteLine(
                $"auto-correction:hook:key={observation.Combo.Key};" +
                $"text={JsonSerializer.Serialize(observation.Text)};dead={observation.IsDeadKey};" +
                $"handled={observation.Handled}");
            if (observation.Combo.Key == "x" && observation.Text == "ч")
                russianPhysicalKeyObserved.TrySetResult();
        };
        using var form = new Form
        {
            Text = "LayoutFix Auto-correction E2E",
            Width = 520,
            Height = 230,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        var editor = new TextBox
        {
            Left = 20,
            Top = 45,
            Width = 460,
            Font = new Font("Segoe UI", 16)
        };
        var rollbackSafetyEditor = new TextBox
        {
            Left = 20,
            Top = 105,
            Width = 460,
            Font = new Font("Segoe UI", 16),
            Text = "SAFE"
        };
        form.Controls.Add(editor);
        form.Controls.Add(rollbackSafetyEditor);

        var result = 71;
        form.Shown += async (_, _) =>
        {
            try
            {
                InputLanguage.CurrentInputLanguage = englishInputLanguage;
                form.Activate();
                editor.Focus();
                await Task.Delay(200);
                if (!TryClaimForeground(form.Handle) || !editor.Focused)
                {
                    result = 72;
                    return;
                }

                SendExternalChord(0x10, (ushort)'T');
                SendExternalKeys((ushort)'E', (ushort)'H', 0xDE, 0x20);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline && editor.Text != "The' ")
                    await Task.Delay(25);

                if (editor.Text != "The' ")
                {
                    result = 73;
                    return;
                }

                settings.Current.AutoConversionBlacklistedProcesses =
                    ["LayoutFix.WindowsE2E.exe"];
                editor.Text = "BLACKLIST ";
                editor.SelectionStart = editor.TextLength;
                SendExternalKeys((ushort)'T', (ushort)'E', (ushort)'H', 0x20);
                await Task.Delay(300);
                if (editor.Text != "BLACKLIST teh ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:blacklist-text={JsonSerializer.Serialize(editor.Text)}");
                    result = 87;
                    return;
                }

                var protectedHostResult = Path.Combine(
                    testDirectory,
                    "protected-process-result.txt");
                var protectedHostReady = protectedHostResult + ".ready";
                File.Copy(
                    Environment.ProcessPath ?? throw new InvalidOperationException(
                        "Current E2E apphost path is unavailable."),
                    blacklistedHostExecutable,
                    overwrite: false);
                settings.Current.AutoConversionBlacklistedProcesses =
                    new List<string>(AppSettings.DefaultAutoConversionBlacklistedProcesses);
                form.TopMost = false;
                Process? protectedHost = null;
                try
                {
                    var startInfo = new ProcessStartInfo(blacklistedHostExecutable)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = AppContext.BaseDirectory
                    };
                    startInfo.ArgumentList.Add("--blacklisted-process-host");
                    startInfo.ArgumentList.Add(protectedHostResult);
                    protectedHost = Process.Start(startInfo);
                    if (protectedHost == null)
                    {
                        result = 88;
                        return;
                    }

                    deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                    while (DateTime.UtcNow < deadline &&
                           !File.Exists(protectedHostReady) &&
                           !protectedHost.HasExited)
                    {
                        await Task.Delay(25);
                    }

                    var resolvedProcess = activeWindow.GetActiveProcessName();
                    var hostedProcess = File.Exists(protectedHostReady)
                        ? File.ReadAllText(protectedHostReady)
                        : string.Empty;
                    protectedHost.Refresh();
                    var protectedWindow = protectedHost.MainWindowHandle;
                    if (protectedWindow == IntPtr.Zero ||
                        !TryClaimForeground(protectedWindow))
                    {
                        Console.Error.WriteLine(
                            $"auto-correction:protected-host-window={protectedWindow};" +
                            $"exit={(protectedHost.HasExited ? protectedHost.ExitCode : -1)}");
                        result = 89;
                        return;
                    }
                    await Task.Delay(100);
                    resolvedProcess = activeWindow.GetActiveProcessName();
                    if (!string.Equals(hostedProcess, "pwsh", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(resolvedProcess, "pwsh", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine(
                            $"auto-correction:protected-host-process=" +
                            $"{JsonSerializer.Serialize(hostedProcess)};" +
                            $"resolved={JsonSerializer.Serialize(resolvedProcess)}");
                        result = 89;
                        return;
                    }

                    var protectedHostThread = Win32.GetWindowThreadProcessId(
                        protectedWindow,
                        out _);
                    if (protectedHostThread == 0 ||
                        !activeWindow.TrySwitchToLayout("en-US"))
                    {
                        result = 99;
                        return;
                    }
                    deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                    while (DateTime.UtcNow < deadline &&
                           Win32.GetKeyboardLayout(protectedHostThread) !=
                           englishInputLanguage.Handle)
                    {
                        await Task.Delay(10);
                    }
                    if (Win32.GetKeyboardLayout(protectedHostThread) !=
                        englishInputLanguage.Handle)
                    {
                        Console.Error.WriteLine(
                            "auto-correction:protected-host-layout=unavailable");
                        result = 99;
                        return;
                    }

                    SendExternalKeys((ushort)'T', (ushort)'E', (ushort)'H', 0x20);
                    await protectedHost.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
                    var protectedText = File.Exists(protectedHostResult)
                        ? File.ReadAllText(protectedHostResult)
                        : string.Empty;
                    if (protectedHost.ExitCode != 0 || protectedText != "teh ")
                    {
                        Console.Error.WriteLine(
                            $"auto-correction:protected-host-exit={protectedHost.ExitCode};" +
                            $"text={JsonSerializer.Serialize(protectedText)}");
                        result = 90;
                        return;
                    }
                    Console.WriteLine(
                        "auto-correction:protected-host=pwsh;injection=blocked");
                }
                finally
                {
                    if (protectedHost != null)
                    {
                        if (!protectedHost.HasExited)
                        {
                            protectedHost.Kill(entireProcessTree: true);
                            protectedHost.WaitForExit(3_000);
                        }
                        protectedHost.Dispose();
                    }
                    try { File.Delete(blacklistedHostExecutable); } catch { }
                    form.TopMost = true;
                }

                form.Activate();
                editor.Focus();
                await Task.Delay(150);
                if (!TryClaimForeground(form.Handle) || !editor.Focused)
                {
                    result = 91;
                    return;
                }
                settings.Current.AutoConversionBlacklistedProcesses = [];
                editor.Text = "The' ";
                editor.SelectionStart = editor.TextLength;

                SendExternalKeys((ushort)'T', (ushort)'L', (ushort)'S', 0x20);
                await Task.Delay(300);
                if (editor.Text != "The' tls ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:protected-token={JsonSerializer.Serialize(editor.Text)}");
                    result = 92;
                    return;
                }

                SendExternalKeys((ushort)'O', (ushort)'F', (ushort)'C', 0x20);
                await Task.Delay(300);
                if (editor.Text != "The' tls ofc ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:frequent-source-token=" +
                        JsonSerializer.Serialize(editor.Text));
                    result = 96;
                    return;
                }

                editor.Text = string.Empty;
                editor.SelectionStart = 0;
                SendExternalKeys((ushort)'G', (ushort)'T', (ushort)'K', 0x20);
                await Task.Delay(300);
                if (editor.Text != "gtk ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:expanded-technical-token=" +
                        JsonSerializer.Serialize(editor.Text));
                    result = 100;
                    return;
                }
                SendExternalKeys((ushort)'R', (ushort)'T', (ushort)'V', 0x20);
                await Task.Delay(300);
                if (editor.Text != "gtk rtv ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:long-tail-en-source=" +
                        JsonSerializer.Serialize(editor.Text));
                    result = 102;
                    return;
                }
                editor.Text = "The' tls ofc ";
                editor.SelectionStart = editor.TextLength;

                SendExternalKeys((ushort)'Y', (ushort)'T', 0x20);
                await Task.Delay(300);
                if (editor.Text != "The' tls ofc yt ")
                {
                    result = 75;
                    return;
                }

                SendExternalKeys((ushort)'Y', (ushort)'K', (ushort)'J', 0x20);
                await Task.Delay(300);
                if (editor.Text != "The' tls ofc yt ykj ")
                {
                    result = 76;
                    return;
                }

                SendExternalKeys((ushort)'Y', (ushort)'T', (ushort)'N');
                SendExternalChord(0x10, 0xBD); // Shift+OEM_MINUS -> underscore
                await Task.Delay(300);
                if (editor.Text != "The' tls ofc yt ykj ytn_")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:technical-token={JsonSerializer.Serialize(editor.Text)}");
                    result = 86;
                    return;
                }
                SendExternalKeys(0x20);

                SendExternalKeys((ushort)'N', (ushort)'F', (ushort)'R', 0x20);
                await Task.Delay(300);
                if (editor.Text != "The' tls ofc yt ykj ytn_ nfr ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:shared-word-ambiguity={JsonSerializer.Serialize(editor.Text)}");
                    result = 93;
                    return;
                }

                SendExternalKeys(
                    (ushort)'C',
                    (ushort)'S',
                    (ushort)'Y',
                    0x20);
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline && editor.Text != "The' tls ofc yt ykj ytn_ nfr сын ")
                    await Task.Delay(25);

                if (editor.Text != "The' tls ofc yt ykj ytn_ nfr сын ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:unambiguous-ru=" +
                        JsonSerializer.Serialize(editor.Text));
                    result = 77;
                    return;
                }

                SendExternalKeys((ushort)'V', (ushort)'B', (ushort)'H', 0x20);
                await Task.Delay(300);
                if (editor.Text != "The' tls ofc yt ykj ytn_ nfr сын мир ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:post-switch-text={JsonSerializer.Serialize(editor.Text)}");
                    result = 81;
                    return;
                }

                editor.Text = string.Empty;
                editor.SelectionStart = 0;
                SendExternalKeys((ushort)'G', (ushort)'U', (ushort)'N', 0x20);
                await Task.Delay(300);
                if (editor.Text != "пгт " ||
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "ru-RU",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"auto-correction:frequent-cyrillic-source=" +
                        $"{JsonSerializer.Serialize(editor.Text)};" +
                        $"layout={InputLanguage.CurrentInputLanguage.Culture.Name}");
                    result = 97;
                    return;
                }

                SendExternalKeys((ushort)'C', (ushort)'E', (ushort)'O', 0x20);
                await Task.Delay(300);
                if (editor.Text != "пгт сущ " ||
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "ru-RU",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"auto-correction:expanded-source-corpus=" +
                        $"{JsonSerializer.Serialize(editor.Text)};" +
                        $"layout={InputLanguage.CurrentInputLanguage.Culture.Name}");
                    result = 98;
                    return;
                }

                SendExternalKeys((ushort)'E', (ushort)'N', (ushort)'D', 0x20);
                await Task.Delay(300);
                if (editor.Text != "пгт сущ утв " ||
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "ru-RU",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"auto-correction:lower-frequency-source-corpus=" +
                        $"{JsonSerializer.Serialize(editor.Text)};" +
                        $"layout={InputLanguage.CurrentInputLanguage.Culture.Name}");
                    result = 101;
                    return;
                }

                SendExternalKeys((ushort)'D', (ushort)'A', (ushort)'D', 0x20);
                await Task.Delay(300);
                if (editor.Text != "пгт сущ утв вфв " ||
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "ru-RU",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"auto-correction:long-tail-ru-source=" +
                        $"{JsonSerializer.Serialize(editor.Text)};" +
                        $"layout={InputLanguage.CurrentInputLanguage.Culture.Name}");
                    result = 103;
                    return;
                }

                for (var iteration = 1; iteration <= soakIterations; iteration++)
                {
                    InputLanguage.CurrentInputLanguage = englishInputLanguage;
                    deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                    while (DateTime.UtcNow < deadline &&
                        !string.Equals(
                            InputLanguage.CurrentInputLanguage.Culture.Name,
                            "en-US",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(10);
                    }
                    if (!string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "en-US",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine(
                            $"auto-correction:soak-layout={iteration}/{soakIterations};" +
                            $"actual={InputLanguage.CurrentInputLanguage.Culture.Name}");
                        result = 94;
                        return;
                    }

                    editor.Text = string.Empty;
                    editor.SelectionStart = 0;
                    SendExternalKeys((ushort)'Y', (ushort)'T', (ushort)'N', 0x20);
                    deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                    while (DateTime.UtcNow < deadline &&
                        (editor.Text != "нет " ||
                         !string.Equals(
                             InputLanguage.CurrentInputLanguage.Culture.Name,
                             "ru-RU",
                             StringComparison.OrdinalIgnoreCase)))
                    {
                        await Task.Delay(10);
                    }
                    if (editor.Text != "нет " ||
                        !string.Equals(
                            InputLanguage.CurrentInputLanguage.Culture.Name,
                            "ru-RU",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine(
                            $"auto-correction:soak-text={iteration}/{soakIterations};" +
                            $"actual={JsonSerializer.Serialize(editor.Text)};" +
                            $"layout={InputLanguage.CurrentInputLanguage.Culture.Name}");
                        result = 95;
                        return;
                    }

                    if (iteration % 25 == 0 || iteration == soakIterations)
                    {
                        Console.WriteLine(
                            $"auto-correction:soak-progress={iteration}/{soakIterations}");
                    }
                }
                Console.WriteLine(
                    $"auto-correction:soak=pass;completed={soakIterations};" +
                    $"requested={soakIterations}");

                InputLanguage.CurrentInputLanguage = englishInputLanguage;
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                while (DateTime.UtcNow < deadline &&
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "en-US",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(10);
                }
                if (!string.Equals(
                    InputLanguage.CurrentInputLanguage.Culture.Name,
                    "en-US",
                    StringComparison.OrdinalIgnoreCase))
                {
                    result = 80;
                    return;
                }

                editor.Text = "PREFIX ";
                editor.SelectionStart = editor.TextLength;
                SendExternalKeys((ushort)'T', (ushort)'E', (ushort)'H', 0x20);
                SendExternalKeyDown(0x11);
                try
                {
                    await Task.Delay(150);
                    if (editor.Text != "PREFIX teh ")
                    {
                        Console.Error.WriteLine(
                            $"auto-correction:modifier-held-text={JsonSerializer.Serialize(editor.Text)}");
                        result = 78;
                        return;
                    }
                }
                finally
                {
                    SendExternalKeyUp(0x11);
                }

                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline && editor.Text != "PREFIX the ")
                    await Task.Delay(25);

                if (editor.Text != "PREFIX the ")
                {
                    result = 79;
                    return;
                }

                editor.Text = string.Empty;
                editor.Focus();
                inputInjector.ArmPartialBackspaceFailure(acceptedPresses: 2);
                SendExternalKeys((ushort)'T', (ushort)'E', (ushort)'H', 0x20);
                await inputInjector.PartialBackspaceFailureObserved.Task.WaitAsync(
                    TimeSpan.FromSeconds(3));
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTime.UtcNow < deadline && editor.Text != "teh ")
                    await Task.Delay(25);
                if (editor.Text != "teh ")
                {
                    Console.Error.WriteLine(
                        $"auto-correction:partial-backspace-text={JsonSerializer.Serialize(editor.Text)}");
                    result = 85;
                    return;
                }

                editor.Text = string.Empty;
                rollbackSafetyEditor.Text = "SAFE";
                editor.Focus();
                inputInjector.ArmNextTextFailure(async () =>
                {
                    var focusCompleted = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    form.BeginInvoke(() =>
                    {
                        rollbackSafetyEditor.SelectionStart = 0;
                        rollbackSafetyEditor.SelectionLength = 0;
                        rollbackSafetyEditor.Focus();
                        focusCompleted.TrySetResult();
                    });
                    await focusCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                });
                SendExternalKeys((ushort)'T', (ushort)'E', (ushort)'H', 0x0D);
                await inputInjector.FailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Task.Delay(200);
                if (rollbackSafetyEditor.Text != "SAFE" || editor.Text.Length != 0)
                {
                    Console.Error.WriteLine(
                        $"auto-correction:unsafe-rollback-source={JsonSerializer.Serialize(editor.Text)};" +
                        $"target={JsonSerializer.Serialize(rollbackSafetyEditor.Text)}");
                    result = 83;
                    return;
                }

                editor.Text = string.Empty;
                editor.Focus();
                InputLanguage.CurrentInputLanguage = englishInputLanguage;
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                while (DateTime.UtcNow < deadline &&
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "en-US",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(10);
                }
                if (!string.Equals(
                    InputLanguage.CurrentInputLanguage.Culture.Name,
                    "en-US",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"auto-correction:race-precondition-layout=" +
                        InputLanguage.CurrentInputLanguage.Culture.Name);
                    result = 87;
                    return;
                }
                pausingTargetGuard.ArmAfterOnePass();
                SendExternalKeys((ushort)'Y', (ushort)'T', (ushort)'N', 0x20);
                await pausingTargetGuard.PauseStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (!string.Equals(
                    InputLanguage.CurrentInputLanguage.Culture.Name,
                    "ru-RU",
                    StringComparison.OrdinalIgnoreCase))
                {
                    result = 84;
                    return;
                }
                try
                {
                    SendExternalKeys((ushort)'X');
                    deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                    while (DateTime.UtcNow < deadline && editor.Text == "ytn ")
                        await Task.Delay(10);
                }
                finally
                {
                    pausingTargetGuard.Release();
                }
                await Task.Delay(300);

                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                while (DateTime.UtcNow < deadline &&
                    !string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "en-US",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(10);
                }

                var physicalInputPreserved = editor.Text is "ytn ч" or "ytn x";
                result = physicalInputPreserved &&
                    russianPhysicalKeyObserved.Task.IsCompleted &&
                    string.Equals(
                        InputLanguage.CurrentInputLanguage.Culture.Name,
                        "en-US",
                        StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 82;
                Console.WriteLine(
                    $"auto-correction:result={result};text={JsonSerializer.Serialize(editor.Text)};" +
                    $"ru-hook={russianPhysicalKeyObserved.Task.IsCompleted};" +
                    $"layout={InputLanguage.CurrentInputLanguage.Culture.Name};" +
                    $"foreground={Win32.GetForegroundWindow() == form.Handle};focused={editor.Focused}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"auto-correction:error={exception.GetType().Name}");
                result = 74;
            }
            finally
            {
                InputLanguage.CurrentInputLanguage = originalInputLanguage;
                form.Close();
            }
        };

        keyboardHook.Start();
        mouseHook.Start();
        try
        {
            Application.Run(form);
        }
        finally
        {
            mouseHook.Stop();
            keyboardHook.Stop();
            service.Dispose();
            InputLanguage.CurrentInputLanguage = originalInputLanguage;
            var diagnosticLog = Path.Combine(testDirectory, "auto-correction.log");
            if (result != 0 && File.Exists(diagnosticLog))
            {
                Console.Error.WriteLine("auto-correction:log:begin");
                Console.Error.WriteLine(File.ReadAllText(diagnosticLog));
                Console.Error.WriteLine("auto-correction:log:end");
            }
            try { File.Delete(blacklistedHostExecutable); } catch { }
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }

        return result;
    }

    private static int RunTranslatorBehaviorTest()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.TranslatorE2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
        settings.Current.OnlineTranslationEnabled = true;
        settings.Current.UseOfflineTranslation = false;
        settings.Current.TranslationHistoryEnabled = true;
        settings.Current.TranslateLang1 = "ru";
        settings.Save(settings.Current);

        var translation = new CountingTranslationService();
        var history = new RecordingTranslationHistoryService();
        var logger = new E2ENullLogger();
        using var form = new TranslatorForm(
            translation,
            new UnavailableOfflineTranslationService(),
            history,
            new PassthroughLocalizationService(),
            settings,
            logger);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var stopwatch = new Stopwatch();
        var stage = 0;
        var result = 50;

        form.Shown += (_, _) =>
        {
            var controls = Descendants(form).ToArray();
            var sourceLanguages = controls
                .OfType<ComboBox>()
                .OrderByDescending(comboBox => comboBox.Items.Count)
                .First();
            var copy = controls
                .OfType<Button>()
                .FirstOrDefault(button => button.Text == "Copy");
            var cancel = controls
                .OfType<Button>()
                .FirstOrDefault(button => button.Text == "Cancel");
            var historyTitle = controls
                .OfType<Label>()
                .FirstOrDefault(label => label.Text == "Translation history ▼");
            if (copy is null ||
                cancel is null ||
                historyTitle is null ||
                sourceLanguages.GetItemText(sourceLanguages.Items[0]) != "Detect language")
            {
                result = 56;
                form.Close();
                return;
            }

            form.SetSourceText("stale request");
            stopwatch.Start();
            timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                result = 53;
                timer.Stop();
                form.Close();
                return;
            }

            var target = Descendants(form)
                .OfType<TextBox>()
                .Single(textBox => textBox.ReadOnly);

            if (stage == 0 && translation.StaleStarted.Task.IsCompleted)
            {
                form.SetSourceText("fresh request");
                stage = 1;
                return;
            }

            if (stage == 1 && translation.FreshCompleted.Task.IsCompleted)
            {
                if (target.Text != "fresh translated")
                {
                    result = 54;
                    timer.Stop();
                    form.Close();
                    return;
                }

                translation.CompleteStale("stale translated");
                stage = 2;
                return;
            }

            if (stage == 2)
            {
                if (target.Text != "fresh translated" ||
                    history.Entries.Count != 1 ||
                    history.Entries.Single().SourceText != "fresh request")
                {
                    result = 55;
                    timer.Stop();
                    form.Close();
                    return;
                }

                form.SetSourceText("cancel request");
                stage = 3;
                return;
            }

            if (stage == 3 && translation.CancelStarted.Task.IsCompleted)
            {
                var cancel = Descendants(form)
                    .OfType<Button>()
                    .FirstOrDefault(button => button.Text == "Cancel" && button.Visible);
                if (cancel == null)
                {
                    result = 51;
                    timer.Stop();
                    form.Close();
                    return;
                }

                cancel.PerformClick();
                stage = 4;
                return;
            }

            if (stage == 4 && translation.CancellationObserved)
            {
                history.FailWrites = true;
                form.SetSourceText("history failure request");
                stage = 5;
                return;
            }

            if (stage == 5 && translation.HistoryFailureCompleted.Task.IsCompleted)
            {
                var status = Descendants(form)
                    .OfType<Label>()
                    .FirstOrDefault(label => label.Text == "Ready (history unavailable).");
                result = target.Text == "history failure translated" &&
                         history.Entries.Count == 1 &&
                         translation.CallCount == 4 &&
                         translation.TargetLanguages.All(language => language == "ru") &&
                         logger.ErrorCount == 1 &&
                         status is not null
                    ? 0
                    : 52;
                timer.Stop();
                form.Close();
            }
        };

        Application.Run(form);
        try { Directory.Delete(testDirectory, recursive: true); } catch { }
        return result;
    }

    private static int RunTranslatorLocalizationTest()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.TranslatorLocalizationE2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        try
        {
            var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
            settings.Current.TranslationHistoryEnabled = false;
            settings.Save(settings.Current);
            var expectations = new[]
            {
                new { Culture = "en", Title = "LayoutFix Translator", Copy = "Copy", Cancel = "Cancel", Detect = "Detect language", History = "Translation history ▼" },
                new { Culture = "ru", Title = "Переводчик LayoutFix", Copy = "Копировать", Cancel = "Отменить", Detect = "Определить язык", History = "История переводов ▼" },
                new { Culture = "uk", Title = "Перекладач LayoutFix", Copy = "Копіювати", Cancel = "Скасувати", Detect = "Визначити мову", History = "Історія перекладів ▼" }
            };

            foreach (var expected in expectations)
            {
                var localization = new LocalizationService();
                localization.SetCulture(expected.Culture);
                using var form = new TranslatorForm(
                    new CountingTranslationService(),
                    new UnavailableOfflineTranslationService(),
                    new RecordingTranslationHistoryService(),
                    localization,
                    settings,
                    new E2ENullLogger());
                form.Show();
                Application.DoEvents();
                var controls = Descendants(form).ToArray();
                var sourceLanguages = controls
                    .OfType<ComboBox>()
                    .OrderByDescending(comboBox => comboBox.Items.Count)
                    .First();
                var valid = form.Text == expected.Title &&
                    controls.OfType<Button>().Any(button => button.Text == expected.Copy) &&
                    controls.OfType<Button>().Any(button => button.Text == expected.Cancel) &&
                    controls.OfType<Label>().Any(label => label.Text == expected.History) &&
                    sourceLanguages.GetItemText(sourceLanguages.Items[0]) == expected.Detect;
                form.Close();
                if (!valid)
                {
                    Console.Error.WriteLine(
                        $"translator-localization:failed culture={expected.Culture}");
                    return 57;
                }
            }

            Console.WriteLine("translator-localization=pass cultures=en,ru,uk native-language-names=pass");
            return 0;
        }
        finally
        {
            try { Directory.Delete(testDirectory, recursive: true); } catch { }
        }
    }

    private static int RunNotificationFocusTest()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.NotificationE2E.{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var settings = new SettingsService(Path.Combine(testDirectory, "settings.json"));
        settings.Current.NotificationsEnabled = false;
        settings.Save(settings.Current);
        using var popup = new LayoutFix.Services.PopupService(settings);
        using var form = new Form
        {
            Text = "LayoutFix Notification Focus E2E",
            Width = 500,
            Height = 180,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        var editor = new TextBox { Left = 20, Top = 40, Width = 430, Text = "keep focus" };
        form.Controls.Add(editor);
        var result = 60;
        form.Shown += async (_, _) =>
        {
            form.Activate();
            editor.Focus();
            await Task.Delay(200);
            var formCountBeforeDisabledPopup = Application.OpenForms.Count;
            popup.ShowStatus("This diagnostic notification must stay hidden");
            await Task.Delay(150);
            var disabledPopupStayedHidden =
                Application.OpenForms.Count == formCountBeforeDisabledPopup;
            settings.Current.NotificationsEnabled = true;
            settings.Save(settings.Current);
            var hadFocusBeforePopup = form.ContainsFocus && editor.Focused;
            popup.ShowStatus("Safe refusal notification");
            await Task.Delay(250);
            var keptFocusAfterPopup = form.ContainsFocus && editor.Focused;
            Console.WriteLine(
                $"notification-focus:hidden={disabledPopupStayedHidden};before={hadFocusBeforePopup};" +
                $"after={keptFocusAfterPopup};active={Form.ActiveForm?.Text}");
            result = !disabledPopupStayedHidden
                ? 63
                : !hadFocusBeforePopup
                    ? 62
                    : keptFocusAfterPopup ? 0 : 61;
            form.Close();
        };

        Application.Run(form);
        try { Directory.Delete(testDirectory, recursive: true); } catch { }
        return result;
    }

    private static string TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode.ToString() : "running";
        }
        catch
        {
            return "unknown";
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void AppendResult(string value) =>
        File.AppendAllText(ResultPath, $"{value}{Environment.NewLine}");

    private static void SendExternalHotkey(string configuredHotkey, ushort virtualKey)
    {
        var combo = HotkeyCombo.Parse(configuredHotkey);
        if (combo.Win)
            throw new InvalidOperationException("Windows-key E2E hotkeys are not supported.");

        var pressedModifiers = new List<ushort>();
        if (combo.Ctrl) pressedModifiers.Add(0x11);
        if (combo.Alt) pressedModifiers.Add(0x12);
        if (combo.Shift) pressedModifiers.Add(0x10);

        var inputs = new List<Win32.INPUT>(pressedModifiers.Count * 2 + 2);
        inputs.AddRange(pressedModifiers.Select(modifier => KeyboardInput(modifier, 0)));
        inputs.Add(KeyboardInput(virtualKey, 0));
        inputs.Add(KeyboardInput(virtualKey, Win32.KEYEVENTF_KEYUP));
        inputs.AddRange(pressedModifiers
            .AsEnumerable()
            .Reverse()
            .Select(modifier => KeyboardInput(modifier, Win32.KEYEVENTF_KEYUP)));

        var batch = inputs.ToArray();
        var sent = Win32.SendInput((uint)batch.Length, batch, Marshal.SizeOf<Win32.INPUT>());
        if (sent != batch.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Physical hotkey E2E SendInput accepted {sent} of {batch.Length} events.");
        }
    }

    private static void SendExternalSelectAll() => SendExternalChord(0x11, (ushort)'A');

    private static void SendExternalKeys(params ushort[] keys)
    {
        var inputs = keys
            .SelectMany(key => new[]
            {
                KeyboardInput(key, 0),
                KeyboardInput(key, Win32.KEYEVENTF_KEYUP)
            })
            .ToArray();
        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Auto-correction E2E SendInput accepted {sent} of {inputs.Length} events.");
        }
    }

    private static void SendExternalKeyDown(ushort key) =>
        SendExternalKeyEvent(key, 0);

    private static void SendExternalKeyUp(ushort key) =>
        SendExternalKeyEvent(key, Win32.KEYEVENTF_KEYUP);

    private static void SendExternalKeyEvent(ushort key, uint flags)
    {
        var input = new[] { KeyboardInput(key, flags) };
        var sent = Win32.SendInput(1, input, Marshal.SizeOf<Win32.INPUT>());
        if (sent != 1)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Auto-correction modifier E2E SendInput was rejected.");
        }
    }

    private static void SendExternalChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            KeyboardInput(modifier, 0),
            KeyboardInput(key, 0),
            KeyboardInput(key, Win32.KEYEVENTF_KEYUP),
            KeyboardInput(modifier, Win32.KEYEVENTF_KEYUP)
        };
        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"External E2E SendInput accepted {sent} of {inputs.Length} events.");
        }
    }

    private static Win32.INPUT KeyboardInput(ushort virtualKey, uint flags) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = flags,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    private static class NativeFocus
    {
        public const uint MouseEventLeftDown = 0x0002;
        public const uint MouseEventLeftUp = 0x0004;
        public const uint MouseEventXDown = 0x0080;
        public const uint MouseEventXUp = 0x0100;
        public const uint XButton1 = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        public delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(
            IntPtr parentWindow,
            EnumWindowProc callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr parentWindow, IntPtr childWindow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, ref Win32.RECT rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", EntryPoint = "mouse_event")]
        public static extern void MouseEvent(
            uint flags,
            uint dx,
            uint dy,
            uint data,
            UIntPtr extraInfo);
    }

    private sealed class NullTranslationCoordinator : ITranslationCoordinator
    {
        public ValueTask<bool> QueueTranslationAsync(TextSelection selection, string targetLanguage, string sourceLanguage = "auto", CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public void Dispose() { }
    }

    private sealed class NullSoundService : ISoundService
    {
        public void PlaySwitchSound() { }
        public void PlayAutoConvertSound() { }
        public void PlayErrorSound() { }
    }

    private sealed class NullTranslatorWindowProvider : ITranslatorWindowProvider
    {
        public void ShowTranslator(string initialText = "") { }
    }

    private sealed class CountingSettingsService(ISettingsService inner) : ISettingsService
    {
        public AppSettings Current => inner.Current;

        public int SaveCount { get; private set; }
        public AppSettings Load() => inner.Load();

        public void Save(AppSettings settings)
        {
            SaveCount++;
            inner.Save(settings);
        }
    }

    private sealed class CountingAutoStartService(bool initialValue) : IAutoStartService
    {
        private bool _isAutoStartEnabled = initialValue;

        public bool IsAutoStartEnabled
        {
            get => _isAutoStartEnabled;
            set
            {
                WriteCount++;
                if (FailWritesRemaining > 0)
                {
                    FailWritesRemaining--;
                    throw new InvalidOperationException(FailureMessage);
                }

                _isAutoStartEnabled = value;
            }
        }

        public int FailWritesRemaining { get; set; }
        public string FailureMessage { get; set; } = "Simulated registry failure.";
        public int WriteCount { get; private set; }
    }

    private sealed class NullAutoStartService : IAutoStartService
    {
        public bool IsAutoStartEnabled { get; set; }
    }

    private sealed class InMemoryTranslationCredentialStore : ITranslationCredentialStore
    {
        private string? _apiKey;
        public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);
        public string? ReadApiKey() => _apiKey;
        public void SaveApiKey(string? apiKey) => _apiKey = apiKey;
    }

    private sealed class PassthroughLocalizationService : ILocalizationService
    {
        public string GetString(string key, string defaultValue) => defaultValue;
        public void SetCulture(string culture) { }
    }

    private sealed class CountingTranslationService : ITranslationService
    {
        private int _callCount;
        private int _cancellationObserved;
        private readonly TaskCompletionSource<string> _staleResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => Volatile.Read(ref _callCount);
        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;
        public System.Collections.Concurrent.ConcurrentQueue<string> TargetLanguages { get; } = new();
        public TaskCompletionSource StaleStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FreshCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HistoryFailureCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> TranslateAsync(
            string text,
            string targetLanguage,
            string sourceLanguage = "auto",
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            TargetLanguages.Enqueue(targetLanguage);
            if (text.Contains("stale", StringComparison.Ordinal))
            {
                StaleStarted.TrySetResult();
                return await _staleResult.Task;
            }

            if (text.Contains("fresh", StringComparison.Ordinal))
            {
                FreshCompleted.TrySetResult();
                return "fresh translated";
            }

            if (text.Contains("history failure", StringComparison.Ordinal))
            {
                HistoryFailureCompleted.TrySetResult();
                return "history failure translated";
            }

            CancelStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "unexpected";
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _cancellationObserved, 1);
                throw;
            }
        }

        public void CompleteStale(string result) => _staleResult.TrySetResult(result);
    }

    private sealed class UnavailableOfflineTranslationService : IOfflineTranslationService
    {
        public bool IsModelAvailable() => false;

        public Task<string> TranslateAsync(
            string text,
            string targetLanguageCode,
            string sourceLanguageCode = "auto",
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Offline translation must not be used in this test.");
    }

    private sealed class PausingTextTargetGuard(ITextTargetGuard inner) : ITextTargetGuard
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _checksBeforePause = -1;

        public TaskCompletionSource PauseStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ArmAfterOnePass() => Volatile.Write(ref _checksBeforePause, 1);

        public void Release() => _release.TrySetResult();

        public async Task<bool> CanModifyAsync(
            ActiveWindowContext context,
            CancellationToken cancellationToken = default)
        {
            var remaining = Volatile.Read(ref _checksBeforePause);
            if (remaining >= 0 &&
                Interlocked.Decrement(ref _checksBeforePause) < 0)
            {
                PauseStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return await inner.CanModifyAsync(context, cancellationToken);
        }
    }

    private sealed class DictionaryPerformanceSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    private sealed class DictionaryPerformanceLayoutManager : IKeyboardLayoutManager
    {
        private static readonly IReadOnlyList<Layout> Layouts =
        [
            new()
            {
                Code = "en-US",
                Keys = CreateKeys("qwertyuiopasdfghjklzxcvbnm")
            },
            new()
            {
                Code = "ru-RU",
                Keys = CreateKeys("йцукенгшщзфывапролдячсмить")
            },
            new()
            {
                Code = "uk-UA",
                Keys = CreateKeys("йцукенгшщзфівапролдячсмить")
            }
        ];

        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => Layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => Layouts;

        private static Dictionary<string, string> CreateKeys(string output)
        {
            const string physicalKeys = "qwertyuiopasdfghjklzxcvbnm";
            return physicalKeys
                .Select((key, index) => new KeyValuePair<string, string>(
                    key.ToString(),
                    output[index].ToString()))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    private sealed class AutoCorrectionE2ELayoutManager(
        IKeyboardLayoutManager inner) : IKeyboardLayoutManager
    {
        private static readonly Layout SyntheticUkrainianLayout = new()
        {
            Code = "uk-UA",
            Keys = CreateKeys("йцукенгшщзфівапролдячсмить")
        };

        public void LoadAll() => inner.LoadAll();

        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() =>
            AppendSyntheticLayout(inner.GetInstalledWindowsLayouts());

        public IReadOnlyList<Layout> GetLayoutOrder() =>
            AppendSyntheticLayout(inner.GetLayoutOrder());

        private static IReadOnlyList<Layout> AppendSyntheticLayout(
            IReadOnlyList<Layout> layouts)
        {
            if (layouts.Any(layout => KeyboardLayoutIdentity.SameCulture(
                    layout.Code,
                    SyntheticUkrainianLayout.Code)))
            {
                return layouts;
            }

            return layouts.Append(SyntheticUkrainianLayout).ToArray();
        }

        private static Dictionary<string, string> CreateKeys(string output)
        {
            const string physicalKeys = "qwertyuiopasdfghjklzxcvbnm";
            return physicalKeys
                .Select((key, index) => new KeyValuePair<string, string>(
                    key.ToString(),
                    output[index].ToString()))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    private sealed class FaultInjectingInputInjector(IInputInjector inner) : IInputInjector
    {
        private Func<Task>? _beforeFailure;
        private int _acceptedBackspacePresses = -1;
        public TaskCompletionSource FailureObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PartialBackspaceFailureObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ArmNextTextFailure(Func<Task> beforeFailure) =>
            Interlocked.Exchange(ref _beforeFailure, beforeFailure);

        public void ArmPartialBackspaceFailure(int acceptedPresses) =>
            Interlocked.Exchange(ref _acceptedBackspacePresses, acceptedPresses);

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key) =>
            inner.SendKeyCombinationAsync(ctrl, alt, shift, key);

        public async Task SendBackspacesAsync(int count)
        {
            var acceptedPresses = Interlocked.Exchange(
                ref _acceptedBackspacePresses,
                -1);
            if (acceptedPresses < 0)
            {
                await inner.SendBackspacesAsync(count);
                return;
            }

            var boundedAcceptedPresses = Math.Clamp(acceptedPresses, 0, count);
            await inner.SendBackspacesAsync(boundedAcceptedPresses);
            PartialBackspaceFailureObserved.TrySetResult();
            throw new InputInjectionException(
                InputInjectionOperation.Backspace,
                count,
                boundedAcceptedPresses,
                count * 2,
                boundedAcceptedPresses * 2);
        }

        public async Task SendTextAsync(string text)
        {
            var beforeFailure = Interlocked.Exchange(ref _beforeFailure, null);
            if (beforeFailure == null)
            {
                await inner.SendTextAsync(text);
                return;
            }

            await beforeFailure();
            FailureObserved.TrySetResult();
            throw new InvalidOperationException(
                "Simulated replacement failure after focus moved to another real Windows text target.");
        }

        public Task SelectWordLeftAsync() => inner.SelectWordLeftAsync();

        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) =>
            inner.WaitForModifiersReleaseAsync(timeoutMs);
    }

    private sealed class PartialTextFailureInputInjector(
        IInputInjector inner,
        int affectedUtf16Length) : IInputInjector
    {
        private int _armed = 1;
        private Func<Task>? _afterPartialText;
        public TaskCompletionSource FailureObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RollbackTextObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PhysicalInputObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SetAfterPartialText(Func<Task> afterPartialText) =>
            _afterPartialText = afterPartialText;

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key) =>
            inner.SendKeyCombinationAsync(ctrl, alt, shift, key);

        public Task SendBackspacesAsync(int count) => inner.SendBackspacesAsync(count);

        public async Task SendTextAsync(string text)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                var boundedAffectedLength = Math.Clamp(
                    affectedUtf16Length,
                    0,
                    text.Length);
                if (boundedAffectedLength > 0)
                    await inner.SendTextAsync(text[..boundedAffectedLength]);
                if (_afterPartialText != null)
                {
                    await _afterPartialText();
                    PhysicalInputObserved.TrySetResult();
                }
                FailureObserved.TrySetResult();
                throw new InputInjectionException(
                    InputInjectionOperation.Text,
                    text.Length,
                    boundedAffectedLength,
                    text.Length * 2,
                    boundedAffectedLength * 2);
            }

            await inner.SendTextAsync(text);
            RollbackTextObserved.TrySetResult();
        }

        public Task SelectWordLeftAsync() => inner.SelectWordLeftAsync();

        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) =>
            inner.WaitForModifiersReleaseAsync(timeoutMs);
    }

    private sealed class RecordingTranslationHistoryService : ITranslationHistoryService
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<TranslationHistoryEntry> _entries = new();
        public bool FailWrites { get; set; }
        public IReadOnlyCollection<TranslationHistoryEntry> Entries => _entries.ToArray();
        public Task AddEntryAsync(TranslationHistoryEntry entry)
        {
            if (FailWrites)
                throw new IOException("Synthetic translation history write failure.");
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }
        public Task<List<TranslationHistoryEntry>> GetHistoryAsync() =>
            Task.FromResult(_entries.ToList());
        public Task ClearHistoryAsync()
        {
            _entries.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnUseInputInjector : IInputInjector
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key) =>
            UnexpectedCall();
        public Task SendBackspacesAsync(int count) => UnexpectedCall();
        public Task SendTextAsync(string text) => UnexpectedCall();
        public Task SelectWordLeftAsync() => UnexpectedCall();
        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => UnexpectedCall();

        private Task UnexpectedCall()
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("SendInput must not run for a rejected text target.");
        }
    }

    private sealed class FailOnUseClipboardService : IClipboardService
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<IClipboardSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default) => UnexpectedCall<IClipboardSnapshot>();
        public Task RestoreAsync(
            IClipboardSnapshot snapshot,
            CancellationToken cancellationToken = default) => UnexpectedCall();
        public Task<string?> ReadTextAsync(
            CancellationToken cancellationToken = default) => UnexpectedCall<string?>();
        public Task SetTextAsync(
            string text,
            CancellationToken cancellationToken = default) => UnexpectedCall();
        public uint GetSequenceNumber()
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("Clipboard must not run for a rejected text target.");
        }
        public void Dispose() { }

        private Task UnexpectedCall()
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("Clipboard must not run for a rejected text target.");
        }

        private Task<T> UnexpectedCall<T>()
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("Clipboard must not run for a rejected text target.");
        }
    }

    private sealed record ManualCorrectionCase(
        string Id,
        string Input,
        string Expected);

    private sealed class E2ENullLogger : ILoggerService
    {
        private int _errorCount;
        public int ErrorCount => Volatile.Read(ref _errorCount);
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? ex = null) =>
            Interlocked.Increment(ref _errorCount);
    }

    private sealed class E2EFixedActiveWindowProvider : IActiveWindowProvider
    {
        public ActiveWindowContext Current { get; set; }
        public ActiveWindowContext CaptureActiveWindow() => Current;
        public bool IsSameActiveWindow(ActiveWindowContext context) => context == Current;
        public string GetActiveProcessName() => "LayoutFix.WindowsE2E";
        public string GetActiveLayoutCode() => "en-US";
        public void SwitchToNextLayout() { }
        public bool TrySwitchToLayout(string layoutCode) => true;
    }
}
