using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Input;

public class InputInjector : IInputInjector
{
    private const int ShortTextSettleDelayMilliseconds = 10;
    private const int LongTextSettleDelayMilliseconds = 50;
    private const int ShortTextUnitLimit = 128;
    private readonly Func<Win32.INPUT[], uint> _inputSender;
    private readonly Func<int, bool> _isKeyPressed;

    public InputInjector()
        : this(inputs => Win32.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Win32.INPUT>()),
            virtualKey => (Win32.GetAsyncKeyState(virtualKey) & 0x8000) != 0)
    {
    }

    internal InputInjector(
        Func<Win32.INPUT[], uint> inputSender,
        Func<int, bool>? isKeyPressed = null)
    {
        _inputSender = inputSender ?? throw new ArgumentNullException(nameof(inputSender));
        _isKeyPressed = isKeyPressed ?? (_ => false);
    }

    public async Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
    {
        ushort vk = MapStringToVirtualKey(key);
        if (vk == 0) return;

        var batch = CreateModifierSafeBatch(
            [CreateKeyboardInput(vk, false), CreateKeyboardInput(vk, true)],
            ctrl,
            alt,
            shift);
        var targetKeyDownIndex = batch.PayloadStartIndex;

        SendInputs(
            batch.Inputs,
            InputInjectionOperation.KeyCombination,
            requestedUnitCount: 1,
            affectedUnitCount: sent => sent > targetKeyDownIndex ? 1 : 0,
            batch.PhysicalModifiersToRestore);
        // Callers either wait for an observable clipboard sequence change or
        // verify the resulting text. A fixed 50 ms pause on every Ctrl+C and
        // Ctrl+V made the two-phase manual transaction pay this cost repeatedly.
        await Task.Delay(15);
    }

    public async Task SendBackspacesAsync(int count)
    {
        if (count <= 0) return;
        var payload = new Win32.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            payload[i * 2] = CreateKeyboardInput(0x08, false);
            payload[i * 2 + 1] = CreateKeyboardInput(0x08, true);
        }
        var batch = CreateModifierSafeBatch(payload);
        SendInputs(
            batch.Inputs,
            InputInjectionOperation.Backspace,
            requestedUnitCount: count,
            affectedUnitCount: sent => Math.Min(
                count,
                (Math.Max(0, sent - batch.PayloadStartIndex) + 1) / 2),
            batch.PhysicalModifiersToRestore);
        await Task.Delay(20);
    }

    public async Task WaitForModifiersReleaseAsync(int timeoutMs = 2000)
    {
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            bool shift = (Win32.GetAsyncKeyState(Win32.VK_SHIFT) & 0x8000) != 0;
            bool ctrl = (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0;
            bool alt = (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;
            bool lwin = (Win32.GetAsyncKeyState(Win32.VK_LWIN) & 0x8000) != 0;
            bool rwin = (Win32.GetAsyncKeyState(Win32.VK_RWIN) & 0x8000) != 0;

            if (!shift && !ctrl && !alt && !lwin && !rwin)
            {
                break;
            }
            await Task.Delay(20);
            elapsed += 20;
        }

        if (elapsed >= timeoutMs)
            throw new TimeoutException("Modifier keys were not released before the input operation timed out.");
    }

    public async Task SendTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var textUnits = CreateTextInjectionUnits(text);
        var payload = new Win32.INPUT[textUnits.Count * 2];
        var inputIndex = 0;
        foreach (var unit in textUnits)
        {
            var down = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
            down.u.ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = unit.Value,
                dwFlags = Win32.KEYEVENTF_UNICODE,
                dwExtraInfo = KeyboardHook.InjectedExtraInfo
            };
            payload[inputIndex++] = down;

            var up = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
            up.u.ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = unit.Value,
                dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
                dwExtraInfo = KeyboardHook.InjectedExtraInfo
            };
            payload[inputIndex++] = up;
        }

        var batch = CreateModifierSafeBatch(payload);
        SendInputs(
            batch.Inputs,
            InputInjectionOperation.Text,
            requestedUnitCount: text.Length,
            affectedUnitCount: sent => textUnits
                .Take(Math.Min(
                    textUnits.Count,
                    (Math.Max(0, sent - batch.PayloadStartIndex) + 1) / 2))
                .Sum(unit => unit.SourceUtf16Length),
            batch.PhysicalModifiersToRestore);
        // Short manual corrections do not need the same queue-settle budget as
        // a batch containing hundreds or thousands of Unicode key events. Keep
        // one scheduler turn so the target can consume the atomic SendInput
        // batch before the coordinator releases its hotkey slot.
        await Task.Delay(textUnits.Count <= ShortTextUnitLimit
            ? ShortTextSettleDelayMilliseconds
            : LongTextSettleDelayMilliseconds);
    }

    private static List<TextInjectionUnit> CreateTextInjectionUnits(string text)
    {
        var units = new List<TextInjectionUnit>(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value == '\r')
            {
                var isCrLf = index + 1 < text.Length && text[index + 1] == '\n';
                units.Add(new TextInjectionUnit('\r', isCrLf ? 2 : 1));
                if (isCrLf)
                    index++;
                continue;
            }

            // KEYEVENTF_UNICODE sends control characters as keyboard input. A
            // standalone LF and the CR+LF pair must therefore become one Enter;
            // sending both halves of CRLF creates two visible lines in Win32 Edit.
            units.Add(new TextInjectionUnit(value == '\n' ? '\r' : value, 1));
        }

        return units;
    }

    public async Task SelectWordLeftAsync()
    {
        await SendKeyCombinationAsync(true, false, true, "left");
    }

    private Win32.INPUT CreateKeyboardInput(ushort vk, bool isKeyUp)
    {
        var input = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
        input.u.ki = new Win32.KEYBDINPUT
        {
            wVk = vk,
            wScan = 0,
            dwFlags = isKeyUp ? Win32.KEYEVENTF_KEYUP : 0,
            dwExtraInfo = KeyboardHook.InjectedExtraInfo
        };
        return input;
    }

    private ModifierSafeInputBatch CreateModifierSafeBatch(
        IReadOnlyCollection<Win32.INPUT> payload,
        bool ctrl = false,
        bool alt = false,
        bool shift = false)
    {
        var modifiers = new[]
        {
            new ModifierState((ushort)Win32.VK_CONTROL, ctrl),
            new ModifierState((ushort)Win32.VK_MENU, alt),
            new ModifierState((ushort)Win32.VK_SHIFT, shift),
            new ModifierState((ushort)Win32.VK_LWIN, Desired: false),
            new ModifierState((ushort)Win32.VK_RWIN, Desired: false)
        };
        var pressed = modifiers
            .Select(modifier => modifier with { Pressed = _isKeyPressed(modifier.VirtualKey) })
            .ToArray();
        var physicalModifiersToRestore = pressed
            .Where(modifier => modifier.Pressed && !modifier.Desired)
            .Select(modifier => modifier.VirtualKey)
            .ToArray();
        var syntheticModifiers = pressed
            .Where(modifier => modifier.Desired && !modifier.Pressed)
            .Select(modifier => modifier.VirtualKey)
            .ToArray();

        var inputs = new List<Win32.INPUT>(
            physicalModifiersToRestore.Length * 2 +
            syntheticModifiers.Length * 2 +
            payload.Count);

        // A hotkey modifier may still be physically down when the low-level hook
        // dispatches the action. Without this wrapper Ctrl+C becomes Ctrl+Shift+C
        // for Shift+Scroll and the capture either stalls or returns no text.
        // Neutralize only unwanted modifiers, execute one atomic SendInput batch,
        // then restore the physical state until the user releases the key.
        inputs.AddRange(physicalModifiersToRestore.Select(
            modifier => CreateKeyboardInput(modifier, isKeyUp: true)));
        inputs.AddRange(syntheticModifiers.Select(
            modifier => CreateKeyboardInput(modifier, isKeyUp: false)));
        var payloadStartIndex = inputs.Count;
        inputs.AddRange(payload);
        inputs.AddRange(syntheticModifiers
            .Reverse()
            .Select(modifier => CreateKeyboardInput(modifier, isKeyUp: true)));
        inputs.AddRange(physicalModifiersToRestore
            .Reverse()
            .Select(modifier => CreateKeyboardInput(modifier, isKeyUp: false)));

        return new ModifierSafeInputBatch(
            inputs.ToArray(),
            payloadStartIndex,
            physicalModifiersToRestore);
    }

    private void SendInputs(
        Win32.INPUT[] inputs,
        InputInjectionOperation operation,
        int requestedUnitCount,
        Func<int, int> affectedUnitCount,
        IReadOnlyCollection<ushort>? physicalModifiersToRestore = null)
    {
        var sent = (int)_inputSender(inputs);
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            ReleaseAcceptedKeyDowns(inputs, sent);
            RestoreStillPressedModifiers(physicalModifiersToRestore);
            throw new InputInjectionException(
                operation,
                requestedUnitCount,
                affectedUnitCount(sent),
                inputs.Length,
                sent,
                new Win32Exception(
                    error,
                    "SendInput was blocked or only partially accepted. The target may be elevated or unavailable."));
        }
    }

    private void RestoreStillPressedModifiers(
        IReadOnlyCollection<ushort>? physicalModifiersToRestore)
    {
        if (physicalModifiersToRestore == null || physicalModifiersToRestore.Count == 0)
            return;

        var restores = physicalModifiersToRestore
            .Where(modifier => _isKeyPressed(modifier))
            .Select(modifier => CreateKeyboardInput(modifier, isKeyUp: false))
            .ToArray();
        if (restores.Length == 0)
            return;

        try
        {
            _inputSender(restores);
        }
        catch
        {
            // Preserve the progress-aware failure from the original operation.
        }
    }

    private void ReleaseAcceptedKeyDowns(Win32.INPUT[] inputs, int acceptedEventCount)
    {
        if (acceptedEventCount <= 0)
            return;

        var pressed = new List<Win32.KEYBDINPUT>();
        foreach (var input in inputs.Take(Math.Min(acceptedEventCount, inputs.Length)))
        {
            if (input.type != Win32.INPUT_KEYBOARD)
                continue;

            var key = input.u.ki;
            if ((key.dwFlags & Win32.KEYEVENTF_KEYUP) == 0)
            {
                pressed.Add(key);
                continue;
            }

            var matchingIndex = pressed.FindLastIndex(candidate =>
                candidate.wVk == key.wVk && candidate.wScan == key.wScan);
            if (matchingIndex >= 0)
                pressed.RemoveAt(matchingIndex);
        }

        if (pressed.Count == 0)
            return;

        var releases = pressed
            .AsEnumerable()
            .Reverse()
            .Select(key =>
            {
                var release = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
                release.u.ki = key;
                release.u.ki.dwFlags |= Win32.KEYEVENTF_KEYUP;
                return release;
            })
            .ToArray();

        try
        {
            _inputSender(releases);
        }
        catch
        {
            // Preserve the original progress-aware failure. Cleanup is best effort.
        }
    }

    internal static ushort MapStringToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') return (ushort)c;
            if (c >= '0' && c <= '9') return (ushort)c;

            short vk = Win32.VkKeyScan(key[0]);
            return (ushort)(vk & 0xFF);
        }
        var normalized = key.ToLowerInvariant();
        if (normalized.Length is 2 or 3 && normalized[0] == 'f' &&
            int.TryParse(normalized.AsSpan(1), out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            return (ushort)(0x70 + functionNumber - 1);
        }

        return normalized switch
        {
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "c" => (ushort)'C',
            "v" => (ushort)'V',
            "x" => (ushort)'X',
            "space" => 0x20,
            "enter" => 0x0D,
            "backspace" => 0x08,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            _ => 0
        };
    }

    private readonly record struct ModifierState(
        ushort VirtualKey,
        bool Desired,
        bool Pressed = false);

    private readonly record struct ModifierSafeInputBatch(
        Win32.INPUT[] Inputs,
        int PayloadStartIndex,
        ushort[] PhysicalModifiersToRestore);

    private readonly record struct TextInjectionUnit(char Value, int SourceUtf16Length);
}
