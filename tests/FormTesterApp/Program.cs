using System;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using LayoutFix;
using LayoutFix.UI;
using LayoutFix.Core;
using LayoutFix.Core.Interfaces;

class Program
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("Initializing DI container...");
        AppHost.Build();
        
        var settingsService = AppHost.Services.GetRequiredService<ISettingsService>();
        var autoStartService = AppHost.Services.GetRequiredService<IAutoStartService>();
        var locService = AppHost.Services.GetRequiredService<ILocalizationService>();
        
        Console.WriteLine("Creating SettingsForm...");
        try 
        {
            var form = new SettingsForm(settingsService, autoStartService, locService);
            
            // Check form properties
            int controlsCount = form.Controls.Count;
            Console.WriteLine($"Form created successfully with {controlsCount} direct controls.");
            
            int checkBoxes = 0;
            int buttons = 0;
            int tabs = 0;
            
            void CountControls(Control.ControlCollection controls)
            {
                foreach (Control c in controls)
                {
                    if (c is CheckBox) checkBoxes++;
                    if (c is Button) buttons++;
                    if (c is TabControl) tabs++;
                    if (c is TabPage) tabs++;
                    CountControls(c.Controls);
                }
            }
            
            CountControls(form.Controls);
            
            Console.WriteLine($"Found Tabs: {tabs}");
            Console.WriteLine($"Found CheckBoxes: {checkBoxes}");
            Console.WriteLine($"Found Buttons: {buttons}");
            
            if (checkBoxes == 0 || buttons == 0)
            {
                Console.WriteLine("FAIL: Form seems empty or missing essential controls.");
                Environment.Exit(1);
            }
            
            Console.WriteLine("TEST PASSED: UI Component instantiates correctly with controls.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: Exception during UI creation:");
            Console.WriteLine(ex.ToString());
            Environment.Exit(1);
        }
    }
}
