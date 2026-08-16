using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Hooks;

namespace LayoutFix.Tests;

public sealed class AppHostTests : IDisposable
{
    private readonly string _profileDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.AppHostTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Build_WithExplicitProfilePaths_IsolatesAllWritableServices()
    {
        var settingsPath = Path.Combine(_profileDirectory, "settings.json");
        var historyPath = Path.Combine(_profileDirectory, "translation_history.json");
        var logPath = Path.Combine(_profileDirectory, "layoutfix.log");

        AppHost.Build(settingsPath, historyPath, logPath);
        var services = AppHost.Services!;
        Assert.IsType<MouseHook>(services.GetService(typeof(IMouseHook)));
        var settings = Assert.IsAssignableFrom<ISettingsService>(
            services.GetService(typeof(ISettingsService)));
        settings.Current.LoggingEnabled = true;
        settings.Current.TranslationHistoryEnabled = true;
        Assert.IsAssignableFrom<ILoggerService>(services.GetService(typeof(ILoggerService)))
            .LogInfo("isolated-profile-probe");
        await Assert.IsAssignableFrom<ITranslationHistoryService>(
            services.GetService(typeof(ITranslationHistoryService))).AddEntryAsync(new()
        {
            SourceText = "source",
            TranslatedText = "target",
            SourceLang = "en",
            TargetLang = "ru"
        });

        Assert.True(File.Exists(settingsPath));
        Assert.True(File.Exists(historyPath));
        Assert.True(File.Exists(logPath));
        Assert.All(
            Directory.GetFiles(_profileDirectory, "*", SearchOption.AllDirectories),
            path => Assert.StartsWith(_profileDirectory, path, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        AppHost.Shutdown();
        if (Directory.Exists(_profileDirectory))
            Directory.Delete(_profileDirectory, recursive: true);
    }
}
