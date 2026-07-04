using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using LayoutFix.Core;
using LayoutFix.UI;

class FormTester
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("Initializing fake services...");
        var settingsService = new SettingsService();
        var localizationService = new LocalizationService();
        var autoStartService = new AutoStartService();
        // Since we are just testing UI creation, we don't need real keyboard manager
        
        Console.WriteLine("Creating SettingsForm...");
        try 
        {
            var form = new SettingsForm(settingsService, localizationService, autoStartService, null, null);
            
            // Check form properties
            int controlsCount = form.Controls.Count;
            Console.WriteLine($"Form created successfully with {controlsCount} direct controls.");
            
            // We can recursively count all controls (tabs, checkboxes, etc)
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
