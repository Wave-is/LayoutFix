using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Services;

internal sealed class SettingsPersistenceCoordinator
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoStartService _autoStartService;
    private bool _appliedAutoStart;
    private bool _settingsFilePending;
    private bool _autoStartPending;

    public bool HasPendingChanges => _settingsFilePending || _autoStartPending;

    public SettingsPersistenceCoordinator(
        ISettingsService settingsService,
        IAutoStartService autoStartService,
        bool initialAutoStart)
    {
        _settingsService = settingsService;
        _autoStartService = autoStartService;
        _appliedAutoStart = initialAutoStart;
    }

    public void Save(AppSettings settings)
    {
        // Save is called after a new UI mutation, so the settings file must be
        // persisted even when a previous autostart write is still pending.
        _settingsFilePending = true;
        PersistSettingsFile(settings);
        PersistAutoStartIfNeeded(settings.AutoStart);
    }

    public void RetryPending(AppSettings settings)
    {
        if (!HasPendingChanges)
            return;

        if (_settingsFilePending)
            PersistSettingsFile(settings);

        PersistAutoStartIfNeeded(settings.AutoStart);
    }

    private void PersistSettingsFile(AppSettings settings)
    {
        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception exception)
        {
            throw new SettingsPersistenceException(
                SettingsPersistenceStage.SettingsFile,
                exception);
        }

        _settingsFilePending = false;
    }

    private void PersistAutoStartIfNeeded(bool desiredAutoStart)
    {
        if (!_autoStartPending && desiredAutoStart == _appliedAutoStart)
            return;

        // Advance the applied state only after Windows accepted the change.
        // A transient registry failure therefore remains retryable without
        // rewriting a settings file that is already durable.
        _autoStartPending = true;
        try
        {
            _autoStartService.IsAutoStartEnabled = desiredAutoStart;
        }
        catch (Exception exception)
        {
            throw new SettingsPersistenceException(
                SettingsPersistenceStage.AutoStartRegistry,
                exception);
        }

        _appliedAutoStart = desiredAutoStart;
        _autoStartPending = false;
    }
}
