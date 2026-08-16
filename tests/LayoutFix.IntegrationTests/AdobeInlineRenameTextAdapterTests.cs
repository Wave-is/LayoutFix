using LayoutFix.Infrastructure.Services;

namespace LayoutFix.IntegrationTests;

public class AdobeInlineRenameTextAdapterTests
{
    [Theory]
    [InlineData("AfterFX", "AE_CApplication_25.0", "Edit", "after-effects-rename-v1")]
    [InlineData("afterfx", "AE_CApplication_25.3", "Edit", "after-effects-rename-v1")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro", "Edit", "premiere-rename-v1")]
    public void ResolveAdapterId_AcceptsOnlyProvenAdobeApplicationProfiles(
        string processName,
        string mainClass,
        string focusedClass,
        string expectedAdapterId)
    {
        Assert.Equal(
            expectedAdapterId,
            AdobeInlineRenameTextAdapter.ResolveAdapterId(
                processName,
                mainClass,
                focusedClass));
    }

    [Theory]
    [InlineData("AfterFX", "AE_CApplication_25.0", "Button")]
    [InlineData("AfterFX", "ae_capplication_25.0", "Edit")]
    [InlineData("AfterFX-helper", "AE_CApplication_25.0", "Edit")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro", "RichEdit20W")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro 2025", "Edit")]
    [InlineData("Premiere Pro", "Premiere Pro", "Edit")]
    [InlineData("Photoshop", "Premiere Pro", "Edit")]
    public void ResolveAdapterId_RejectsLookalikeOrUnprovenProfiles(
        string processName,
        string mainClass,
        string focusedClass)
    {
        Assert.Null(AdobeInlineRenameTextAdapter.ResolveAdapterId(
            processName,
            mainClass,
            focusedClass));
    }

    [Theory]
    [InlineData("UI_TextEdit", "TEST")]
    [InlineData("TEST", "TEST")]
    [InlineData("привет", "привет")]
    public void IsSupportedAccessibleName_AcceptsAdobeStableOrValueBackedName(
        string accessibleName,
        string currentValue)
    {
        Assert.True(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            accessibleName,
            currentValue));
    }

    [Fact]
    public void IsSupportedAccessibleName_AcceptsCapturedNameWhileAdobeValueUpdates()
    {
        Assert.True(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            "ghbdtn",
            "привет",
            expectedAccessibleName: "ghbdtn"));
    }

    [Theory]
    [InlineData("Other", "TEST")]
    [InlineData("test", "TEST")]
    [InlineData("", "TEST")]
    [InlineData("", "")]
    public void IsSupportedAccessibleName_RejectsAmbiguousOrMismatchedName(
        string accessibleName,
        string currentValue)
    {
        Assert.False(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            accessibleName,
            currentValue));
    }

    [Fact]
    public void IsSupportedAccessibleName_RejectsUnrelatedNameDuringReplacement()
    {
        Assert.False(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            "Other",
            "привет",
            expectedAccessibleName: "ghbdtn"));
    }
}
