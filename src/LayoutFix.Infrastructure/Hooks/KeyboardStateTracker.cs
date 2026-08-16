using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Hooks;

internal readonly record struct KeyboardTransition(HotkeyCombo? Combo, bool IsRepeat);

/// <summary>
/// Tracks modifier and key state from the hook event stream itself. GetAsyncKeyState
/// is intentionally not used inside a low-level hook callback because Windows updates
/// its asynchronous state after invoking WH_KEYBOARD_LL.
/// </summary>
internal sealed class KeyboardStateTracker
{
    private readonly HashSet<int> _pressedKeys = [];
    private readonly HashSet<int> _suppressedKeys = [];
    private readonly Dictionary<int, uint> _lastKeyDownTimes = [];
    private bool _shift;
    private bool _ctrl;
    private bool _alt;
    private bool _win;
    private bool _capsLock;
    private bool _numLock;
    private bool _scrollLock;

    public void SeedModifiers(Func<int, bool> isPressed)
    {
        _pressedKeys.RemoveWhere(IsModifier);
        foreach (var key in ModifierKeys)
        {
            if (isPressed(key))
                _pressedKeys.Add(key);
        }
        RefreshModifierState();
    }

    public void SeedToggleKeys(Func<int, bool> isToggled)
    {
        _capsLock = isToggled(Win32.VK_CAPITAL);
        _numLock = isToggled(Win32.VK_NUMLOCK);
        _scrollLock = isToggled(Win32.VK_SCROLL);
    }

    /// <summary>
    /// Repairs state left behind when Windows, a remote session, or a keyboard
    /// driver did not deliver a key-up event. During a low-level key-down hook,
    /// GetAsyncKeyState still represents the state before the current event, so
    /// it can distinguish a real held-key repeat from a fresh physical press.
    /// </summary>
    public void ReconcilePriorStateBeforeKeyDown(Func<int, bool> isPressed)
    {
        ArgumentNullException.ThrowIfNull(isPressed);
        var changed = false;
        foreach (var pressedKey in _pressedKeys.ToArray())
        {
            if (isPressed(pressedKey))
                continue;

            _pressedKeys.Remove(pressedKey);
            _suppressedKeys.Remove(pressedKey);
            _lastKeyDownTimes.Remove(pressedKey);
            changed = true;
        }

        if (changed)
            RefreshModifierState();
    }

    public KeyboardTransition ProcessKeyDown(int virtualKey, uint flags, uint eventTime = 0)
    {
        // Pause/Break is a special scan-code sequence and some keyboards/drivers do
        // not deliver a conventional key-up. Recover after the OS autorepeat window
        // so one missing key-up cannot suppress this shortcut until restart.
        if (virtualKey == Win32.VK_PAUSE &&
            _pressedKeys.Contains(virtualKey) &&
            eventTime != 0 &&
            _lastKeyDownTimes.TryGetValue(virtualKey, out var previousTime) &&
            unchecked(eventTime - previousTime) > 250)
        {
            _pressedKeys.Remove(virtualKey);
            _suppressedKeys.Remove(virtualKey);
        }

        if (eventTime != 0)
            _lastKeyDownTimes[virtualKey] = eventTime;
        var isRepeat = !_pressedKeys.Add(virtualKey);
        if (!isRepeat)
        {
            if (virtualKey == Win32.VK_CAPITAL) _capsLock = !_capsLock;
            if (virtualKey == Win32.VK_NUMLOCK) _numLock = !_numLock;
            if (virtualKey == Win32.VK_SCROLL) _scrollLock = !_scrollLock;
        }
        if (TryUpdateModifier(virtualKey))
            return new KeyboardTransition(null, isRepeat);

        return new KeyboardTransition(new HotkeyCombo
        {
            Alt = _alt || (flags & Win32.LLKHF_ALTDOWN) != 0,
            Ctrl = _ctrl,
            Shift = _shift,
            Win = _win,
            PrintScreen = virtualKey == Win32.VK_SNAPSHOT,
            Key = KeyboardHook.MapVirtualKeyToString(virtualKey),
            VirtualKey = virtualKey
        }, isRepeat);
    }

    public void ProcessKeyUp(int virtualKey)
    {
        _pressedKeys.Remove(virtualKey);
        _lastKeyDownTimes.Remove(virtualKey);
        TryUpdateModifier(virtualKey);
    }

    public void SuppressUntilKeyUp(int virtualKey) => _suppressedKeys.Add(virtualKey);
    public bool IsSuppressed(int virtualKey) => _suppressedKeys.Contains(virtualKey);
    public bool ReleaseSuppression(int virtualKey) => _suppressedKeys.Remove(virtualKey);

    public byte[] CreateKeyboardState()
    {
        var state = new byte[256];
        foreach (var key in _pressedKeys.Where(key => key is >= 0 and < 256))
            state[key] = 0x80;

        if (_shift) state[Win32.VK_SHIFT] = 0x80;
        if (_ctrl) state[Win32.VK_CONTROL] = 0x80;
        if (_alt) state[Win32.VK_MENU] = 0x80;
        if (_capsLock) state[Win32.VK_CAPITAL] |= 0x01;
        if (_numLock) state[Win32.VK_NUMLOCK] |= 0x01;
        if (_scrollLock) state[Win32.VK_SCROLL] |= 0x01;
        return state;
    }

    public void Reset()
    {
        _pressedKeys.Clear();
        _suppressedKeys.Clear();
        _lastKeyDownTimes.Clear();
        _shift = _ctrl = _alt = _win = false;
        _capsLock = _numLock = _scrollLock = false;
    }

    private bool TryUpdateModifier(int virtualKey)
    {
        if (!IsModifier(virtualKey))
            return false;

        RefreshModifierState();
        return true;
    }

    private void RefreshModifierState()
    {
        _shift = IsAnyPressed(Win32.VK_SHIFT, Win32.VK_LSHIFT, Win32.VK_RSHIFT);
        _ctrl = IsAnyPressed(Win32.VK_CONTROL, Win32.VK_LCONTROL, Win32.VK_RCONTROL);
        _alt = IsAnyPressed(Win32.VK_MENU, Win32.VK_LMENU, Win32.VK_RMENU);
        _win = IsAnyPressed(Win32.VK_LWIN, Win32.VK_RWIN);
    }

    private bool IsAnyPressed(params int[] keys) => keys.Any(_pressedKeys.Contains);

    private static bool IsModifier(int key) => ModifierKeys.Contains(key);

    private static readonly int[] ModifierKeys =
    [
        Win32.VK_SHIFT, Win32.VK_LSHIFT, Win32.VK_RSHIFT,
        Win32.VK_CONTROL, Win32.VK_LCONTROL, Win32.VK_RCONTROL,
        Win32.VK_MENU, Win32.VK_LMENU, Win32.VK_RMENU,
        Win32.VK_LWIN, Win32.VK_RWIN
    ];
}
