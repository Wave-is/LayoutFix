using System.Diagnostics;
using System.Runtime.InteropServices;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Input;
using LayoutFix.Infrastructure.Native;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.WindowsE2E;

internal static class EmbeddedFieldFixture
{
    [DllImport("user32.dll")] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern nint GetParent(nint child);
    private sealed class Log : ILoggerService
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message, Exception? ex = null) => Console.Error.WriteLine(message);
    }

    internal static int Child(nint owner, string state)
    {
        using var child = new Form { TopLevel = false, FormBorderStyle = FormBorderStyle.None };
        using var edit = new TextBox { Text = "ghbdtn vbh", Dock = DockStyle.Top };
        child.Controls.Add(edit);
        SetParent(child.Handle, owner);
        if (GetParent(child.Handle) != owner) return 2;
        child.Show();
        edit.Focus();
        edit.SelectAll();
        File.WriteAllText(state + ".ready", edit.Handle.ToInt64().ToString());
        edit.TextChanged += (_, _) => File.WriteAllText(state, edit.Text);
        Application.Run();
        return 0;
    }

    internal static int Run(Func<nint, bool> foreground)
    {
        using var host = new Form { Text = "LayoutFix isolated embedded field", TopMost = true };
        using var clip = new ClipboardService(new Log());
        var result = 1;
        var state = Path.Combine(Path.GetTempPath(), "LayoutFix-embedded-" + Guid.NewGuid().ToString("N"));
        host.Shown += async (_, _) =>
        {
            Process? child = null;
            IClipboardSnapshot? saved = null;
            try
            {
                if (!foreground(host.Handle)) throw new InvalidOperationException("Host not foreground.");
                saved = await clip.CaptureAsync();
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                start.ArgumentList.Add("--embedded-field-child");
                start.ArgumentList.Add(host.Handle.ToInt64().ToString());
                start.ArgumentList.Add(state);
                child = Process.Start(start)!;
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(state + ".ready") && deadline.ElapsedMilliseconds < 5000) await Task.Delay(25);
                var hwnd = (nint)long.Parse(await File.ReadAllTextAsync(state + ".ready"));
                var active = new ActiveWindowProvider();
                var window = active.CaptureActiveWindow();
                if (window.ForegroundWindow != host.Handle || window.FocusedWindow != hwnd ||
                    Win32.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid != child.Id || pid == window.ProcessId)
                    throw new InvalidOperationException("The isolated cross-process field does not have focus.");
                using var guard = new WindowsTextTargetGuard(active, new Log());
                var selected = await guard.TryReadSelectedTextAsync(window);
                if (!selected.IsSafeToModify || selected.Text != "ghbdtn vbh") throw new InvalidOperationException("Child UIA proof failed.");
                var tx = new TextTransactionService(new InputInjector(), clip, active, new Log(), guard);
                var capture = await tx.CaptureExplicitCorrectionAsync();
                if (capture?.Text != "ghbdtn vbh" || !capture.CopyOnly || await tx.ReplaceAsync(capture, "привет мир"))
                    throw new InvalidOperationException("Embedded safe fallback failed.");
                var after = await guard.TryReadSelectedTextAsync(window);
                if (after.Text != selected.Text || File.Exists(state)) throw new InvalidOperationException("Embedded field changed.");
                Console.WriteLine("embedded-field=pass separate-process=True selection=verified copyOnly=True originalPreserved=True");
                result = 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); }
            finally
            {
                if (child != null) { if (!child.HasExited) child.Kill(); child.WaitForExit(2000); child.Dispose(); }
                if (saved != null) { try { await clip.RestoreAsync(saved); } finally { saved.Dispose(); } }
                File.Delete(state); File.Delete(state + ".ready"); host.Close();
            }
        };
        Application.Run(host);
        return result;
    }
}
