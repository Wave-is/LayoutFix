using LayoutFix.Core.Models;

namespace LayoutFix.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3-preview.4+abcdef", 1, 2, 3, "preview.4")]
    [InlineData("  V10.20.30-rc.1  ", 10, 20, 30, "rc.1")]
    public void TryParseAcceptsReleaseTags(
        string value,
        int major,
        int minor,
        int patch,
        string? prerelease)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var parsed));
        Assert.Equal(new ReleaseVersion(major, minor, patch, prerelease), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3+build..1")]
    [InlineData("1.2.3+build+other")]
    [InlineData("not-a-version")]
    public void TryParseRejectsInvalidTags(string value)
        => Assert.False(ReleaseVersion.TryParse(value, out _));

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.99")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.999999999999999999999", "1.0.0-beta.2")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11")]
    public void CompareToOrdersSemVer(string newer, string older)
    {
        Assert.True(ReleaseVersion.TryParse(newer, out var newerVersion));
        Assert.True(ReleaseVersion.TryParse(older, out var olderVersion));
        Assert.True(newerVersion.CompareTo(olderVersion) > 0);
        Assert.True(olderVersion.CompareTo(newerVersion) < 0);
    }

    [Fact]
    public void BuildMetadataDoesNotAffectPrecedence()
    {
        Assert.True(ReleaseVersion.TryParse("1.2.3+first", out var first));
        Assert.True(ReleaseVersion.TryParse("1.2.3+second", out var second));
        Assert.Equal(0, first.CompareTo(second));
    }
}
