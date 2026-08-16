using System;

namespace LayoutFix.Core.Interfaces;

public interface IMouseHook : IDisposable
{
    event EventHandler? MouseClicked;
    long InputGeneration => 0;
    void Start();
    void Stop();
}
