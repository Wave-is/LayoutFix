using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface IWindowsLayoutProvider
{
    IReadOnlyList<Layout> GetInstalledLayouts();
}
