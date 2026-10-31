using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Adnd.Core.Config;

namespace Adnd.Game;

/// <summary>The thick, rounded frame shared by every old-style dialog.</summary>
internal static class OldStyleFrame
{
    public const int Thickness = 6;
    private const int OuterDiameter = 24;

    public static void Draw(Graphics graphics, Size size, Color color) =>
        Draw(graphics, new Rectangle(Point.Empty, size), color);

    public static void Draw(Graphics graphics, Rectangle bounds, Color color)
    {
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, Thickness);
        using var path = RoundedRectangle(
            new Rectangle(bounds.X + Thickness / 2, bounds.Y + Thickness / 2, bounds.Width - Thickness - 1, bounds.Height - Thickness - 1),
            OuterDiameter);
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = previous;
    }

    public static GraphicsPath RoundedRectangle(Rectangle rect, int diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static int InnerDiameter => OuterDiameter - Thickness;
}

/// <summary>
/// Replaces the one-pixel panel border of an old-style dialog. The parent form paints the thick rounded frame and
/// this panel clips its own outer edge so its contents never cover the frame; sizes and positions are unchanged.
/// </summary>
internal sealed class OldStyleFramePanel : Panel
{
    private bool _installed;

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (_installed || Parent is not Form form)
            return;

        if (form.FormBorderStyle != FormBorderStyle.None)
        {
            BorderStyle = BorderStyle.FixedSingle;
            return;
        }

        _installed = true;
        form.Paint += (_, args) => OldStyleFrame.Draw(args.Graphics, form.ClientSize, GameRulesProvider.Current.DefaultColor);
        form.Resize += (_, _) => form.Invalidate();
        ApplyClip();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyClip();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        ApplyClip();
    }

    private void ApplyClip()
    {
        if (!_installed || Width <= 0 || Height <= 0)
            return;

        var inset = Math.Max(0, OldStyleFrame.Thickness - Left);
        var rect = new Rectangle(inset, inset, Width - 2 * inset, Height - 2 * inset);
        using var path = OldStyleFrame.RoundedRectangle(rect, OldStyleFrame.InnerDiameter);
        Region = new Region(path);
    }
}
