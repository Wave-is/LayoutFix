using System.Text.Json;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public sealed class TranslationHistoryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.TranslationHistoryTests.{Guid.NewGuid():N}");
    private readonly string _historyPath;

    public TranslationHistoryServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _historyPath = Path.Combine(_directory, "translation_history.json");
    }

    [Fact]
    public async Task GetHistoryAsync_TransientReadFailure_DoesNotPublishEmptyCache()
    {
        WriteHistory(CreateEntry("existing", "существующий"));
        var service = CreateService();

        await using (var locked = new FileStream(
            _historyPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => service.GetHistoryAsync());
        }

        var recovered = await service.GetHistoryAsync();

        var entry = Assert.Single(recovered);
        Assert.Equal("existing", entry.SourceText);
    }

    [Fact]
    public async Task AddEntryAsync_FailedSave_RollsBackCacheAndRetriesDurably()
    {
        WriteHistory(CreateEntry("existing", "существующий"));
        var service = CreateService();
        Assert.Single(await service.GetHistoryAsync());
        var added = CreateEntry("new", "новый");

        await using (var locked = new FileStream(
            _historyPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => service.AddEntryAsync(added));
            Assert.True(
                failure is IOException or UnauthorizedAccessException,
                $"Expected a file access failure, got {failure?.GetType().FullName ?? "no exception"}.");
        }

        await service.AddEntryAsync(added);

        var persisted = await CreateService().GetHistoryAsync();
        Assert.Equal(["new", "existing"], persisted.Select(entry => entry.SourceText));
    }

    [Fact]
    public async Task AddEntryAsync_MalformedHistory_DoesNotOverwriteUserData()
    {
        const string malformedJson = "[{\"SourceText\":\"keep me\"";
        await File.WriteAllTextAsync(_historyPath, malformedJson);
        var service = CreateService();

        await Assert.ThrowsAsync<JsonException>(() =>
            service.AddEntryAsync(CreateEntry("new", "новый")));

        Assert.Equal(malformedJson, await File.ReadAllTextAsync(_historyPath));
    }

    [Fact]
    public async Task ClearHistoryAsync_FailedDelete_DoesNotPublishEmptyCache()
    {
        WriteHistory(CreateEntry("existing", "существующий"));
        var service = CreateService();
        Assert.Single(await service.GetHistoryAsync());

        await using (var locked = new FileStream(
            _historyPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => service.ClearHistoryAsync());
            Assert.True(
                failure is IOException or UnauthorizedAccessException,
                $"Expected a file access failure, got {failure?.GetType().FullName ?? "no exception"}.");
        }

        var recovered = await service.GetHistoryAsync();
        Assert.Equal("existing", Assert.Single(recovered).SourceText);
        Assert.True(File.Exists(_historyPath));
    }

    [Fact]
    public async Task AddEntryAsync_SameSourceAndTargetWithDifferentTranslation_IsNotDropped()
    {
        var service = CreateService();
        await service.AddEntryAsync(CreateEntry("bank", "банк"));

        await service.AddEntryAsync(CreateEntry("bank", "берег"));

        var persisted = await CreateService().GetHistoryAsync();
        Assert.Equal(["берег", "банк"], persisted.Select(entry => entry.TranslatedText));
    }

    [Fact]
    public async Task AddEntryAsync_ExactConsecutiveDuplicate_IsStillSuppressed()
    {
        var service = CreateService();
        await service.AddEntryAsync(CreateEntry("hello", "привет"));

        await service.AddEntryAsync(CreateEntry("hello", "привет"));

        Assert.Single(await CreateService().GetHistoryAsync());
    }

    [Fact]
    public async Task EntryAndReturnedHistoryAreSnapshotsOfDurableData()
    {
        var service = CreateService();
        var submitted = CreateEntry("original source", "исходный перевод");
        await service.AddEntryAsync(submitted);

        submitted.SourceText = "mutated submitted source";
        submitted.TranslatedText = "изменённый submitted перевод";
        var returned = await service.GetHistoryAsync();
        returned[0].SourceText = "mutated returned source";
        returned[0].TranslatedText = "изменённый returned перевод";

        var inMemory = Assert.Single(await service.GetHistoryAsync());
        var durable = Assert.Single(await CreateService().GetHistoryAsync());
        Assert.Equal("original source", inMemory.SourceText);
        Assert.Equal("исходный перевод", inMemory.TranslatedText);
        Assert.Equal(inMemory.SourceText, durable.SourceText);
        Assert.Equal(inMemory.TranslatedText, durable.TranslatedText);
    }

    private TranslationHistoryService CreateService() =>
        new(new FakeSettingsService(), _historyPath);

    private void WriteHistory(params TranslationHistoryEntry[] entries) =>
        File.WriteAllText(_historyPath, JsonSerializer.Serialize(entries));

    private static TranslationHistoryEntry CreateEntry(string source, string translated) => new()
    {
        Timestamp = new DateTime(2026, 8, 13, 7, 0, 0, DateTimeKind.Utc),
        SourceText = source,
        TranslatedText = translated,
        SourceLang = "en",
        TargetLang = "ru"
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new()
        {
            TranslationHistoryEnabled = true
        };

        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
