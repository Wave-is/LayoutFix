using System.Threading.Tasks;

namespace LayoutFix.Core.Interfaces;

public interface IInputInjector
{
    Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key);
    Task SendBackspacesAsync(int count);
    Task ReleaseModifiersAsync();
    Task SendTextAsync(string text);
    Task SelectWordLeftAsync();
    Task<string?> GetClipboardTextAsync();
    Task SetClipboardTextAsync(string text);
}
