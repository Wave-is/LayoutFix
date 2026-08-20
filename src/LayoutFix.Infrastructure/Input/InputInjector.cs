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
    private readonly Func<Win32.INPUT[], uint> _inputSender;

    public InputInjector()
        : this(inputs => Win32.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Win32.INPUT>()))
    {
    }

    internal InputInjector(Func<Win32.INPUT[], uint> inputSender)
    {
        _inputSender = inputSender ?? throw new ArgumentNullException(nameof(inputSender));
    }

    public async Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
    {
        ushort vk = MapStringToVirtualKey(key);
        if (vk == 0) return;

        int numInputs = 0;
        if (ctrl) numInputs += 2;
        if (alt) numInputs += 2;
        if (shift) numInputs += 2;
        numInputs += 2;

        var inputs = new Win32.INPUT[numInputs];
        int idx = 0;

        if (ctrl) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_CONTROL, false);
        if (alt) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_MENU, false);
        if (shift) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_SHIFT, false);

        var targetKeyDownIndex = idx;
        inputs[idx++] = CreateKeyboardInput(vk, false);
        inputs[idx++] = CreateKeyboardInput(vk, true);

        if (shift) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_SHIFT, true);
        if (alt) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_MENU, true);
        if (ctrl) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_CONTROL, true);

        SendInputs(
            inputs,
            InputInjectionOperation.KeyCombination,
            requestedUnitCount: 1,
            affectedUnitCount: sent => sent > targetKeyDownIndex ? 1 : 0);
        await Task.Delay(50);
    }

    public async Task SendBackspacesAsync(int count)
    {
        if (count <= 0) return;
        var inputs = new Win32.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = CreateKeyboardInput(0x08, false);
            inputs[i * 2 + 1] = CreateKeyboardInput(0x08, true);
        }
        SendInputs(
            inputs,
            InputInjectionOperation.Backspace,
            requestedUnitCount: count,
            affectedUnitCount: sent => Math.Min(count, (sent + 1) / 2));
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
        var inputs = new Win32.INPUT[textUnits.Count * 2];
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
            inputs[inputIndex++] = down;

            var up = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
            up.u.ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = unit.Value,
                dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
                dwExtraInfo = KeyboardHook.InjectedExtraInfo
            };
            inputs[inputIndex++] = up;
        }

        SendInputs(
            inputs,
            InputInjectionOperation.Text,
            requestedUnitCount: text.Length,
            affectedUnitCount: sent => textUnits
                .Take(Math.Min(textUnits.Count, (sent + 1) / 2))
                .Sum(unit => unit.SourceUtf16Length));
        await Task.Delay(50);
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

    private void SendInputs(
        Win32.INPUT[] inputs,
        InputInjectionOperation operation,
        int requestedUnitCount,
        Func<int, int> affectedUnitCount)
    {
        var sent = (int)_inputSender(inputs);
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            ReleaseAcceptedKeyDowns(inputs, sent);
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

    private readonly record struct TextInjectionUnit(char Value, int SourceUtf16Length);
}
