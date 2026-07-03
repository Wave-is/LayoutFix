using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.UI;

public class SettingsForm : Form
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoStartService _autoStartService;
    private readonly ILocalizationService _locService;
    private AppSettings _currentSettings;
    
    private CheckBox _chkAutoStart = null!;
    private CheckBox _chkSound = null!;
    private CheckBox _chkUseFlagIcons = null!;
    private ListBox _lstExceptions = null!;
    private DataGridView _gridHotkeys = null!;

    public SettingsForm(ISettingsService settingsService, IAutoStartService autoStartService, ILocalizationService locService)
    {
        _settingsService = settingsService;
        _autoStartService = autoStartService;
        _locService = locService;
        _currentSettings = _settingsService.Current;
        
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = _locService.GetString("Settings_Title", "LayoutFix - Settings");
        this.Size = new Size(650, 450);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Icon = CreateAppIcon();

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        
        var tabHotkeys = new TabPage(_locService.GetString("Settings_Hotkeys", "Hotkeys"));
        InitializeHotkeysTab(tabHotkeys);

        var tabExceptions = new TabPage(_locService.GetString("Settings_Exceptions", "Exceptions"));
        InitializeExceptionsTab(tabExceptions);

        var tabDict = new TabPage(_locService.GetString("Settings_Dict", "Dictionary"));
        InitializeDictionaryTab(tabDict);

        var tabGeneral = new TabPage(_locService.GetString("Settings_General", "General"));
        InitializeGeneralTab(tabGeneral);

        var tabAbout = new TabPage(_locService.GetString("Settings_About", "About"));
        InitializeAboutTab(tabAbout);

        tabControl.TabPages.Add(tabHotkeys);
        tabControl.TabPages.Add(tabExceptions);
        tabControl.TabPages.Add(tabDict);
        tabControl.TabPages.Add(tabGeneral);
        tabControl.TabPages.Add(tabAbout);

        this.Controls.Add(tabControl);

        this.FormClosing += (s, e) =>
        {
            _currentSettings.AutoStart = _chkAutoStart.Checked;
            _currentSettings.SoundEnabled = _chkSound.Checked;
            _currentSettings.UseFlagIcons = _chkUseFlagIcons.Checked;
            
            _currentSettings.BlacklistedProcesses.Clear();
            foreach (string proc in _lstExceptions.Items)
            {
                _currentSettings.BlacklistedProcesses.Add(proc);
            }
            
            if (_lstUserExceptions != null)
            {
                _currentSettings.UserExceptions.Clear();
                foreach (string w in _lstUserExceptions.Items) _currentSettings.UserExceptions.Add(w);
            }
            
            _settingsService.Save(_currentSettings);
            _autoStartService.IsAutoStartEnabled = _currentSettings.AutoStart;
        };
    }

    private void InitializeHotkeysTab(TabPage tab)
    {
        var label = new Label
        {
            Text = "Выберите действие. Для отключения/включения комбинации кликните по ней правой кнопкой мыши.",
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 0, 0)
        };
        tab.Controls.Add(label);

        _gridHotkeys = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };

        _gridHotkeys.Columns.Add("Action", "Действие");
        _gridHotkeys.Columns.Add("Set1", "Набор 1 (Scroll Lock)");
        _gridHotkeys.Columns.Add("Set2", "Набор 2 (Pause)");
        _gridHotkeys.Columns.Add("Set3", "Набор 3 (Тильда)");

        _gridHotkeys.EnableHeadersVisualStyles = false;
        _gridHotkeys.ColumnHeadersDefaultCellStyle.BackColor = Color.LightBlue;
        _gridHotkeys.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.LightBlue;

        // Add 4 actions exactly as requested, we have 2 FixLayout variants.
        // Let's manually group them to look like the Punto Switcher screenshot.
        AddHotkeyRow("FixLayout (Word)", "Отменить конвертацию раскладки", "FixLayout", "Scroll", "Pause", "Ctrl+`");
        AddHotkeyRow("FixLayout (Selected)", "Сменить раскладку выделенного текста", "FixLayout", "Shift+Scroll", "Shift+Pause", "Ctrl+Shift+`");
        AddHotkeyRow("ChangeCase", "Сменить регистр выделенного текста", "ChangeCase", "Alt+Scroll", "Alt+Pause", "Alt+`");
        AddHotkeyRow("Transliterate", "Транслитерировать выделенный текст", "Transliterate", "Ctrl+Alt+Scroll", "Ctrl+Alt+Pause", "Ctrl+Alt+`");

        _gridHotkeys.CellMouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 1)
            {
                var cell = _gridHotkeys.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell.Tag is HotkeyConfig config)
                {
                    config.Enabled = !config.Enabled;
                    UpdateCellAppearance(cell, config.Enabled);
                    _gridHotkeys.ClearSelection();
                }
            }
        };

        _gridHotkeys.ColumnHeaderMouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.ColumnIndex >= 1)
            {
                int preset = e.ColumnIndex;
                var configsForPreset = _currentSettings.HotkeyConfigs.Where(c => c.Preset == preset).ToList();
                if (configsForPreset.Any())
                {
                    bool allEnabled = configsForPreset.All(c => c.Enabled);
                    bool newState = !allEnabled;
                    
                    foreach (var config in configsForPreset)
                    {
                        config.Enabled = newState;
                    }
                    
                    foreach (DataGridViewRow row in _gridHotkeys.Rows)
                    {
                        var cell = row.Cells[e.ColumnIndex];
                        if (cell.Tag is HotkeyConfig cfg)
                        {
                            UpdateCellAppearance(cell, cfg.Enabled);
                        }
                    }
                }
            }
        };

        _gridHotkeys.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 1)
            {
                var cell = _gridHotkeys.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell.Tag is HotkeyConfig config)
                {
                    using var editor = new HotkeyEditorForm(config.Hotkey);
                    if (editor.ShowDialog(this) == DialogResult.OK)
                    {
                        config.Hotkey = editor.ResultHotkey;
                        cell.Value = config.Hotkey;
                    }
                }
            }
        };

        tab.Controls.Add(_gridHotkeys);
        _gridHotkeys.BringToFront(); // Bring grid below label
    }

    private void AddHotkeyRow(string key, string title, string action, string hotkey1, string hotkey2, string hotkey3)
    {
        int r = _gridHotkeys.Rows.Add();
        var row = _gridHotkeys.Rows[r];
        row.Cells[0].Value = title;

        AssignHotkeyCell(row.Cells[1], action, hotkey1, 1);
        AssignHotkeyCell(row.Cells[2], action, hotkey2, 2);
        AssignHotkeyCell(row.Cells[3], action, hotkey3, 3);
    }

    private void AssignHotkeyCell(DataGridViewCell cell, string action, string expectedHotkey, int preset)
    {
        var config = _currentSettings.HotkeyConfigs.FirstOrDefault(c => c.Action == action && c.Hotkey == expectedHotkey && c.Preset == preset);
        if (config == null)
        {
            // Create if missing from settings
            config = new HotkeyConfig { Action = action, Hotkey = expectedHotkey, Preset = preset, Enabled = true };
            _currentSettings.HotkeyConfigs.Add(config);
        }

        cell.Value = config.Hotkey;
        cell.Tag = config;
        UpdateCellAppearance(cell, config.Enabled);
    }

    private void UpdateCellAppearance(DataGridViewCell cell, bool enabled)
    {
        if (enabled)
        {
            cell.Style.ForeColor = Color.Black;
            cell.Style.Font = new Font(_gridHotkeys.Font, FontStyle.Regular);
        }
        else
        {
            cell.Style.ForeColor = Color.Gray;
            cell.Style.Font = new Font(_gridHotkeys.Font, FontStyle.Strikeout);
        }
    }

    private ListBox _lstUserExceptions = null!;

    private void InitializeDictionaryTab(TabPage tab)
    {
        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 60 };
        tab.Controls.Add(splitContainer);

        var pnlTop = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var txtWord = new TextBox { Width = 200, Location = new Point(10, 20) };
        var btnAdd = new Button { Text = _locService.GetString("Settings_Add", "Add"), Location = new Point(220, 18), Width = 100 };
        var btnRemove = new Button { Text = _locService.GetString("Settings_Remove", "Remove"), Location = new Point(330, 18), Width = 100 };
        pnlTop.Controls.AddRange(new Control[] { new Label { Text = _locService.GetString("Settings_ExceptionWord", "Word to never auto-convert:"), Location = new Point(10, 5), AutoSize = true }, txtWord, btnAdd, btnRemove });

        _lstUserExceptions = new ListBox { Dock = DockStyle.Fill };
        foreach (var w in _currentSettings.UserExceptions) _lstUserExceptions.Items.Add(w);

        btnAdd.Click += (s, e) =>
        {
            string w = txtWord.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(w) && !_lstUserExceptions.Items.Contains(w))
            {
                _lstUserExceptions.Items.Add(w);
                txtWord.Clear();
            }
        };

        btnRemove.Click += (s, e) =>
        {
            if (_lstUserExceptions.SelectedIndex >= 0)
                _lstUserExceptions.Items.RemoveAt(_lstUserExceptions.SelectedIndex);
        };

        splitContainer.Panel1.Controls.Add(pnlTop);
        splitContainer.Panel2.Controls.Add(_lstUserExceptions);
        splitContainer.Panel2.Padding = new Padding(10);
    }

    private void InitializeExceptionsTab(TabPage tab)
    {
        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 60 };
        tab.Controls.Add(splitContainer);

        var pnlTop = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var txtProcess = new TextBox { Width = 200, Location = new Point(10, 20) };
        var btnAdd = new Button { Text = _locService.GetString("Settings_Add", "Add"), Location = new Point(220, 18), Width = 100 };
        var btnRemove = new Button { Text = _locService.GetString("Settings_Remove", "Remove"), Location = new Point(330, 18), Width = 100 };
        pnlTop.Controls.AddRange(new Control[] { new Label { Text = _locService.GetString("Settings_ProcessName", "Process name (e.g., devenv.exe):"), Location = new Point(10, 5), AutoSize = true }, txtProcess, btnAdd, btnRemove });

        _lstExceptions = new ListBox { Dock = DockStyle.Fill };
        foreach (var proc in _currentSettings.BlacklistedProcesses) _lstExceptions.Items.Add(proc);

        btnAdd.Click += (s, e) =>
        {
            string proc = txtProcess.Text.Trim();
            if (!string.IsNullOrEmpty(proc) && !_lstExceptions.Items.Contains(proc))
            {
                _lstExceptions.Items.Add(proc);
                txtProcess.Clear();
            }
        };

        btnRemove.Click += (s, e) =>
        {
            if (_lstExceptions.SelectedIndex >= 0)
                _lstExceptions.Items.RemoveAt(_lstExceptions.SelectedIndex);
        };

        splitContainer.Panel1.Controls.Add(pnlTop);
        splitContainer.Panel2.Controls.Add(_lstExceptions);
        splitContainer.Panel2.Padding = new Padding(10);
    }

    private void InitializeGeneralTab(TabPage tab)
    {
        _chkAutoStart = new CheckBox
        {
            Text = _locService.GetString("Settings_AutoStart", "Start with Windows"),
            AutoSize = true,
            Location = new Point(20, 20),
            Checked = _currentSettings.AutoStart
        };

        _chkSound = new CheckBox
        {
            Text = _locService.GetString("Settings_Sound", "Enable sound notifications"),
            AutoSize = true,
            Location = new Point(20, 50),
            Checked = _currentSettings.SoundEnabled
        };

        _chkUseFlagIcons = new CheckBox
        {
            Text = _locService.GetString("Settings_Flags", "Use country flags in tray"),
            AutoSize = true,
            Location = new Point(20, 80),
            Checked = _currentSettings.UseFlagIcons
        };

        _chkUseFlagIcons.CheckedChanged += (s, e) =>
        {
            _currentSettings.UseFlagIcons = _chkUseFlagIcons.Checked;
            _settingsService.Save(_currentSettings);
        };

        var chkAutoConversion = new CheckBox
        {
            Text = _locService.GetString("Settings_AutoConv", "Enable auto-conversion during typing"),
            AutoSize = true,
            Location = new Point(20, 110),
            Checked = _currentSettings.AutoConversionEnabled
        };

        chkAutoConversion.CheckedChanged += (s, e) =>
        {
            _currentSettings.AutoConversionEnabled = chkAutoConversion.Checked;
            _settingsService.Save(_currentSettings);
        };

        tab.Controls.Add(_chkAutoStart);
        tab.Controls.Add(_chkSound);
        tab.Controls.Add(_chkUseFlagIcons);
        tab.Controls.Add(chkAutoConversion);
    }

    private Icon CreateAppIcon()
    {
        int size = 64;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(0, 120, 215)); // Windows 10/11 blue
        g.FillEllipse(brush, 2, 2, size - 4, size - 4);

        using var font = new Font("Segoe UI", 24, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("LF", font, textBrush, new RectangleF(0, 0, size, size + 2), format);

        return Icon.FromHandle(bmp.GetHicon());
    }

    private void InitializeAboutTab(TabPage tab)
    {
        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png");
        if (System.IO.File.Exists(logoPath))
        {
            var pb = new PictureBox
            {
                Image = Image.FromFile(logoPath),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                Margin = new Padding(20)
            };
            tlp.Controls.Add(pb, 0, 0);
        }

        var lblAbout = new Label
        {
            Text = "LayoutFix v1.0\nA modern alternative to Punto Switcher.\n\nBuilt with .NET 8.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 12),
            Padding = new Padding(0, 20, 0, 0)
        };
        tlp.Controls.Add(lblAbout, 0, 1);
        
        tab.Controls.Add(tlp);
    }

}
