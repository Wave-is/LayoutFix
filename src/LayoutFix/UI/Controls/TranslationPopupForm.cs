using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutFix.UI.Controls;

public class TranslationPopupForm : Form
{
    private readonly Label _lblText;
    private readonly System.Windows.Forms.Timer _timerFade;
    private double _opacity = 0;

    public TranslationPopupForm(string text, Color bgColor, Color textColor)
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _timerFade.Start();
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
        Clipboard.SetText(_lblText.Text);
        this.Close();
    }
}
