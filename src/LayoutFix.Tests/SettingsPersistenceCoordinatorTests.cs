using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Services;

namespace LayoutFix.Tests;

public class SettingsPersistenceCoordinatorTests
{
    [Fact]
    public void NewCoordinatorStartsWithoutPendingChanges()
    {
        var coordinator = new SettingsPersistenceCoordinator(
            new FakeSettingsService(),
            new FakeAutoStartService(false),
            initialAutoStart: false);

        Assert.False(coordinator.HasPendingChanges);
    }

    [Fact]
    public void RetryWithoutPendingChangesDoesNotWriteAnything()
    {
        var settingsService = new FakeSettingsService();
        var autoStartService = new FakeAutoStartService(false);
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initialAutoStart: false);

        coordinator.RetryPending(new AppSettings());

        Assert.Equal(0, settingsService.SaveCount);
        Assert.Equal(0, autoStartService.WriteCount);
        Assert.False(coordinator.HasPendingChanges);
    }

    [Fact]
    public void UnrelatedSettingSaveDoesNotRewriteAutoStartRegistration()
    {
        var settingsService = new FakeSettingsService();
        var autoStartService = new FakeAutoStartService(true);
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initialAutoStart: true);
        var settings = new AppSettings { AutoStart = true, SoundEnabled = false };

        coordinator.Save(settings);

        Assert.Equal(1, settingsService.SaveCount);
        Assert.Equal(0, autoStartService.WriteCount);
        Assert.False(coordinator.HasPendingChanges);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AutoStartChangeIsAppliedExactlyOnce(bool initial, bool desired)
    {
        var settingsService = new FakeSettingsService();
        var autoStartService = new FakeAutoStartService(initial);
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initial);
        var settings = new AppSettings { AutoStart = desired };

        coordinator.Save(settings);
        coordinator.Save(settings);

        Assert.Equal(2, settingsService.SaveCount);
        Assert.Equal(1, autoStartService.WriteCount);
        Assert.Equal(desired, autoStartService.IsAutoStartEnabled);
        Assert.False(coordinator.HasPendingChanges);
    }

    [Fact]
    public void SettingsFailureDoesNotTouchAutoStartRegistration()
    {
        var settingsService = new FakeSettingsService { FailSavesRemaining = 1 };
        var autoStartService = new FakeAutoStartService(false);
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initialAutoStart: false);

        var exception = Assert.Throws<SettingsPersistenceException>(() =>
            coordinator.Save(new AppSettings { AutoStart = true }));

        Assert.Equal(SettingsPersistenceStage.SettingsFile, exception.Stage);
        Assert.Equal("LF-ST-001", exception.DiagnosticCode);
        Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal(0, autoStartService.WriteCount);
        Assert.False(autoStartService.IsAutoStartEnabled);
        Assert.True(coordinator.HasPendingChanges);
    }

    [Fact]
    public void RegistryFailureKeepsAutoStartChangePendingForRetry()
    {
        var settingsService = new FakeSettingsService();
        var autoStartService = new FakeAutoStartService(false) { FailWritesRemaining = 1 };
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initialAutoStart: false);
        var settings = new AppSettings { AutoStart = true };

        var exception = Assert.Throws<SettingsPersistenceException>(() => coordinator.Save(settings));
        Assert.Equal(SettingsPersistenceStage.AutoStartRegistry, exception.Stage);
        Assert.Equal("LF-ST-002", exception.DiagnosticCode);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.True(coordinator.HasPendingChanges);
        coordinator.RetryPending(settings);

        Assert.Equal(1, settingsService.SaveCount);
        Assert.Equal(2, autoStartService.WriteCount);
        Assert.True(autoStartService.IsAutoStartEnabled);
        Assert.False(coordinator.HasPendingChanges);
    }

    [Fact]
    public void NewUserSaveAfterRegistryFailurePersistsNewSettingsBeforeRetry()
    {
        var settingsService = new FakeSettingsService();
        var autoStartService = new FakeAutoStartService(false) { FailWritesRemaining = 1 };
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initialAutoStart: false);
        var settings = new AppSettings { AutoStart = true, SoundEnabled = false };

        Assert.Throws<SettingsPersistenceException>(() => coordinator.Save(settings));
        settings.SoundEnabled = true;
        coordinator.Save(settings);

        Assert.Equal(2, settingsService.SaveCount);
        Assert.Equal(2, autoStartService.WriteCount);
        Assert.True(settingsService.Current.SoundEnabled);
        Assert.False(coordinator.HasPendingChanges);
    }

    [Fact]
    public void SettingsFileFailureRetryPersistsFileBeforeApplyingAutoStart()
    {
        var settingsService = new FakeSettingsService { FailSavesRemaining = 1 };
        var autoStartService = new FakeAutoStartService(false);
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            autoStartService,
            initialAutoStart: false);
        var settings = new AppSettings { AutoStart = true };

        Assert.Throws<SettingsPersistenceException>(() => coordinator.Save(settings));
        coordinator.RetryPending(settings);

        Assert.Equal(1, settingsService.SaveCount);
        Assert.Equal(1, autoStartService.WriteCount);
        Assert.True(autoStartService.IsAutoStartEnabled);
        Assert.False(coordinator.HasPendingChanges);
    }

    [Theory]
    [InlineData(0, "LF-ST-001", "settings-file")]
    [InlineData(1, "LF-ST-002", "autostart-registry")]
    public void DiagnosticMessageContainsOnlyStableMetadata(
        int stageValue,
        string expectedCode,
        string expectedStage)
    {
        const string privateSentinel = "PRIVATE_PATH_OR_REGISTRY_VALUE";
        var exception = new SettingsPersistenceException(
            (SettingsPersistenceStage)stageValue,
            new IOException(privateSentinel));

        Assert.Contains($"DiagnosticCode: {expectedCode}", exception.SafeLogMessage);
        Assert.Contains($"Stage: {expectedStage}", exception.SafeLogMessage);
        Assert.Contains("Action: settings-save", exception.SafeLogMessage);
        Assert.Contains("Outcome: failed", exception.SafeLogMessage);
        Assert.DoesNotContain(privateSentinel, exception.SafeLogMessage);
        Assert.DoesNotContain(exception.InnerException!.Message, exception.SafeLogMessage);
    }

    private sealed class FakeAutoStartService(bool initialValue) : IAutoStartService
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
                    throw new InvalidOperationException("Simulated registry failure.");
                }

                _isAutoStartEnabled = value;
            }
        }

        public int FailWritesRemaining { get; set; }
        public int WriteCount { get; private set; }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public int FailSavesRemaining { get; set; }
        public int SaveCount { get; private set; }
        public AppSettings Load() => Current;

        public void Save(AppSettings settings)
        {
            if (FailSavesRemaining > 0)
            {
                FailSavesRemaining--;
                throw new IOException("Simulated settings failure.");
            }

            Current = settings;
            SaveCount++;
        }
    }
}
