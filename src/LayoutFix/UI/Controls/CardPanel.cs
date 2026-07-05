using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LayoutFix.UI.Controls;

public class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 15;
    public Color CardBackColor { get; set; } = Color.FromArgb(35, 35, 35);
    public Color CardBorderColor { get; set; } = Color.FromArgb(60, 60, 60);

    public CardPanel()
    {
        this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        this.BackColor = Color.Transparent;
        this.Padding = new Padding(15);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
        var path = GetRoundedRect(rect, CornerRadius);

        using (var brush = new SolidBrush(CardBackColor))
        {
            g.FillPath(brush, path);
        }

        using (var pen = new Pen(CardBorderColor, 1))
        {
            g.DrawPath(pen, path);
        }

        base.OnPaint(e);
    }

    private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var size = new Size(diameter, diameter);
        var arc = new Rectangle(bounds.Location, size);
        var path = new GraphicsPath();

        if (radius == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        // Top left
        path.AddArc(arc, 180, 90);
        
        // Top right
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        
        // Bottom right
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        
        // Bottom left
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        
        path.CloseFigure();
        return path;
    }
}
