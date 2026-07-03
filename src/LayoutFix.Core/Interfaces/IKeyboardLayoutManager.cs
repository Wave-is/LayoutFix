using System.Collections.Generic;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface IKeyboardLayoutManager
{
    void LoadAll();
    IReadOnlyList<Layout> GetInstalledWindowsLayouts();
    IReadOnlyList<Layout> GetLayoutOrder();
}
