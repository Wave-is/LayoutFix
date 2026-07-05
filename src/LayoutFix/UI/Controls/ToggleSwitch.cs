using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LayoutFix.UI.Controls;

public class ToggleSwitch : Control
{
    private bool _checked = false;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ToggleSwitch()
    {
        this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        this.Cursor = Cursors.Hand;
        this.Size = new Size(50, 26);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            Checked = !Checked;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(this.BackColor);

        int r = this.Height - 1;
        var path = new GraphicsPath();
        path.AddArc(0, 0, r, r, 90, 180);
        path.AddArc(this.Width - r - 1, 0, r, r, 270, 180);
        path.CloseFigure();

        Color bgColor = _checked ? Color.FromArgb(0, 120, 215) : Color.FromArgb(100, 100, 100);
        
        using (var brush = new SolidBrush(bgColor))
        {
            g.FillPath(brush, path);
        }

        int thumbSize = this.Height - 6;
        int targetX = _checked ? this.Width - thumbSize - 3 : 3;

        using (var thumbBrush = new SolidBrush(Color.White))
        {
            g.FillEllipse(thumbBrush, targetX, 3, thumbSize, thumbSize);
        }
    }
}
