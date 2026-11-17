namespace LayoutFix.Core.Interfaces;

using LayoutFix.Core.Models;

public interface IActiveWindowProvider
{
    ActiveWindowContext CaptureActiveWindow();
    bool IsSameActiveWindow(ActiveWindowContext context);
    bool HasEmbeddedProcessFocus(ActiveWindowContext context) => false;
    string GetActiveProcessName();
    string GetActiveLayoutCode();
    void SwitchToNextLayout();
    bool TrySwitchToLayout(string layoutCode);
}
