using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Services;

namespace LayoutFix.Tests;

public class AutoStartSynchronizerTests
{
    [Fact]
    public void SavedOptInRepairsMissingOrStaleRegistrationWithoutLosingIntent()
    {
        var settings = new FakeSettingsService { Current = new AppSettings { AutoStart = true } };
        var autoStart = new FakeAutoStartService(false);

        AutoStartSynchronizer.Synchronize(settings, autoStart);

        Assert.True(settings.Current.AutoStart);
        Assert.True(autoStart.IsAutoStartEnabled);
        Assert.Equal(1, autoStart.WriteCount);
        Assert.Equal(0, settings.SaveCount);
    }

    [Fact]
    public void FreshProfileAdoptsInstallerOptInRegistration()
    {
        var settings = new FakeSettingsService { Current = new AppSettings { AutoStart = false } };
        var autoStart = new FakeAutoStartService(true);

        AutoStartSynchronizer.Synchronize(settings, autoStart);

        Assert.True(settings.Current.AutoStart);
        Assert.Equal(1, settings.SaveCount);
        Assert.True(autoStart.IsAutoStartEnabled);
        Assert.Equal(0, autoStart.WriteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingStateDoesNotWriteRegistryOrSettings(bool enabled)
    {
        var settings = new FakeSettingsService { Current = new AppSettings { AutoStart = enabled } };
        var autoStart = new FakeAutoStartService(enabled);

        AutoStartSynchronizer.Synchronize(settings, autoStart);

        Assert.Equal(0, autoStart.WriteCount);
        Assert.Equal(0, settings.SaveCount);
    }

    [Fact]
    public void FailedRegistrationRepairDoesNotOverwriteSavedOptIn()
    {
        var settings = new FakeSettingsService { Current = new AppSettings { AutoStart = true } };
        var autoStart = new FakeAutoStartService(false) { ThrowOnWrite = true };

        Assert.Throws<InvalidOperationException>(() =>
            AutoStartSynchronizer.Synchronize(settings, autoStart));

        Assert.True(settings.Current.AutoStart);
        Assert.Equal(1, autoStart.WriteCount);
        Assert.Equal(0, settings.SaveCount);
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
                if (ThrowOnWrite)
                    throw new InvalidOperationException("Simulated registry failure.");

                _isAutoStartEnabled = value;
            }
        }

        public bool ThrowOnWrite { get; init; }
        public int WriteCount { get; private set; }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public int SaveCount { get; private set; }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings)
        {
            Current = settings;
            SaveCount++;
        }
    }
}
