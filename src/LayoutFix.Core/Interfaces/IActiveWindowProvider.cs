namespace LayoutFix.Core.Interfaces;

public interface IActiveWindowProvider
{
    string GetActiveProcessName();
    string GetActiveLayoutCode();
    void SwitchToNextLayout();
}
