using System;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public class HotkeyEventArgs : EventArgs
{
    public HotkeyCombo Combo { get; }
    public string Text { get; }
    public bool IsRepeat { get; }
    public bool IsDeadKey { get; }
    public bool Handled { get; set; }

    public HotkeyEventArgs(
        HotkeyCombo combo,
        bool isRepeat = false,
        string text = "",
        bool isDeadKey = false)
    {
        Combo = combo;
        IsRepeat = isRepeat;
        Text = text;
        IsDeadKey = isDeadKey;
    }
}

public interface IKeyboardHook : IDisposable
{
    event EventHandler<HotkeyEventArgs> HotkeyPressed;
    long InputGeneration => 0;
    void Start();
    void Stop();
}
