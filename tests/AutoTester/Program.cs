using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Hooks;
using LayoutFix.Infrastructure.Input;
using LayoutFix.Infrastructure.Layouts;
using LayoutFix.Infrastructure.Services;

namespace AutoTester;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Console.WriteLine("Starting AutoTester...");
        
        var logger = new FileLoggerService();
        var hook = new KeyboardHook(logger);
        var injector = new InputInjector(logger);
        var settings = new SettingsService();
        var layoutManager = new KeyboardLayoutManager(settings, new WindowsLayoutProvider());
        var converter = new LayoutConverter();
        var transformer = new TextTransformer();
        var translit = new TransliterationService();
        var numbers = new NumberToTextConverter();

        var coordinator = new HotkeyCoordinator(
            hook, injector, settings, layoutManager, converter, transformer, translit, numbers, logger);

        coordinator.Initialize();
        hook.Start();

        Console.WriteLine("Hook started. Creating test form...");

        var form = new Form { Width = 300, Height = 200, TopMost = true };
        var tb = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        form.Controls.Add(tb);
        
        form.Shown += async (s, e) => 
        {
            tb.Focus();
            await Task.Delay(500);

            Console.WriteLine("Injecting text 'ghbdtn'...");
            tb.Text = "ghbdtn";
            tb.SelectAll();
            
            await Task.Delay(500);

            Console.WriteLine("Invoking ExecuteActionAsync directly...");
            await coordinator.ExecuteActionAsync(HotkeyAction.FixLayout);

            await Task.Delay(2000); // wait for coordinator to finish

            Console.WriteLine($"Final text in textbox: '{tb.Text}'");

            if (tb.Text == "привет")
            {
                Console.WriteLine("SUCCESS: Layout auto-conversion worked!");
            }
            else
            {
                Console.WriteLine("ERROR: Conversion failed.");
            }

            form.Close();
        };

        Application.Run(form);

        hook.Stop();
        Console.WriteLine("AutoTester finished.");
    }
}
