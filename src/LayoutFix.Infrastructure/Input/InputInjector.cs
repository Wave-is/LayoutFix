using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.Infrastructure.Input;

public class InputInjector : IInputInjector
{
    private readonly ILoggerService _logger;

    public InputInjector(ILoggerService logger)
    {
        _logger = logger;
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

        inputs[idx++] = CreateKeyboardInput(vk, false);
        inputs[idx++] = CreateKeyboardInput(vk, true);

        if (shift) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_SHIFT, true);
        if (alt) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_MENU, true);
        if (ctrl) inputs[idx++] = CreateKeyboardInput((ushort)Win32.VK_CONTROL, true);

        uint res = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        if (res == 0)
        {
            _logger.LogError($"SendInput failed. Error: {Marshal.GetLastWin32Error()}", null!);
        }
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
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        await Task.Delay(20);
    }

    public async Task ReleaseModifiersAsync()
    {
        var inputs = new Win32.INPUT[5];
        inputs[0] = CreateKeyboardInput((ushort)Win32.VK_SHIFT, true);
        inputs[1] = CreateKeyboardInput((ushort)Win32.VK_MENU, true);
        inputs[2] = CreateKeyboardInput((ushort)Win32.VK_CONTROL, true);
        inputs[3] = CreateKeyboardInput((ushort)Win32.VK_LWIN, true);
        inputs[4] = CreateKeyboardInput((ushort)Win32.VK_RWIN, true);
        
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        await Task.Delay(50);
    }

    public async Task SendTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new Win32.INPUT[text.Length * 2];
        int idx = 0;

        foreach (char c in text)
        {
            var down = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
            down.u.ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Win32.KEYEVENTF_UNICODE,
                dwExtraInfo = KeyboardHook.InjectedExtraInfo
            };
            inputs[idx++] = down;

            var up = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
            up.u.ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
                dwExtraInfo = KeyboardHook.InjectedExtraInfo
            };
            inputs[idx++] = up;
        }

        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
        await Task.Delay(50);
    }

    public async Task SelectWordLeftAsync()
    {
        await SendKeyCombinationAsync(true, false, true, "left");
    }

    public async Task<string?> GetClipboardTextAsync()
    {
        string? result = null;
        var t = new Thread(() =>
        {
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        result = Clipboard.GetText();
                    }
                    break;
                }
                catch (ExternalException ex)
                {
                    _logger.LogWarning($"GetClipboardTextAsync failed (retries left: {retries - 1}). Exception: {ex.Message}");
                    retries--;
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Unexpected error in GetClipboardTextAsync", ex);
                    break;
                }
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return await Task.FromResult(result);
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var t = new Thread(() =>
        {
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        Clipboard.Clear();
                    }
                    else
                    {
                        Clipboard.SetText(text);
                    }
                    break;
                }
                catch (ExternalException ex)
                {
                    _logger.LogWarning($"SetClipboardTextAsync failed (retries left: {retries - 1}). Exception: {ex.Message}");
                    retries--;
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Unexpected error in SetClipboardTextAsync", ex);
                    break;
                }
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        await Task.CompletedTask;
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

    private ushort MapStringToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') return (ushort)c;
            if (c >= '0' && c <= '9') return (ushort)c;

            short vk = Win32.VkKeyScan(key[0]);
            return (ushort)(vk & 0xFF);
        }
        return key.ToLowerInvariant() switch
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
            _ => 0
        };
    }
}
