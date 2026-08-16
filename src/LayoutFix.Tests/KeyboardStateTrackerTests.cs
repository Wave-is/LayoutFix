using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Tests;

public class KeyboardStateTrackerTests
{
    [Fact]
    public void ModifierState_ComesFromHookEventOrder()
    {
        var tracker = new KeyboardStateTracker();

        Assert.Null(tracker.ProcessKeyDown(Win32.VK_LCONTROL, 0).Combo);
        var withControl = tracker.ProcessKeyDown('C', 0);
        tracker.ProcessKeyUp('C');
        tracker.ProcessKeyUp(Win32.VK_LCONTROL);
        var withoutControl = tracker.ProcessKeyDown('C', 0);

        Assert.True(withControl.Combo!.Ctrl);
        Assert.False(withoutControl.Combo!.Ctrl);
    }

    [Fact]
    public void LeftAndRightModifiers_AreNormalized()
    {
        var tracker = new KeyboardStateTracker();

        tracker.ProcessKeyDown(Win32.VK_RSHIFT, 0);
        tracker.ProcessKeyDown(Win32.VK_RMENU, 0);
        var transition = tracker.ProcessKeyDown(0x13, 0);

        Assert.True(transition.Combo!.Shift);
        Assert.True(transition.Combo.Alt);
        Assert.Equal("pause", transition.Combo.Key);
    }

    [Fact]
    public void ReleasingOneSide_KeepsOtherModifierPressed()
    {
        var tracker = new KeyboardStateTracker();

        tracker.ProcessKeyDown(Win32.VK_LCONTROL, 0);
        tracker.ProcessKeyDown(Win32.VK_RCONTROL, 0);
        tracker.ProcessKeyUp(Win32.VK_LCONTROL);
        var transition = tracker.ProcessKeyDown('C', 0);

        Assert.True(transition.Combo!.Ctrl);
    }

    [Fact]
    public void SeededModifierState_TracksBothSidesIndependently()
    {
        var tracker = new KeyboardStateTracker();
        tracker.SeedModifiers(key => key is Win32.VK_LSHIFT or Win32.VK_RSHIFT);

        tracker.ProcessKeyUp(Win32.VK_LSHIFT);
        var transition = tracker.ProcessKeyDown('A', 0);

        Assert.True(transition.Combo!.Shift);
    }

    [Fact]
    public void AltFlag_RecoversAltStateWhenHookStartedMidChord()
    {
        var tracker = new KeyboardStateTracker();

        var transition = tracker.ProcessKeyDown('T', Win32.LLKHF_ALTDOWN);

        Assert.True(transition.Combo!.Alt);
    }

    [Fact]
    public void AutoRepeat_IsIdentifiedAndSuppressionLastsUntilKeyUp()
    {
        var tracker = new KeyboardStateTracker();

        var first = tracker.ProcessKeyDown(0x13, 0);
        tracker.SuppressUntilKeyUp(0x13);
        var repeated = tracker.ProcessKeyDown(0x13, 0);

        Assert.False(first.IsRepeat);
        Assert.True(repeated.IsRepeat);
        Assert.True(tracker.IsSuppressed(0x13));

        tracker.ProcessKeyUp(0x13);
        Assert.True(tracker.ReleaseSuppression(0x13));
        Assert.False(tracker.IsSuppressed(0x13));
    }

    [Fact]
    public void HookNamesExtendedFunctionKeys()
    {
        Assert.Equal("f24", KeyboardHook.MapVirtualKeyToString(0x87));
    }

    [Fact]
    public void PauseRecoversWhenDriverOmitsKeyUpButStillDebouncesHeldKey()
    {
        var tracker = new KeyboardStateTracker();

        var first = tracker.ProcessKeyDown(Win32.VK_PAUSE, 0, 1_000);
        tracker.SuppressUntilKeyUp(Win32.VK_PAUSE);
        var heldRepeat = tracker.ProcessKeyDown(Win32.VK_PAUSE, 0, 1_100);
        var nextPhysicalPress = tracker.ProcessKeyDown(Win32.VK_PAUSE, 0, 1_500);

        Assert.False(first.IsRepeat);
        Assert.True(heldRepeat.IsRepeat);
        Assert.False(nextPhysicalPress.IsRepeat);
        Assert.False(tracker.IsSuppressed(Win32.VK_PAUSE));
    }

    [Fact]
    public void FreshFunctionKeyPress_RecoversAfterMissingKeyUp()
    {
        const int f12 = 0x7B;
        var tracker = new KeyboardStateTracker();

        var first = tracker.ProcessKeyDown(f12, 0, 1_000);
        tracker.SuppressUntilKeyUp(f12);
        tracker.ReconcilePriorStateBeforeKeyDown(_ => false);
        var nextPhysicalPress = tracker.ProcessKeyDown(f12, 0, 1_500);

        Assert.False(first.IsRepeat);
        Assert.False(nextPhysicalPress.IsRepeat);
        Assert.False(tracker.IsSuppressed(f12));
    }

    [Fact]
    public void MissingModifierKeyUp_IsReconciledWithoutClearingHeldModifiers()
    {
        var tracker = new KeyboardStateTracker();
        tracker.ProcessKeyDown(Win32.VK_LCONTROL, 0);

        tracker.ReconcilePriorStateBeforeKeyDown(_ => false);
        var afterMissingKeyUp = tracker.ProcessKeyDown('A', 0);
        tracker.ProcessKeyUp('A');

        tracker.ProcessKeyDown(Win32.VK_LCONTROL, 0);
        tracker.ReconcilePriorStateBeforeKeyDown(
            key => key == Win32.VK_LCONTROL);
        var whileStillHeld = tracker.ProcessKeyDown('B', 0);

        Assert.False(afterMissingKeyUp.Combo!.Ctrl);
        Assert.True(whileStillHeld.Combo!.Ctrl);
    }
}
