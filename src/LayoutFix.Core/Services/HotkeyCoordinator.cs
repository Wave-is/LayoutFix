using System;
using System.Linq;
using System.Threading.Tasks;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public interface IHotkeyCoordinator : IDisposable
{
    void Initialize();
    Task ExecuteActionAsync(HotkeyAction action);
}

public class HotkeyCoordinator : IHotkeyCoordinator
{
    private readonly IKeyboardHook _keyboardHook;
    private readonly IInputInjector _inputInjector;
    private readonly ISettingsService _settingsService;
    private readonly IKeyboardLayoutManager _keyboardLayoutManager;
    private readonly ILayoutConverter _layoutConverter;
    private readonly ITextTransformer _textTransformer;
    private readonly TransliterationService _transliterationService;
    private readonly INumberToTextConverter _numberToTextConverter;
    private readonly ILoggerService _logger;

    private readonly IActiveWindowProvider _activeWindowProvider;
    private readonly ISoundService _soundService;

    public HotkeyCoordinator(
        IKeyboardHook keyboardHook,
        IInputInjector inputInjector,
        ISettingsService settingsService,
        IKeyboardLayoutManager keyboardLayoutManager,
        ILayoutConverter layoutConverter,
        ITextTransformer textTransformer,
        TransliterationService transliterationService,
        INumberToTextConverter numberToTextConverter,
        ILoggerService logger,
        IActiveWindowProvider activeWindowProvider,
        ISoundService soundService)
    {
        _keyboardHook = keyboardHook;
        _inputInjector = inputInjector;
        _settingsService = settingsService;
        _keyboardLayoutManager = keyboardLayoutManager;
        _layoutConverter = layoutConverter;
        _textTransformer = textTransformer;
        _transliterationService = transliterationService;
        _numberToTextConverter = numberToTextConverter;
        _logger = logger;
        _activeWindowProvider = activeWindowProvider;
        _soundService = soundService;
    }

