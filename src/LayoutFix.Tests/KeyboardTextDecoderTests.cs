using System.Text;
using LayoutFix.Infrastructure.Hooks;
using Xunit;

namespace LayoutFix.Tests;

public class KeyboardTextDecoderTests
{
    [Fact]
    public void PositiveResult_ReturnsOnlyCommittedText()
    {
        var result = KeyboardTextDecoder.Decode(1, new StringBuilder("éx"));

        Assert.Equal("é", result.Text);
        Assert.False(result.IsDeadKey);
    }

    [Fact]
    public void ZeroResult_ReturnsNoTextWithoutDeadKeyMarker()
    {
        var result = KeyboardTextDecoder.Decode(0, new StringBuilder());

        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.IsDeadKey);
    }

    [Fact]
    public void NegativeResult_MarksUncommittedDeadKeyWithoutGuessingText()
    {
        var result = KeyboardTextDecoder.Decode(-1, new StringBuilder("´"));

        Assert.Equal(string.Empty, result.Text);
        Assert.True(result.IsDeadKey);
    }
}
