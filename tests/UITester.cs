using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

class UITester
{
    static void Main()
    {
        string exePath = @"..\src\LayoutFix\bin\Release\net8.0-windows\win-x64\publish\LayoutFix.exe";
        Console.WriteLine("Starting LayoutFix...");
        Process p = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--settings",
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)
        });

        Thread.Sleep(3000); // wait for UI

        Console.WriteLine("Finding Settings window...");
        AutomationElement window = null;
        for(int i=0; i<10; i++)
        {
            window = AutomationElement.RootElement.FindFirst(TreeScope.Children, 
                new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id));
            if (window != null && (window.Current.Name.Contains("Settings") || window.Current.Name.Contains("Настройки")))
                break;
            Thread.Sleep(500);
        }

        if (window == null)
        {
            Console.WriteLine("FAIL: Settings window not found!");
            p.Kill();
            Environment.Exit(1);
        }

        Console.WriteLine("SUCCESS: Found window - " + window.Current.Name);

        // Scan controls
        var elements = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        int tabsCount = 0;
        int checkBoxesCount = 0;

        foreach (AutomationElement el in elements)
        {
            if (el.Current.ControlType == ControlType.TabItem)
                tabsCount++;
            else if (el.Current.ControlType == ControlType.CheckBox)
                checkBoxesCount++;
        }

        Console.WriteLine($"Found {tabsCount} Tab Items.");
        Console.WriteLine($"Found {checkBoxesCount} CheckBoxes.");

        if (tabsCount == 0) Console.WriteLine("FAIL: No tabs found! The UI might be broken.");
        if (checkBoxesCount == 0) Console.WriteLine("FAIL: No checkboxes found!");

        p.Kill();
        Console.WriteLine("Test completed.");
    }
}
