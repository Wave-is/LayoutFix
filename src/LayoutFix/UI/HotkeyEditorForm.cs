using System;
using System.Drawing;
using System.Windows.Forms;
using LayoutFix.Core.Models;

namespace LayoutFix.UI;

public class HotkeyEditorForm : Form
{
    private Label _lblInstruction = null!;
    private TextBox _txtHotkey = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;

    public string ResultHotkey { get; private set; }

    public HotkeyEditorForm(string initialHotkey)
    {
        ResultHotkey = initialHotkey;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Редактирование хоткея";
        this.Size = new Size(350, 150);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;

        _lblInstruction = new Label
        {
            Text = "Нажмите новую комбинацию клавиш:",
            Location = new Point(20, 15),
            AutoSize = true
        };

        _txtHotkey = new TextBox
        {
            Text = ResultHotkey,
            Location = new Point(20, 40),
            Width = 290,
            ReadOnly = true,
            BackColor = SystemColors.Window
        };
        // Disable focusing to avoid caret blinking and standard typing, we intercept keys at Form level
        _txtHotkey.GotFocus += (s, e) => this.Focus();

        _btnSave = new Button
        {
            Text = "ОК",
            Location = new Point(150, 75),
            Width = 75,
            DialogResult = DialogResult.OK
        };

        _btnCancel = new Button
        {
            Text = "Отмена",
            Location = new Point(235, 75),
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        this.Controls.Add(_lblInstruction);
        this.Controls.Add(_txtHotkey);
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
