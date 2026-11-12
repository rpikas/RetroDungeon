using System.Drawing;
using System.Windows.Forms;

namespace Adnd.Game;

/// <summary>Makes the Y and the N of a "(Y/N)" text answer to a mouse click, like pressing the key.</summary>
internal static class YesNoClick
{
    public static void Attach(Control surface, string text, Font font, Func<RectangleF> layout, StringAlignment horizontal, Action yes, Action no)
    {
        var at = text.IndexOf("Y/N", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return;

        surface.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            using var graphics = surface.CreateGraphics();
            using var format = new StringFormat { Alignment = horizontal, LineAlignment = StringAlignment.Center };
            format.SetMeasurableCharacterRanges(new[] { new CharacterRange(at, 1), new CharacterRange(at + 2, 1) });
            var regions = graphics.MeasureCharacterRanges(text, font, layout(), format);

            var answers = new[] { yes, no };
            for (var i = 0; i < regions.Length; i++)
            {
                var bounds = regions[i].GetBounds(graphics);
                bounds.Inflate(12f, 12f);
                regions[i].Dispose();
                if (bounds.Contains(e.Location))
                {
                    answers[i]();
                    return;
                }
            }
        };
    }
}
