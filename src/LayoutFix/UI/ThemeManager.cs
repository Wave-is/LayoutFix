using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LayoutFix.UI;

public static class ThemeManager
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static bool IsDarkTheme()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return value != null && (int)value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyTheme(Form form)
    {
        bool isDark = IsDarkTheme();
        
        // 1. Title bar
        if (Environment.OSVersion.Version.Major >= 10)
        {
            int useImmersiveDarkMode = isDark ? 1 : 0;
            var buildInfo = Environment.OSVersion.Version.Build;
            int attribute = buildInfo < 18985 ? DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 : DWMWA_USE_IMMERSIVE_DARK_MODE;
            DwmSetWindowAttribute(form.Handle, attribute, ref useImmersiveDarkMode, sizeof(int));
        }

        // 2. Form Background
        var backColor = isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
        var foreColor = isDark ? Color.White : SystemColors.ControlText;

        form.BackColor = backColor;
        form.ForeColor = foreColor;

        // 3. Child Controls
        ApplyThemeToControls(form.Controls, isDark);
    }

    private static void ApplyThemeToControls(Control.ControlCollection controls, bool isDark)
    {
        var backColor = isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
        var foreColor = isDark ? Color.White : SystemColors.ControlText;
        var panelBackColor = isDark ? Color.FromArgb(40, 40, 40) : SystemColors.Window;
        var inputBackColor = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Window;

        foreach (Control control in controls)
        {
            if (control is Panel || control is TabPage)
            {
                control.BackColor = panelBackColor;
                control.ForeColor = foreColor;
            }
            else if (control is TextBox || control is ListBox)
            {
                control.BackColor = inputBackColor;
                control.ForeColor = foreColor;
                if (control is TextBox txt) txt.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is Button btn)
            {
                btn.BackColor = isDark ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
                btn.ForeColor = foreColor;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = isDark ? Color.FromArgb(100, 100, 100) : Color.Gray;
            }
            else if (control is DataGridView grid)
            {
                grid.BackgroundColor = panelBackColor;
                grid.GridColor = isDark ? Color.FromArgb(60, 60, 60) : SystemColors.ControlDark;
                grid.DefaultCellStyle.BackColor = panelBackColor;
                grid.DefaultCellStyle.ForeColor = foreColor;
                grid.ColumnHeadersDefaultCellStyle.BackColor = isDark ? Color.FromArgb(50, 50, 50) : Color.LightBlue;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = foreColor;
                grid.EnableHeadersVisualStyles = false;
            }
            else if (control is Label || control is CheckBox || control is RadioButton || control is GroupBox)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = foreColor;
            }
            else if (control is NumericUpDown || control is ComboBox)
            {
                control.BackColor = inputBackColor;
                control.ForeColor = foreColor;
            }
            else
            {
                // Fallback for TabControl, TrackBar, etc.
                control.BackColor = panelBackColor;
                control.ForeColor = foreColor;
            }

            if (control.HasChildren)
            {
                ApplyThemeToControls(control.Controls, isDark);
            }
        }
    }
}
