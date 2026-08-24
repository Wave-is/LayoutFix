using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Services;
using LayoutFix.UI.Controls;

namespace LayoutFix.UI;

public class SettingsForm : Form
{
    public class LangItem { public string Code { get; set; } = ""; public string Name { get; set; } = ""; }

    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _locService;
    private readonly ILoggerService _logger;
    private readonly ModelDownloadService _modelDownloadService;
    private readonly ITranslationHistoryService _translationHistoryService;
    private readonly ITranslationCredentialStore _translationCredentials;
    private readonly LayoutFix.Services.SettingsPersistenceCoordinator _persistenceCoordinator;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
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

    public SettingsForm(
        ISettingsService settingsService,
        IAutoStartService autoStartService,
        ILocalizationService locService,
        ILoggerService logger,
        ModelDownloadService modelDownloadService,
        ITranslationHistoryService translationHistoryService,
        ITranslationCredentialStore translationCredentials)
    {
        _settingsService = settingsService;
        _locService = locService;
        _logger = logger;
        _modelDownloadService = modelDownloadService;
        _translationHistoryService = translationHistoryService;
        _translationCredentials = translationCredentials;
        _currentSettings = _settingsService.Current;
        _persistenceCoordinator = new LayoutFix.Services.SettingsPersistenceCoordinator(
            _settingsService,
            autoStartService,
            _currentSettings.AutoStart);

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
        // This form's whole layout is hand-placed pixel coordinates (Location,
        // row-spacing increments like "y += 40", fixed widths) with no
        // Designer-generated baseline. The app is PerMonitorV2 DPI-aware, so
        // at DPI above 100% GDI+ renders every label's text taller/wider in
        // device pixels while these literal pixel budgets stay identical,
        // which is exactly the overlap reported at 150% scaling. AutoScaleMode
        // = Dpi with an explicit 96 DPI baseline makes WinForms scale the
        // entire built control tree (locations, sizes, fonts) by the real
        // monitor-DPI ratio in one pass, which keeps every hand-tuned margin
        // proportionally correct at any scale factor.
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.AutoScaleDimensions = new SizeF(96F, 96F);
        this.Text = _locService.GetString("Settings_Title", "LayoutFix");
        this.Size = new Size(1320, 760);
        this.MinimumSize = new Size(1000, 650);
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
            Width = 290,
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
            _lifetimeCancellation.Cancel();
            if (_persistenceCoordinator.HasPendingChanges)
                SaveSettings(retryPendingOnly: true);
        };
    }

    private void SaveSettings(bool retryPendingOnly = false)
    {
        try
        {
            if (retryPendingOnly)
                _persistenceCoordinator.RetryPending(_currentSettings);
            else
                _persistenceCoordinator.Save(_currentSettings);
        }
        catch (LayoutFix.Services.SettingsPersistenceException ex)
        {
            _logger.LogError(ex.SafeLogMessage, ex);
            ShowSettingsSaveError();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "DiagnosticCode: LF-ST-003 | Action: settings-save | Stage: unknown | Outcome: failed",
                ex);
            ShowSettingsSaveError();
        }
    }

    private void OfferRestart()
    {
        var result = MessageBox.Show(
            this,
            _locService.GetString(
                "Settings_RestartRequiredMessage",
                "Language changed. Restart LayoutFix now to apply all changes?"),
            _locService.GetString("Settings_RestartRequired", "Restart required"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result != DialogResult.Yes)
            return;

        try
        {
            var exePath = Application.ExecutablePath;
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // A short delay lets this process release the single-instance
                // mutex before the relaunched one starts, or it would see the
                // mutex still held and immediately exit with nothing running.
                Arguments = $"/C timeout /t 1 /nobreak >nul & start \"\" \"{exePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Exit();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to restart LayoutFix after a language change", ex);
        }
    }

    private void ShowSettingsSaveError()
    {
        MessageBox.Show(
            _locService.GetString(
                "Settings_SaveErrorMessage",
                "Settings could not be saved. Check file and Windows startup permissions."),
            _locService.GetString("Settings_SaveErrorTitle", "Settings not saved"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
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
            ResetScrollPositions(targetPanel);
            targetPanel.BeginInvoke(() => ResetScrollPositions(targetPanel));

            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btn.BackColor = _accentColor;
            _activeTabButton = btn;
        };

        _pnlSidebar.Controls.Add(btn);
        btn.BringToFront();
        return btn;
    }

    private static void ResetScrollPositions(Control root)
    {
        if (root is ScrollableControl scrollable && scrollable.AutoScroll)
            scrollable.AutoScrollPosition = Point.Empty;

        foreach (Control child in root.Controls)
            ResetScrollPositions(child);
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

        pnl.Controls.Add(CreateToggleSetting(_locService.GetString("Settings_AutoConv", "Enable automatic correction while typing"), _currentSettings.AutoConversionEnabled, v => { _currentSettings.AutoConversionEnabled = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting(_locService.GetString("Settings_AutoStart", "Start with Windows"), _currentSettings.AutoStart, v => { _currentSettings.AutoStart = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting(_locService.GetString("Settings_Sound", "Enable sound notifications"), _currentSettings.SoundEnabled, v => { _currentSettings.SoundEnabled = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting(_locService.GetString("Settings_Notifications", "Show diagnostic popup messages (LF-... codes)"), _currentSettings.NotificationsEnabled, v => { _currentSettings.NotificationsEnabled = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting(_locService.GetString("Settings_Flags", "Use country flags in tray"), _currentSettings.UseFlagIcons, v => { _currentSettings.UseFlagIcons = v; SaveSettings(); }, y)); y += 50;
        pnl.Controls.Add(CreateToggleSetting(_locService.GetString("Settings_Logging", "Diagnostic logs for testing (include application name)"), _currentSettings.LoggingEnabled, v => { _currentSettings.LoggingEnabled = v; SaveSettings(); }, y)); y += 50;

        var lblTheme = new Label { Text = _locService.GetString("Settings_Theme", "Color theme"), ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        MeasureNow(lblTheme);
        int themeComboX = Math.Max(lblTheme.Right + 24, MinSettingsRowWidth - 150);
        var cmbTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(themeComboX, 8), BackColor = _sidebarColor, ForeColor = _textColor };
        var pnlTheme = new Panel { Width = Math.Max(MinSettingsRowWidth, themeComboX + cmbTheme.Width), Height = 40, Location = new Point(0, y) };
        cmbTheme.Items.AddRange(new[]
        {
            _locService.GetString("Settings_ThemeAuto", "Follow Windows"),
            _locService.GetString("Settings_ThemeLight", "Light"),
            _locService.GetString("Settings_ThemeDark", "Dark")
        });
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

        var lblLang = new Label { Text = _locService.GetString("Settings_InterfaceLanguage", "Interface language"), ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        MeasureNow(lblLang);
        int langComboX = Math.Max(lblLang.Right + 24, MinSettingsRowWidth - 150);
        var cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(langComboX, 8), BackColor = _sidebarColor, ForeColor = _textColor };
        var pnlLang = new Panel { Width = Math.Max(MinSettingsRowWidth, langComboX + cmbLang.Width), Height = 40, Location = new Point(0, y) };

        var locales = new LangItem[] {
            new LangItem{ Code="en", Name="English" }, new LangItem{ Code="ru", Name="Русский" }, new LangItem{ Code="uk", Name="Українська" },
            new LangItem{ Code="de", Name="Deutsch" }, new LangItem{ Code="pl", Name="Polski" }, new LangItem{ Code="es", Name="Español" },
            new LangItem{ Code="cs", Name="Čeština" }, new LangItem{ Code="fr", Name="Français" }, new LangItem{ Code="it", Name="Italiano" },
            new LangItem{ Code="tr", Name="Türkçe" }, new LangItem{ Code="kk", Name="Қазақ тілі" }, new LangItem{ Code="ka", Name="ქართული" },
            new LangItem{ Code="sr", Name="Српски" }, new LangItem{ Code="hy", Name="Հայերեն" }, new LangItem{ Code="he", Name="עברית" },
            new LangItem{ Code="ro", Name="Română" }, new LangItem{ Code="sk", Name="Slovenčina" }, new LangItem{ Code="nl", Name="Nederlands" },
            new LangItem{ Code="bg", Name="Български" }, new LangItem{ Code="el", Name="Ελληνικά" }, new LangItem{ Code="th", Name="ไทย" },
            new LangItem{ Code="pt", Name="Português" }
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
                OfferRestart();
            }
        };

        pnlLang.Controls.Add(lblLang);
        pnlLang.Controls.Add(cmbLang);
        pnl.Controls.Add(pnlLang);

        _tabGeneral.Controls.Add(pnl);
    }

    // The toggle is positioned relative to the label's actual rendered width
    // (not a hardcoded X) so longer translations never overlap or get hidden
    // behind the switch. The row itself grows to fit both, so nothing is
    // silently clipped by the parent's bounds either.
    private const int MinSettingsRowWidth = 740;

    // AutoSize controls report a placeholder 100x23 size until they are
    // parented (or otherwise laid out); reading .Right/.Bottom right after
    // construction — as every "position sibling relative to this label"
    // fix below does — would silently use that placeholder instead of the
    // real rendered size. Forcing GetPreferredSize makes it correct
    // regardless of parenting order or timing.
    private static void MeasureNow(Control control) => control.Size = control.GetPreferredSize(Size.Empty);

    private Panel CreateToggleSetting(string title, bool initialValue, Action<bool> onChanged, int y)
    {
        var lbl = new Label { Text = title, ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        MeasureNow(lbl);
        int switchX = Math.Max(lbl.Right + 24, MinSettingsRowWidth - 50);
        var sw = new ToggleSwitch { Checked = initialValue, Location = new Point(switchX, 5), BackColor = _bgColor };
        sw.CheckedChanged += (s, e) => onChanged(sw.Checked);

        var pnl = new Panel { Width = Math.Max(MinSettingsRowWidth, switchX + sw.Width + 10), Height = 40, Location = new Point(0, y) };
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

        var useWindowsLayouts = CreateToggleSetting(
            _locService.GetString(
                "Settings_UseWindowsLayouts",
                "Use installed Windows keyboard layouts"),
            _currentSettings.UseWindowsLayoutList,
            value =>
            {
                _currentSettings.UseWindowsLayoutList = value;
                SaveSettings();
            },
            y);
        pnl.Controls.Add(useWindowsLayouts);
        y += 50;

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
            var layout = new Layout
            {
                Code = lang.Culture?.Name ?? string.Empty,
                Identifier = KeyboardLayoutIdentity.Create(
                    lang.Culture?.Name ?? string.Empty,
                    lang.Handle.ToInt64()),
                DisplayName = name
            };
            bool isActive = !KeyboardLayoutPreferences.IsDisabled(
                layout,
                _currentSettings.DisabledLanguages);

            var card = new CardPanel { Width = 500, Height = 70, Location = new Point(0, y) };
            
            var lblName = new Label { Text = name, ForeColor = _textColor, Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Location = new Point(60, 15), BackColor = Color.Transparent };
            var lblLayout = new Label { Text = _locService.GetString("Settings_KeyboardPrefix", "Keyboard:") + " " + lang.LayoutName, ForeColor = Color.Gray, Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(60, 38), BackColor = Color.Transparent };

            var sw = new ToggleSwitch { Checked = isActive, Location = new Point(430, 22), BackColor = card.CardBackColor };
            sw.CheckedChanged += (s, e) =>
            {
                if (sw.Checked)
                    KeyboardLayoutPreferences.Enable(_currentSettings, layout);
                else
                    KeyboardLayoutPreferences.Disable(_currentSettings, layout);
                SaveSettings();
            };

            var lblActive = new Label { Text = _locService.GetString("Settings_Active", "Active"), ForeColor = _accentColor, Font = new Font("Segoe UI", 10), AutoSize = true, Location = new Point(370, 25), BackColor = Color.Transparent };
            
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
        // The table fits in the fixed settings window. AutoScroll caused WinForms
        // to shift the entire tab when a child received focus, placing the title
        // and table underneath the custom top bar.
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label
        {
            Text = _locService.GetString("Settings_Hotkeys", "Global Shortcuts"),
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 58,
            TextAlign = ContentAlignment.MiddleLeft
        };
        
        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 7,
            RowCount = 10,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            Padding = new Padding(0, 10, 0, 0)
        };
        
        typeof(TableLayoutPanel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(tlp, true, null);
        
        tlp.SuspendLayout();
        
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24.666F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24.667F));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35F));
        for (var row = 0; row < tlp.RowCount; row++)
            tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24.667F));
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
        AddTlpRow(tlp, 9, "Undo", _locService.GetString("Settings_UndoAutoCorrection", "Undo last auto-correction"), "Ctrl+Shift+Backspace", "", "");

        tlp.ResumeLayout(false);
        tlp.PerformLayout();

        var tableHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AccessibleName = "Hotkeys.TableHost"
        };
        tableHost.Controls.Add(tlp);
        pnl.Controls.Add(tableHost);
        pnl.Controls.Add(lblTitle);
        _tabHotkeys.Controls.Add(pnl);
    }
    
    private void AddTlpRow(TableLayoutPanel tlp, int row, string action, string title, string hk1, string hk2, string hk3)
    {
        var lbl = new Label { Text = title, ForeColor = _textColor, Font = new Font("Segoe UI", 10), AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        tlp.Controls.Add(lbl, 0, row);

        AssignTlpCell(tlp, action, title, hk1, 1, 1, 2, row);
        AssignTlpCell(tlp, action, title, hk2, 2, 3, 4, row);
        AssignTlpCell(tlp, action, title, hk3, 3, 5, 6, row);
    }

    private void AssignTlpCell(TableLayoutPanel tlp, string action, string actionTitle, string expectedHotkey, int preset, int textCol, int chkCol, int row)
    {
        var config = _currentSettings.HotkeyConfigs.FirstOrDefault(c => c.Action == action && c.Preset == preset);
        if (config == null && action == "FixLayoutSelected")
        {
            config = _currentSettings.HotkeyConfigs.FirstOrDefault(c => c.Action == "FixLayout" && c.Hotkey.Contains("Shift") && c.Preset == preset);
            if (config != null) config.Action = "FixLayoutSelected";
        }
        if (config == null)
        {
            config = new HotkeyConfig
            {
                Action = action,
                Hotkey = expectedHotkey,
                Preset = preset,
                Enabled = !string.IsNullOrWhiteSpace(expectedHotkey)
            };
            _currentSettings.HotkeyConfigs.Add(config);
        }

        var btn = new Button
        {
            Text = config.Hotkey,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            MinimumSize = new Size(0, 34),
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
            using var editor = new HotkeyEditorForm(config.Hotkey, actionTitle, _locService);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                if (HasHotkeyConflict(config, editor.ResultHotkey))
                {
                    ShowHotkeyConflict(editor.ResultHotkey);
                    return;
                }
                config.Hotkey = editor.ResultHotkey;
                btn.Text = config.Hotkey;
                SaveSettings();
            }
        };

        var chk = new CheckBox { Checked = config.Enabled, CheckAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Tag = config };
        chk.CheckedChanged += (s, e) =>
        {
            if (chk.Checked && HasHotkeyConflict(config, config.Hotkey))
            {
                ShowHotkeyConflict(config.Hotkey);
                chk.Checked = false;
                return;
            }

            config.Enabled = chk.Checked;
            SaveSettings();
        };

        tlp.Controls.Add(btn, textCol, row);
        tlp.Controls.Add(chk, chkCol, row);
    }

    private bool HasHotkeyConflict(HotkeyConfig current, string hotkey)
    {
        var candidate = HotkeyCombo.Parse(hotkey);
        return _currentSettings.HotkeyConfigs.Any(other =>
            !ReferenceEquals(other, current) &&
            other.Enabled &&
            candidate.Matches(HotkeyCombo.Parse(other.Hotkey)));
    }

    private void ShowHotkeyConflict(string hotkey) => MessageBox.Show(
        this,
        _locService.GetString(
            "Settings_HotkeyConflictMessage",
            "This shortcut is already assigned to another enabled action.") + $"\n\n{hotkey}",
        _locService.GetString("Settings_HotkeyConflictTitle", "Shortcut conflict"),
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);

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
    private ListBox _lstAutoConversionExceptions = null!;

    private void BuildExceptionsTab()
    {
        var lblTitle = new Label
        {
            Text = _locService.GetString("Settings_Exceptions", "App Exceptions"),
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 6)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

        var titlePanel = new Panel { Dock = DockStyle.Fill };
        titlePanel.Controls.Add(lblTitle);

        var allActionsPanel = CreateProcessExceptionEditor(
            _locService.GetString("Settings_AllActionsExclusions", "All LayoutFix actions"),
            _locService.GetString(
                "Settings_AppExceptionsDescription",
                "Manual hotkeys and automatic correction stay disabled in these processes."),
            _currentSettings.BlacklistedProcesses,
            "GlobalProcessExclusions",
            includeRestoreDefaults: false,
            out _lstExceptions);

        var autoCorrectionPanel = CreateProcessExceptionEditor(
            _locService.GetString("Settings_AutoCorrectionExclusions", "Automatic correction only"),
            _locService.GetString(
                "Settings_AutoCorrectionExclusionsDescription",
                "Typing is never changed automatically in these processes. Manual hotkeys remain available."),
            _currentSettings.AutoConversionBlacklistedProcesses,
            "AutoCorrectionProcessExclusions",
            includeRestoreDefaults: true,
            out _lstAutoConversionExceptions);

        layout.Controls.Add(titlePanel, 0, 0);
        layout.Controls.Add(allActionsPanel, 0, 1);
        layout.Controls.Add(autoCorrectionPanel, 0, 2);
        _tabExceptions.Controls.Add(layout);
    }

    private Panel CreateProcessExceptionEditor(
        string title,
        string description,
        List<string> processes,
        string accessiblePrefix,
        bool includeRestoreDefaults,
        out ListBox listBox)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };
        var lblSectionTitle = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        var lblDescription = new Label
        {
            Text = description,
            Dock = DockStyle.Top,
            Height = 38,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9)
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0),
            Margin = Padding.Empty
        };
        var txtAdd = new TextBox
        {
            Width = includeRestoreDefaults ? 190 : 300,
            BackColor = _sidebarColor,
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 10),
            AccessibleName = accessiblePrefix + ".Input"
        };
        var btnAdd = CreateExceptionButton(
            _locService.GetString("Settings_Add", "Add"),
            _accentColor,
            accessiblePrefix + ".Add");
        var btnRemove = CreateExceptionButton(
            _locService.GetString("Settings_Remove", "Remove"),
            Color.FromArgb(200, 50, 50),
            accessiblePrefix + ".Remove");

        listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = _sidebarColor,
            ForeColor = _textColor,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10),
            AccessibleName = accessiblePrefix + ".List"
        };
        foreach (var process in processes)
            listBox.Items.Add(process);
        var editorList = listBox;

        void AddProcess()
        {
            var process = txtAdd.Text.Trim();
            if (process.Length == 0 || processes.Contains(process, StringComparer.OrdinalIgnoreCase))
                return;

            processes.Add(process);
            editorList.Items.Add(process);
            txtAdd.Clear();
            SaveSettings();
        }

        btnAdd.Click += (_, _) => AddProcess();
        txtAdd.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            AddProcess();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        btnRemove.Click += (_, _) =>
        {
            if (editorList.SelectedItem is not string process)
                return;

            processes.Remove(process);
            editorList.Items.Remove(process);
            SaveSettings();
        };

        actions.Controls.AddRange([txtAdd, btnAdd, btnRemove]);
        if (includeRestoreDefaults)
        {
            var btnRestore = CreateExceptionButton(
                _locService.GetString("Settings_RestoreSafetyDefaults", "Restore safety defaults"),
                Color.FromArgb(72, 72, 76),
                accessiblePrefix + ".RestoreDefaults",
                width: 240);
            btnRestore.Click += (_, _) =>
            {
                var changed = false;
                foreach (var process in AppSettings.DefaultAutoConversionBlacklistedProcesses)
                {
                    if (processes.Contains(process, StringComparer.OrdinalIgnoreCase))
                        continue;

                    processes.Add(process);
                    editorList.Items.Add(process);
                    changed = true;
                }

                if (changed)
                    SaveSettings();
            };
            actions.Controls.Add(btnRestore);
        }

        panel.Controls.Add(editorList);
        panel.Controls.Add(actions);
        panel.Controls.Add(lblDescription);
        panel.Controls.Add(lblSectionTitle);
        return panel;
    }

    private static Button CreateExceptionButton(
        string text,
        Color backColor,
        string accessibleName,
        int width = 80)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(width, 28),
            Height = 28,
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = accessibleName,
            Margin = new Padding(5, 0, 0, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private ListBox _lstUserExceptions = null!;
    private TextBox _txtAddDict = null!;
    private ListView _lstUserAutocorrect = null!;
    private void BuildDictionaryTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Text = _locService.GetString("Settings_Dict", "Dictionary"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top };

        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 12, 0, 0)
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

        var exceptionsGroup = new GroupBox
        {
            Text = _locService.GetString("Settings_DictionaryExceptions", "Never auto-correct"),
            Dock = DockStyle.Fill,
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 11),
            Padding = new Padding(10)
        };
        var exceptionsPanel = new Panel { Dock = DockStyle.Fill };
        var pnlAdd = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, ColumnCount = 3 };
        pnlAdd.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlAdd.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        pnlAdd.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        _txtAddDict = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 9, 6, 3), BackColor = _sidebarColor, ForeColor = _textColor, Font = new Font("Segoe UI", 11) };
        var btnAdd = new Button { Text = "+", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 6, 4), BackColor = _accentColor, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnAdd.FlatAppearance.BorderSize = 0;
        
        btnAdd.Click += (s, e) =>
        {
            string w = _txtAddDict.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(w) && !_currentSettings.UserExceptions.Contains(w, StringComparer.OrdinalIgnoreCase))
            {
                _currentSettings.UserExceptions.Add(w);
                _lstUserExceptions.Items.Add(w);
                _txtAddDict.Clear();
                SaveSettings();
            }
        };

        var btnRemove = new Button { Text = "−", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
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

        pnlAdd.Controls.Add(_txtAddDict, 0, 0);
        pnlAdd.Controls.Add(btnAdd, 1, 0);
        pnlAdd.Controls.Add(btnRemove, 2, 0);

        _lstUserExceptions = new ListBox { Dock = DockStyle.Fill, BackColor = _sidebarColor, ForeColor = _textColor, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 12) };
        foreach (var w in _currentSettings.UserExceptions) _lstUserExceptions.Items.Add(w);

        var pnlList = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        pnlList.Controls.Add(_lstUserExceptions);

        exceptionsPanel.Controls.Add(pnlList);
        exceptionsPanel.Controls.Add(pnlAdd);
        exceptionsGroup.Controls.Add(exceptionsPanel);

        var autocorrectGroup = new GroupBox
        {
            Text = _locService.GetString("Settings_CustomReplacements", "Custom replacements"),
            Dock = DockStyle.Fill,
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 11),
            Padding = new Padding(10)
        };
        var autocorrectPanel = new Panel { Dock = DockStyle.Fill };
        var autocorrectAddPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 70,
            ColumnCount = 3,
            RowCount = 2
        };
        autocorrectAddPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        autocorrectAddPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        autocorrectAddPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        var sourceLabel = new Label
        {
            Text = _locService.GetString("Settings_ReplaceFrom", "Typed text"),
            ForeColor = _textColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };
        var replacementLabel = new Label
        {
            Text = _locService.GetString("Settings_ReplaceWith", "Replacement"),
            ForeColor = _textColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };
        var sourceText = new TextBox { Dock = DockStyle.Fill, BackColor = _sidebarColor, ForeColor = _textColor };
        var replacementText = new TextBox { Dock = DockStyle.Fill, BackColor = _sidebarColor, ForeColor = _textColor };
        var addReplacement = new Button
        {
            Text = _locService.GetString("Settings_Add", "Add"),
            Dock = DockStyle.Fill,
            BackColor = _accentColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(6, 3, 0, 3)
        };
        addReplacement.FlatAppearance.BorderSize = 0;
        autocorrectAddPanel.Controls.Add(sourceLabel, 0, 0);
        autocorrectAddPanel.Controls.Add(replacementLabel, 1, 0);
        autocorrectAddPanel.Controls.Add(sourceText, 0, 1);
        autocorrectAddPanel.Controls.Add(replacementText, 1, 1);
        autocorrectAddPanel.Controls.Add(addReplacement, 2, 1);

        _lstUserAutocorrect = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BackColor = _sidebarColor,
            ForeColor = _textColor,
            BorderStyle = BorderStyle.None
        };
        _lstUserAutocorrect.Columns.Add(_locService.GetString("Settings_ReplaceFrom", "Typed text"), 180);
        _lstUserAutocorrect.Columns.Add(_locService.GetString("Settings_ReplaceWith", "Replacement"), 220);
        _lstUserAutocorrect.SizeChanged += (_, _) =>
        {
            var availableWidth = Math.Max(120, _lstUserAutocorrect.ClientSize.Width - 24);
            _lstUserAutocorrect.Columns[0].Width = (int)(availableWidth * 0.42);
            _lstUserAutocorrect.Columns[1].Width = availableWidth - _lstUserAutocorrect.Columns[0].Width;
        };

        void RefreshAutocorrectList()
        {
            _lstUserAutocorrect.BeginUpdate();
            _lstUserAutocorrect.Items.Clear();
            foreach (var pair in _currentSettings.UserAutocorrect.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new ListViewItem(pair.Key) { Tag = pair.Key };
                item.SubItems.Add(pair.Value);
                _lstUserAutocorrect.Items.Add(item);
            }
            _lstUserAutocorrect.EndUpdate();
        }

        addReplacement.Click += (_, _) =>
        {
            var source = sourceText.Text.Trim().ToLowerInvariant();
            var replacement = replacementText.Text.Trim();
            if (source.Length == 0 || replacement.Length == 0)
                return;

            _currentSettings.UserAutocorrect[source] = replacement;
            sourceText.Clear();
            replacementText.Clear();
            RefreshAutocorrectList();
            SaveSettings();
        };

        var removeReplacement = new Button
        {
            Text = _locService.GetString("Settings_RemoveSelected", "Remove selected"),
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = Color.FromArgb(200, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        removeReplacement.FlatAppearance.BorderSize = 0;
        removeReplacement.Click += (_, _) =>
        {
            if (_lstUserAutocorrect.SelectedItems.Count == 0 ||
                _lstUserAutocorrect.SelectedItems[0].Tag is not string source)
            {
                return;
            }

            _currentSettings.UserAutocorrect.Remove(source);
            RefreshAutocorrectList();
            SaveSettings();
        };

        RefreshAutocorrectList();
        autocorrectPanel.Controls.Add(_lstUserAutocorrect);
        autocorrectPanel.Controls.Add(removeReplacement);
        autocorrectPanel.Controls.Add(autocorrectAddPanel);
        autocorrectGroup.Controls.Add(autocorrectPanel);

        columns.Controls.Add(exceptionsGroup, 0, 0);
        columns.Controls.Add(autocorrectGroup, 1, 0);
        pnl.Controls.Add(columns);
        pnl.Controls.Add(lblTitle);
        _tabDict.Controls.Add(pnl);
    }

    private void BuildTranslateTab()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 0;
        var lblTitle = new Label { Text = _locService.GetString("Settings_Translate", "Auto-Translate"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(0, y) };
        MeasureNow(lblTitle);
        pnl.Controls.Add(lblTitle);
        y = lblTitle.Bottom + 10;

        var lblNote = new Label
        {
            Text = _locService.GetString("Settings_GoogleTranslateNote", "⚠️ Online mode uses the billed Google Cloud Translation API and sends selected text to Google."),
            ForeColor = Color.Orange,
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Location = new Point(0, y),
            AccessibleName = "Translation.OnlinePrivacyNote"
        };
        MeasureNow(lblNote);
        pnl.Controls.Add(lblNote);
        y = lblNote.Bottom + 12;

        var hasTranslationCredential = false;
        try
        {
            hasTranslationCredential = _translationCredentials.HasApiKey;
        }
        catch (Exception exception)
        {
            _logger.LogError("Failed to inspect the translation API credential", exception);
        }
        Panel? onlineSetting = null;

        var credentialLabel = new Label
        {
            Text = _locService.GetString("Settings_GoogleCloudApiKey", "Google Cloud API key"),
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Location = new Point(0, 9)
        };
        MeasureNow(credentialLabel);
        var credentialInput = new TextBox
        {
            Width = 260,
            Location = new Point(credentialLabel.Right + 15, 5),
            UseSystemPasswordChar = true,
            MaxLength = 256,
            PlaceholderText = hasTranslationCredential
                ? _locService.GetString("Settings_ApiKeyStored", "Stored securely in Windows")
                : _locService.GetString("Settings_ApiKeyMissing", "Required for online translation"),
            AccessibleName = "Translation.ApiKey"
        };
        var saveCredential = new Button
        {
            Text = _locService.GetString("Settings_SaveApiKey", "Save key"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(95, 30),
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            ForeColor = _textColor,
            BackColor = _sidebarColor,
            AccessibleName = "Translation.ApiKeySave"
        };
        MeasureNow(saveCredential);
        saveCredential.Location = new Point(credentialInput.Right + 15, 4);
        var removeCredential = new Button
        {
            Text = _locService.GetString("Settings_RemoveApiKey", "Remove"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(95, 30),
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            ForeColor = _textColor,
            BackColor = _sidebarColor,
            Enabled = true,
            TabStop = hasTranslationCredential,
            AccessibleName = "Translation.ApiKeyRemove"
        };
        MeasureNow(removeCredential);
        removeCredential.Location = new Point(saveCredential.Right + 10, 4);
        var credentialPanel = new Panel
        {
            Width = Math.Max(700, removeCredential.Right + 10),
            Height = 40,
            Location = new Point(0, y),
            AccessibleName = "Translation.CredentialRow"
        };
        saveCredential.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(credentialInput.Text)) return;
            try
            {
                _translationCredentials.SaveApiKey(credentialInput.Text);
                credentialInput.Clear();
                credentialInput.PlaceholderText = _locService.GetString(
                    "Settings_ApiKeyStored",
                    "Stored securely in Windows");
                hasTranslationCredential = true;
                removeCredential.TabStop = true;
            }
            catch (Exception exception)
            {
                _logger.LogError("Failed to save the translation API credential", exception);
                MessageBox.Show(
                    this,
                    _locService.GetString("Settings_ApiKeySaveError", "The API key could not be saved securely."),
                    "LayoutFix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        removeCredential.Click += (_, _) =>
        {
            if (!hasTranslationCredential)
                return;

            try
            {
                _translationCredentials.SaveApiKey(null);
                credentialInput.Clear();
                credentialInput.PlaceholderText = _locService.GetString(
                    "Settings_ApiKeyMissing",
                    "Required for online translation");
                hasTranslationCredential = false;
                removeCredential.TabStop = false;
                _currentSettings.OnlineTranslationEnabled = false;
                var onlineSwitch = onlineSetting?.Controls.OfType<ToggleSwitch>().FirstOrDefault();
                if (onlineSwitch != null) onlineSwitch.Checked = false;
                SaveSettings();
            }
            catch (Exception exception)
            {
                _logger.LogError("Failed to remove the translation API credential", exception);
                MessageBox.Show(
                    this,
                    _locService.GetString("Settings_ApiKeyRemoveError", "The API key could not be removed."),
                    "LayoutFix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        credentialPanel.Controls.AddRange([
            credentialLabel,
            credentialInput,
            saveCredential,
            removeCredential
        ]);
        pnl.Controls.Add(credentialPanel);
        y += 45;

        onlineSetting = CreateToggleSetting(
            _locService.GetString("Settings_EnableOnlineTranslation", "Allow online translation through Google Cloud"),
            _currentSettings.OnlineTranslationEnabled,
            v => { _currentSettings.OnlineTranslationEnabled = v; SaveSettings(); },
            y);
        pnl.Controls.Add(onlineSetting);
        y += 50;

        var tsOffline = CreateToggleSetting(_locService.GetString("Settings_UseOfflineModel", "Use local offline model (No internet)"), _currentSettings.UseOfflineTranslation, v => { _currentSettings.UseOfflineTranslation = v; SaveSettings(); }, y);
        pnl.Controls.Add(tsOffline);
        y += 50;

        var tsHistory = CreateToggleSetting(
            _locService.GetString(
                "Settings_SaveHistoryLocally",
                "Save translation history locally (contains your text)"),
            _currentSettings.TranslationHistoryEnabled,
            v => { _currentSettings.TranslationHistoryEnabled = v; SaveSettings(); },
            y);
        pnl.Controls.Add(tsHistory);
        y += 50;

        var clearHistory = new Button
        {
            Text = _locService.GetString("Settings_ClearTranslationHistory", "Clear saved translation history"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(280, 30),
            Height = 30,
            Location = new Point(0, y),
            FlatStyle = FlatStyle.Flat,
            ForeColor = _textColor,
            BackColor = _sidebarColor
        };
        clearHistory.Click += async (_, _) =>
        {
            var confirmation = MessageBox.Show(
                this,
                _locService.GetString("Settings_ClearHistoryConfirmation", "Delete all locally saved translations?"),
                _locService.GetString("Settings_ClearTranslationHistory", "Clear saved translation history"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes)
                return;

            try
            {
                await _translationHistoryService.ClearHistoryAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError("Failed to clear translation history", exception);
                MessageBox.Show(
                    this,
                    _locService.GetString("Settings_ClearHistoryError", "Translation history could not be deleted."),
                    "LayoutFix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        pnl.Controls.Add(clearHistory);
        y += 42;

        var lblModel = new Label { Text = _locService.GetString("Settings_ModelSelection", "Model selection:"), Font = new Font("Segoe UI", 10F), AutoSize = true, Location = new Point(0, y + 4), ForeColor = _textColor };
        MeasureNow(lblModel);
        int modelComboX = lblModel.Right + 15;
        var cmbModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350, Location = new Point(modelComboX, y), BackColor = _sidebarColor, ForeColor = _textColor };
        cmbModel.Items.Add(new { Id = "light", Name = _locService.GetString("Settings_ModelLight", "Light Qwen 0.5B (398 MB, 4 languages)") });
        cmbModel.Items.Add(new { Id = "alma", Name = _locService.GetString("Settings_ModelAlma", "Specialized ALMA 7B (4.08 GB, 6 languages)") });
        cmbModel.Items.Add(new { Id = "pro", Name = _locService.GetString("Settings_ModelQwenPro", "Balanced Qwen2.5 1.5B (1.12 GB, 5 languages)") });
        cmbModel.ValueMember = "Id";
        cmbModel.DisplayMember = "Name";

        cmbModel.SelectedIndex = _currentSettings.OfflineModelType == "pro" ? 2 : (_currentSettings.OfflineModelType == "alma" ? 1 : 0);
        pnl.Controls.Add(lblModel);
        pnl.Controls.Add(cmbModel);
        var lblModelCapabilities = new Label
        {
            AutoSize = true,
            // Sits on the same row as btnDownload (below), not under cmbModel:
            // it must clear btnDownload's fixed 200px width, not modelComboX.
            Location = new Point(220, y + 44),
            ForeColor = _textColor
        };
        pnl.Controls.Add(lblModelCapabilities);
        y += 40;

        var downloadService = _modelDownloadService;
        
        var btnDownload = new Button
        {
            Width = 200,
            Height = 30,
            Location = new Point(0, y),
            FlatStyle = FlatStyle.Flat,
            ForeColor = _textColor,
            BackColor = _sidebarColor,
            AccessibleName = "Translation.ModelDownload"
        };
        var progressDownload = new ProgressBar { Width = 280, Height = 10, Location = new Point(220, y + 10), Visible = false };
        var btnCancelDownload = new Button
        {
            Text = _locService.GetString("Common_Cancel", "Cancel"),
            Width = 110,
            Height = 30,
            Location = new Point(510, y),
            FlatStyle = FlatStyle.Flat,
            ForeColor = _textColor,
            BackColor = _sidebarColor,
            Visible = false
        };
        CancellationTokenSource? activeDownloadCancellation = null;
        var selectedModelIsDownloaded = false;
        
        Action updateDownloadButton = () => {
            string modelType = (cmbModel.SelectedItem as dynamic)?.Id ?? "light";
            var descriptor = OfflineModelCatalog.Get(modelType);
            string path = OfflineModelLocator.GetModelPath(modelType);
            
            selectedModelIsDownloaded = downloadService.IsModelDownloaded(path, descriptor);
            if (selectedModelIsDownloaded) {
                btnDownload.Text = _locService.GetString("Settings_ModelDownloaded", "Model Downloaded");
                btnDownload.Enabled = true;
                btnDownload.TabStop = false;
            } else {
                btnDownload.Text = _locService.GetString("Settings_DownloadModel", "Download Model");
                btnDownload.Enabled = true;
                btnDownload.TabStop = true;
            }
        };

        Action updateModelCapabilities = () =>
        {
            string modelType = (cmbModel.SelectedItem as dynamic)?.Id ?? "light";
            lblModelCapabilities.Text = modelType switch
            {
                "alma" => _locService.GetString(
                    "Settings_ModelAlmaCapabilities",
                    "Validated offline targets: EN, RU, UK, DE, FR, ES."),
                "pro" => _locService.GetString(
                    "Settings_ModelQwenProCapabilities",
                    "Validated offline targets: EN, RU, UK, FR, ES."),
                _ => _locService.GetString(
                    "Settings_ModelLightCapabilities",
                    "Validated offline targets: EN, RU, FR, ES.")
            };
        };

        cmbModel.SelectedIndexChanged += (s, e) => {
            _currentSettings.OfflineModelType = (cmbModel.SelectedItem as dynamic)?.Id ?? "light";
            SaveSettings();
            updateDownloadButton();
            updateModelCapabilities();
        };
        
        updateDownloadButton();
        updateModelCapabilities();

        btnCancelDownload.Click += (_, _) =>
        {
            btnCancelDownload.Enabled = false;
            activeDownloadCancellation?.Cancel();
        };
        
        btnDownload.Click += async (s, e) =>
        {
            if (activeDownloadCancellation != null || selectedModelIsDownloaded)
                return;

            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token);
            activeDownloadCancellation = downloadCancellation;
            btnDownload.Enabled = false;
            cmbModel.Enabled = false;
            lblModelCapabilities.Visible = false;
            progressDownload.Visible = true;
            progressDownload.Value = 0;
            btnCancelDownload.Enabled = true;
            btnCancelDownload.Visible = true;
            btnDownload.Text = _locService.GetString("Settings_Downloading", "Downloading...");
            try
            {
                string modelType = _currentSettings.OfflineModelType;
                var descriptor = OfflineModelCatalog.Get(modelType);
                string path = OfflineModelLocator.GetModelPath(modelType);

                await downloadService.DownloadModelAsync(descriptor, path, p =>
                {
                    if (downloadCancellation.IsCancellationRequested || progressDownload.IsDisposed)
                        return;

                    void UpdateProgress() => progressDownload.Value = Math.Clamp((int)(p * 100), 0, 100);
                    if (progressDownload.InvokeRequired)
                        progressDownload.BeginInvoke(UpdateProgress);
                    else
                        UpdateProgress();
                }, downloadCancellation.Token);
            }
            catch (OperationCanceledException) when (downloadCancellation.IsCancellationRequested)
            {
                if (!_lifetimeCancellation.IsCancellationRequested && !IsDisposed && !Disposing)
                    btnDownload.Text = _locService.GetString("Settings_DownloadCancelled", "Download cancelled");
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return;
                btnDownload.Text = _locService.GetString("Settings_DownloadError", "Download Error");
                MessageBox.Show(
                    this,
                    _locService.GetString("Settings_DownloadError", "Download Error") + ": " + ex.Message,
                    "LayoutFix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                activeDownloadCancellation = null;
                if (!_lifetimeCancellation.IsCancellationRequested && !IsDisposed && !Disposing)
                {
                    cmbModel.Enabled = true;
                    lblModelCapabilities.Visible = true;
                    progressDownload.Visible = false;
                    btnCancelDownload.Visible = false;
                    updateDownloadButton();
                }
            }
        };

        pnl.Controls.Add(btnDownload);
        pnl.Controls.Add(progressDownload);
        pnl.Controls.Add(btnCancelDownload);
        y += 50;

        var locales = new LangItem[] {
            new LangItem{ Code="en", Name="English" }, new LangItem{ Code="ru", Name="Русский" }, new LangItem{ Code="uk", Name="Українська" },
            new LangItem{ Code="de", Name="Deutsch" }, new LangItem{ Code="pl", Name="Polski" }, new LangItem{ Code="es", Name="Español" },
            new LangItem{ Code="cs", Name="Čeština" }, new LangItem{ Code="fr", Name="Français" }, new LangItem{ Code="it", Name="Italiano" },
            new LangItem{ Code="tr", Name="Türkçe" }, new LangItem{ Code="kk", Name="Қазақ тілі" }, new LangItem{ Code="ka", Name="ქართული" },
            new LangItem{ Code="sr", Name="Српски" }, new LangItem{ Code="hy", Name="Հայերեն" }, new LangItem{ Code="he", Name="עברית" },
            new LangItem{ Code="ro", Name="Română" }, new LangItem{ Code="sk", Name="Slovenčina" }, new LangItem{ Code="nl", Name="Nederlands" },
            new LangItem{ Code="bg", Name="Български" }, new LangItem{ Code="el", Name="Ελληνικά" }, new LangItem{ Code="th", Name="ไทย" },
            new LangItem{ Code="pt", Name="Português" }
        };

        pnl.Controls.Add(CreateLanguageDropdown(_locService.GetString("Settings_TranslateLang1", "Translate to Language 1"), _currentSettings.TranslateLang1, locales.ToArray(), v => { _currentSettings.TranslateLang1 = v; SaveSettings(); }, ref y));
        pnl.Controls.Add(CreateLanguageDropdown(_locService.GetString("Settings_TranslateLang2", "Translate to Language 2"), _currentSettings.TranslateLang2, locales.ToArray(), v => { _currentSettings.TranslateLang2 = v; SaveSettings(); }, ref y));
        pnl.Controls.Add(CreateLanguageDropdown(_locService.GetString("Settings_TranslateLang3", "Translate to Language 3"), _currentSettings.TranslateLang3, locales.ToArray(), v => { _currentSettings.TranslateLang3 = v; SaveSettings(); }, ref y));

        _tabTranslate.Controls.Add(pnl);
    }

    private Panel CreateLanguageDropdown(string label, string currentVal, LangItem[] locales, Action<string> onChange, ref int y)
    {
        var lbl = new Label { Text = label, ForeColor = _textColor, Font = new Font("Segoe UI", 12), AutoSize = true, Location = new Point(0, 8) };
        MeasureNow(lbl);
        int comboX = Math.Max(lbl.Right + 15, 350);
        var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(comboX, 8), BackColor = _sidebarColor, ForeColor = _textColor, DisplayMember = "Name", ValueMember = "Code" };
        var pnl = new Panel { Width = Math.Max(500, comboX + cmb.Width), Height = 40, Location = new Point(0, y) };

        cmb.Items.AddRange(locales.Cast<object>().ToArray());
        var selectedIndex = Array.FindIndex(locales, locale =>
            string.Equals(locale.Code, currentVal, StringComparison.OrdinalIgnoreCase));
        cmb.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        cmb.SelectedIndexChanged += (s, e) =>
        {
            if (cmb.SelectedItem is LangItem item)
                onChange(item.Code);
        };
        
        pnl.Controls.Add(lbl);
        pnl.Controls.Add(cmb);
        y += 50;
        return pnl;
    }

    private void BuildAboutTab()
    {
        // Wrapped in an AutoScroll panel like the other tabs: this tab's
        // content is tall enough (logo, description, link, full diagnostics
        // report, copy button) that without scrolling the bottom controls
        // become unreachable whenever the window is short, at higher DPI, or
        // with larger Windows text scaling.
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var lblTitle = new Label { Text = _locService.GetString("Settings_About", "About LayoutFix"), ForeColor = _textColor, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) };

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

        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.9";
        var lblDesc = new Label
        {
            Text = $"LayoutFix v{version}\n" +
                _locService.GetString("Settings_BuiltWith", "Built with .NET 8.") + "\n" +
                _locService.GetString("Settings_AboutTagline", "Automatic keyboard layout correction and translation."),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 12),
            AutoSize = true,
            Location = new Point(120, 50)
        };
        MeasureNow(lblDesc);

        var linkGitHub = new LinkLabel
        {
            Text = _locService.GetString("Settings_ViewOnGitHub", "View on GitHub"),
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            LinkColor = _accentColor,
            ActiveLinkColor = _accentColor,
            VisitedLinkColor = _accentColor,
            Location = new Point(120, lblDesc.Bottom + 8)
        };
        MeasureNow(linkGitHub);
        const string repoUrl = "https://github.com/Wave-is/LayoutFix";
        linkGitHub.Links.Add(0, linkGitHub.Text.Length, repoUrl);
        linkGitHub.LinkClicked += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Link?.LinkData as string ?? repoUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                _logger.LogError("Failed to open the GitHub page", exception);
            }
        };

        int diagnosticsY = Math.Max(picLogo.Bottom, linkGitHub.Bottom) + 20;
        var lblDiagnostics = new Label
        {
            Text = _locService.GetString("Settings_DiagnosticsReport", "Safe diagnostic report"),
            ForeColor = _textColor,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, diagnosticsY)
        };
        MeasureNow(lblDiagnostics);
        var lblDiagnosticsPrivacy = new Label
        {
            Text = _locService.GetString(
                "Settings_DiagnosticsPrivacy",
                "Contains technical configuration counts only—no typed text, clipboard data, paths, API keys, or log contents."),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9),
            Location = new Point(0, lblDiagnostics.Bottom + 8),
            Width = 780,
            Height = 34
        };
        var txtDiagnostics = new TextBox
        {
            Text = DiagnosticsReportBuilder.Build(_currentSettings, version),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = _sidebarColor,
            ForeColor = _textColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9),
            Location = new Point(0, lblDiagnosticsPrivacy.Bottom + 8),
            Width = 780,
            Height = 238,
            AccessibleName = "DiagnosticsReport.Preview"
        };
        var btnCopyDiagnostics = new Button
        {
            Text = _locService.GetString("Settings_CopyDiagnostics", "Copy report"),
            Location = new Point(0, txtDiagnostics.Bottom + 12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(160, 30),
            Height = 30,
            BackColor = _accentColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = "DiagnosticsReport.Copy"
        };
        btnCopyDiagnostics.FlatAppearance.BorderSize = 0;
        MeasureNow(btnCopyDiagnostics);
        var lblCopyStatus = new Label
        {
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(btnCopyDiagnostics.Right + 10, txtDiagnostics.Bottom + 19),
            AccessibleName = "DiagnosticsReport.Status"
        };
        btnCopyDiagnostics.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(txtDiagnostics.Text);
                lblCopyStatus.Text = _locService.GetString("Settings_DiagnosticsCopied", "Copied to clipboard.");
            }
            catch
            {
                lblCopyStatus.Text = _locService.GetString("Settings_DiagnosticsCopyError", "Clipboard is temporarily unavailable.");
            }
        };

        pnl.Controls.Add(picLogo);
        pnl.Controls.Add(lblDesc);
        pnl.Controls.Add(linkGitHub);
        pnl.Controls.Add(lblDiagnostics);
        pnl.Controls.Add(lblDiagnosticsPrivacy);
        pnl.Controls.Add(txtDiagnostics);
        pnl.Controls.Add(btnCopyDiagnostics);
        pnl.Controls.Add(lblCopyStatus);
        pnl.Controls.Add(lblTitle);
        _tabAbout.Controls.Add(pnl);
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
