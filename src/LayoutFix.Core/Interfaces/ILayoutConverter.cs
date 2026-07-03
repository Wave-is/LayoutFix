using System.Collections.Generic;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface ILayoutConverter
{
    string ConvertTo(string text, Layout target, Layout source);
    (string? ConvertedText, Layout? Source, Layout? Target) AutoConvert(string text, IReadOnlyList<Layout> activeLayouts, string? currentLayoutCode = null);
}
