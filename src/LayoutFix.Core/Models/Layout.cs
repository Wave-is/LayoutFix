using System.Collections.Generic;

namespace LayoutFix.Core.Models;

public class Layout
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, string> Keys { get; set; } = [];
    public Dictionary<string, string> ShiftKeys { get; set; } = [];
}
