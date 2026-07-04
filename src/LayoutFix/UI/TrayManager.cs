using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Native;

namespace LayoutFix.UI;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyCoordinator _hotkeyCoordinator;
    private readonly System.Windows.Forms.Timer _layoutTimer;
    private string _lastLayout = string.Empty;
    private bool _lastUseFlagIcons = true;
    private bool _lastEnabled = true;

    public TrayManager(ISettingsService settingsService, IHotkeyCoordinator hotkeyCoordinator)
    {
        _settingsService = settingsService;
        _lastUseFlagIcons = _settingsService.Current.UseFlagIcons;
        _lastEnabled = _settingsService.Current.AutoConversionEnabled;
        _hotkeyCoordinator = hotkeyCoordinator;

        _notifyIcon = new NotifyIcon
        {
            Text = "LayoutFix",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.MouseClick += NotifyIcon_MouseClick;

        _layoutTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _layoutTimer.Tick += LayoutTimer_Tick;
        _layoutTimer.Start();
        
        UpdateTrayIcon();
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowSettings();
        }
    }

    private void ShowSettings()
    {
        var form = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<SettingsForm>(AppHost.Services!);
        form.Show();
        form.Activate();
    }

    private void LayoutTimer_Tick(object? sender, EventArgs e)
    {
        string currentLayout = GetActiveLayout();
        bool currentUseFlags = _settingsService.Current.UseFlagIcons;
        bool currentEnabled = _settingsService.Current.AutoConversionEnabled;

        if (currentLayout != _lastLayout || currentUseFlags != _lastUseFlagIcons || currentEnabled != _lastEnabled)
        {
            _lastLayout = currentLayout;
            _lastUseFlagIcons = currentUseFlags;
            _lastEnabled = currentEnabled;
            UpdateTrayIcon();
        }
    }

    private string GetActiveLayout()
    {
        try
        {
            return Win32.GetActiveLayoutCode();
        }
        catch
        {
            return "??";
        }
    }

    private void UpdateTrayIcon()
    {
        int size = SystemInformation.SmallIconSize.Width;
        if (size < 16) size = 16;

        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        
        graphics.Clear(Color.Transparent);

        var text = string.IsNullOrEmpty(_lastLayout) ? "EN" : _lastLayout;
        if (text.Length > 2) text = text.Substring(0, 2);
        
        bool isEnabled = _settingsService.Current.AutoConversionEnabled;

        if (_settingsService.Current.UseFlagIcons)
        {
            DrawFlag(graphics, text, size);
        }
        else
        {
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float fontSize = (size / 16f) * 7.5f; 
            using var font = new Font("Segoe UI", fontSize, FontStyle.Regular);
            
            bool isLightTheme = false;
            try
            {
                var value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                if (value != null && (int)value == 1) isLightTheme = true;
            }
            catch { }
            
            using var brush = new SolidBrush(isLightTheme ? Color.Black : Color.White);
            
            var textSize = graphics.MeasureString(text, font);
            float x = (size - textSize.Width) / 2f;
            float y = (size - textSize.Height) / 2f;
            
            graphics.DrawString(text, font, brush, x, y);
        }

        // Draw status border
        using (var borderPen = new Pen(isEnabled ? Color.FromArgb(76, 175, 80) : Color.FromArgb(244, 67, 54), 1))
        {
            graphics.DrawRectangle(borderPen, 0, 0, size - 1, size - 1);
        }

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = Icon.FromHandle(bitmap.GetHicon());
        
        if (oldIcon != null)
        {
            Win32.DestroyIcon(oldIcon.Handle);
            oldIcon.Dispose();
        }
    }

    private void DrawFlag(Graphics g, string langCode, int size)
    {
        // Simple and stylish flags
        if (langCode == "EN" || langCode == "US")
        {
            // US Flag (Simplified)
            g.FillRectangle(Brushes.White, 0, 0, size, size);
            int stripeHeight = Math.Max(1, size / 5);
            for(int i = 0; i < size; i += stripeHeight)
            {
                if ((i / stripeHeight) % 2 == 0) 
                    using (var b = new SolidBrush(Color.FromArgb(178, 34, 52))) g.FillRectangle(b, 0, i, size, stripeHeight);
            }
            using (var b = new SolidBrush(Color.FromArgb(60, 59, 110))) g.FillRectangle(b, 0, 0, size / 2, size / 2);
            g.FillRectangle(Brushes.White, size / 4 - 1, size / 4 - 1, 2, 2); // Star
        }
        else if (langCode == "RU")
        {
            float h = size / 3f;
            g.FillRectangle(Brushes.White, 0, 0, size, h);
            using (var b = new SolidBrush(Color.FromArgb(0, 57, 166))) g.FillRectangle(b, 0, h, size, h);
            using (var b = new SolidBrush(Color.FromArgb(213, 43, 30))) g.FillRectangle(b, 0, h * 2, size, size - h * 2);
        }
        else if (langCode == "UK" || langCode == "UA") // Ukrainian
        {
            float h = size / 2f;
            using (var b = new SolidBrush(Color.FromArgb(0, 87, 183))) g.FillRectangle(b, 0, 0, size, h);
            using (var b = new SolidBrush(Color.FromArgb(255, 215, 0))) g.FillRectangle(b, 0, h, size, size - h);
        }
        else
        {
            g.FillRectangle(Brushes.Gray, 0, 0, size, size);
        }
        // Border is drawn in UpdateTrayIcon
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        
        menu.Items.Add("Settings...", null, (s, e) => ShowSettings());
        menu.Items.Add("About...", null, (s, e) => ShowSettings()); // Assuming About is a tab in Settings
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Application.Exit());

        return menu;
    }

    public void Dispose()
    {
        _layoutTimer.Stop();
        _layoutTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
