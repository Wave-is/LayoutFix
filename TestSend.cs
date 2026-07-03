using System;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public struct INPUT
    {
        public uint type;
        public KEYBDINPUT u;
    }

    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    static void Main()
    {
        Console.WriteLine("Waiting 2s...");
        Thread.Sleep(2000);
        
        ushort[] keys = { 0x47, 0x48, 0x42, 0x44, 0x54, 0x4E, 0x20 };
        foreach (var k in keys)
        {
            var inputs = new INPUT[2];
            inputs[0].type = 1; // KEYBOARD
            inputs[0].u = new KEYBDINPUT { wVk = k, dwFlags = 0 };
            inputs[1].type = 1;
            inputs[1].u = new KEYBDINPUT { wVk = k, dwFlags = 2 }; // KEYUP
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            Thread.Sleep(50);
        }
        Console.WriteLine("Done sending.");
    }
}
