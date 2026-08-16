namespace LayoutFix.Core.Models;

public readonly record struct ActiveWindowContext(
    nint ForegroundWindow,
    nint FocusedWindow,
    uint ProcessId)
{
    public bool IsValid => ForegroundWindow != 0 && ProcessId != 0;
}
