using System.Collections.Generic;

namespace LayoutFix.Core.Models;

public class HotkeyScheme
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, HotkeyAction> Actions { get; set; } = [];
}
