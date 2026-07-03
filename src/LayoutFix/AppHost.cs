using System;
using Microsoft.Extensions.DependencyInjection;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Input;
using LayoutFix.Infrastructure.Layouts;
using LayoutFix.Infrastructure.Services;
using LayoutFix.UI;

namespace LayoutFix;

public static class AppHost
{
    public static IServiceProvider? Services { get; private set; }

    public static void Build()
    {
        var services = new ServiceCollection();
        
        // Infrastructure
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ILoggerService, FileLoggerService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IWindowsLayoutProvider, WindowsLayoutProvider>();
        services.AddSingleton<IKeyboardHook, KeyboardHook>();
        services.AddSingleton<IInputInjector, InputInjector>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<IActiveWindowProvider, ActiveWindowProvider>();
        
        // Core Services
        services.AddSingleton<IKeyboardLayoutManager, KeyboardLayoutManager>();
        services.AddSingleton<ILayoutConverter, LayoutConverter>();
        services.AddSingleton<ITextTransformer, TextTransformer>();
        services.AddSingleton<INumberToTextConverter, NumberToTextConverter>();
        services.AddSingleton<TransliterationService>();
        services.AddSingleton<IHotkeyCoordinator, HotkeyCoordinator>();
        services.AddSingleton<IDictionaryAnalyzer, DictionaryAnalyzer>();
        services.AddSingleton<IMouseHook, MouseHook>();
        services.AddSingleton<AutoConversionService>();
        
        // UI
        services.AddSingleton<TrayManager>();
        services.AddTransient<SettingsForm>();
        
        Services = services.BuildServiceProvider();
    }
}
