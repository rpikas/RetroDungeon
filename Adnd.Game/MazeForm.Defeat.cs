using System.Drawing.Drawing2D;
using Adnd.Core.Characters;
using Adnd.Game.Viewer;

namespace Adnd.Game;

public partial class MazeForm
{
    /// <summary>
    /// The cemetery screen (see Assets\ScenPictures\RIP.png): a tombstone per fallen character with the name
    /// beside it and the age at death on it, over a box announcing the slaughter. Closes on Enter.
    /// </summary>
    private void ShowCemeteryOnBoth(IReadOnlyList<Character> dead)
    {
        if (dead.Count == 0)
            return;

        const int columns = 2;
        const int cellWidth = 340;
        const int cellHeight = 100;
        var rows = (Math.Min(dead.Count, 6) + columns - 1) / columns;
        var graveyardHeight = Math.Max(rows, 3) * cellHeight + 20;

        using var form = new Form
        {
            Text = "Cemetery",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = Color.White,
            KeyPreview = true,
            ClientSize = new Size(columns * cellWidth + 20, graveyardHeight + 90),
        };

        using var nameFont = new Font("Consolas", 22f, FontStyle.Bold);
        using var messageFont = new Font("Consolas", 18f, FontStyle.Bold);
        form.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (var i = 0; i < dead.Count && i < 6; i++)
            {
                var x = 20 + (i % columns) * cellWidth;
                var y = 10 + (i / columns) * cellHeight;
                DrawTombstone(g, new Rectangle(x, y, 68, 88), dead[i].Age);
                g.DrawString(dead[i].Name.ToUpperInvariant(), nameFont, Brushes.White, x + 82, y + 22);
            }

            var box = new Rectangle(6, graveyardHeight, form.ClientSize.Width - 12, 80);
            var state = g.Save();
            g.TranslateTransform(box.X, box.Y);
            OldStyleFrame.Draw(g, box.Size, Color.White);
            g.Restore(state);

            var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("YOUR ENTIRE PARTY HAS BEEN SLAUGHTERED", messageFont, Brushes.White,
                new RectangleF(box.X, box.Y + 6, box.Width, 34), centered);
            g.DrawString("PRESS RETURN TO LEAVE THE CEMETERY", messageFont, Brushes.White,
                new RectangleF(box.X, box.Y + 40, box.Width, 34), centered);
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Escape or Keys.Space)
            {
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        var names = string.Join(", ", dead.Select(c => $"{c.Name} (aged {c.Age})"));
        var prompt = new ViewerPrompt("message", $"Your entire party has been slaughtered: {names}.", null, new[]
        {
            new ViewerPromptOption("ok", "Leave the cemetery"),
        });

        var answers = new Dictionary<string, DialogResult> { ["ok"] = DialogResult.OK };

        ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
    }

    // Black and white only: a white slab behind the black face gives the stone its depth.
    private static void DrawTombstone(Graphics g, Rectangle r, int age)
    {
        const int depth = 6;
        var face = new Rectangle(r.X, r.Y, r.Width - depth, r.Height - depth);

        using var side = FacePath(new Rectangle(face.X + depth, face.Y + depth, face.Width, face.Height));
        g.FillPath(Brushes.White, side);
        g.FillRectangle(Brushes.White, face.X, face.Bottom - 1, face.Width + depth, depth + 1);

        using var path = FacePath(face);
        g.FillPath(Brushes.Black, path);
        using var edge = new Pen(Color.White, 2);
        g.DrawPath(edge, path);

        using var inner = FacePath(Rectangle.Inflate(face, -4, -4));
        using var thin = new Pen(Color.White, 1);
        g.DrawPath(thin, inner);

        using var small = new Font("Consolas", 8f, FontStyle.Bold);
        using var big = new Font("Consolas", 13f, FontStyle.Bold);
        var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString("RIP", small, Brushes.White, new RectangleF(face.X, face.Y + 8, face.Width, 14), centered);

        var band = new Rectangle(face.X + 9, face.Y + 24, face.Width - 18, 26);
        g.DrawLine(Pens.White, face.X + 4, face.Y + 22, face.Right - 4, face.Y + 22);
        g.DrawString("DIED", small, Brushes.White, new RectangleF(band.X, band.Y, band.Width, 13), centered);
        g.DrawString("AGED", small, Brushes.White, new RectangleF(band.X, band.Y + 13, band.Width, 13), centered);

        g.DrawString(age.ToString(), big, Brushes.White, new RectangleF(face.X, face.Y + 52, face.Width, 24), centered);
    }

    private static GraphicsPath FacePath(Rectangle r)
    {
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, r.Width, r.Width, 180, 180);
        path.AddLine(r.Right, r.Y + r.Width / 2, r.Right, r.Bottom);
        path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        path.CloseFigure();
        return path;
    }
}
