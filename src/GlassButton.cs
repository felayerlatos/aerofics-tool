using System.Drawing.Drawing2D;

namespace AeroficsLauncher;

internal sealed class GlassButton : Button
{
    public Bitmap? ToolIcon { get; set; }
    public bool Warm { get; set; }
    public bool Primary { get; set; }
    public bool Compact { get; set; }
    public bool Selected { get; private set; }
    public Func<Bitmap?>? Backdrop { get; set; }

    public GlassButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.FromArgb(238, 246, 255);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Selected = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Selected = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var backdrop = Backdrop?.Invoke();
        if (backdrop != null) g.DrawImage(backdrop, ClientRectangle, Bounds, GraphicsUnit.Pixel);
        else g.Clear(Color.FromArgb(10, 19, 34));
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using var shape = LauncherForm.RoundedPath(r, Math.Min(12, Height / 4f));
        Color edge = Warm ? Color.FromArgb(150, 239, 178, 104) : Color.FromArgb(Selected || Focused ? 230 : 85, 128, 216, 255);
        if (Primary)
        {
            using var fill = new LinearGradientBrush(r, Color.FromArgb(165, 239, 255), Color.FromArgb(119, 200, 244), 30);
            g.FillPath(fill, shape);
        }
        else
        {
            using var fill = new SolidBrush(Warm ? Color.FromArgb(225, 50, 39, 32) : Color.FromArgb(Selected ? 220 : 180, 17, 31, 52));
            g.FillPath(fill, shape);
        }
        using var border = new Pen(edge, Focused ? 2 : 1);
        g.DrawPath(border, shape);
        Color textColor = Primary ? Color.FromArgb(16, 33, 54) : Warm ? Color.FromArgb(255, 224, 184) : ForeColor;
        Rectangle textRect;
        if (ToolIcon != null && !Compact)
        {
            int iconSize = Math.Clamp(Height / 3, 26, 48);
            g.DrawImage(ToolIcon, new Rectangle((Width - iconSize) / 2, Math.Max(10, Height / 6), iconSize, iconSize));
            textRect = new Rectangle(5, Height * 3 / 5, Width - 10, Height * 2 / 5 - 3);
        }
        else textRect = Rectangle.Inflate(ClientRectangle, -7, -4);
        TextRenderer.DrawText(g, Text, Font, textRect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
