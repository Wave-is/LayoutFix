using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    const int KEYEVENTF_KEYUP = 0x0002;

    static void Main()
    {
        Console.WriteLine("Starting UI_TestApp...");
        
        string appPath = @"..\UI_TestApp\bin\Debug\net8.0-windows\UI_TestApp.exe";
        string outPath = @"..\UI_TestApp\bin\Debug\net8.0-windows\output.txt";
        
        if (File.Exists(outPath)) File.Delete(outPath);

        Process? testApp = Process.Start(new ProcessStartInfo
        {
            FileName = appPath,
            WorkingDirectory = Path.GetDirectoryName(appPath)
        });
        
        Thread.Sleep(3000); // Wait for window to load
        
        SetForegroundWindow(testApp!.MainWindowHandle);
        Thread.Sleep(500);

        Console.WriteLine("Typing 'vfibyf '...");
        
        System.Windows.Forms.SendKeys.SendWait("vfibyf ");

        Console.WriteLine("Waiting for Auto-conversion...");
        Thread.Sleep(2000);

        // Read output
        if (File.Exists(outPath))
        {
            string text = File.ReadAllText(outPath).Trim();
            Console.WriteLine($"Result text: '{text}'");
            
            if (text.Contains("машина"))
            {
                Console.WriteLine("TEST PASSED: Auto-conversion to Russian works.");
            }
            else if (text.Contains("vfibyf"))
            {
                Console.WriteLine("TEST FAILED: No conversion occurred.");
            }
            else
            {
                Console.WriteLine("TEST FAILED: Unexpected text.");
            }
        }
        else
        {
            Console.WriteLine("TEST FAILED: output.txt not found.");
        }

        // Cleanup
        testApp!.Kill();
    }
}
