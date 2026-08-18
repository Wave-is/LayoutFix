using System;
using System.Drawing;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.UI;

public class HotkeyEditorForm : Form
{
    private Label _lblActionDesc = null!;
    private TextBox _txtActionName = null!;
    private GroupBox _gbCombo = null!;
    private Label _lblInstruction = null!;
    private TextBox _txtHotkey = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;

    public string ResultHotkey { get; private set; }
    private readonly string _actionDescription;
    private readonly ILocalizationService _locService;
    private bool _winPressed;

    public HotkeyEditorForm(string initialHotkey, string actionDescription, ILocalizationService locService)
    {
        ResultHotkey = initialHotkey;
        _actionDescription = actionDescription;
        _locService = locService;
        InitializeComponent();
    }

    private const int DialogContentWidth = 335;

    // AutoSize controls report a placeholder size until they are parented;
    // forcing GetPreferredSize makes .Right/.Bottom correct immediately, so
    // sibling controls can be positioned relative to them before layout.
    private static void MeasureNow(Control control) => control.Size = control.GetPreferredSize(Size.Empty);

    private void InitializeComponent()
    {
        // See the matching comment in SettingsForm.InitializeComponent: this
        // dialog's width (DialogContentWidth, the instruction's wrap width)
        // is still hand-placed pixels, so it needs the same DPI baseline.
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.AutoScaleDimensions = new SizeF(96F, 96F);
        this.Text = _locService.GetString("HotkeyEditor_Title", "Choose key combination");
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;
        this.Font = new Font("Segoe UI", 9F);
        this.BackColor = SystemColors.Control;

        _lblActionDesc = new Label
        {
            Text = _locService.GetString("HotkeyEditor_Action", "Action:"),
            Location = new Point(15, 15),
            AutoSize = true,
            ForeColor = SystemColors.ControlDarkDark
        };

        _txtActionName = new TextBox
        {
            Text = _actionDescription,
            Location = new Point(15, 35),
            Width = DialogContentWidth,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Font = new Font("Segoe UI", 10F)
        };
        _txtActionName.GotFocus += (s, e) => this.Focus();

        // The instruction text may need two lines in some languages, so the
        // hotkey field, the group box and the whole dialog height all follow
        // from its actual rendered size instead of a fixed magic Y. AutoSize
        // controls don't compute their real size until parented, so it's
        // forced explicitly (MeasureNow) before any sibling reads .Bottom.
        _lblInstruction = new Label
        {
            Text = _locService.GetString(
                "HotkeyEditor_Instruction",
                "Place the cursor in the field and press the desired keys:"),
            Location = new Point(15, 20),
            AutoSize = true,
            MaximumSize = new Size(DialogContentWidth - 30, 0),
            Font = new Font("Segoe UI", 9F),
            ForeColor = SystemColors.ControlDarkDark
        };
        MeasureNow(_lblInstruction);

        _txtHotkey = new TextBox
        {
            Text = ResultHotkey,
            Location = new Point(15, _lblInstruction.Bottom + 8),
            Width = DialogContentWidth - 35,
            ReadOnly = true,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.IBeam
        };

        _gbCombo = new GroupBox
        {
            Text = _locService.GetString("HotkeyEditor_Combination", "Combination"),
            Location = new Point(15, 70),
            Size = new Size(DialogContentWidth, _txtHotkey.Bottom + 15)
        };
        _gbCombo.Controls.Add(_lblInstruction);
        _gbCombo.Controls.Add(_txtHotkey);

        int buttonsY = _gbCombo.Bottom + 15;
        _btnCancel = new Button
        {
            Text = _locService.GetString("HotkeyEditor_Cancel", "Cancel"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(75, 28),
            Height = 28,
            Font = new Font("Segoe UI", 9F),
            DialogResult = DialogResult.Cancel
        };
        MeasureNow(_btnCancel);
        _btnCancel.Location = new Point(DialogContentWidth + 15 - _btnCancel.Width, buttonsY);

        _btnSave = new Button
        {
            Text = _locService.GetString("HotkeyEditor_OK", "OK"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(75, 28),
            Height = 28,
            Font = new Font("Segoe UI", 9F),
            DialogResult = DialogResult.OK
        };
        MeasureNow(_btnSave);
        _btnSave.Location = new Point(_btnCancel.Left - 10 - _btnSave.Width, buttonsY);

        this.ClientSize = new Size(DialogContentWidth + 30, _btnSave.Bottom + 20);

        this.Controls.Add(_lblActionDesc);
        this.Controls.Add(_txtActionName);
        this.Controls.Add(_gbCombo);
        this.Controls.Add(_btnSave);
        this.Controls.Add(_btnCancel);

        this.KeyDown += HotkeyEditorForm_KeyDown;
        this.KeyUp += (_, e) =>
        {
            if (e.KeyCode is Keys.LWin or Keys.RWin)
                _winPressed = false;
        };
    }

    private void HotkeyEditorForm_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
            return;
        }

        if (e.KeyCode == Keys.Enter)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
            return;
        }

        if (e.KeyCode is Keys.LWin or Keys.RWin)
        {
            _winPressed = true;
            return;
        }

        // Ignore modifier-only presses
        if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
        {
            return;
        }

        string newCombo = "";
        if (e.Control) newCombo += "Ctrl+";
        if (e.Shift) newCombo += "Shift+";
        if (e.Alt) newCombo += "Alt+";
        if (_winPressed) newCombo += "Win+";

        var keyName = HotkeyCombo.GetCanonicalKeyName((int)e.KeyCode);
        if (keyName.Length == 0)
            return;

        newCombo += keyName;

        ResultHotkey = newCombo;
        _txtHotkey.Text = ResultHotkey;
    }
}