    public void Initialize()
    {
        _keyboardLayoutManager.LoadAll();
        _keyboardHook.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        try
        {
            if (IsBlacklisted()) return;

            var configs = _settingsService.Current.HotkeyConfigs;
            if (configs == null || !configs.Any()) return;

            var activeConfigs = configs.Where(c => c.Enabled).OrderByDescending(c => c.Hotkey.Length);

            foreach (var config in activeConfigs)
            {
                var expectedCombo = HotkeyCombo.Parse(config.Hotkey);
                if (IsComboMatch(expectedCombo, e.Combo))
                {
                    if (Enum.TryParse<HotkeyAction>(config.Action, true, out var action))
                    {
                        e.Handled = true;
                        _ = ExecuteActionAsync(action);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error in OnHotkeyPressed", ex);
        }
    }

    private bool IsBlacklisted()
    {
        var blacklisted = _settingsService.Current.BlacklistedProcesses;
        if (blacklisted == null || blacklisted.Count == 0) return false;
        
        string procName = _activeWindowProvider.GetActiveProcessName();
        if (string.IsNullOrEmpty(procName)) return false;
        
        string procNameExe = procName + ".exe";
        return blacklisted.Any(b => string.Equals(b, procName, StringComparison.OrdinalIgnoreCase) || 
                                    string.Equals(b, procNameExe, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsComboMatch(HotkeyCombo expected, HotkeyCombo actual)
    {
        if (expected.Ctrl != actual.Ctrl) return false;
        if (expected.Alt != actual.Alt) return false;
        if (expected.Shift != actual.Shift) return false;
        if (expected.Win != actual.Win) return false;
        if (expected.PrintScreen != actual.PrintScreen) return false;
        
        if (expected.VirtualKey != 0 && actual.VirtualKey != 0)
        {
            return expected.VirtualKey == actual.VirtualKey;
        }
        
        return string.Equals(expected.Key, actual.Key, StringComparison.OrdinalIgnoreCase);
    }

    public async Task ExecuteActionAsync(HotkeyAction action)
    {
        try
        {
            _logger.LogInfo($"--- ExecuteActionAsync Started for action: {action} ---");
            
            await _inputInjector.ReleaseModifiersAsync();
            _logger.LogInfo($"Modifiers released.");
            
            string? backup = await _inputInjector.GetClipboardTextAsync();
            _logger.LogInfo($"Clipboard backup captured. Length: {backup?.Length ?? 0}");

            await _inputInjector.SetClipboardTextAsync(""); 
            _logger.LogInfo($"Clipboard cleared.");
            
            await _inputInjector.SendKeyCombinationAsync(true, false, false, "c");
            await Task.Delay(200);
            
            string? text = await _inputInjector.GetClipboardTextAsync();
            _logger.LogInfo($"Text after Ctrl+C: '{text}' (Length: {text?.Length ?? 0})");
            
            if (string.IsNullOrEmpty(text))
            {
                _logger.LogInfo($"Text was empty. Attempting to select word left...");
                await _inputInjector.SelectWordLeftAsync();
                await Task.Delay(100);
                await _inputInjector.SendKeyCombinationAsync(true, false, false, "c");
                await Task.Delay(200);
                text = await _inputInjector.GetClipboardTextAsync();
                _logger.LogInfo($"Text after SelectWordLeft + Ctrl+C: '{text}' (Length: {text?.Length ?? 0})");
            }

            if (!string.IsNullOrEmpty(text))
            {
                var (newText, targetLayoutCode) = ProcessText(text, action);
                _logger.LogInfo($"ProcessText result: '{newText}', TargetLayout: '{targetLayoutCode}'");

                bool textChanged = newText != null && newText != text;

                if (textChanged)
                {
                    await _inputInjector.SetClipboardTextAsync(newText!);
                    await Task.Delay(100);
                    _logger.LogInfo($"Sending Ctrl+V...");
                    await _inputInjector.SendKeyCombinationAsync(true, false, false, "v");
                    await Task.Delay(200);
                    _logger.LogInfo($"Ctrl+V sent.");
                }

                if (targetLayoutCode != null)
                {
                    // Switch to target layout
                    _logger.LogInfo($"Switching active layout to {targetLayoutCode}");
                    _activeWindowProvider.SwitchToNextLayout();
                }

                if ((textChanged || targetLayoutCode != null) && _settingsService.Current.SoundEnabled)
                {
                    _soundService.PlaySwitchSound();
                }

                if (!textChanged && targetLayoutCode == null)
                {
                    _logger.LogInfo($"Text was not changed by LayoutConverter and layout was not switched.");
                }
            }
            
            _logger.LogInfo($"--- ExecuteActionAsync Finished ---");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in ExecuteActionAsync for {action}", ex);
        }
    }

    private (string? newText, string? targetLayoutCode) ProcessText(string text, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.FixLayout:
                var activeLayouts = _keyboardLayoutManager.GetLayoutOrder();
                string currentLayout = _activeWindowProvider.GetActiveLayoutCode();
                var (converted, _, targetLayout) = _layoutConverter.AutoConvert(text, activeLayouts, currentLayout);
                return (converted, targetLayout?.Code);
            case HotkeyAction.ChangeCase:
                return (_textTransformer.ChangeCase(text), null);
            case HotkeyAction.Transliterate:
                return (_transliterationService.Transliterate(text), null);
            case HotkeyAction.NumberToText:
                if (long.TryParse(text, out long num))
                {
                    return (_numberToTextConverter.Convert(num, "ru-RU"), null);
                }
                return (text, null);
            case HotkeyAction.ConvertToEnglish:
                return (ConvertToLayoutCode(text, "en-US"), null);
            case HotkeyAction.ConvertToRussian:
                return (ConvertToLayoutCode(text, "ru-RU"), null);
            case HotkeyAction.ConvertToUkrainian:
                return (ConvertToLayoutCode(text, "uk-UA"), null);
            default:
                return (null, null);
        }
    }

    private string? ConvertToLayoutCode(string text, string code)
    {
        var activeLayouts = _keyboardLayoutManager.GetLayoutOrder();
        var target = activeLayouts.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (target == null) return text;
        
        var (_, source, _) = _layoutConverter.AutoConvert(text, activeLayouts);
        if (source == null || source.Code == target.Code) return text;
        
        return _layoutConverter.ConvertTo(text, target, source);
    }

    public void Dispose()
    {
        _keyboardHook.HotkeyPressed -= OnHotkeyPressed;
    }
}
