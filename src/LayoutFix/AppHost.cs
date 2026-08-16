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

    public static void Build(
        string? settingsFilePath = null,
        string? historyFilePath = null,
        string? logFilePath = null)
        => BuildCore(
            settingsFilePath,
            historyFilePath,
            logFilePath,
            injectedMouseHookStartFailures: 0);

    internal static void BuildForStartupLifecycle(
        string settingsFilePath,
        string historyFilePath,
        string logFilePath,
        int injectedMouseHookStartFailures)
        => BuildCore(
            settingsFilePath,
            historyFilePath,
            logFilePath,
            injectedMouseHookStartFailures);

    private static void BuildCore(
        string? settingsFilePath,
        string? historyFilePath,
        string? logFilePath,
        int injectedMouseHookStartFailures)
    {
        Shutdown();
        var services = new ServiceCollection();
        
        // Infrastructure
        services.AddSingleton<ITranslationCredentialStore, WindowsTranslationCredentialStore>();
        services.AddSingleton<ITranslationService, TranslationService>();
        services.AddSingleton<IOfflineTranslationService, LayoutFix.Services.OfflineTranslationWorkerClient>();
        services.AddSingleton<ITranslationHistoryService>(provider =>
            new TranslationHistoryService(
                provider.GetRequiredService<ISettingsService>(),
                historyFilePath));
        services.AddSingleton<ModelDownloadService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService>(_ => new SettingsService(settingsFilePath));
        services.AddSingleton<ILoggerService>(provider =>
            new FileLoggerService(
                provider.GetRequiredService<ISettingsService>(),
                logFilePath));
        services.AddSingleton<IWindowsLayoutProvider, WindowsLayoutProvider>();
        services.AddSingleton<IKeyboardHook, KeyboardHook>();
        services.AddSingleton<IInputInjector, InputInjector>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<IActiveWindowProvider, ActiveWindowProvider>();
        services.AddSingleton<ITextTargetGuard, WindowsTextTargetGuard>();
        services.AddSingleton<IDirectTextAdapter, AdobeInlineRenameTextAdapter>();
        
        // Core Services
        services.AddSingleton<IKeyboardLayoutManager, KeyboardLayoutManager>();
        services.AddSingleton<ILayoutConverter, LayoutConverter>();
        services.AddSingleton<ITextTransformer, TextTransformer>();
        services.AddSingleton<INumberToTextConverter, NumberToTextConverter>();
        services.AddSingleton<ITextTransactionService, TextTransactionService>();
        services.AddSingleton<ITranslationCoordinator, TranslationCoordinator>();
        services.AddSingleton<IAutoCorrectionMemory, AutoCorrectionMemory>();
        services.AddSingleton<TransliterationService>();
        services.AddSingleton<IHotkeyCoordinator, HotkeyCoordinator>();
        services.AddSingleton<DictionaryAnalyzer>();
        services.AddSingleton<IDictionaryAnalyzer>(provider =>
            provider.GetRequiredService<DictionaryAnalyzer>());
        services.AddSingleton<IPopupService, LayoutFix.Services.PopupService>();
        services.AddSingleton<ITranslatorWindowProvider, LayoutFix.Services.TranslatorWindowProvider>();
        services.AddSingleton<LayoutFix.Services.SettingsWindowProvider>();
        if (injectedMouseHookStartFailures > 0)
        {
            services.AddSingleton<IMouseHook>(provider =>
                new FailStartMouseHook(
                    new MouseHook(provider.GetRequiredService<ILoggerService>()),
                    injectedMouseHookStartFailures));
        }
        else
        {
            services.AddSingleton<IMouseHook, MouseHook>();
        }
        services.AddSingleton<HookRecoveryCoordinator>();
        services.AddSingleton<LayoutFix.Services.WindowsSessionRecoveryMonitor>();
        services.AddSingleton<AutoConversionService>();
        
        // UI
        services.AddSingleton<TrayManager>();
        
        Services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    public static void Shutdown()
    {
        if (Services is IDisposable disposable)
            disposable.Dispose();
        Services = null;
    }

    private sealed class FailStartMouseHook(
        IMouseHook inner,
        int startFailures) : IMouseHook
    {
        private int _startFailuresRemaining = startFailures;

        public event EventHandler? MouseClicked
        {
            add => inner.MouseClicked += value;
            remove => inner.MouseClicked -= value;
        }

        public long InputGeneration => inner.InputGeneration;

        public void Start()
        {
            if (InterlockedExtensions.DecrementIfPositive(ref _startFailuresRemaining))
                throw new InvalidOperationException("Injected startup hook failure.");
            inner.Start();
        }

        public void Stop() => inner.Stop();
        public void Dispose() => inner.Dispose();
    }

    private static class InterlockedExtensions
    {
        public static bool DecrementIfPositive(ref int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref value);
                if (current <= 0)
                    return false;
                if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
                    return true;
            }
        }
    }
}
