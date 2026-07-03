using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class TransliterationTests
{
    [Theory]
    [InlineData("Привет", "Privet")]
    [InlineData("Щука", "Shhuka")]
    [InlineData("Privet", "Привет")]
    [InlineData("Shhuka", "Щука")]
    public void Transliterate_Works_Bidirectionally(string input, string expected)
    {
        var service = new TransliterationService();
        var result = service.Transliterate(input);
        Assert.Equal(expected, result);
    }
}
