using System;

namespace LayoutFix.Core.Interfaces;

public interface IMouseHook : IDisposable
{
    event EventHandler? MouseClicked;
    void Start();
    void Stop();
}
