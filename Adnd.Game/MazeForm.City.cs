using Adnd.Core.Config;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Adnd.Game;

// The RetroDungeon window in the city: a castle picture on top, the text menus below.
public sealed partial class MazeForm
{
    private void InitCityHost()
    {
        TermConsole.Attach(this);
        KeyDown += (_, e) => TermConsole.FeedKeyDown(e);
        KeyPress += (_, e) =>
        {
            TermConsole.FeedKeyPress(e.KeyChar);
            e.Handled = true;
        };
        MouseWheel += (_, e) =>
        {
            TermConsole.Scrolled = Math.Max(0, TermConsole.Scrolled + (e.Delta > 0 ? 3 : -3));
            Invalidate();
        };
        MouseDown += (_, e) =>
        {
            if (!TermConsole.RulesetPictures || e.Button != MouseButtons.Left)
                return;

            if (_adndPictureRect.Contains(e.Location))
                TermConsole.EnqueueLine("D");
            else if (_wizardryPictureRect.Contains(e.Location))
                TermConsole.EnqueueLine("W");
        };
    }

    private Rectangle _adndPictureRect;
    private Rectangle _wizardryPictureRect;

    private static void DrawChoicePicture(Graphics graphics, Image image, Rectangle slot, bool selected, Color color, out Rectangle drawn)
    {
        var scale = Math.Min(slot.Width / (float)image.Width, slot.Height / (float)image.Height);
        drawn = new Rectangle(slot.X, slot.Y, (int)(image.Width * scale), (int)(image.Height * scale));
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (selected)
        {
            graphics.DrawImage(image, drawn);
            using var pen = new Pen(color, 4);
            graphics.DrawRectangle(pen, drawn);
            return;
        }

        // Unselected: grayscale and faded.
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { 0.3f, 0.3f, 0.3f, 0f, 0f },
            new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
            new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0.35f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(matrix);
        graphics.DrawImage(image, drawn, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    private static readonly Dictionary<string, Image?> CityImages = new(StringComparer.OrdinalIgnoreCase);

    private static Image? CityImage(string file)
    {
        if (CityImages.TryGetValue(file, out var cached))
            return cached;

        Image? image = null;
        var candidates = new[]
        {
            Path.Combine("Assets", "ScenPictures", file),
            Path.Combine(AppContext.BaseDirectory, "Assets", "ScenPictures", file),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "ScenPictures", file),
        };

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                using var stream = File.OpenRead(path);
                image = new Bitmap(stream);
                break;
            }
            catch
            {
                // Try the next location.
            }
        }

        CityImages[file] = image;
        return image;
    }

    private void DrawCity(Graphics graphics)
    {
        var color = GameRulesProvider.Current.DefaultColor;
        using var brush = new SolidBrush(color);
        graphics.DrawString("Castle", HudFont, brush, new PointF(16f, 6f));

        var area = new Rectangle(12, 36, ClientSize.Width - 24, ClientSize.Height - 36 - 12);
        var text = area;
        var columns = TermConsole.Columns;

        if (TermConsole.RulesetPictures
            && CityImage("AdndPlayersHandbookCover.png") is { } adnd
            && CityImage("WizardryStartUpSplashScreen.png") is { } wizardry)
        {
            var width = (int)(area.Width * 0.4f);
            var half = (area.Height - EncounterForm.FrameMargin) / 2;
            var isWizardry = GameRulesProvider.Current.Rules == Ruleset.Wizardry;
            DrawChoicePicture(graphics, adnd, new Rectangle(area.X, area.Y, width, half), !isWizardry, color, out _adndPictureRect);
            DrawChoicePicture(graphics, wizardry, new Rectangle(area.X, area.Y + half + EncounterForm.FrameMargin, width, half), isWizardry, color, out _wizardryPictureRect);

            var left = Math.Max(_adndPictureRect.Right, _wizardryPictureRect.Right) + EncounterForm.FrameMargin;
            text = new Rectangle(left, area.Y, area.Right - left, area.Height);
            columns = 48;
        }
        else if (TermConsole.CityPicture is { } pictureFile && CityImage(pictureFile) is { } town)
        {
            // Shop and temple pictures are drawn a bit smaller than the town picture.
            var share = pictureFile is "WizardryBoltax.png" or "WizardryTemple.png" or "AdndTemple.png" ? 0.48f : 0.6f;
            var side = Math.Min(area.Height, (int)(area.Width * share));
            var scale = Math.Min(side / (float)town.Width, side / (float)town.Height);
            var picture = new Rectangle(area.X, area.Y, (int)(town.Width * scale), (int)(town.Height * scale));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(town, picture);

            text = new Rectangle(picture.Right + EncounterForm.FrameMargin, area.Y, area.Right - picture.Right - EncounterForm.FrameMargin, area.Height);
            columns = 48;
        }

        OldStyleFrame.Draw(graphics, text, color);
        DrawTerminal(graphics, brush, text, columns);
    }

    private void DrawTerminal(Graphics graphics, Brush brush, Rectangle bounds, int columns)
    {
        var inner = Rectangle.Inflate(bounds, -(OldStyleFrame.Thickness + 8), -(OldStyleFrame.Thickness + 4));
        if (inner.Width < 100 || inner.Height < 40)
            return;

        // Consolas is 0.55 em wide: size the font so a full line of columns fits across the area.
        var fontPixels = Math.Clamp(inner.Width / (columns * 0.55f), 10f, 20f);
        using var font = new Font("Consolas", fontPixels, FontStyle.Bold, GraphicsUnit.Pixel);
        var lineHeight = (float)Math.Ceiling(fontPixels * 1.25f);
        var rows = Math.Max(1, (int)(inner.Height / lineHeight));

        var lines = TermConsole.Snapshot();
        var maxScroll = Math.Max(0, lines.Count - rows);
        TermConsole.Scrolled = Math.Min(TermConsole.Scrolled, maxScroll);
        var first = Math.Max(0, lines.Count - rows - TermConsole.Scrolled);

        var state = graphics.Save();
        graphics.SetClip(inner, CombineMode.Intersect);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        for (var i = 0; i < rows && first + i < lines.Count; i++)
            graphics.DrawString(lines[first + i], font, brush, inner.X, inner.Y + (i * lineHeight), StringFormat.GenericTypographic);
        graphics.Restore(state);
    }
}
