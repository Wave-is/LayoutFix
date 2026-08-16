using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class DictionaryAnalyzerTests : IDisposable
{
    private readonly string _dictionaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LayoutFix.DictionaryTests.{Guid.NewGuid():N}");

    public DictionaryAnalyzerTests()
    {
        Directory.CreateDirectory(_dictionaryDirectory);
        File.WriteAllLines(Path.Combine(_dictionaryDirectory, "en.txt"), ["hello", "world", "test"]);
        File.WriteAllLines(Path.Combine(_dictionaryDirectory, "ru.txt"), ["машина", "тест", "привет"]);
    }

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
            var en = new Layout
            {
                Code = "en-US",
                Keys = new Dictionary<string, string>
                {
                    ["v"] = "v", ["f"] = "f", ["i"] = "i",
                    ["b"] = "b", ["y"] = "y", ["k"] = "k",
                    ["j"] = "j", ["t"] = "t", ["n"] = "n",
                    ["h"] = "h", ["s"] = "s"
                }
            };
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
            if (text == "ykj" && targetLayout.Code.StartsWith("ru")) return "нло";
            if (text == "ytn" && targetLayout.Code.StartsWith("ru")) return "нет";
            if (text == "hfhht" && targetLayout.Code.StartsWith("ru")) return "слово";
            if (text == "hfhhs" && targetLayout.Code.StartsWith("ru")) return "слова";
            if (text == "vivi" && targetLayout.Code.StartsWith("ru")) return "1";
            if (text == "viviv" && targetLayout.Code.StartsWith("ru")) return "слово";
            if (text == "vifif" && targetLayout.Code.StartsWith("ru")) return "слово1";
            if (text == "hfhsh" && targetLayout.Code.StartsWith("ru")) return "слово.точка";
            if (text == "fifibf" && targetLayout.Code.StartsWith("ru")) return "ма\u0301шина";
            if (text == "hfhth" && targetLayout.Code.StartsWith("ru")) return "слово-слово";
            if (text == "руддщ" && targetLayout.Code.StartsWith("en")) return "hello";
            return text;
        }
        public (string? ConvertedText, Layout? Source, Layout? Target) AutoConvert(string text, IReadOnlyList<Layout> activeLayouts, string? currentLayoutCode = null)
        {
            return (text, activeLayouts[0], activeLayouts[1]);
        }
    }

    [Fact]
    public void IsGibberish_WhenWordIsValidInCurrentLayout_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer();

        bool result = analyzer.IsGibberish("hello", "en-US");
        Assert.False(result, "Golden rule: Valid word in current layout should NOT trigger conversion.");
    }

    [Fact]
    public void IsGibberish_WhenWordIsInvalidAndMatchesTarget_ReturnsTrue()
    {
        var analyzer = CreateAnalyzer();

        bool result = analyzer.IsGibberish("vfibyf", "en-US");
        Assert.True(result, "Typing 'vfibyf' on EN layout should trigger conversion to 'машина'.");
        Assert.True(analyzer.TryGetCorrection("vfibyf", "en-US", out var suggestion));
        Assert.Equal("машина", suggestion.Replacement);
        Assert.Equal("ru-RU", suggestion.TargetLayoutCode);
    }

    [Fact]
    public void CommaSeparatedDictionary_LoadsEverySingleWordEntry()
    {
        File.WriteAllText(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            "тест,машина,два слова;привет");
        var analyzer = CreateAnalyzer();

        Assert.True(analyzer.TryGetCorrection("vfibyf", "en-US", out var suggestion));
        Assert.Equal("машина", suggestion.Replacement);
    }

    [Fact]
    public void ShortTargetFrequency_DistinguishesAutomaticConfidenceFromManualValidity()
    {
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            ["нло 499", "нет 1049935"]);
        var analyzer = CreateAnalyzer();

        Assert.True(analyzer.TryGetCorrection("ykj", "en-US", out var rare));
        Assert.Equal("нло", rare.Replacement);
        Assert.False(rare.IsConfidentForAutomaticCorrection);

        Assert.True(analyzer.TryGetCorrection("ytn", "en-US", out var common));
        Assert.Equal("нет", common.Replacement);
        Assert.True(common.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void FiveLetterTarget_RequiresFrequencyEvidenceForAutomaticCorrection()
    {
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            ["слово 42", "слова 5000"]);
        var analyzer = CreateAnalyzer();

        Assert.True(analyzer.TryGetCorrection("hfhht", "en-US", out var rare));
        Assert.Equal("слово", rare.Replacement);
        Assert.False(rare.IsConfidentForAutomaticCorrection);

        Assert.True(analyzer.TryGetCorrection("hfhhs", "en-US", out var common));
        Assert.Equal("слова", common.Replacement);
        Assert.True(common.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void ShortTargetRank_UsesOrderedListWhenFrequencyIsUnavailable()
    {
        var entries = new List<string> { "нет" };
        entries.AddRange(Enumerable.Range(0, 5_000).Select(CreateLexicalDictionaryWord));
        entries.Add("нло");
        File.WriteAllLines(Path.Combine(_dictionaryDirectory, "ru.txt"), entries);
        var analyzer = CreateAnalyzer();

        Assert.True(analyzer.TryGetCorrection("ytn", "en-US", out var common));
        Assert.True(common.IsConfidentForAutomaticCorrection);

        Assert.True(analyzer.TryGetCorrection("ykj", "en-US", out var rare));
        Assert.False(rare.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void CompactUnrankedDictionary_RequiresManualChoiceForShortTargets()
    {
        File.WriteAllText(Path.Combine(_dictionaryDirectory, "ru.txt"), "нет,нло");
        var analyzer = CreateAnalyzer();

        Assert.True(analyzer.TryGetCorrection("ytn", "en-US", out var common));
        Assert.False(common.IsConfidentForAutomaticCorrection);

        Assert.True(analyzer.TryGetCorrection("ykj", "en-US", out var rare));
        Assert.False(rare.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void MalformedCommaFrequencyRow_DoesNotLoadNumericFragment()
    {
        File.WriteAllText(Path.Combine(_dictionaryDirectory, "ru.txt"), "1,80m 151");
        var analyzer = CreateAnalyzer();

        Assert.False(analyzer.TryGetCorrection("vivi", "en-US", out _));
    }

    [Fact]
    public void FrequencyRowWithSeparator_DoesNotLoadLexicalFragment()
    {
        File.WriteAllText(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            "слово,другое 5000");
        var analyzer = CreateAnalyzer();

        Assert.False(analyzer.TryGetCorrection("viviv", "en-US", out _));
    }

    [Fact]
    public void NonLexicalDictionaryEntries_AreIgnored()
    {
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            ["слово1 5000", "слово.точка 5000"]);
        var analyzer = CreateAnalyzer();

        Assert.False(analyzer.TryGetCorrection("vifif", "en-US", out _));
        Assert.False(analyzer.TryGetCorrection("hfhsh", "en-US", out _));
    }

    [Fact]
    public void UnicodeMarksAndWordJoiners_RemainValidDictionaryEntries()
    {
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            ["ма\u0301шина 5000", "слово-слово 5000"]);
        var analyzer = CreateAnalyzer();

        Assert.True(analyzer.TryGetCorrection("fifibf", "en-US", out var marked));
        Assert.Equal("ма\u0301шина", marked.Replacement);
        Assert.True(analyzer.TryGetCorrection("hfhth", "en-US", out var joined));
        Assert.Equal("слово-слово", joined.Replacement);
    }

    [Fact]
    public void TryGetCorrection_WhenTwoTargetLayoutsAreDictionaryValid_RejectsAmbiguousChange()
    {
        File.WriteAllLines(Path.Combine(_dictionaryDirectory, "uk.txt"), ["машина"]);
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new AmbiguousLayoutConverter(),
            new ThreeLayoutManager(),
            new FakeSettingsService(),
            _dictionaryDirectory);

        Assert.False(analyzer.TryGetCorrection("vfibyf", "en-US", out _));
    }

    [Fact]
    public void IsGibberish_WhenWordIsInUserExceptions_ReturnsFalse()
    {
        var fakeSettings = new FakeSettingsService();
        fakeSettings.Current.UserExceptions.Add("vfibyf"); // User explicitly wants to keep "vfibyf"
        
        var analyzer = CreateAnalyzer(fakeSettings);

        bool result = analyzer.IsGibberish("vfibyf", "en-US");
        Assert.False(result, "Word in UserExceptions should never trigger auto-conversion.");
    }

    [Fact]
    public void IsGibberish_WhenWordIsShort_ReturnsFalse()
    {
        var analyzer = CreateAnalyzer();

        bool result = analyzer.IsGibberish("v", "en-US");
        Assert.False(result, "1-character words should not trigger conversion.");
    }

    [Fact]
    public void ProductionDictionaries_RecognizeCommonWrongLayoutWord()
    {
        var layouts = new ProductionLayoutManager();
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new LayoutConverter(),
            layouts,
            new FakeSettingsService(),
            Path.Combine(AppContext.BaseDirectory, "ProductionDictionaries"));

        Assert.True(analyzer.IsGibberish("ghbdtn", "en-US"));
        Assert.False(analyzer.IsGibberish("hello", "en-US"));
    }

    [Fact]
    public void WarmUp_LoadsConfiguredDictionariesAndReleasesTheirFiles()
    {
        var analyzer = CreateAnalyzer();

        analyzer.WarmUp();
        File.Delete(Path.Combine(_dictionaryDirectory, "en.txt"));
        File.Delete(Path.Combine(_dictionaryDirectory, "ru.txt"));

        Assert.True(analyzer.TryGetCorrection("vfibyf", "en-US", out var suggestion));
        Assert.Equal("машина", suggestion.Replacement);
    }

    [Fact]
    public async Task TransientDictionaryReadFailure_RetriesWithoutRestartAfterBackoff()
    {
        var clock = new ManualTimeProvider();
        var retryDelay = TimeSpan.FromSeconds(5);
        var russianDictionary = Path.Combine(_dictionaryDirectory, "ru.txt");
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new FakeLayoutConverter(),
            new FakeLayoutManager(),
            new FakeSettingsService(),
            _dictionaryDirectory,
            clock,
            retryDelay);

        using (File.Open(
            russianDictionary,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            analyzer.WarmUp();
            Assert.False(analyzer.TryGetCorrection("vfibyf", "en-US", out _));
        }

        Assert.False(analyzer.TryGetCorrection("vfibyf", "en-US", out _));
        clock.Advance(retryDelay);
        var recovered = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            analyzer.TryGetCorrection("vfibyf", "en-US", out var suggestion) &&
            suggestion.Replacement == "машина")));
        Assert.All(recovered, Assert.True);
    }

    [Fact]
    public void MissingDictionary_RemainsPermanentNegativeCache()
    {
        var missingDirectory = Path.Combine(
            _dictionaryDirectory,
            "permanently-missing");
        Directory.CreateDirectory(missingDirectory);
        File.WriteAllLines(Path.Combine(missingDirectory, "en.txt"), ["hello"]);
        var clock = new ManualTimeProvider();
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new FakeLayoutConverter(),
            new FakeLayoutManager(),
            new FakeSettingsService(),
            missingDirectory,
            clock,
            TimeSpan.FromSeconds(1));

        Assert.False(analyzer.TryGetCorrection("vfibyf", "en-US", out _));
        File.WriteAllLines(Path.Combine(missingDirectory, "ru.txt"), ["машина"]);
        clock.Advance(TimeSpan.FromDays(1));

        Assert.False(analyzer.TryGetCorrection("vfibyf", "en-US", out _));
    }

    [Fact]
    public void ProductionTechnicalCorpus_RemainsManualOnlyWhenItCollidesWithTargetWords()
    {
        var layouts = new ProductionLayoutManager();
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new LayoutConverter(),
            layouts,
            new FakeSettingsService(),
            Path.Combine(AppContext.BaseDirectory, "ProductionDictionaries"));
        var technicalTokens = File.ReadAllLines(Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "technical-tokens.txt"));
        Assert.Equal(269, technicalTokens.Length);
        Assert.Equal(
            technicalTokens.Length,
            technicalTokens.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var token in technicalTokens)
        {
            if (analyzer.TryGetCorrection(token, "en-US", out var suggestion))
            {
                Assert.False(
                    suggestion.IsConfidentForAutomaticCorrection,
                    $"Technical token '{token}' must remain manual-only, but suggested " +
                    $"'{suggestion.Replacement}' with automatic confidence.");
            }
        }

        Assert.True(analyzer.TryGetCorrection("tls", "en-US", out var tls));
        Assert.Equal("еды", tls.Replacement);
        Assert.False(tls.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void EntireTechnicalCorpus_WithHighFrequencyCollisions_RemainsManualOnly()
    {
        var technicalTokens = File.ReadAllLines(Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "technical-tokens.txt"));
        Assert.All(
            technicalTokens,
            token => Assert.True(
                AutomaticCorrectionTokenPolicy.IsProtected(token),
                $"Technical token '{token}' is absent from the runtime policy."));
        var layoutCompatibleTokens = technicalTokens
            .Where(token => token.All(char.IsAsciiLetter))
            .ToArray();
        Assert.Equal(267, layoutCompatibleTokens.Length);
        var layouts = new ProductionLayoutManager();
        var orderedLayouts = layouts.GetLayoutOrder();
        var converter = new LayoutConverter();
        var collisions = layoutCompatibleTokens.ToDictionary(
            token => token,
            token => converter.ConvertTo(token, orderedLayouts[1], orderedLayouts[0]),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            layoutCompatibleTokens.Length,
            collisions.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "en.txt"),
            ["hello 10000"]);
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            collisions.Values.Select(collision => $"{collision} 5000"));
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            converter,
            layouts,
            new FakeSettingsService(),
            _dictionaryDirectory);

        foreach (var token in layoutCompatibleTokens)
        {
            Assert.True(
                analyzer.TryGetCorrection(token, "en-US", out var suggestion),
                $"Synthetic collision for technical token '{token}' was not found.");
            Assert.Equal(collisions[token], suggestion.Replacement);
            Assert.False(
                suggestion.IsConfidentForAutomaticCorrection,
                $"Technical token '{token}' escaped the runtime manual-only policy.");
        }
    }

    [Theory]
    [InlineData("ofc", "щас", "ru-RU")]
    [InlineData("boe", "ищу", "ru-RU")]
    [InlineData("ult", "где", "ru-RU")]
    [InlineData("dsl", "від", "uk-UA")]
    [InlineData("csv", "сім", "uk-UA")]
    [InlineData("ltv", "дем", "ru-RU")]
    public void FrequentSourceCorpusOmission_RemainsManualOnly(
        string token,
        string replacement,
        string targetLayoutCode)
    {
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new LayoutConverter(),
            new ProductionThreeLayoutManager(),
            new FakeSettingsService(),
            Path.Combine(AppContext.BaseDirectory, "ProductionDictionaries"));

        Assert.True(analyzer.TryGetCorrection(token, "en-US", out var suggestion));
        Assert.Equal(replacement, suggestion.Replacement);
        Assert.Equal(targetLayoutCode, suggestion.TargetLayoutCode);
        Assert.False(suggestion.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void FrequentSourceCorpus_IsCompleteAndProtected()
    {
        var tokens = File.ReadAllLines(Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "frequent-source-tokens.txt"));

        Assert.Equal(122, tokens.Length);
        Assert.Equal(
            tokens.Length,
            tokens.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(tokens, token => Assert.True(
            AutomaticCorrectionTokenPolicy.IsProtected(token),
            $"Frequent source token '{token}' is absent from the runtime policy."));
    }

    [Fact]
    public void FrequentSourceCorpus_ProductionCollisionsRemainManualOnly()
    {
        var tokens = File.ReadAllLines(Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "frequent-source-tokens.txt"));
        var dictionaryDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "ProductionDictionaries");
        var analyzers = new Dictionary<string, DictionaryAnalyzer>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = DictionaryAnalyzer.CreateForDirectory(
                new LayoutConverter(),
                new ProductionThreeLayoutManager(),
                new FakeSettingsService(),
                dictionaryDirectory),
            ["ru-RU"] = DictionaryAnalyzer.CreateForDirectory(
                new LayoutConverter(),
                new ProductionSourcePairLayoutManager("ru-RU"),
                new FakeSettingsService(),
                dictionaryDirectory),
            ["uk-UA"] = DictionaryAnalyzer.CreateForDirectory(
                new LayoutConverter(),
                new ProductionSourcePairLayoutManager("uk-UA"),
                new FakeSettingsService(),
                dictionaryDirectory)
        };

        foreach (var token in tokens)
        {
            var sourceLayoutCodes = token.All(char.IsAsciiLetter)
                ? new[] { "en-US" }
                : new[] { "ru-RU", "uk-UA" };
            var foundProductionCollision = false;

            foreach (var sourceLayoutCode in sourceLayoutCodes)
            {
                if (!analyzers[sourceLayoutCode].TryGetCorrection(
                        token,
                        sourceLayoutCode,
                        out var suggestion))
                {
                    continue;
                }

                foundProductionCollision = true;
                Assert.False(
                    suggestion.IsConfidentForAutomaticCorrection,
                    $"Frequent source token '{token}' escaped the runtime " +
                    $"manual-only policy for {sourceLayoutCode}.");
            }

            Assert.True(
                foundProductionCollision,
                $"Frequent source token '{token}' no longer collides with the " +
                "production dictionaries and should be re-audited.");
        }
    }

    [Theory]
    [InlineData("уфе", "ru-RU", "eat")]
    [InlineData("кун", "ru-RU", "rey")]
    [InlineData("руд", "ru-RU", "hel")]
    [InlineData("афк", "ru-RU", "far")]
    [InlineData("пфр", "ru-RU", "gah")]
    [InlineData("пгт", "ru-RU", "gun")]
    [InlineData("кум", "ru-RU", "rev")]
    [InlineData("пут", "ru-RU", "gen")]
    [InlineData("нур", "ru-RU", "yeh")]
    [InlineData("вгу", "ru-RU", "due")]
    [InlineData("тис", "uk-UA", "nbc")]
    [InlineData("кну", "uk-UA", "rye")]
    [InlineData("нгу", "uk-UA", "yue")]
    [InlineData("уку", "uk-UA", "ere")]
    [InlineData("дів", "uk-UA", "lsd")]
    public void FrequentCyrillicSourceToken_RemainsManualOnly(
        string token,
        string sourceLayoutCode,
        string replacement)
    {
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new LayoutConverter(),
            new ProductionSourcePairLayoutManager(sourceLayoutCode),
            new FakeSettingsService(),
            Path.Combine(AppContext.BaseDirectory, "ProductionDictionaries"));

        Assert.True(analyzer.TryGetCorrection(token, sourceLayoutCode, out var suggestion));
        Assert.Equal(replacement, suggestion.Replacement);
        Assert.Equal("en-US", suggestion.TargetLayoutCode);
        Assert.False(suggestion.IsConfidentForAutomaticCorrection);
    }

    [Theory]
    [InlineData("qwen")]
    [InlineData("gguf")]
    [InlineData("onnx")]
    [InlineData("cuda")]
    [InlineData("vulkan")]
    [InlineData("webgpu")]
    [InlineData("wasm")]
    [InlineData("protobuf")]
    [InlineData("openapi")]
    [InlineData("graphql")]
    [InlineData("websocket")]
    [InlineData("webrtc")]
    [InlineData("oidc")]
    [InlineData("saml")]
    [InlineData("webauthn")]
    [InlineData("pytorch")]
    [InlineData("tensorflow")]
    [InlineData("opentelemetry")]
    public void ModernTechnicalToken_WithHighFrequencyCollision_RemainsManualOnly(
        string token)
    {
        var layouts = new ProductionLayoutManager();
        var orderedLayouts = layouts.GetLayoutOrder();
        var converter = new LayoutConverter();
        var collision = converter.ConvertTo(
            token,
            orderedLayouts[1],
            orderedLayouts[0]);
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "en.txt"),
            ["hello 10000"]);
        File.WriteAllLines(
            Path.Combine(_dictionaryDirectory, "ru.txt"),
            [$"{collision} 5000"]);
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            converter,
            layouts,
            new FakeSettingsService(),
            _dictionaryDirectory);

        Assert.True(analyzer.TryGetCorrection(token, "en-US", out var suggestion));
        Assert.Equal(collision, suggestion.Replacement);
        Assert.False(suggestion.IsConfidentForAutomaticCorrection);
    }

    [Fact]
    public void ProductionThreeLanguageDictionaries_ResolveOnlyStrongSharedWordEvidence()
    {
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new LayoutConverter(),
            new ProductionThreeLayoutManager(),
            new FakeSettingsService(),
            Path.Combine(AppContext.BaseDirectory, "ProductionDictionaries"));

        Assert.True(analyzer.TryGetCorrection("ytn", "en-US", out var russian));
        Assert.Equal("нет", russian.Replacement);
        Assert.Equal("ru-RU", russian.TargetLayoutCode);
        Assert.True(russian.IsConfidentForAutomaticCorrection);

        Assert.True(analyzer.TryGetCorrection("fkt", "en-US", out var ukrainian));
        Assert.Equal("але", ukrainian.Replacement);
        Assert.Equal("uk-UA", ukrainian.TargetLayoutCode);
        Assert.True(ukrainian.IsConfidentForAutomaticCorrection);

        Assert.False(analyzer.TryGetCorrection("nfr", "en-US", out _));
    }

    [Fact]
    public void MixedAlphabetWord_DoesNotBypassSourceLayoutSafety()
    {
        var layouts = new ProductionLayoutManager();
        var analyzer = DictionaryAnalyzer.CreateForDirectory(
            new LayoutConverter(),
            layouts,
            new FakeSettingsService(),
            Path.Combine(AppContext.BaseDirectory, "ProductionDictionaries"));

        Assert.False(analyzer.TryGetCorrection("ghbdtт", "en-US", out _));
    }

    [Fact]
    public async Task ConcurrentManualAndAutomaticRequests_ShareThreadSafeLazyDictionaries()
    {
        var analyzer = CreateAnalyzer();
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, 24).Select(async workerIndex =>
        {
            await start.Task;
            for (var iteration = 0; iteration < 100; iteration++)
            {
                if ((workerIndex + iteration) % 2 == 0)
                {
                    Assert.True(analyzer.TryGetCorrection(
                        "vfibyf",
                        "en-US",
                        out var suggestion));
                    Assert.Equal("машина", suggestion.Replacement);
                }
                else
                {
                    Assert.False(analyzer.TryGetCorrection(
                        "hello",
                        "en-US",
                        out _));
                }
            }
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(workers);
    }

    private DictionaryAnalyzer CreateAnalyzer(FakeSettingsService? settings = null) => DictionaryAnalyzer.CreateForDirectory(
        new FakeLayoutConverter(),
        new FakeLayoutManager(),
        settings ?? new FakeSettingsService(),
        _dictionaryDirectory);

    private static Dictionary<string, string> CreateProductionKeys(string output)
    {
        const string physicalKeys = "qwertyuiopasdfghjklzxcvbnm";
        return physicalKeys
            .Select((key, index) => new KeyValuePair<string, string>(
                key.ToString(),
                output[index].ToString()))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static string CreateLexicalDictionaryWord(int index) =>
        $"слово{(char)('а' + index % 32)}{(char)('а' + index / 32 % 32)}{(char)('а' + index / (32 * 32) % 32)}";

    public void Dispose()
    {
        try { Directory.Delete(_dictionaryDirectory, recursive: true); } catch { }
    }

    private sealed class ProductionLayoutManager : IKeyboardLayoutManager
    {
        private static readonly IReadOnlyList<Layout> Layouts =
        [
            new Layout
            {
                Code = "en-US",
                Keys = CreateProductionKeys("qwertyuiopasdfghjklzxcvbnm")
            },
            new Layout
            {
                Code = "ru-RU",
                Keys = CreateProductionKeys("йцукенгшщзфывапролдячсмить")
            }
        ];

        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => Layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => Layouts;
    }

    private sealed class ProductionThreeLayoutManager : IKeyboardLayoutManager
    {
        private static readonly IReadOnlyList<Layout> Layouts =
        [
            new()
            {
                Code = "en-US",
                Keys = CreateProductionKeys("qwertyuiopasdfghjklzxcvbnm")
            },
            new()
            {
                Code = "ru-RU",
                Keys = CreateProductionKeys("йцукенгшщзфывапролдячсмить")
            },
            new()
            {
                Code = "uk-UA",
                Keys = CreateProductionKeys("йцукенгшщзфівапролдячсмить")
            }
        ];

        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => Layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => Layouts;
    }

    private sealed class ProductionSourcePairLayoutManager : IKeyboardLayoutManager
    {
        private readonly IReadOnlyList<Layout> _layouts;

        public ProductionSourcePairLayoutManager(string sourceLayoutCode)
        {
            var sourceOutput = sourceLayoutCode switch
            {
                "ru-RU" => "йцукенгшщзфывапролдячсмить",
                "uk-UA" => "йцукенгшщзфівапролдячсмить",
                _ => throw new ArgumentOutOfRangeException(nameof(sourceLayoutCode))
            };
            _layouts =
            [
                new Layout
                {
                    Code = "en-US",
                    Keys = CreateProductionKeys("qwertyuiopasdfghjklzxcvbnm")
                },
                new Layout
                {
                    Code = sourceLayoutCode,
                    Keys = CreateProductionKeys(sourceOutput)
                }
            ];
        }

        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => _layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => _layouts;
    }

    private sealed class ThreeLayoutManager : IKeyboardLayoutManager
    {
        private static readonly IReadOnlyList<Layout> Layouts =
        [
            new() { Code = "en-US" },
            new() { Code = "ru-RU" },
            new() { Code = "uk-UA" }
        ];

        public void LoadAll() { }
        public IReadOnlyList<Layout> GetInstalledWindowsLayouts() => Layouts;
        public IReadOnlyList<Layout> GetLayoutOrder() => Layouts;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse(
            "2026-08-13T00:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
    }

    private sealed class AmbiguousLayoutConverter : ILayoutConverter
    {
        public string ConvertTo(string text, Layout target, Layout source) =>
            target.Code.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
            target.Code.StartsWith("uk", StringComparison.OrdinalIgnoreCase)
                ? "машина"
                : text;

        public (string? ConvertedText, Layout? Source, Layout? Target) AutoConvert(
            string text,
            IReadOnlyList<Layout> activeLayouts,
            string? currentLayoutCode = null) => (null, null, null);
    }
}
