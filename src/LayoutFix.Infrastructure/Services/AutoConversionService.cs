using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Services;

public class AutoConversionService : IDisposable
{
    private readonly IKeyboardHook _keyboardHook;
    private readonly IMouseHook _mouseHook;
    private readonly ISettingsService _settingsService;
    private readonly IDictionaryAnalyzer _dictionaryAnalyzer;
    private readonly IInputInjector _inputInjector;
    private readonly IKeyboardLayoutManager _layoutManager;
    private readonly ILayoutConverter _layoutConverter;
    private readonly ILoggerService _logger;
    private readonly ISoundService _soundService;

    private readonly StringBuilder _buffer = new();

    public AutoConversionService(
        IKeyboardHook keyboardHook,
        IMouseHook mouseHook,
        ISettingsService settingsService,
        IDictionaryAnalyzer dictionaryAnalyzer,
        IInputInjector inputInjector,
        IKeyboardLayoutManager layoutManager,
        ILayoutConverter layoutConverter,
        ILoggerService logger,
        ISoundService soundService)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _settingsService = settingsService;
        _dictionaryAnalyzer = dictionaryAnalyzer;
        _inputInjector = inputInjector;
        _layoutManager = layoutManager;
        _layoutConverter = layoutConverter;
        _logger = logger;
        _soundService = soundService;

        _keyboardHook.HotkeyPressed += OnKeyPressed;
        _mouseHook.MouseClicked += OnMouseClicked;
    }

    private void OnMouseClicked(object? sender, EventArgs e)
    {
        _buffer.Clear();
    }

    private void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        _logger.LogInfo($"KEY PRESSED: '{e.Combo.Key}' (VK: {e.Combo.VirtualKey}), Enabled: {_settingsService.Current.AutoConversionEnabled}");
        if (!_settingsService.Current.AutoConversionEnabled) return;

        if (e.Combo.Key == "space" || e.Combo.Key == "enter")
        {
            if (_buffer.Length > 0)
            {
                CheckAndConvert(e.Combo.Key);
                _buffer.Clear();
            }
            return;
        }

        if (e.Combo.Key == "backspace")
        {
            if (_buffer.Length > 0) _buffer.Length--;
            return;
        }

        if (e.Combo.Key.Length > 1)
        {
            // Нажата функциональная клавиша (Shift, Ctrl, Esc, Tab и тд)
            // Мы пока игнорируем Shift для буфера, но если это Tab/Esc, сбрасываем буфер
            if (e.Combo.VirtualKey != Win32.VK_SHIFT && e.Combo.VirtualKey != Win32.VK_CONTROL && e.Combo.VirtualKey != Win32.VK_MENU)
            {
                _buffer.Clear();
            }
            return;
        }

        _buffer.Append(e.Combo.Key);
    }

    private void CheckAndConvert(string triggerKey)
    {
        string word = _buffer.ToString();
        _logger.LogInfo($"CheckAndConvert triggered with word: '{word}'");
        if (word.Length < 2) return;

        string currentLayout = Win32.GetActiveLayoutCode();
        
        bool isGibberish = _dictionaryAnalyzer.IsGibberish(word, currentLayout);
        _logger.LogInfo($"Current layout: {currentLayout}, IsGibberish: {isGibberish}");

        if (isGibberish)
        {
            _logger.LogInfo($"Auto-conversion triggered for '{word}' from {currentLayout}");

            // Отправляем конвертацию в фон, чтобы не блокировать хук
            Task.Run(async () => 
            {
                await Task.Delay(20); // Даем пробелу напечататься
                
                // 1. Удаляем напечатанное слово + 1 символ триггера (пробел)
                await _inputInjector.SendBackspacesAsync(word.Length + 1);

                // 2. Получаем сконвертированное слово
                var layouts = _layoutManager.GetLayoutOrder();
                var (converted, _, _) = _layoutConverter.AutoConvert(word, layouts, currentLayout);
                _logger.LogInfo($"Converted word: {converted}");

                if (converted != null)
                {
                    if (_settingsService.Current.SoundEnabled)
                    {
                        _soundService.PlayAutoConvertSound();
                    }
                    // Переключаем раскладку (посылаем сигнал следующей раскладки)
                    var hwnd = Win32.GetForegroundWindow();
                    Win32.PostMessage(hwnd, Win32.WM_INPUTLANGCHANGEREQUEST, (IntPtr)Win32.INPUTLANGCHANGE_FORWARD, IntPtr.Zero);
                    await Task.Delay(30);

                    // Печатаем слово и триггер
                    await _inputInjector.SendTextAsync(converted);
                    if (triggerKey == "space") await _inputInjector.SendTextAsync(" ");
                    if (triggerKey == "enter") await _inputInjector.SendKeyCombinationAsync(false, false, false, "enter");
                }
            });
        }
    }

    public void Dispose()
    {
        _keyboardHook.HotkeyPressed -= OnKeyPressed;
        _mouseHook.MouseClicked -= OnMouseClicked;
    }
}
