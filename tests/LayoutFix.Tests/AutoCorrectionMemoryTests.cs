using LayoutFix.Core.Models;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public class AutoCorrectionMemoryTests
{
    private static readonly ActiveWindowContext Window = new((nint)1, (nint)2, 3);

    [Theory]
    [InlineData("привет", "ghbdtn")]
    [InlineData("привет ", "ghbdtn ")]
    public void PreparesExactUndoWithOrWithoutCapturedTrigger(
        string selectedText,
        string expectedRestoration)
    {
        var memory = new AutoCorrectionMemory();
        memory.Record("ghbdtn", "привет", " ", Window);

        var found = memory.TryPrepareUndo(
            new TextSelection(selectedText, Window, true),
            out var candidate);

        Assert.True(found);
        Assert.Equal("ghbdtn", candidate.OriginalText);
        Assert.Equal(expectedRestoration, candidate.RestoredSelectionText);
    }

    [Fact]
    public void RejectsDifferentWindowOrDifferentText()
    {
        var memory = new AutoCorrectionMemory();
        memory.Record("ghbdtn", "привет", " ", Window);

        Assert.False(memory.TryPrepareUndo(
            new TextSelection("привет", new ActiveWindowContext((nint)9, (nint)2, 3), true),
            out _));

        memory.Record("ghbdtn", "привет", " ", Window);
        Assert.False(memory.TryPrepareUndo(
            new TextSelection("пользователь продолжил ввод", Window, true),
            out _));
    }

    [Fact]
    public void CommitConsumesOnlyMatchingGeneration()
    {
        var memory = new AutoCorrectionMemory();
        memory.Record("ghbdtn", "привет", " ", Window);
        Assert.True(memory.TryPrepareUndo(new TextSelection("привет", Window, true), out var first));

        memory.Record("руддщ", "hello", " ", Window);
        memory.CommitUndo(first.Generation);

        Assert.True(memory.TryPrepareUndo(new TextSelection("hello", Window, true), out _));
    }
}
