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
using Xunit;

namespace LayoutFix.IntegrationTests;

public class UIAutomationTests
{
    [Fact]
    public void Test_AutoConvert_EndToEnd()
    {
        // This test simulates a real user typing in a textbox and pressing the hotkey
        // It requires an active desktop session.

        bool success = false;
        string finalResult = string.Empty;

        var t = new Thread(async () =>
        {
            try
            {
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

                // Create a dummy form with a textbox
                using var form = new Form { Width = 300, Height = 200, TopMost = true };
                var tb = new TextBox { Dock = DockStyle.Fill, Multiline = true };
                form.Controls.Add(tb);
                form.Show();
                tb.Focus();

                // Wait for form to be ready
                await Task.Delay(500);

                // Inject text 'ghbdtn'
                tb.Text = "ghbdtn";
                tb.SelectionStart = tb.Text.Length;
                
                await Task.Delay(200);

                // Inject Shift+PrintScreen (our default hotkey for FixLayout)
                await injector.SendKeyCombinationAsync(false, false, true, "printscreen");

                // Wait for coordinator to process it
                await Task.Delay(1000);

                finalResult = tb.Text;
                success = finalResult == "привет";

                hook.Stop();
                form.Close();
            }
            catch
            {
                // Ignore
            }
            finally
            {
                Application.ExitThread();
            }
        });

        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        
        // Run message loop for the thread if necessary, or just wait
        t.Join(5000);

        // Assert
        Assert.True(success, $"Expected 'привет', but got '{finalResult}'");
    }
}
