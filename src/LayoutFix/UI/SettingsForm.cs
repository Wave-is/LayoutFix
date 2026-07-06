using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.UI.Controls;

namespace LayoutFix.UI;

public class SettingsForm : Form
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoStartService _autoStartService;
    private readonly ILocalizationService _locService;
    private AppSettings _currentSettings;

    // UI Panels
    private Panel _pnlTopBar = null!;
    private Panel _pnlSidebar = null!;
    private Panel _pnlContent = null!;
    private Button _activeTabButton = null!;
    private Button _btnGeneral = null!;

    // Tabs
    private Panel _tabGeneral = null!;
    private Panel _tabLanguages = null!;
    private Panel _tabHotkeys = null!;
    private Panel _tabExceptions = null!;
    private Panel _tabDict = null!;
    private Panel _tabTranslate = null!;
    private Panel _tabAbout = null!;

    // Form settings
    private Color _bgColor = Color.FromArgb(28, 28, 30);
    private Color _sidebarColor = Color.FromArgb(35, 35, 38);
    private Color _textColor = Color.White;
    private Color _accentColor = Color.FromArgb(0, 120, 215);

    public SettingsForm(ISettingsService settingsService, IAutoStartService autoStartService, ILocalizationService locService)
    {
        _settingsService = settingsService;
        _autoStartService = autoStartService;
        _locService = locService;
        _currentSettings = _settingsService.Current;

        InitializeComponent();
        this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ApplyTheme();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _btnGeneral?.PerformClick();
    }

    private void InitializeComponent()
    {
        this.Text = _locService.GetString("Settings_Title", "LayoutFix");
        this.Size = new Size(850, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font = new Font("Segoe UI", 10F);
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = _bgColor;

        _pnlTopBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
        _pnlTopBar.MouseDown += PnlTopBar_MouseDown;
        
        var lblTitle = new Label { Text = "LayoutFix", ForeColor = _textColor, Font = new Font("Segoe UI", 12F, FontStyle.Bold), AutoSize = true, Location = new Point(20, 10) };
        lblTitle.MouseDown += PnlTopBar_MouseDown;

        var btnClose = new Button { Text = "✕", ForeColor = _textColor, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Right, Width = 40, Cursor = Cursors.Hand };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (s, e) => this.Close();

        _pnlTopBar.Controls.Add(lblTitle);
        _pnlTopBar.Controls.Add(btnClose);

        _pnlSidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 220,
            BackColor = _sidebarColor,
            Padding = new Padding(0, 20, 0, 0)
        };

        _pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _bgColor,
            Padding = new Padding(30)
        };

        this.Controls.Add(_pnlContent);
        this.Controls.Add(_pnlSidebar);
        this.Controls.Add(_pnlTopBar);

        InitializeTabs();
        InitializeSidebar();

        this.FormClosing += (s, e) =>
        {
            SaveSettings();
        };
    }

    private void SaveSettings()
    {
        _settingsService.Save(_currentSettings);
        _autoStartService.IsAutoStartEnabled = _currentSettings.AutoStart;
    }

    private void ApplyTheme()
    {
        bool isDark = _currentSettings.AppTheme == "Dark" || (_currentSettings.AppTheme == "Auto" && IsSystemDarkTheme());
        
        _bgColor = isDark ? Color.FromArgb(28, 28, 30) : Color.FromArgb(243, 243, 243);
        _sidebarColor = isDark ? Color.FromArgb(35, 35, 38) : Color.FromArgb(230, 230, 230);
        _textColor = isDark ? Color.White : Color.Black;

        this.BackColor = _bgColor;
        _pnlSidebar.BackColor = _sidebarColor;
        _pnlContent.BackColor = _bgColor;

        UpdateThemeRecursive(this);

        if (_activeTabButton != null)
        {
            _activeTabButton.BackColor = _accentColor;
            _activeTabButton.ForeColor = Color.White;
        }
    }

    private void UpdateThemeRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is CardPanel card)
            {
                card.CardBackColor = _bgColor == Color.FromArgb(28, 28, 30) ? Color.FromArgb(35, 35, 35) : Color.White;
                card.CardBorderColor = _bgColor == Color.FromArgb(28, 28, 30) ? Color.FromArgb(60, 60, 60) : Color.LightGray;
                card.Invalidate();
            }
            else if (c is ToggleSwitch ts)
            {
                if (c.Parent is CardPanel) ts.BackColor = (c.Parent as CardPanel)!.CardBackColor;
                else ts.BackColor = _bgColor;
                ts.Invalidate();
            }
            else if (c is Label lbl && lbl.ForeColor != _accentColor)
            {
                lbl.ForeColor = lbl.Font.Bold ? _textColor : (lbl.Text.StartsWith("Keyboard:") || lbl.Text.StartsWith("v1.0") ? Color.Gray : _textColor);
            }
            else if (c is Button btn)
            {
                if (btn.Parent == _pnlSidebar && btn != _activeTabButton)
                {
                    btn.ForeColor = _bgColor == Color.FromArgb(28, 28, 30) ? Color.DarkGray : Color.DimGray;
                }
                else if (btn.Parent == _pnlTopBar)
                {
                    btn.ForeColor = _textColor;
                }
            }
            else if (c is ComboBox cb)
            {
                cb.BackColor = _sidebarColor;
                cb.ForeColor = _textColor;
            }
            else if (c is ListBox lb)
            {
                lb.BackColor = _sidebarColor;
                lb.ForeColor = _textColor;
            }
            else if (c is TextBox tb && !tb.ReadOnly)
            {
                tb.BackColor = _sidebarColor;
                tb.ForeColor = _textColor;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is TableLayoutPanel tlp)
            {
                tlp.ForeColor = _textColor;
                tlp.BackColor = _bgColor;
            }

            if (c.HasChildren) UpdateThemeRecursive(c);
        }
    }

    private bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val != null && val is int i) return i == 0;
        }
        catch { }
        return true;
    }

    private void InitializeSidebar()
    {
        _btnGeneral = AddSidebarButton("⚙️ " + _locService.GetString("Settings_General", "Settings"), _tabGeneral);
        AddSidebarButton("🌐 " + _locService.GetString("Settings_Languages", "Languages"), _tabLanguages);
        AddSidebarButton("⌨️ " + _locService.GetString("Settings_Hotkeys", "Hotkeys"), _tabHotkeys);
        AddSidebarButton("🛡️ " + _locService.GetString("Settings_Exceptions", "Exceptions"), _tabExceptions);
        AddSidebarButton("📖 " + _locService.GetString("Settings_Dict", "Dictionary"), _tabDict);
        AddSidebarButton("🌍 " + _locService.GetString("Settings_Translate", "Auto-Translate"), _tabTranslate);
        AddSidebarButton("ℹ️ " + _locService.GetString("Settings_About", "About"), _tabAbout);
    }

    private Button AddSidebarButton(string text, Panel targetPanel)
    {
        var btn = new Button
        {
            Text = "  " + text,
            Dock = DockStyle.Top,
            Height = 50,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            ForeColor = Color.DarkGray,
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent
        };
        btn.FlatAppearance.BorderSize = 0;

        btn.Click += (s, e) =>
        {
            foreach (Control c in _pnlContent.Controls) c.Visible = false;
            foreach (Control c in _pnlSidebar.Controls)
            {
                if (c is Button b && b.Parent == _pnlSidebar)
                {
                    b.ForeColor = _bgColor == Color.FromArgb(28, 28, 30) ? Color.DarkGray : Color.DimGray;
                    b.BackColor = Color.Transparent;
                    b.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
                }
            }
            
            targetPanel.Visible = true;
            targetPanel.BringToFront();

            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btn.BackColor = _accentColor;
            _activeTabButton = btn;
        };

        _pnlSidebar.Controls.Add(btn);
        btn.BringToFront();
        return btn;
    }

    private void InitializeTabs()
    {
        _tabGeneral = CreateTabPanel();
        _tabLanguages = CreateTabPanel();
        _tabHotkeys = CreateTabPanel();
        _tabExceptions = CreateTabPanel();
        _tabDict = CreateTabPanel();
        _tabTranslate = CreateTabPanel();
        _tabAbout = CreateTabPanel();

        BuildGeneralTab();
        BuildLanguagesTab();
        BuildHotkeysTab();
        BuildExceptionsTab();
        BuildDictionaryTab();
        BuildTranslateTab();
        BuildAboutTab();

        _pnlContent.Controls.AddRange(new[] { _tabGeneral, _tabLanguages, _tabHotkeys, _tabExceptions, _tabDict, _tabTranslate, _tabAbout });
    }

    private Panel CreateTabPanel()
    {
        return new Panel { Dock = DockStyle.Fill, Visible = false };
    }

    private void BuildGeneralTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 0;
        var lblTitle = new Label { Text = _locService.GetString("Settings_General", "App Settings"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) };
        pnl.Controls.Add(lblTitle);
        y += 50;

        pnl.Controls.Add(CreateToggleSetting("Automatic Conversion", _currentSettings.AutoConversionEnabled, v => { _currentSettings.AutoConversionEnabled = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting("Start on Boot", _currentSettings.AutoStart, v => { _currentSettings.AutoStart = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting("Enable Sound Notifications", _currentSettings.SoundEnabled, v => { _currentSettings.SoundEnabled = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting("Show Country Flags in Tray", _currentSettings.UseFlagIcons, v => { _currentSettings.UseFlagIcons = v; SaveSettings(); }, y)); y += 50;

        var pnlTheme = new Panel { Width = 500, Height = 40, Location = new Point(0, y) };
        var lblTheme = new Label { Text = "Color Theme", ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        var cmbTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(350, 8), BackColor = _sidebarColor, ForeColor = _textColor };
        cmbTheme.Items.AddRange(new[] { "Auto", "Light", "Dark" });
        cmbTheme.SelectedIndex = _currentSettings.AppTheme switch { "Dark" => 2, "Light" => 1, _ => 0 };
        cmbTheme.SelectedIndexChanged += (s, e) =>
        {
            _currentSettings.AppTheme = cmbTheme.SelectedIndex switch { 2 => "Dark", 1 => "Light", _ => "Auto" };
            SaveSettings();
            ApplyTheme();
        };

        pnlTheme.Controls.Add(lblTheme);
        pnlTheme.Controls.Add(cmbTheme);
        pnl.Controls.Add(pnlTheme);
        y += 50;

        var pnlLang = new Panel { Width = 500, Height = 40, Location = new Point(0, y) };
        var lblLang = new Label { Text = "Interface Language", ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        var cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(350, 8), BackColor = _sidebarColor, ForeColor = _textColor };
        
        var locales = new[] {
            new { Code="en", Name="English" }, new { Code="ru", Name="Русский" }, new { Code="uk", Name="Українська" },
            new { Code="de", Name="Deutsch" }, new { Code="pl", Name="Polski" }, new { Code="es", Name="Español" },
            new { Code="cs", Name="Čeština" }, new { Code="fr", Name="Français" }, new { Code="it", Name="Italiano" },
            new { Code="tr", Name="Türkçe" }, new { Code="kk", Name="Қазақ тілі" }, new { Code="ka", Name="ქართული" },
            new { Code="sr", Name="Српски" }, new { Code="hy", Name="Հայերեն" }, new { Code="he", Name="עברית" },
            new { Code="ro", Name="Română" }, new { Code="sk", Name="Slovenčina" }, new { Code="nl", Name="Nederlands" },
            new { Code="bg", Name="Български" }, new { Code="el", Name="Ελληνικά" }, new { Code="th", Name="ไทย" },
            new { Code="pt", Name="Português" }
        };
        cmbLang.DataSource = locales;
        cmbLang.DisplayMember = "Name";
        cmbLang.ValueMember = "Code";
        cmbLang.SelectedValue = _currentSettings.UiLanguage;
        
        cmbLang.SelectedIndexChanged += (s, e) =>
        {
            if (cmbLang.SelectedValue is string code && _currentSettings.UiLanguage != code)
            {
                _currentSettings.UiLanguage = code;
                _locService.SetCulture(code);
                SaveSettings();
                MessageBox.Show("Language changed. Please restart the application to apply all changes.", "Restart Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        pnlLang.Controls.Add(lblLang);
        pnlLang.Controls.Add(cmbLang);
        pnl.Controls.Add(pnlLang);

        _tabGeneral.Controls.Add(pnl);
    }

    private Panel CreateToggleSetting(string title, bool initialValue, Action<bool> onChanged, int y)
    {
        var pnl = new Panel { Width = 500, Height = 40, Location = new Point(0, y) };
        var lbl = new Label { Text = title, ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        var sw = new ToggleSwitch { Checked = initialValue, Location = new Point(450, 5), BackColor = _bgColor };
        sw.CheckedChanged += (s, e) => onChanged(sw.Checked);
        
        pnl.Controls.Add(lbl);
        pnl.Controls.Add(sw);
        return pnl;
    }

    private void BuildLanguagesTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var lblTitle = new Label { Text = _locService.GetString("Settings_Languages", "Language & Keyboard Layouts"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) };
        
        pnl.Controls.Add(lblTitle);

        var activeKeyboardLayoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var preloadKey = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload");
            if (preloadKey != null)
            {
                foreach (string valName in preloadKey.GetValueNames())
                {
                    string? hklStr = preloadKey.GetValue(valName)?.ToString();
                    if (!string.IsNullOrEmpty(hklStr))
                    {
                        // Match either the whole string or the lower 4 bytes to handle d0010419 etc
                        activeKeyboardLayoutIds.Add(hklStr.TrimStart('0').ToLower());
                        if (hklStr.Length >= 4)
                        {
                            activeKeyboardLayoutIds.Add(hklStr.Substring(hklStr.Length - 4).ToLower());
                        }
                    }
                }
            }
        }
        catch { }

        int y = 50;

        foreach (InputLanguage lang in InputLanguage.InstalledInputLanguages)
        {
            // Filter ghost layouts from GetKeyboardLayoutList using Preload registry
            string handleHex = ((long)lang.Handle & 0xFFFFFFFF).ToString("x8").ToLower();
            string shortHandle = handleHex.Substring(4);
            
            if (activeKeyboardLayoutIds.Count > 0 && 
                !activeKeyboardLayoutIds.Contains(handleHex) && 
                !activeKeyboardLayoutIds.Contains(handleHex.TrimStart('0')) &&
                !activeKeyboardLayoutIds.Contains(shortHandle))
            {
                continue;
            }

            string name = lang.Culture?.EnglishName ?? lang.LayoutName;
            bool isActive = !_currentSettings.DisabledLanguages.Contains(name);

            var card = new CardPanel { Width = 500, Height = 70, Location = new Point(0, y) };
            
            var lblName = new Label { Text = name, ForeColor = _textColor, Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Location = new Point(60, 15), BackColor = Color.Transparent };
            var lblLayout = new Label { Text = "Keyboard: " + lang.LayoutName, ForeColor = Color.Gray, Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(60, 38), BackColor = Color.Transparent };

            var sw = new ToggleSwitch { Checked = isActive, Location = new Point(430, 22), BackColor = card.CardBackColor };
            sw.CheckedChanged += (s, e) =>
            {
                if (sw.Checked) _currentSettings.DisabledLanguages.Remove(name);
                else if (!_currentSettings.DisabledLanguages.Contains(name)) _currentSettings.DisabledLanguages.Add(name);
                SaveSettings();
            };

            var lblActive = new Label { Text = "Active", ForeColor = _accentColor, Font = new Font("Segoe UI", 10), AutoSize = true, Location = new Point(370, 25), BackColor = Color.Transparent };
            
            string isoCode = lang.Culture?.TwoLetterISOLanguageName.ToUpperInvariant() ?? "??";
            var pnlFlag = new Label { Text = isoCode, Width = 34, Height = 22, Location = new Point(15, 24), BackColor = _accentColor, ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

            card.Controls.AddRange(new Control[] { pnlFlag, lblName, lblLayout, lblActive, sw });
            pnl.Controls.Add(card);
            y += 80;
        }

        _tabLanguages.Controls.Add(pnl);
    }

    private void BuildHotkeysTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var lblTitle = new Label { Text = _locService.GetString("Settings_Hotkeys", "Global Shortcuts"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top };
        
        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 7,
            RowCount = 9,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            Padding = new Padding(0, 10, 0, 0)
        };
        
        typeof(TableLayoutPanel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(tlp, true, null);
        
        tlp.SuspendLayout();
        
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35F));

        // Header Row
        tlp.Controls.Add(new Label { Text = _locService.GetString("Settings_Action", "Action"), ForeColor = Color.Gray, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        tlp.Controls.Add(new Label { Text = _locService.GetString("Settings_Set1", "Set 1"), ForeColor = Color.Gray, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        
        var chkAll1 = new CheckBox { CheckAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Checked = true };
        chkAll1.CheckedChanged += (s, e) => ToggleSet(1, chkAll1.Checked, tlp);
        tlp.Controls.Add(chkAll1, 2, 0);
        
        tlp.Controls.Add(new Label { Text = _locService.GetString("Settings_Set2", "Set 2"), ForeColor = Color.Gray, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 3, 0);
        
        var chkAll2 = new CheckBox { CheckAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Checked = true };
        chkAll2.CheckedChanged += (s, e) => ToggleSet(2, chkAll2.Checked, tlp);
        tlp.Controls.Add(chkAll2, 4, 0);
        
        tlp.Controls.Add(new Label { Text = _locService.GetString("Settings_Set3", "Set 3"), ForeColor = Color.Gray, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 5, 0);
        
        var chkAll3 = new CheckBox { CheckAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Checked = true };
        chkAll3.CheckedChanged += (s, e) => ToggleSet(3, chkAll3.Checked, tlp);
        tlp.Controls.Add(chkAll3, 6, 0);

        AddTlpRow(tlp, 1, "FixLayout", _locService.GetString("Settings_CancelConversion", "Cancel Conversion"), "Scroll", "Pause", "Ctrl+`");
        AddTlpRow(tlp, 2, "FixLayoutSelected", _locService.GetString("Settings_ChangeLayoutSelected", "Change Layout (Selected)"), "Shift+Scroll", "Shift+Pause", "Ctrl+Shift+`");
        AddTlpRow(tlp, 3, "ChangeCase", _locService.GetString("Settings_ChangeCaseSelected", "Change Case (Selected)"), "Alt+Scroll", "Alt+Pause", "Alt+`");
        AddTlpRow(tlp, 4, "Transliterate", _locService.GetString("Settings_TransliterateSelected", "Transliterate (Selected)"), "Ctrl+Alt+Scroll", "Ctrl+Alt+Pause", "Ctrl+Alt+`");
        AddTlpRow(tlp, 5, "Translate1", _locService.GetString("Settings_TranslateLang1", "Translate to Lang 1"), "Alt+Shift+T", "", "");
        AddTlpRow(tlp, 6, "Translate2", _locService.GetString("Settings_TranslateLang2", "Translate to Lang 2 (En)"), "Alt+T", "", "");
        AddTlpRow(tlp, 7, "Translate3", _locService.GetString("Settings_TranslateLang3", "Translate to Lang 3"), "Ctrl+Alt+T", "", "");
        AddTlpRow(tlp, 8, "OpenTranslator", _locService.GetString("Settings_OpenTranslator", "Open Translator"), "Ctrl+Shift+T", "", "");

        tlp.ResumeLayout(false);
        tlp.PerformLayout();

        pnl.Controls.Add(tlp);
        pnl.Controls.Add(lblTitle);
        _tabHotkeys.Controls.Add(pnl);
    }
    
    private void AddTlpRow(TableLayoutPanel tlp, int row, string action, string title, string hk1, string hk2, string hk3)
    {
        var lbl = new Label { Text = title, ForeColor = _textColor, Font = new Font("Segoe UI", 10), AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        tlp.Controls.Add(lbl, 0, row);
        
        AssignTlpCell(tlp, action, hk1, 1, 1, 2, row);
        AssignTlpCell(tlp, action, hk2, 2, 3, 4, row);
        AssignTlpCell(tlp, action, hk3, 3, 5, 6, row);
    }

    private void AssignTlpCell(TableLayoutPanel tlp, string action, string expectedHotkey, int preset, int textCol, int chkCol, int row)
    {
        var config = _currentSettings.HotkeyConfigs.FirstOrDefault(c => c.Action == action && c.Preset == preset);
        if (config == null && action == "FixLayoutSelected")
        {
            config = _currentSettings.HotkeyConfigs.FirstOrDefault(c => c.Action == "FixLayout" && c.Hotkey.Contains("Shift") && c.Preset == preset);
            if (config != null) config.Action = "FixLayoutSelected";
        }
        if (config == null)
        {
            config = new HotkeyConfig { Action = action, Hotkey = expectedHotkey, Preset = preset, Enabled = true };
            _currentSettings.HotkeyConfigs.Add(config);
        }

        var btn = new Button
        {
            Text = config.Hotkey,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = _sidebarColor,
            ForeColor = _textColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (s, e) =>
        {
            using var editor = new HotkeyEditorForm(config.Hotkey, action);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                config.Hotkey = editor.ResultHotkey;
                btn.Text = config.Hotkey;
                SaveSettings();
            }
        };

        var chk = new CheckBox { Checked = config.Enabled, CheckAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Tag = config };
        chk.CheckedChanged += (s, e) => { config.Enabled = chk.Checked; SaveSettings(); };

        tlp.Controls.Add(btn, textCol, row);
        tlp.Controls.Add(chk, chkCol, row);
    }

    private void ToggleSet(int preset, bool enable, TableLayoutPanel tlp)
    {
        int chkCol = preset == 1 ? 2 : (preset == 2 ? 4 : 6);
        for (int r = 1; r < tlp.RowCount; r++)
        {
            var control = tlp.GetControlFromPosition(chkCol, r);
            if (control is CheckBox chk)
            {
                chk.Checked = enable;
            }
        }
    }

    private ListBox _lstExceptions = null!;
    private void BuildExceptionsTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Text = _locService.GetString("Settings_Exceptions", "App Exceptions"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top };
        
        var pnlAdd = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 10, 0, 0) };
        var txtAdd = new TextBox { Width = 300, Location = new Point(0, 10), BackColor = _sidebarColor, ForeColor = _textColor, Font = new Font("Segoe UI", 11) };
        var btnAdd = new Button { Text = "Add", Location = new Point(310, 9), Width = 80, Height = 28, BackColor = _accentColor, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnAdd.FlatAppearance.BorderSize = 0;
        
        btnAdd.Click += (s, e) =>
        {
            string w = txtAdd.Text.Trim();
            if (!string.IsNullOrEmpty(w) && !_currentSettings.BlacklistedProcesses.Contains(w, StringComparer.OrdinalIgnoreCase))
            {
                _currentSettings.BlacklistedProcesses.Add(w);
                _lstExceptions.Items.Add(w);
                txtAdd.Clear();
                SaveSettings();
            }
        };

        var btnRemove = new Button { Text = "Remove", Location = new Point(400, 9), Width = 80, Height = 28, BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnRemove.FlatAppearance.BorderSize = 0;
        btnRemove.Click += (s, e) =>
        {
            if (_lstExceptions.SelectedItem is string w)
            {
                _currentSettings.BlacklistedProcesses.Remove(w);
                _lstExceptions.Items.Remove(w);
                SaveSettings();
            }
        };

        pnlAdd.Controls.AddRange(new Control[] { txtAdd, btnAdd, btnRemove });

        _lstExceptions = new ListBox { Dock = DockStyle.Fill, BackColor = _sidebarColor, ForeColor = _textColor, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 12) };
        foreach (var proc in _currentSettings.BlacklistedProcesses) _lstExceptions.Items.Add(proc);

        var pnlList = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        pnlList.Controls.Add(_lstExceptions);

        pnl.Controls.AddRange(new Control[] { pnlList, pnlAdd, lblTitle });
        _tabExceptions.Controls.Add(pnl);
    }

    private ListBox _lstUserExceptions = null!;
    private TextBox _txtAddDict = null!;
    private void BuildDictionaryTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Text = _locService.GetString("Settings_Dict", "Dictionary"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top };
        
        var pnlAdd = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 10, 0, 0) };
        _txtAddDict = new TextBox { Width = 300, Location = new Point(0, 10), BackColor = _sidebarColor, ForeColor = _textColor, Font = new Font("Segoe UI", 11) };
        var btnAdd = new Button { Text = "Add", Location = new Point(310, 9), Width = 80, Height = 28, BackColor = _accentColor, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnAdd.FlatAppearance.BorderSize = 0;
        
        btnAdd.Click += (s, e) =>
        {
            string w = _txtAddDict.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(w) && !_currentSettings.UserExceptions.Contains(w))
            {
                _currentSettings.UserExceptions.Add(w);
                _lstUserExceptions.Items.Add(w);
                _txtAddDict.Clear();
                SaveSettings();
            }
        };

        var btnRemove = new Button { Text = "Remove", Location = new Point(400, 9), Width = 80, Height = 28, BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnRemove.FlatAppearance.BorderSize = 0;
        btnRemove.Click += (s, e) =>
        {
            if (_lstUserExceptions.SelectedItem is string w)
            {
                _currentSettings.UserExceptions.Remove(w);
                _lstUserExceptions.Items.Remove(w);
                SaveSettings();
            }
        };

        pnlAdd.Controls.Add(_txtAddDict);
        pnlAdd.Controls.Add(btnAdd);
        pnlAdd.Controls.Add(btnRemove);

        _lstUserExceptions = new ListBox { Dock = DockStyle.Fill, BackColor = _sidebarColor, ForeColor = _textColor, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 12) };
        foreach (var w in _currentSettings.UserExceptions) _lstUserExceptions.Items.Add(w);

        var pnlList = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        pnlList.Controls.Add(_lstUserExceptions);

        pnl.Controls.Add(pnlList);
        pnl.Controls.Add(pnlAdd);
        pnl.Controls.Add(lblTitle);
        _tabDict.Controls.Add(pnl);
    }

    private void BuildTranslateTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 0;
        var lblTitle = new Label { Text = _locService.GetString("Settings_Translate", "Auto-Translate"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) };
        pnl.Controls.Add(lblTitle);
        y += 40;

        var lblNote = new Label { Text = _locService.GetString("Settings_GoogleTranslateNote", "⚠️ Google Translate is used by default (requires internet)."), ForeColor = Color.Orange, Font = new Font("Segoe UI", 10, FontStyle.Italic), AutoSize = true, Location = new Point(0, y) };
        pnl.Controls.Add(lblNote);
        y += 40;

        var tsOffline = CreateToggleSetting(_locService.GetString("Settings_UseOfflineModel", "Use local offline model (No internet)"), _currentSettings.UseOfflineTranslation, v => { _currentSettings.UseOfflineTranslation = v; SaveSettings(); }, y);
        pnl.Controls.Add(tsOffline);
        y += 50;

        var lblModel = new Label { Text = _locService.GetString("Settings_ModelSelection", "Model selection:"), AutoSize = true, Location = new Point(0, y + 4), ForeColor = _textColor };
        var cmbModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350, Location = new Point(120, y), BackColor = _sidebarColor, ForeColor = _textColor };
        cmbModel.Items.Add(new { Id = "light", Name = _locService.GetString("Settings_ModelLight", "Light Qwen 0.5B (350 MB, CPU)") });
        cmbModel.Items.Add(new { Id = "alma", Name = _locService.GetString("Settings_ModelAlma", "Specialized ALMA 7B (4 GB, GPU)") });
        cmbModel.Items.Add(new { Id = "pro", Name = _locService.GetString("Settings_ModelGemma", "General Gemma 2B (1.6 GB, GPU)") });
        cmbModel.ValueMember = "Id";
        cmbModel.DisplayMember = "Name";
        
        cmbModel.SelectedIndex = _currentSettings.OfflineModelType == "pro" ? 2 : (_currentSettings.OfflineModelType == "alma" ? 1 : 0);
        pnl.Controls.Add(lblModel);
        pnl.Controls.Add(cmbModel);
        y += 40;

        var downloadService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<LayoutFix.Core.Services.ModelDownloadService>(AppHost.Services!);
        
        var btnDownload = new Button { Width = 200, Height = 30, Location = new Point(0, y), FlatStyle = FlatStyle.Flat, ForeColor = _textColor, BackColor = _sidebarColor };
        var progressDownload = new ProgressBar { Width = 280, Height = 10, Location = new Point(220, y + 10), Visible = false };
        
        Action updateDownloadButton = () => {
            string modelType = (cmbModel.SelectedItem as dynamic)?.Id ?? "light";
            string fileName = modelType == "pro" ? "gemma-2b-it-q4_k_m.gguf" : (modelType == "alma" ? "alma-7b.Q4_K_M.gguf" : "qwen2-0_5b-instruct-q4_k_m.gguf");
            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LayoutFix", "Models", fileName);
            
            if (downloadService.IsModelDownloaded(path)) {
                btnDownload.Text = _locService.GetString("Settings_ModelDownloaded", "Model Downloaded");
                btnDownload.Enabled = false;
            } else {
                btnDownload.Text = _locService.GetString("Settings_DownloadModel", "Download Model");
                btnDownload.Enabled = true;
            }
        };

        cmbModel.SelectedIndexChanged += (s, e) => {
            _currentSettings.OfflineModelType = (cmbModel.SelectedItem as dynamic)?.Id ?? "light";
            SaveSettings();
            updateDownloadButton();
        };
        
        updateDownloadButton();
        
        btnDownload.Click += async (s, e) =>
        {
            btnDownload.Enabled = false;
            progressDownload.Visible = true;
            btnDownload.Text = _locService.GetString("Settings_Downloading", "Downloading...");
            try
            {
                string modelType = _currentSettings.OfflineModelType;
                string url = modelType == "pro" 
                    ? "https://huggingface.co/lmstudio-ai/gemma-2b-it-GGUF/resolve/main/gemma-2b-it-q4_k_m.gguf"
                    : (modelType == "alma" ? "https://huggingface.co/TheBloke/ALMA-7B-GGUF/resolve/main/alma-7b.Q4_K_M.gguf" : "https://huggingface.co/Qwen/Qwen2-0.5B-Instruct-GGUF/resolve/main/qwen2-0_5b-instruct-q4_k_m.gguf");
                    
                string fileName = modelType == "pro" ? "gemma-2b-it-q4_k_m.gguf" : (modelType == "alma" ? "alma-7b.Q4_K_M.gguf" : "qwen2-0_5b-instruct-q4_k_m.gguf");
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LayoutFix", "Models", fileName);

                await downloadService.DownloadModelAsync(url, path, p => {
                    if (progressDownload.InvokeRequired) progressDownload.Invoke(new Action(() => progressDownload.Value = (int)(p * 100)));
                    else progressDownload.Value = (int)(p * 100);
                });
                updateDownloadButton();
                progressDownload.Visible = false;
            }
            catch (Exception ex)
            {
                btnDownload.Text = _locService.GetString("Settings_DownloadError", "Download Error");
                MessageBox.Show(_locService.GetString("Settings_DownloadError", "Download Error") + ": " + ex.Message);
                btnDownload.Enabled = true;
            }
        };

        pnl.Controls.Add(btnDownload);
        pnl.Controls.Add(progressDownload);
        y += 50;

        var locales = new[] {
            new { Code="en", Name="English" }, new { Code="ru", Name="Русский" }, new { Code="uk", Name="Українська" },
            new { Code="de", Name="Deutsch" }, new { Code="pl", Name="Polski" }, new { Code="es", Name="Español" },
            new { Code="cs", Name="Čeština" }, new { Code="fr", Name="Français" }, new { Code="it", Name="Italiano" },
            new { Code="tr", Name="Türkçe" }, new { Code="kk", Name="Қазақ тілі" }, new { Code="ka", Name="ქართული" },
            new { Code="sr", Name="Српски" }, new { Code="hy", Name="Հայերեն" }, new { Code="he", Name="עברית" },
            new { Code="ro", Name="Română" }, new { Code="sk", Name="Slovenčina" }, new { Code="nl", Name="Nederlands" },
            new { Code="bg", Name="Български" }, new { Code="el", Name="Ελληνικά" }, new { Code="th", Name="ไทย" },
            new { Code="pt", Name="Português" }
        };

        pnl.Controls.Add(CreateLanguageDropdown("Translation Language 1", _currentSettings.TranslateLang1, locales.ToList<object>().ToArray(), v => { _currentSettings.TranslateLang1 = v; SaveSettings(); }, ref y));
        pnl.Controls.Add(CreateLanguageDropdown("Translation Language 2", _currentSettings.TranslateLang2, locales.ToList<object>().ToArray(), v => { _currentSettings.TranslateLang2 = v; SaveSettings(); }, ref y));
        pnl.Controls.Add(CreateLanguageDropdown("Translation Language 3", _currentSettings.TranslateLang3, locales.ToList<object>().ToArray(), v => { _currentSettings.TranslateLang3 = v; SaveSettings(); }, ref y));

        _tabTranslate.Controls.Add(pnl);
    }

    private Panel CreateLanguageDropdown(string label, string currentVal, object[] locales, Action<string> onChange, ref int y)
    {
        var pnl = new Panel { Width = 500, Height = 40, Location = new Point(0, y) };
        var lbl = new Label { Text = label, ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(350, 8), BackColor = _sidebarColor, ForeColor = _textColor, DataSource = locales, DisplayMember = "Name", ValueMember = "Code" };
        
        cmb.SelectedValue = currentVal;
        cmb.SelectedIndexChanged += (s, e) => { if (cmb.SelectedValue != null) onChange(cmb.SelectedValue.ToString()!); };
        
        pnl.Controls.Add(lbl);
        pnl.Controls.Add(cmb);
        y += 50;
        return pnl;
    }

    private void BuildAboutTab()
    {
        var lblTitle = new Label { Text = _locService.GetString("Settings_About", "About LayoutFix"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top };
        
        var picLogo = new PictureBox 
        { 
            SizeMode = PictureBoxSizeMode.Zoom, 
            Width = 100, Height = 100, 
            Location = new Point(0, 50) 
        };
        
        string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png");
        if (System.IO.File.Exists(logoPath))
        {
            picLogo.Image = Image.FromFile(logoPath);
        }
        else
        {
            picLogo.Image = this.Icon?.ToBitmap();
        }

        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.6";
        var lblDesc = new Label { Text = $"LayoutFix v{version}\nBuilt with .NET 8.\nAutomatic Layout Converter.", ForeColor = Color.Gray, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(120, 50) };
        
        _tabAbout.Controls.Add(picLogo);
        _tabAbout.Controls.Add(lblDesc);
        _tabAbout.Controls.Add(lblTitle);
    }

    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    private void PnlTopBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(Color.FromArgb(50, 50, 50), 1))
        {
            e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
        }
    }
}
