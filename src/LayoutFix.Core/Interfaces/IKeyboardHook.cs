using System;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public class HotkeyEventArgs : EventArgs
{
    public HotkeyCombo Combo { get; }
    public bool Handled { get; set; }

    public HotkeyEventArgs(HotkeyCombo combo)
    {
        Combo = combo;
    }
}

public interface IKeyboardHook : IDisposable
{
    event EventHandler<HotkeyEventArgs> HotkeyPressed;
    void Start();
    void Stop();
}
