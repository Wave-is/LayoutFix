using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Native;
using LayoutFix.Services;

namespace LayoutFix.UI;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyCoordinator _hotkeyCoordinator;
    private readonly SettingsWindowProvider _settingsWindowProvider;
    private readonly ITranslatorWindowProvider _translatorWindowProvider;
    private readonly HookRecoveryCoordinator _hookRecoveryCoordinator;
    private readonly System.Windows.Forms.Timer _layoutTimer;
    private string _lastLayout = string.Empty;
    private bool _lastUseFlagIcons = true;
    private bool _lastEnabled = true;
    private ToolStripMenuItem? _autoCorrectionMenuItem;
    private ToolStripMenuItem? _hookStatusMenuItem;
    private bool _updatingMenuState;
    private bool _disposed;

    public TrayManager(
        ISettingsService settingsService,
        IHotkeyCoordinator hotkeyCoordinator,
        SettingsWindowProvider settingsWindowProvider,
        ITranslatorWindowProvider translatorWindowProvider,
        HookRecoveryCoordinator hookRecoveryCoordinator)
    {
        _settingsService = settingsService;
        _lastUseFlagIcons = _settingsService.Current.UseFlagIcons;
        _lastEnabled = _settingsService.Current.AutoConversionEnabled;
        _hotkeyCoordinator = hotkeyCoordinator;
        _settingsWindowProvider = settingsWindowProvider;
        _translatorWindowProvider = translatorWindowProvider;
        _hookRecoveryCoordinator = hookRecoveryCoordinator;

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

        _hookRecoveryCoordinator.OperationalStateChanged += HookRecovery_OperationalStateChanged;
        UpdateHookStatusMenuItem();
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
        _settingsWindowProvider.Show();
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
            if (_autoCorrectionMenuItem != null)
            {
                _updatingMenuState = true;
                _autoCorrectionMenuItem.Checked = currentEnabled;
                _updatingMenuState = false;
            }
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
        text = text.ToUpperInvariant();
        
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

        // Red means the input hooks are reconnecting. Otherwise orange means
        // opt-in automatic correction is active and blue means manual mode.
        var hooksOperational = _hookRecoveryCoordinator.IsOperational;
        var borderColor = hooksOperational
            ? (isEnabled ? Color.Orange : Color.DodgerBlue)
            : Color.Crimson;
        using (var borderPen = new Pen(borderColor, 1))
        {
            graphics.DrawRectangle(borderPen, 0, 0, size - 1, size - 1);
        }

        var iconHandle = bitmap.GetHicon();
        Icon newIcon;
        try
        {
            using var temporaryIcon = Icon.FromHandle(iconHandle);
            newIcon = (Icon)temporaryIcon.Clone();
        }
        finally
        {
            Win32.DestroyIcon(iconHandle);
        }

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = newIcon;
        _notifyIcon.Text = !hooksOperational
            ? "LayoutFix — reconnecting input hooks"
            : isEnabled
                ? "LayoutFix — automatic correction on"
                : "LayoutFix — manual correction active";
        oldIcon?.Dispose();
    }

    private void HookRecovery_OperationalStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed)
            return;

        UpdateHookStatusMenuItem();
        UpdateTrayIcon();
    }

    private void UpdateHookStatusMenuItem()
    {
        if (_hookStatusMenuItem != null)
        {
            _hookStatusMenuItem.Text = _hookRecoveryCoordinator.IsOperational
                ? "Input hooks: active"
                : "Input hooks: reconnecting…";
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

        _hookStatusMenuItem = new ToolStripMenuItem
        {
            Enabled = false
        };
        menu.Items.Add(_hookStatusMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        _autoCorrectionMenuItem = new ToolStripMenuItem("Automatic correction")
        {
            CheckOnClick = true,
            Checked = _settingsService.Current.AutoConversionEnabled
        };
        _autoCorrectionMenuItem.CheckedChanged += (_, _) =>
        {
            if (_updatingMenuState) return;
            _settingsService.Current.AutoConversionEnabled = _autoCorrectionMenuItem.Checked;
            _settingsService.Save(_settingsService.Current);
            _lastEnabled = _autoCorrectionMenuItem.Checked;
            UpdateTrayIcon();
        };
        menu.Items.Add(_autoCorrectionMenuItem);
        menu.Items.Add("Undo last auto-correction", null, async (s, e) =>
            await _hotkeyCoordinator.ExecuteActionAsync(LayoutFix.Core.Models.HotkeyAction.Undo));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Translator...", null, (s, e) => OpenTranslator());
        menu.Items.Add("Settings...", null, (s, e) => ShowSettings());
        menu.Items.Add("About...", null, (s, e) => ShowSettings()); // Assuming About is a tab in Settings
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Application.Exit());

        return menu;
    }

    private void OpenTranslator()
    {
        _translatorWindowProvider.ShowTranslator();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hookRecoveryCoordinator.OperationalStateChanged -= HookRecovery_OperationalStateChanged;
        _layoutTimer.Stop();
        _layoutTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
