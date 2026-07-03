using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class ScreenshotGrabber
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

    [DllImport("user32.dll")]
    static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
    public delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    static void Main()
    {
        string exePath = Path.GetFullPath(@"..\src\LayoutFix\bin\Release\net8.0-windows\win-x64\LayoutFix.exe");
        if (!File.Exists(exePath))
        {
            Console.WriteLine("Exe not found: " + exePath);
            return;
        }

        Process p = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--settings",
            WorkingDirectory = Path.GetDirectoryName(exePath)
        });

        Thread.Sleep(3000); // wait for UI to render

        // The Settings window title depends on locale. Usually "Settings - LayoutFix" or "Настройки - LayoutFix"
        // Let's iterate all windows of this process
        IntPtr hwnd = IntPtr.Zero;
        if (p.MainWindowHandle != IntPtr.Zero)
            hwnd = p.MainWindowHandle;
        else
        {
            foreach (ProcessThread pt in p.Threads)
            {
                EnumThreadWindows((uint)pt.Id, (hWnd, lParam) =>
                {
                    hwnd = hWnd;
                    return false;
                }, IntPtr.Zero);
                if (hwnd != IntPtr.Zero) break;
            }
        }

        if (hwnd != IntPtr.Zero)
        {
            Thread.Sleep(1000); // let animations finish
            RECT rc;
            GetWindowRect(hwnd, out rc);
            if (rc.Width > 0 && rc.Height > 0)
            {
                using (Bitmap bmp = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics gfxBmp = Graphics.FromImage(bmp))
                    {
                        IntPtr hdcBitmap = gfxBmp.GetHdc();
                        PrintWindow(hwnd, hdcBitmap, 2); // 2 = PW_RENDERFULLCONTENT (captures DirectComposition on win 10/11)
                        gfxBmp.ReleaseHdc(hdcBitmap);
                    }
                    string outPath = Path.Combine(Environment.CurrentDirectory, "screenshot_settings.png");
                    bmp.Save(outPath, ImageFormat.Png);
                    Console.WriteLine("Screenshot saved to " + outPath);
                }
            }
        }
        else
        {
            Console.WriteLine("Window handle not found!");
        }

        p.Kill();
    }
}
