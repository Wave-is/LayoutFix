using System.Threading.Tasks;

namespace LayoutFix.Core.Interfaces;

public interface IInputInjector
{
    Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key);
    Task SendBackspacesAsync(int count);
    Task SendTextAsync(string text);
    Task SelectWordLeftAsync();
    Task WaitForModifiersReleaseAsync(int timeoutMs = 2000);
}
