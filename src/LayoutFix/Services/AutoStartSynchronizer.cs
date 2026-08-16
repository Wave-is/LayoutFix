using LayoutFix.Core.Interfaces;

namespace LayoutFix.Services;

internal static class AutoStartSynchronizer
{
    public static void Synchronize(
        ISettingsService settingsService,
        IAutoStartService autoStartService)
    {
        var registered = autoStartService.IsAutoStartEnabled;
        if (registered == settingsService.Current.AutoStart)
            return;

        if (settingsService.Current.AutoStart)
        {
            // A persisted opt-in survives application moves and stale/missing
            // Run values. Repair the registration instead of silently changing
            // the user's preference to false.
            autoStartService.IsAutoStartEnabled = true;
            return;
        }

        // On a fresh profile the installer task may have created the Run value
        // before LayoutFix creates settings.json. Adopt that explicit opt-in.
        settingsService.Current.AutoStart = true;
        settingsService.Save(settingsService.Current);
    }
}
