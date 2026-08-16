using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public class KeyboardLayoutManagerTests
{
    private static readonly Layout EnglishPrimary = new()
    {
        Code = "en-US",
        Identifier = "en-US@04090409",
        DisplayName = "English (US)"
    };

    private static readonly Layout EnglishAlternative = new()
    {
        Code = "en-US",
        Identifier = "en-US@00010409",
        DisplayName = "English (Dvorak)"
    };

    private static readonly Layout Russian = new()
    {
        Code = "ru-RU",
        Identifier = "ru-RU@04190419",
        DisplayName = "Russian"
    };

    private static readonly Layout German = new()
    {
        Code = "de-DE",
        Identifier = "de-DE@04070407",
        DisplayName = "German (Germany)"
    };

    [Fact]
    public void LoadAll_PreservesDistinctVariantsAndDropsExactDuplicates()
    {
        var manager = CreateManager(
            [EnglishPrimary, EnglishAlternative, EnglishAlternative, Russian],
            ["en-US", "ru-RU"]);

        var installed = manager.GetInstalledWindowsLayouts();

        Assert.Equal(3, installed.Count);
        Assert.Equal(
            ["en-US@04090409", "en-US@00010409", "ru-RU@04190419"],
            installed.Select(layout => layout.EffectiveIdentifier));
    }

    [Fact]
    public void LegacyLayoutOrder_SelectsFirstInstalledVariantWithoutChangingSettings()
    {
        var settings = new AppSettings { LayoutOrder = ["en-US", "ru-RU"] };
        var manager = CreateManager(
            [EnglishPrimary, EnglishAlternative, Russian],
            settings);

        var ordered = manager.GetLayoutOrder();

        Assert.Equal(
            ["en-US@04090409", "ru-RU@04190419"],
            ordered.Select(layout => layout.EffectiveIdentifier));
        Assert.Equal(["en-US", "ru-RU"], settings.LayoutOrder);
    }

    [Fact]
    public void ExactLayoutOrder_SelectsRequestedVariant()
    {
        var manager = CreateManager(
            [EnglishPrimary, EnglishAlternative, Russian],
            ["en-US@00010409", "ru-RU"]);

        var ordered = manager.GetLayoutOrder();

        Assert.Equal(
            ["en-US@00010409", "ru-RU@04190419"],
            ordered.Select(layout => layout.EffectiveIdentifier));
    }

    [Fact]
    public void ActiveVariant_ReplacesLegacyVariantOnlyForCurrentOperation()
    {
        var settings = new AppSettings { LayoutOrder = ["en-US", "ru-RU"] };
        IKeyboardLayoutManager manager = CreateManager(
            [EnglishPrimary, EnglishAlternative, Russian],
            settings);

        var ordered = manager.GetLayoutOrder("en-US@00010409");

        Assert.Equal(
            ["en-US@00010409", "ru-RU@04190419"],
            ordered.Select(layout => layout.EffectiveIdentifier));
        Assert.Equal("en-US@04090409", manager.GetLayoutOrder()[0].EffectiveIdentifier);
        Assert.Equal(["en-US", "ru-RU"], settings.LayoutOrder);
    }

    [Fact]
    public void KeyboardLayoutIdentity_RoundTripsNativeHandleAndCulture()
    {
        var identifier = KeyboardLayoutIdentity.Create("en-US", 0x00010409);

        Assert.Equal("en-US@00010409", identifier);
        Assert.Equal("en-US", KeyboardLayoutIdentity.GetCultureCode(identifier));
        Assert.Equal("en", KeyboardLayoutIdentity.GetLanguageCode(identifier));
        Assert.True(KeyboardLayoutIdentity.SameLanguage(identifier, "en-GB"));
        Assert.False(KeyboardLayoutIdentity.SameCulture(identifier, "en-GB"));
        Assert.True(KeyboardLayoutIdentity.TryGetNativeHandle(identifier, out var handle));
        Assert.Equal(0x00010409u, handle);
        Assert.False(KeyboardLayoutIdentity.TryGetNativeHandle("en-US@invalid", out _));
    }

    [Fact]
    public void WindowsLayoutList_AppendsInstalledCulturesWithoutRewritingOrder()
    {
        var settings = new AppSettings
        {
            LayoutOrder = ["en-US"],
            UseWindowsLayoutList = true
        };
        var manager = CreateManager([EnglishPrimary, Russian, German], settings);

        var ordered = manager.GetLayoutOrder();

        Assert.Equal(
            ["en-US@04090409", "ru-RU@04190419", "de-DE@04070407"],
            ordered.Select(layout => layout.EffectiveIdentifier));
        Assert.Equal(["en-US"], settings.LayoutOrder);
    }

    [Fact]
    public void CustomLayoutList_DoesNotAppendUnconfiguredCultures()
    {
        var manager = CreateManager(
            [EnglishPrimary, Russian, German],
            new AppSettings
            {
                LayoutOrder = ["en-US"],
                UseWindowsLayoutList = false
            });

        var ordered = manager.GetLayoutOrder();

        Assert.Single(ordered);
        Assert.Equal("en-US@04090409", ordered[0].EffectiveIdentifier);
    }

    [Fact]
    public void DisabledExactVariant_FallsBackToEnabledVariantOfSameCulture()
    {
        var manager = CreateManager(
            [EnglishPrimary, EnglishAlternative, Russian],
            new AppSettings
            {
                LayoutOrder = ["en-US", "ru-RU"],
                UseWindowsLayoutList = true,
                DisabledLanguages = [EnglishPrimary.Identifier]
            });

        var ordered = manager.GetLayoutOrder();

        Assert.Equal(
            ["en-US@00010409", "ru-RU@04190419"],
            ordered.Select(layout => layout.EffectiveIdentifier));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("de")]
    [InlineData("German (Germany)")]
    public void DisabledLegacyAlias_ExcludesInstalledCulture(string disabledAlias)
    {
        var manager = CreateManager(
            [EnglishPrimary, German],
            new AppSettings
            {
                LayoutOrder = ["en-US"],
                UseWindowsLayoutList = true,
                DisabledLanguages = [disabledAlias]
            });

        var ordered = manager.GetLayoutOrder();

        Assert.Single(ordered);
        Assert.Equal("en-US", ordered[0].Code);
    }

    [Fact]
    public void Enable_RemovesLegacyAliasesAndDisableStoresExactIdentifier()
    {
        var settings = new AppSettings
        {
            DisabledLanguages = ["en", "English (United States)"]
        };

        KeyboardLayoutPreferences.Enable(settings, EnglishPrimary);
        KeyboardLayoutPreferences.Disable(settings, EnglishPrimary);

        Assert.Equal([EnglishPrimary.Identifier], settings.DisabledLanguages);
    }

    [Fact]
    public void DisabledActiveLayout_DoesNotReenterOperationalOrder()
    {
        IKeyboardLayoutManager manager = CreateManager(
            [EnglishPrimary, EnglishAlternative, Russian],
            new AppSettings
            {
                LayoutOrder = ["en-US", "ru-RU"],
                UseWindowsLayoutList = true,
                DisabledLanguages = [EnglishAlternative.Identifier]
            });

        var ordered = manager.GetLayoutOrder(EnglishAlternative.Identifier);

        Assert.Empty(ordered);
    }

    private static KeyboardLayoutManager CreateManager(
        IReadOnlyList<Layout> layouts,
        IReadOnlyList<string> order) =>
        CreateManager(layouts, new AppSettings { LayoutOrder = order.ToList() });

    private static KeyboardLayoutManager CreateManager(
        IReadOnlyList<Layout> layouts,
        AppSettings settings)
    {
        var manager = new KeyboardLayoutManager(
            new FakeSettingsService(settings),
            new FakeWindowsLayoutProvider(layouts));
        manager.LoadAll();
        return manager;
    }

    private sealed class FakeWindowsLayoutProvider(IReadOnlyList<Layout> layouts)
        : IWindowsLayoutProvider
    {
        public IReadOnlyList<Layout> GetInstalledLayouts() => layouts;
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;
        public AppSettings Load() => Current;
        public void Save(AppSettings value) => Current = value;
    }
}
