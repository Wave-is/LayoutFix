using LayoutFix.Core.Models;

namespace LayoutFix.Tests;

public class HotkeyComboTests
{
    [Theory]
    [InlineData("Scroll", 0x91)]
    [InlineData("Pause", 0x13)]
    [InlineData("Ctrl+`", 0xC0)]
    [InlineData("Ctrl+F12", 0x7B)]
    [InlineData("Win+F24", 0x87)]
    public void Parse_MapsSupportedKeysToVirtualKey(string text, int expectedVirtualKey)
    {
        Assert.Equal(expectedVirtualKey, HotkeyCombo.Parse(text).VirtualKey);
    }

    [Fact]
    public void Parse_PreservesAllModifiers()
    {
        var combo = HotkeyCombo.Parse("Ctrl+Alt+Shift+Win+F8");

        Assert.True(combo.Ctrl);
        Assert.True(combo.Alt);
        Assert.True(combo.Shift);
        Assert.True(combo.Win);
        Assert.Equal(0x77, combo.VirtualKey);
    }

    [Fact]
    public void Matches_NormalizesKeyNamesAndRejectsDifferentModifiers()
    {
        Assert.True(HotkeyCombo.Parse("Ctrl+F12").Matches(HotkeyCombo.Parse("ctrl+f12")));
        Assert.False(HotkeyCombo.Parse("Ctrl+F12").Matches(HotkeyCombo.Parse("Shift+F12")));
    }

    [Theory]
    [InlineData("Ctrl+OemQuestion", 0xBF)]
    [InlineData("Alt+OemPeriod", 0xBE)]
    [InlineData("Shift+OemOpenBrackets", 0xDB)]
    [InlineData("Ctrl+OemPipe", 0xDC)]
    [InlineData("Ctrl+NumPad0", 0x60)]
    [InlineData("Ctrl+NumPad9", 0x69)]
    [InlineData("Ctrl+Divide", 0x6F)]
    [InlineData("Ctrl+0", 0x30)]
    [InlineData("Ctrl+9", 0x39)]
    public void Parse_AcceptsNamesPreviouslySavedByTheHotkeyEditor(
        string text,
        int expectedVirtualKey)
    {
        Assert.Equal(expectedVirtualKey, HotkeyCombo.Parse(text).VirtualKey);
    }

    [Fact]
    public void EveryCanonicalKeyNameRoundTripsToItsVirtualKey()
    {
        for (var virtualKey = 1; virtualKey <= byte.MaxValue; virtualKey++)
        {
            var name = HotkeyCombo.GetCanonicalKeyName(virtualKey);
            if (name.Length == 0)
                continue;

            Assert.Equal(virtualKey, HotkeyCombo.Parse(name).VirtualKey);
        }
    }
}
