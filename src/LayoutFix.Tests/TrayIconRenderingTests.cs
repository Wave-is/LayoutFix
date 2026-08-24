using System.Drawing;
using LayoutFix.UI;

namespace LayoutFix.Tests;

public sealed class TrayIconRenderingTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void TextIconUsesRoundedTransparentCornersAtSupportedDpiSizes(int size)
    {
        using var bitmap = TrayManager.RenderTrayIconBitmap(
            "ru",
            useFlagIcons: false,
            automaticCorrectionEnabled: false,
            hooksOperational: true,
            size);

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(0, bitmap.GetPixel(size - 1, 0).A);
        Assert.Equal(0, bitmap.GetPixel(0, size - 1).A);
        Assert.Equal(0, bitmap.GetPixel(size - 1, size - 1).A);
        Assert.True(bitmap.GetPixel(size / 2, size / 2).A > 0);
    }

    [Fact]
    public void TextIconBodyReflectsOperationalStateWithoutSquareBorder()
    {
        using var manual = TrayManager.RenderTrayIconBitmap("en", false, false, true, 32);
        using var automatic = TrayManager.RenderTrayIconBitmap("en", false, true, true, 32);
        using var reconnecting = TrayManager.RenderTrayIconBitmap("en", false, false, false, 32);

        var sample = new Point(5, 16);
        Assert.NotEqual(manual.GetPixel(sample.X, sample.Y), automatic.GetPixel(sample.X, sample.Y));
        Assert.NotEqual(manual.GetPixel(sample.X, sample.Y), reconnecting.GetPixel(sample.X, sample.Y));
        Assert.Equal(0, manual.GetPixel(0, 0).A);
        Assert.Equal(0, automatic.GetPixel(0, 0).A);
        Assert.Equal(0, reconnecting.GetPixel(0, 0).A);
    }

    [Theory]
    [InlineData("RU")]
    [InlineData("EN")]
    [InlineData("UK")]
    public void FlagIconIsClippedToRoundedBody(string layout)
    {
        using var bitmap = TrayManager.RenderTrayIconBitmap(
            layout,
            useFlagIcons: true,
            automaticCorrectionEnabled: false,
            hooksOperational: true,
            size: 32);

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.True(bitmap.GetPixel(16, 16).A > 0);
    }
}
