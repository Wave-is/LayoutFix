using System;
using System.Collections.Generic;
using System.IO;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class DictionaryAnalyzerTests
{
    private class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new AppSettings();
        public void Save(AppSettings settings) { }
        public AppSettings Load() => Current;
    }

    private class FakeLayoutManager : IKeyboardLayoutManager
    {
#pragma warning disable CS0067
        public event EventHandler? LayoutsChanged;
#pragma warning restore CS0067
        public IReadOnlyList<Layout> GetLayoutOrder()
        {
            var en = new Layout { Code = "en-US" };
            var ru = new Layout { Code = "ru-RU" };
            return new List<Layout> { en, ru };
        }
        public Layout? GetLayout(string code) => null;
        public string GetNextLayout(string currentCode) => "ru-RU";
        public void Initialize() { }
        public void SetLayoutOrder(IEnumerable<string> codes) { }
        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => new List<Layout>();
    }

    private class FakeLayoutConverter : ILayoutConverter
    {
        public string ConvertTo(string text, Layout targetLayout, Layout sourceLayout)
        {
            // Simple mock conversion for 'vfibyf' -> 'машина'
            if (text == "vfibyf" && targetLayout.Code.StartsWith("ru")) return "машина";
            if (text == "руддщ" && targetLayout.Code.StartsWith("en")) return "hello";
            return text;
        }
        public (string? ConvertedText, Layout? Source, Layout? Target) AutoConvert(string text, IReadOnlyList<Layout> activeLayouts, string? currentLayoutCode = null)
        {
            return (text, activeLayouts[0], activeLayouts[1]);
        }
    }

    private void PrepareFakeDictionaries()
    {
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries");
        Directory.CreateDirectory(dir);
        
        File.WriteAllLines(Path.Combine(dir, "en.txt"), new[] { "hello", "world", "test" });
        File.WriteAllLines(Path.Combine(dir, "ru.txt"), new[] { "машина", "тест", "привет" });
    }

    [Fact]
    public void IsGibberish_WhenWordIsValidInCurrentLayout_ReturnsFalse()
    {
        PrepareFakeDictionaries();
        var analyzer = new DictionaryAnalyzer(new FakeLayoutConverter(), new FakeLayoutManager(), new FakeSettingsService());

        bool result = analyzer.IsGibberish("hello", "en-US");
        Assert.False(result, "Golden rule: Valid word in current layout should NOT trigger conversion.");
    }

    [Fact]
    public void IsGibberish_WhenWordIsInvalidAndMatchesTarget_ReturnsTrue()
    {
        PrepareFakeDictionaries();
        var analyzer = new DictionaryAnalyzer(new FakeLayoutConverter(), new FakeLayoutManager(), new FakeSettingsService());

        bool result = analyzer.IsGibberish("vfibyf", "en-US");
        Assert.True(result, "Typing 'vfibyf' on EN layout should trigger conversion to 'машина'.");
    }

    [Fact]
    public void IsGibberish_WhenWordIsInUserExceptions_ReturnsFalse()
    {
        PrepareFakeDictionaries();
        var fakeSettings = new FakeSettingsService();
        fakeSettings.Current.UserExceptions.Add("vfibyf"); // User explicitly wants to keep "vfibyf"
        
        var analyzer = new DictionaryAnalyzer(new FakeLayoutConverter(), new FakeLayoutManager(), fakeSettings);

        bool result = analyzer.IsGibberish("vfibyf", "en-US");
        Assert.False(result, "Word in UserExceptions should never trigger auto-conversion.");
    }

    [Fact]
    public void IsGibberish_WhenWordIsShort_ReturnsFalse()
    {
        PrepareFakeDictionaries();
        var analyzer = new DictionaryAnalyzer(new FakeLayoutConverter(), new FakeLayoutManager(), new FakeSettingsService());

        bool result = analyzer.IsGibberish("v", "en-US");
        Assert.False(result, "1-character words should not trigger conversion.");
    }
}
