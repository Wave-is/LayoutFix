using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Input;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Tests;

public class InputInjectorTests
{
    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("f12", 0x7B)]
    [InlineData("F24", 0x87)]
    [InlineData("escape", 0x1B)]
    [InlineData("delete", 0x2E)]
    public void MapsNamedKeys(string key, ushort expected)
    {
        Assert.Equal(expected, InputInjector.MapStringToVirtualKey(key));
    }

    [Fact]
    public async Task PartialBackspaceReportsAffectedPressesAndReleasesKeyDown()
    {
        var callSizes = new List<int>();
        var injector = new InputInjector(inputs =>
        {
            callSizes.Add(inputs.Length);
            return (uint)(callSizes.Count == 1 ? 5 : inputs.Length);
        });

        var exception = await Assert.ThrowsAsync<InputInjectionException>(
            () => injector.SendBackspacesAsync(4));

        Assert.Equal(InputInjectionOperation.Backspace, exception.Operation);
        Assert.Equal(4, exception.RequestedUnitCount);
        Assert.Equal(3, exception.AffectedUnitCount);
        Assert.Equal(8, exception.RequestedEventCount);
        Assert.Equal(5, exception.AcceptedEventCount);
        Assert.Equal([8, 1], callSizes);
    }

    [Fact]
    public async Task PartialUnicodeTextReportsAcceptedUtf16Prefix()
    {
        var callSizes = new List<int>();
        var injector = new InputInjector(inputs =>
        {
            callSizes.Add(inputs.Length);
            return (uint)(callSizes.Count == 1 ? 3 : inputs.Length);
        });

        var exception = await Assert.ThrowsAsync<InputInjectionException>(
            () => injector.SendTextAsync("abc"));

        Assert.Equal(InputInjectionOperation.Text, exception.Operation);
        Assert.Equal(3, exception.RequestedUnitCount);
        Assert.Equal(2, exception.AffectedUnitCount);
        Assert.Equal([6, 1], callSizes);
    }

    [Fact]
    public async Task UnicodeTextNormalizesEveryLineEndingToOneEnter()
    {
        Win32.INPUT[]? sentInputs = null;
        var injector = new InputInjector(inputs =>
        {
            sentInputs = inputs;
            return (uint)inputs.Length;
        });

        await injector.SendTextAsync("a\r\nb\nc\rd");

        Assert.NotNull(sentInputs);
        var keyDownScans = sentInputs!
            .Where(input =>
                input.type == Win32.INPUT_KEYBOARD &&
                (input.u.ki.dwFlags & Win32.KEYEVENTF_KEYUP) == 0)
            .Select(input => input.u.ki.wScan)
            .ToArray();
        Assert.Equal(
            new ushort[] { 'a', '\r', 'b', '\r', 'c', '\r', 'd' },
            keyDownScans);
    }

    [Fact]
    public async Task PartialCrLfInjectionReportsBothSourceUtf16Units()
    {
        var callSizes = new List<int>();
        var injector = new InputInjector(inputs =>
        {
            callSizes.Add(inputs.Length);
            return (uint)(callSizes.Count == 1 ? 1 : inputs.Length);
        });

        var exception = await Assert.ThrowsAsync<InputInjectionException>(
            () => injector.SendTextAsync("\r\nx"));

        Assert.Equal(3, exception.RequestedUnitCount);
        Assert.Equal(2, exception.AffectedUnitCount);
        Assert.Equal([4, 1], callSizes);
    }

    [Fact]
    public async Task LongUnicodeTextIsSentAsOneAtomicInputBatch()
    {
        var callSizes = new List<int>();
        var injector = new InputInjector(inputs =>
        {
            callSizes.Add(inputs.Length);
            return (uint)inputs.Length;
        });

        await injector.SendTextAsync(new string('x', 130));

        Assert.Equal([260], callSizes);
    }

    [Fact]
    public async Task PartialChordReportsTargetKeyDownAndReleasesAllPressedKeys()
    {
        var callSizes = new List<int>();
        var injector = new InputInjector(inputs =>
        {
            callSizes.Add(inputs.Length);
            return (uint)(callSizes.Count == 1 ? 3 : inputs.Length);
        });

        var exception = await Assert.ThrowsAsync<InputInjectionException>(
            () => injector.SendKeyCombinationAsync(true, false, true, "c"));

        Assert.Equal(InputInjectionOperation.KeyCombination, exception.Operation);
        Assert.Equal(1, exception.AffectedUnitCount);
        Assert.Equal([6, 3], callSizes);
    }

    [Fact]
    public async Task RejectedInjectionReportsZeroProgressWithoutCleanupAttempt()
    {
        var callCount = 0;
        var injector = new InputInjector(_ =>
        {
            callCount++;
            return 0;
        });

        var exception = await Assert.ThrowsAsync<InputInjectionException>(
            () => injector.SendBackspacesAsync(2));

        Assert.Equal(0, exception.AffectedUnitCount);
        Assert.Equal(1, callCount);
    }
}
