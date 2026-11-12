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
        MouseClick += (_, e) => HandleCityClick(e);
    }

    private readonly List<(Rectangle Bounds, string Line)> _sideLineRects = new();
    private float _terminalCharWidth = 7f;

    private void AddSideLineRects(string[] lines, float x, float top, float lineHeight, float fontPixels)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var width = (int)(lines[i].Length * fontPixels * 0.55f) + 4;
            _sideLineRects.Add((new Rectangle((int)x, (int)(top + (i * lineHeight)), width, (int)lineHeight), lines[i]));
        }
    }

    // The key a menu line answers to: "T)raining", "A)  Stables", "B. Item", "[C] Item", "L<-eave".
    private static string? MenuKeyOf(string line)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line,         @"^\s*(?:\[([A-Za-z0-9])\]|([A-Za-z0-9])\)|(\d)\.\s|([A-Za-z])<-|(\d)\s{2,}\S)");
        if (!match.Success)
            return null;

                for (var g = 1; g <= 5; g++)
        {
            if (match.Groups[g].Success)
                return match.Groups[g].Value;
        }

        return null;
    }

    private void HandleCityClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || TermConsole.RulesetPictures)
            return;

        if (TermConsole.WaitingForKey && TermConsole.PressAnyKeyShown())
        {
            TermConsole.EnqueueText("\r");
            return;
        }

        foreach (var (bounds, line) in _sideLineRects)
        {
            if (!bounds.Contains(e.Location))
                continue;

            if (TermConsole.WaitingForKey && MenuKeyOf(line) is { } sideKey)
                TermConsole.EnqueueText(sideKey);
            return;
        }

        if (!_terminalInner.Contains(e.Location))
            return;

        var lines = TermConsole.Snapshot();
        var index = _terminalFirstLine + (int)((e.Y - _terminalInner.Y) / _terminalLineHeight);
        if (index < 0 || index >= lines.Count)
            return;

        var text = lines[index];

        if (TermConsole.WaitingForKey)
        {
            var yn = text.IndexOf("Y/N", StringComparison.OrdinalIgnoreCase);
            if (yn >= 0)
            {
                var column = (int)((e.X - _terminalInner.X) / _terminalCharWidth);
                if (column >= yn - 1 && column <= yn + 1)
                    TermConsole.EnqueueText("Y");
                else if (column >= yn + 2 && column <= yn + 3)
                    TermConsole.EnqueueText("N");
                return;
            }
        }

        if (TermConsole.ClickableLetters)
        {
            var letter = System.Text.RegularExpressions.Regex.Match(text, @"^([A-Z])\.\s");
            if (letter.Success)
                TermConsole.EnqueueText(letter.Groups[1].Value);
            return;
        }

        if (TermConsole.ClickableNumberDigits > 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)[.)]?\s");
            if (!match.Success)
                return;

            var digits = match.Groups[1].Value;
            if (digits.Length > TermConsole.ClickableNumberDigits)
                return;

            // A reader that submits by itself after N characters must not be left an Enter to read later.
            TermConsole.EnqueueText(digits.Length < TermConsole.ClickableNumberDigits ? digits + "\r" : digits);
            return;
        }

        if (TermConsole.WaitingForKey && MenuKeyOf(text) is { } key)
            TermConsole.EnqueueText(key);
    }

    private Rectangle _terminalInner;
    private float _terminalLineHeight = 1f;
    private int _terminalFirstLine;

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

    private float? _pictureScreenFont;

    private void DrawCity(Graphics graphics)
    {
        var color = GameRulesProvider.Current.DefaultColor;
        _sideLineRects.Clear();
        using var brush = new SolidBrush(color);
        graphics.DrawString("Castle", HudFont, brush, new PointF(16f, 6f));

        var area = new Rectangle(12, 36, ClientSize.Width - 24, ClientSize.Height - 36 - 12);
        _pictureScreenFont = null;
        // One font for every city screen: the size the main menu gets beside its picture.
        var cityFont = Math.Clamp((area.Width * 0.52f - EncounterForm.FrameMargin - (2 * OldStyleFrame.Thickness + 16)) / (48 * 0.55f), 10f, 20f);
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
        else if (TermConsole.SideLines is { Length: > 0 } menuLines
                 && (TermConsole.CityPicture is not { } shownFile || CityImage(shownFile) is null))
        {
            // No picture: the options still go in the left column, so the shop reads the same either way.
            var share = 0.48f;
            var fontPx = cityFont;
            _pictureScreenFont = fontPx;
            var lh = (float)Math.Ceiling(fontPx * 1.25f);
            var left = (int)(area.Width * share);
            using (var menuFont = new Font("Consolas", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                AddSideLineRects(menuLines, area.X, area.Y, lh, fontPx);
                for (var i = 0; i < menuLines.Length; i++)
                    graphics.DrawString(menuLines[i], menuFont, brush, area.X, area.Y + (i * lh), StringFormat.GenericTypographic);
            }

            text = new Rectangle(area.X + left + EncounterForm.FrameMargin, area.Y, area.Width - left - EncounterForm.FrameMargin, area.Height);
            columns = 48;
        }
        else if (TermConsole.SideLines is { Length: > 0 } feeMenu
                 && TermConsole.CityPicture is "WizardryTemple.png" or "AdndTemple.png"
                 && CityImage(TermConsole.CityPicture!) is { } templeImage)
        {
            // Temple: the fees run across the top, the options sit under them on the left, the picture on the right.
            var fontPx = cityFont;
            _pictureScreenFont = fontPx;
            var lh = (float)Math.Ceiling(fontPx * 1.25f);
            var rowsNeeded = TermConsole.Snapshot().Count + 1;
            text = new Rectangle(area.X, area.Y, area.Width, Math.Min(area.Height / 2, (int)(rowsNeeded * lh) + (2 * (OldStyleFrame.Thickness + 4)) + 4));
            columns = 48;

            var top = text.Bottom + EncounterForm.FrameMargin;
            var menuWidth = (int)(feeMenu.Max(l => l.Length) * fontPx * 0.55f) + EncounterForm.FrameMargin;
            using (var menuFont = new Font("Consolas", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                AddSideLineRects(feeMenu, area.X, top, lh, fontPx);
                for (var i = 0; i < feeMenu.Length; i++)
                    graphics.DrawString(feeMenu[i], menuFont, brush, area.X, top + (i * lh), StringFormat.GenericTypographic);
            }

            var availableWidth = area.Width - menuWidth;
            var availableHeight = area.Bottom - top;
            if (availableWidth > 40 && availableHeight > 40)
            {
                var fit = Math.Min(availableWidth / (float)templeImage.Width, availableHeight / (float)templeImage.Height);
                var w = (int)(templeImage.Width * fit);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(templeImage, new Rectangle(area.Right - w, top, w, (int)(templeImage.Height * fit)));
            }
        }
        else if (TermConsole.SideLines is { Length: > 0 } groundsMenu
                 && TermConsole.CityPicture is "WizardryTraningGrounds.png" or "AdndTraningGrounds.png"
                 && CityImage(TermConsole.CityPicture!) is { } groundsImage)
        {
            // Training grounds: the character list keeps the full-width text of the no-picture screen; the options
            // sit bottom left and the picture bottom right.
            var fontPx = cityFont;
            _pictureScreenFont = fontPx;
            var lh = (float)Math.Ceiling(fontPx * 1.25f);
            var bottomMin = Math.Max((int)(groundsMenu.Length * lh) + EncounterForm.FrameMargin, (int)(area.Height * 0.3f));
            var rowsNeeded = TermConsole.Snapshot().Count + 1;
            var listHeight = (int)(rowsNeeded * lh) + (2 * (OldStyleFrame.Thickness + 4)) + 4;
            text = new Rectangle(area.X, area.Y, area.Width, Math.Max(60, Math.Min(listHeight, area.Height - bottomMin)));
            columns = TermConsole.Columns;

            var top = text.Bottom + EncounterForm.FrameMargin;
            var menuWidth = (int)(groundsMenu.Max(l => l.Length) * fontPx * 0.55f) + EncounterForm.FrameMargin;
            using (var menuFont = new Font("Consolas", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                AddSideLineRects(groundsMenu, area.X, top, lh, fontPx);
                for (var i = 0; i < groundsMenu.Length; i++)
                    graphics.DrawString(groundsMenu[i], menuFont, brush, area.X, top + (i * lh), StringFormat.GenericTypographic);
            }

            var availableWidth = area.Width - menuWidth;
            var availableHeight = area.Bottom - top;
            if (availableWidth > 40 && availableHeight > 40)
            {
                var fit = Math.Min(availableWidth / (float)groundsImage.Width, availableHeight / (float)groundsImage.Height);
                var w = (int)(groundsImage.Width * fit);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(groundsImage, new Rectangle(area.Right - w, top, w, (int)(groundsImage.Height * fit)));
            }
        }
        else if (TermConsole.CityPicture is "WizardryGilgameshTavern.png" or "AdndGilgameshTavern.png" or "AdndAdventuresInn.png" or "WizardryAdventuresInn.png"
                 && CityImage(TermConsole.CityPicture!) is { } bottomImage)
        {
            // Tavern and inn: the text uses the top of the window and the picture sits along the bottom.
            _pictureScreenFont = cityFont;
            columns = 48;
            var pictureHeight = Math.Min((int)(area.Height * 0.35f), (int)(area.Width * bottomImage.Height / (float)bottomImage.Width));
            text = new Rectangle(area.X, area.Y, area.Width, area.Height - pictureHeight - EncounterForm.FrameMargin);
            var fit = pictureHeight / (float)bottomImage.Height;
            var w = (int)(bottomImage.Width * fit);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bottomImage, new Rectangle(area.X + ((area.Width - w) / 2), area.Bottom - pictureHeight, w, pictureHeight));
        }
        else if (TermConsole.CityPicture == "AdndTown.png" && CityImage("AdndTown.png") is { } adndTown)
        {
            // AD&D's wide town picture goes under the options instead of beside them.
            var fontPx = cityFont;
            _pictureScreenFont = fontPx;
            var rowsNeeded = TermConsole.Snapshot().Count + 1;
            var textHeight = Math.Min(area.Height / 2, (int)(rowsNeeded * Math.Ceiling(fontPx * 1.25f)) + (2 * (OldStyleFrame.Thickness + 4)) + 4);
            text = new Rectangle(area.X, area.Y, area.Width, textHeight);

            var top = text.Bottom + EncounterForm.FrameMargin;
            var available = area.Bottom - top;
            if (available > 40)
            {
                var fit = Math.Min(area.Width / (float)adndTown.Width, available / (float)adndTown.Height);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(adndTown, new Rectangle(area.X, top, (int)(adndTown.Width * fit), (int)(adndTown.Height * fit)));
            }

            columns = 48;
        }
        else if (TermConsole.CityPicture is { } pictureFile && CityImage(pictureFile) is { } town)
        {
            // Shop and temple pictures are drawn a bit smaller than the town picture.
            var share = pictureFile is "WizardryBoltax.png" or "WizardryTemple.png" or "AdndTemple.png" or "AdndShop.png" or "WizardryTown.png" or "AdndTown.png" ? 0.48f : pictureFile is "WizardrySelectingClass1.png" or "WizardryTraningGrounds.png" or "AdndTraningGrounds.png" ? 0.3f : 0.6f;
            var side = Math.Min(area.Height, (int)(area.Width * share));
            var sideLines = TermConsole.SideLines;
            // One font for every picture screen, sized from the text area the picture's share leaves, so the
            // town menu, shop and temple read alike whatever the picture ends up measuring.
            var sideFontPixels = cityFont;
            _pictureScreenFont = sideFontPixels;
            var sideLineHeight = (float)Math.Ceiling(sideFontPixels * 1.25f);
            if (sideLines is { Length: > 0 })
                side = Math.Max(120, Math.Min(side, area.Height - (int)(sideLines.Length * sideLineHeight) - EncounterForm.FrameMargin));

            var scale = Math.Min(side / (float)town.Width, side / (float)town.Height);
            var picture = new Rectangle(area.X, area.Y, (int)(town.Width * scale), (int)(town.Height * scale));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(town, picture);

            if (sideLines is { Length: > 0 })
            {
                using var sideFont = new Font("Consolas", sideFontPixels, FontStyle.Bold, GraphicsUnit.Pixel);
                var top = picture.Bottom + EncounterForm.FrameMargin;
                AddSideLineRects(sideLines, area.X, top, sideLineHeight, sideFontPixels);
                for (var i = 0; i < sideLines.Length; i++)
                    graphics.DrawString(sideLines[i], sideFont, brush, area.X, top + (i * sideLineHeight), StringFormat.GenericTypographic);
            }

            text = new Rectangle(picture.Right + EncounterForm.FrameMargin, area.Y, area.Right - picture.Right - EncounterForm.FrameMargin, area.Height);
            columns = 48;
        }

        _pictureScreenFont = cityFont;

        OldStyleFrame.Draw(graphics, text, color);
        DrawTerminal(graphics, brush, text, columns);
    }

    private void DrawTerminal(Graphics graphics, Brush brush, Rectangle bounds, int columns)
    {
        var inner = Rectangle.Inflate(bounds, -(OldStyleFrame.Thickness + 8), -(OldStyleFrame.Thickness + 4));
        if (inner.Width < 100 || inner.Height < 40)
            return;

        // Consolas is 0.55 em wide: size the font so a full line of columns fits across the area.
        var fontPixels = _pictureScreenFont ?? Math.Clamp(inner.Width / (columns * 0.55f), 10f, 20f);
        using var font = new Font("Consolas", fontPixels, FontStyle.Bold, GraphicsUnit.Pixel);
        var lineHeight = (float)Math.Ceiling(fontPixels * 1.25f);
        var rows = Math.Max(1, (int)(inner.Height / lineHeight));
        TermConsole.VisibleRows = rows;

        var lines = TermConsole.Snapshot();
        var maxScroll = Math.Max(0, lines.Count - rows);
        TermConsole.Scrolled = Math.Min(TermConsole.Scrolled, maxScroll);
        var first = Math.Max(0, lines.Count - rows - TermConsole.Scrolled);
        _terminalInner = inner;
        _terminalLineHeight = lineHeight;
        _terminalCharWidth = fontPixels * 0.55f;
        _terminalFirstLine = first;

        var state = graphics.Save();
        graphics.SetClip(inner, CombineMode.Intersect);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        for (var i = 0; i < rows && first + i < lines.Count; i++)
            graphics.DrawString(lines[first + i], font, brush, inner.X, inner.Y + (i * lineHeight), StringFormat.GenericTypographic);
        graphics.Restore(state);
    }
}
