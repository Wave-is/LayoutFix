using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

class E2E_Emulator
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    const int KEYEVENTF_KEYUP = 0x0002;

    static void Main()
    {
        Console.WriteLine("Starting E2E Emulator...");
        
        // Start Notepad
        Process notepad = Process.Start("notepad.exe");
        Thread.Sleep(2000); // Wait for notepad to load
        
        SetForegroundWindow(notepad.MainWindowHandle);
        Thread.Sleep(500);

        // Find the text box in notepad
        AutomationElement edit = null;
        var window = AutomationElement.FromHandle(notepad.MainWindowHandle);
        var children = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement child in children)
        {
            if (child.Current.ControlType == ControlType.Document || child.Current.ControlType == ControlType.Edit)
            {
                edit = child;
                break;
            }
        }

        if (edit == null)
        {
            Console.WriteLine("Could not find Notepad text area.");
            notepad.Kill();
            return;
        }

        Console.WriteLine("Notepad ready. Typing 'vfibyf '...");
        
        // Type "vfibyf " (マшина in Russian layout, but typed on EN layout)
        // v = 0x56, f = 0x46, i = 0x49, b = 0x42, y = 0x59, f = 0x46, space = 0x20
        byte[] vks = { 0x56, 0x46, 0x49, 0x42, 0x59, 0x46, 0x20 };
        foreach (var vk in vks)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(100);
        }

        Console.WriteLine("Waiting for Auto-conversion...");
        Thread.Sleep(2000); // Wait for LayoutFix to intercept, backspace, change layout, and retype

        // Read value
        object patternObj;
        if (edit.TryGetCurrentPattern(TextPattern.Pattern, out patternObj))
        {
            var textPattern = (TextPattern)patternObj;
            string text = textPattern.DocumentRange.GetText(-1).Trim();
            Console.WriteLine($"Result text: '{text}'");
            
            if (text == "машина")
            {
                Console.WriteLine("TEST PASSED!");
            }
            else
            {
                Console.WriteLine("TEST FAILED! Expected 'машина'.");
            }
        }
        else if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
        {
            var valuePattern = (ValuePattern)patternObj;
            string text = valuePattern.Current.Value.Trim();
            Console.WriteLine($"Result text: '{text}'");
        }
        else
        {
            Console.WriteLine("Could not read text from Notepad.");
        }

        // Cleanup
        notepad.Kill();
    }
}
