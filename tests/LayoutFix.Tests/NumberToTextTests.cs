using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class NumberToTextTests
{
    [Theory]
    [InlineData(0, "ноль")]
    [InlineData(1, "один")]
    [InlineData(25, "двадцать пять")]
    [InlineData(125, "сто двадцать пять")]
    [InlineData(1000, "одна тысяча")]
    [InlineData(2000, "две тысячи")]
    [InlineData(5000, "пять тысяч")]
    [InlineData(1000000, "один миллион")]
    [InlineData(123456789, "сто двадцать три миллиона четыреста пятьдесят шесть тысяч семьсот восемьдесят девять")]
    public void ConvertRu_Numbers(long number, string expected)
    {
        var converter = new NumberToTextConverter();
        var result = converter.Convert(number, "ru");
        Assert.Equal(expected, result);
    }
}
