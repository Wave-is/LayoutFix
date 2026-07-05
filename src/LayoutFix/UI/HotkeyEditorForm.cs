using System;
using System.Drawing;
using System.Windows.Forms;
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

    public HotkeyEditorForm(string initialHotkey, string actionDescription)
    {
        ResultHotkey = initialHotkey;
        _actionDescription = actionDescription;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Выбор комбинации клавиш";
        this.Size = new Size(380, 240);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;
        this.Font = new Font("Segoe UI", 9F);
        this.BackColor = SystemColors.Control;

        _lblActionDesc = new Label
        {
            Text = "Действие:",
            Location = new Point(15, 15),
            AutoSize = true,
            ForeColor = SystemColors.ControlDarkDark
        };

        _txtActionName = new TextBox
        {
            Text = _actionDescription,
            Location = new Point(15, 35),
            Width = 335,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Font = new Font("Segoe UI", 10F)
        };
        _txtActionName.GotFocus += (s, e) => this.Focus();

        _gbCombo = new GroupBox
        {
            Text = "Комбинация",
            Location = new Point(15, 70),
            Size = new Size(335, 80)
        };

        _lblInstruction = new Label
        {
            Text = "Установите курсор в поле и нажмите нужные клавиши:",
            Location = new Point(15, 20),
            AutoSize = true,
            ForeColor = SystemColors.ControlDarkDark
        };

        _txtHotkey = new TextBox
        {
            Text = ResultHotkey,
            Location = new Point(15, 45),
            Width = 300,
            ReadOnly = true,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.IBeam
        };

        _gbCombo.Controls.Add(_lblInstruction);
        _gbCombo.Controls.Add(_txtHotkey);

        _btnSave = new Button
        {
            Text = "ОК",
            Location = new Point(190, 165),
            Width = 75,
            Height = 28,
            DialogResult = DialogResult.OK
        };

        _btnCancel = new Button
        {
            Text = "Отмена",
            Location = new Point(275, 165),
            Width = 75,
            Height = 28,
            DialogResult = DialogResult.Cancel
        };

        this.Controls.Add(_lblActionDesc);
        this.Controls.Add(_txtActionName);
        this.Controls.Add(_gbCombo);
        this.Controls.Add(_btnSave);
        this.Controls.Add(_btnCancel);

        this.KeyDown += HotkeyEditorForm_KeyDown;
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

        // Ignore modifier-only presses
        if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
        {
            return;
        }

        string newCombo = "";
        if (e.Control) newCombo += "Ctrl+";
        if (e.Shift) newCombo += "Shift+";
        if (e.Alt) newCombo += "Alt+";

        string keyName = e.KeyCode.ToString();
        
        // Normalize some keys to match our parser
        if (e.KeyCode == Keys.Scroll) keyName = "Scroll";
        else if (e.KeyCode == Keys.Oemtilde) keyName = "`";
        else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) keyName = keyName.Substring(1);
        else if (e.KeyCode == Keys.Oemplus) keyName = "=";
        else if (e.KeyCode == Keys.OemMinus) keyName = "-";

        newCombo += keyName;

        ResultHotkey = newCombo;
        _txtHotkey.Text = ResultHotkey;
    }
}
