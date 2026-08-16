using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutFix.UI.Controls;

public class TranslationPopupForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private readonly Label _lblText;
    private readonly System.Windows.Forms.Timer _timerFade;
    private readonly System.Windows.Forms.Timer? _timerClose;
    private readonly bool _copyOnClick;
    private double _opacity = 0;

    public TranslationPopupForm(
        string text,
        Color bgColor,
        Color textColor,
        int autoCloseMilliseconds = 0,
        bool copyOnClick = true)
    {
        _copyOnClick = copyOnClick;
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.BackColor = bgColor;
        this.Opacity = 0;

        _lblText = new Label
        {
            Text = text,
            ForeColor = textColor,
            Font = new Font("Segoe UI", 12),
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Location = new Point(15, 15),
            BackColor = Color.Transparent
        };

        this.Controls.Add(_lblText);

        this.Size = new Size(_lblText.PreferredWidth + 30, _lblText.PreferredHeight + 30);

        _timerFade = new System.Windows.Forms.Timer { Interval = 15 };
        _timerFade.Tick += (s, e) =>
        {
            _opacity += 0.1;
            if (_opacity >= 0.95)
            {
                this.Opacity = 0.95;
                _timerFade.Stop();
            }
            else
            {
                this.Opacity = _opacity;
            }
        };

        if (autoCloseMilliseconds > 0)
        {
            _timerClose = new System.Windows.Forms.Timer { Interval = autoCloseMilliseconds };
            _timerClose.Tick += (_, _) => Close();
        }

        SetLocationNearCursor();
    }

    private void SetLocationNearCursor()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        
        int x = cursor.X + 15;
        int y = cursor.Y + 15;

        if (x + this.Width > screen.WorkingArea.Right)
            x = cursor.X - this.Width - 15;
        
        if (y + this.Height > screen.WorkingArea.Bottom)
            y = cursor.Y - this.Height - 15;

        this.Location = new Point(x, y);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
            return parameters;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _timerFade.Start();
        _timerClose?.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timerFade.Dispose();
            _timerClose?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var path = new GraphicsPath())
        {
            int radius = 10;
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(this.Width - radius - 1, 0, radius, radius, 270, 90);
            path.AddArc(this.Width - radius - 1, this.Height - radius - 1, radius, radius, 0, 90);
            path.AddArc(0, this.Height - radius - 1, radius, radius, 90, 90);
            path.CloseAllFigures();

            this.Region = new Region(path);

            using (var pen = new Pen(Color.FromArgb(100, 100, 100), 1))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        this.Close();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_copyOnClick)
            Clipboard.SetText(_lblText.Text);
        this.Close();
    }
}
