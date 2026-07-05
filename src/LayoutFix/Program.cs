using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Services;
using LayoutFix.UI;

namespace LayoutFix;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        const string appName = "LayoutFix_SingleInstance_Mutex";
        _mutex = new Mutex(true, appName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("LayoutFix is already running.", "LayoutFix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppHost.Build();

        var trayManager = AppHost.Services?.GetRequiredService<TrayManager>();
        var keyboardHook = AppHost.Services?.GetRequiredService<IKeyboardHook>();
        var coordinator = AppHost.Services?.GetRequiredService<IHotkeyCoordinator>();
        var settingsService = AppHost.Services?.GetRequiredService<ISettingsService>();
        var autoStartService = AppHost.Services?.GetRequiredService<IAutoStartService>();
        var locService = AppHost.Services?.GetRequiredService<ILocalizationService>();

        if (settingsService != null)
        {
            if (locService != null) locService.SetCulture(settingsService.Current.UiLanguage);
            if (autoStartService != null && autoStartService.IsAutoStartEnabled != settingsService.Current.AutoStart)
            {
                settingsService.Current.AutoStart = autoStartService.IsAutoStartEnabled;
                settingsService.Save(settingsService.Current);
            }
        }

        coordinator?.Initialize();
        keyboardHook?.Start();
        
        var mouseHook = AppHost.Services?.GetRequiredService<IMouseHook>();
        mouseHook?.Start();
        
        // Resolve AutoConversionService to start its event subscriptions
        var autoConversion = AppHost.Services?.GetRequiredService<AutoConversionService>();

        Application.Run();

        keyboardHook?.Stop();
        mouseHook?.Stop();
        trayManager?.Dispose();
        _mutex.ReleaseMutex();
    }
}