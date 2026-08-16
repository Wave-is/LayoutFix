using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public sealed class TranslationHistoryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.HistoryTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task DisabledHistory_DoesNotPersistUserText()
    {
        var path = Path.Combine(_directory, "history.json");
        var settings = new FakeSettingsService { Current = new AppSettings { TranslationHistoryEnabled = false } };
        var service = new TranslationHistoryService(settings, path);

        await service.AddEntryAsync(Entry("private text"));

        Assert.False(File.Exists(path));
        Assert.Empty(await service.GetHistoryAsync());
    }

    [Fact]
    public async Task ConcurrentHistory_IsBoundedAndCanBeDeleted()
    {
        var path = Path.Combine(_directory, "history.json");
        var settings = new FakeSettingsService { Current = new AppSettings { TranslationHistoryEnabled = true } };
        var service = new TranslationHistoryService(settings, path);

        await Task.WhenAll(Enumerable.Range(0, 80)
            .Select(index => service.AddEntryAsync(Entry($"source-{index}"))));

        Assert.Equal(50, (await service.GetHistoryAsync()).Count);
        Assert.True(File.Exists(path));

        await service.ClearHistoryAsync();

        Assert.Empty(await service.GetHistoryAsync());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ConcurrentAddsAndClears_KeepMemoryAndDurableSnapshotsAligned()
    {
        var path = Path.Combine(_directory, "concurrent-clear-history.json");
        var settings = new FakeSettingsService { Current = new AppSettings { TranslationHistoryEnabled = true } };
        var service = new TranslationHistoryService(settings, path);

        var operations = Enumerable.Range(0, 120).Select(index => index % 17 == 0
            ? service.ClearHistoryAsync()
            : service.AddEntryAsync(Entry($"source-{index:D3}")));
        await Task.WhenAll(operations);

        var inMemory = await service.GetHistoryAsync();
        var durable = await new TranslationHistoryService(settings, path).GetHistoryAsync();
        Assert.Equal(inMemory.Count, durable.Count);
        Assert.Equal(
            inMemory.Select(Identity),
            durable.Select(Identity));
        Assert.InRange(inMemory.Count, 0, 50);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private static TranslationHistoryEntry Entry(string text) => new()
    {
        Timestamp = DateTime.UtcNow,
        SourceText = text,
        TranslatedText = "translated",
        SourceLang = "en",
        TargetLang = "ru"
    };

    private static string Identity(TranslationHistoryEntry entry) =>
        $"{entry.Timestamp:O}|{entry.SourceText}|{entry.TranslatedText}|{entry.SourceLang}|{entry.TargetLang}";

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
    }
}
