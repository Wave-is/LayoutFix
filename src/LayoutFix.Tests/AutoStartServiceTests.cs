using LayoutFix.Infrastructure.Services;

namespace LayoutFix.Tests;

public class AutoStartServiceTests
{
    [Fact]
    public void StartupCommand_QuotesPathsWithSpaces()
    {
        const string path = @"C:\Program Files\LayoutFix\LayoutFix.exe";

        Assert.Equal($"\"{path}\"", AutoStartService.BuildStartupCommand(path));
        Assert.True(AutoStartService.StartupCommandMatches($"\"{path}\"", path));
    }

    [Fact]
    public void StartupCommand_AcceptsLegacyUnquotedExactPathWithoutWhitespace()
    {
        const string path = @"C:\LayoutFix\LayoutFix.exe";

        Assert.True(AutoStartService.StartupCommandMatches(path, path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\LayoutFix\LayoutFix.exe")]
    [InlineData("C:\\LayoutFix Test\\LayoutFix.exe")]
    [InlineData("C:\\LayoutFix\tTest\\LayoutFix.exe")]
    public void StartupCommand_RejectsUnquotedExactPathContainingWhitespace(string path)
    {
        Assert.False(AutoStartService.StartupCommandMatches(path, path));
        Assert.True(AutoStartService.StartupCommandMatches($"\"{path}\"", path));
    }

    [Fact]
    public void StartupCommand_RejectsDifferentExecutableAndPrefixCollision()
    {
        const string path = @"C:\Apps\LayoutFix\LayoutFix.exe";

        Assert.False(AutoStartService.StartupCommandMatches(
            "\"C:\\Apps\\LayoutFix\\LayoutFix-Evil.exe\"",
            path));
        Assert.False(AutoStartService.StartupCommandMatches(
            @"C:\Apps\LayoutFix\LayoutFix.exe --unexpected",
            path));
        Assert.False(AutoStartService.StartupCommandMatches(
            $"\"{path}\" --unexpected",
            path));
    }
}
