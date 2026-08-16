using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Layouts;

namespace LayoutFix.Tests;

public class WindowsLayoutProviderTests
{
    [Fact]
    public void InstalledLayouts_HaveUniqueRoundTrippableIdentifiersAndMappings()
    {
        var layouts = new WindowsLayoutProvider().GetInstalledLayouts();

        Assert.NotEmpty(layouts);
        Assert.Equal(
            layouts.Count,
            layouts.Select(layout => layout.EffectiveIdentifier)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        foreach (var layout in layouts)
        {
            Assert.False(string.IsNullOrWhiteSpace(layout.Code));
            Assert.Equal(
                layout.Code,
                KeyboardLayoutIdentity.GetCultureCode(layout.EffectiveIdentifier),
                ignoreCase: true);
            Assert.True(KeyboardLayoutIdentity.TryGetNativeHandle(
                layout.EffectiveIdentifier,
                out _));
            Assert.NotEmpty(layout.Keys);
        }
    }
}
