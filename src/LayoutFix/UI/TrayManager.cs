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
    private readonly ILocalizationService _locService;
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
        HookRecoveryCoordinator hookRecoveryCoordinator,
        ILocalizationService locService)
    {
        _settingsService = settingsService;
        _lastUseFlagIcons = _settingsService.Current.UseFlagIcons;
        _lastEnabled = _settingsService.Current.AutoConversionEnabled;
        _hotkeyCoordinator = hotkeyCoordinator;
        _settingsWindowProvider = settingsWindowProvider;
        _translatorWindowProvider = translatorWindowProvider;
        _hookRecoveryCoordinator = hookRecoveryCoordinator;
        _locService = locService;

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
        bool isEnabled = _settingsService.Current.AutoConversionEnabled;
        var hooksOperational = _hookRecoveryCoordinator.IsOperational;
        using var bitmap = RenderTrayIconBitmap(
            _lastLayout,
            _settingsService.Current.UseFlagIcons,
            isEnabled,
            hooksOperational,
            size);

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
        var tooltip = !hooksOperational
            ? _locService.GetString("Tray_TooltipReconnecting", "LayoutFix — reconnecting input hooks")
            : isEnabled
                ? _locService.GetString("Tray_TooltipAutoOn", "LayoutFix — automatic correction on")
                : _locService.GetString("Tray_TooltipManual", "LayoutFix — manual correction active");
        // NotifyIcon.Text has a hard 63-character limit and throws if exceeded.
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        oldIcon?.Dispose();
    }

    internal static Bitmap RenderTrayIconBitmap(
        string? layout,
        bool useFlagIcons,
        bool automaticCorrectionEnabled,
        bool hooksOperational,
        int size)
    {
        size = Math.Clamp(size, 16, 64);
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

        var text = string.IsNullOrWhiteSpace(layout) ? "EN" : layout.Trim();
        if (text.Length > 2) text = text[..2];
        text = text.ToUpperInvariant();

        // The body itself carries the operating state, so the icon remains
        // readable at 16-24 px without the old square outline around "RU".
        var stateColor = hooksOperational
            ? (automaticCorrectionEnabled ? Color.FromArgb(232, 138, 0) : Color.FromArgb(0, 120, 215))
            : Color.FromArgb(205, 45, 58);
        var inset = Math.Max(1F, size * 0.06F);
        var body = new RectangleF(inset, inset, size - (2F * inset), size - (2F * inset));
        using var bodyPath = CreateRoundedIconPath(body, Math.Max(3F, size * 0.24F));

        if (useFlagIcons)
        {
            var savedState = graphics.Save();
            graphics.SetClip(bodyPath);
            DrawFlag(graphics, text, size);
            graphics.Restore(savedState);

            using var statePen = new Pen(stateColor, Math.Max(1.5F, size * 0.09F));
            graphics.DrawPath(statePen, bodyPath);
        }
        else
        {
            using var bodyBrush = new SolidBrush(stateColor);
            graphics.FillPath(bodyBrush, bodyPath);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(
                "Segoe UI",
                Math.Max(7F, size * 0.43F),
                FontStyle.Bold,
                GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            graphics.DrawString(text, font, textBrush, body, format);
        }

        return bitmap;
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedIconPath(
        RectangleF bounds,
        float radius)
    {
        var diameter = Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2F);
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
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
                ? _locService.GetString("Tray_HooksActive", "Input hooks: active")
                : _locService.GetString("Tray_HooksReconnecting", "Input hooks: reconnecting…");
        }
    }

    private static void DrawFlag(Graphics g, string langCode, int size)
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

        _autoCorrectionMenuItem = new ToolStripMenuItem(
            _locService.GetString("Tray_AutomaticCorrection", "Automatic correction"))
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
        menu.Items.Add(
            _locService.GetString("Settings_UndoAutoCorrection", "Undo last auto-correction"),
            null,
            async (s, e) => await _hotkeyCoordinator.ExecuteActionAsync(LayoutFix.Core.Models.HotkeyAction.Undo));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_locService.GetString("Tray_Translator", "Translator..."), null, (s, e) => OpenTranslator());
        menu.Items.Add(_locService.GetString("Tray_Settings", "Settings..."), null, (s, e) => ShowSettings());
        menu.Items.Add(_locService.GetString("Tray_About", "About..."), null, (s, e) => ShowSettings()); // Assuming About is a tab in Settings
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_locService.GetString("Tray_Exit", "Exit"), null, (s, e) => Application.Exit());

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
