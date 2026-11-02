using Adnd.Game.Viewer;
using Adnd.Core.Config;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Adnd.Game.Combat;

internal enum LairChestChoice
{
    Open,
    CastFindTraps,
    LeaveAlone,
    Inspect,
    Back
}

internal sealed class LairTreasureChestDialog : Form
{
    internal static readonly IReadOnlyDictionary<string, Keys> Vocabulary = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = Keys.O,
        ["cast"] = Keys.C,
        ["leave"] = Keys.L,
        ["inspect"] = Keys.I,
        ["back"] = Keys.B,
    };

    private readonly Action<ViewerPrompt?>? _publish;
    private ViewerControlPump? _pump;

    public LairChestChoice Choice { get; private set; } = LairChestChoice.Back;

    public LairTreasureChestDialog(Action<ViewerPrompt?>? publish)
    {
        _publish = publish;

        Text = "Treasure Chest";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        ForeColor = GameRulesProvider.Current.DefaultColor;
        KeyPreview = true;
        ClientSize = new Size(620, 450);

        var frame = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = ClientSize.Width - 8,
            Height = ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var title = new Label
        {
            Left = 0,
            Top = 10,
            Width = frame.Width,
            Height = 36,
            Text = "TREASURE CHEST",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var picture = new PictureBox
        {
            Left = 170,
            Top = 56,
            Width = 280,
            Height = 180,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };

        picture.Image = ResolveChestImage();

        var options = new Label
        {
            Left = 0,
            Top = 260,
            Width = frame.Width,
            Height = 140,
            Text = "O)PEN   C)AST SPELL FIND TRAPS\nL)EAVE   I)NSPECT   B)ACK",
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 18f, FontStyle.Bold)
        };

        if (GameRulesProvider.Current.EffectiveMonsterSourceOptions == SourceOptions.OnlyWizardry)
        {
            BuildWizardryLayout(picture.Image);
        }
        else
        {
            frame.Controls.Add(title);
            frame.Controls.Add(picture);
            frame.Controls.Add(options);
            Controls.Add(frame);
        }

        KeyDown += (_, e) => HandleChoiceKey(e.KeyCode);
    }

    // Proving Grounds look: options in a frame on top, the corridor below with a small chest where a monster would stand.
    private void BuildWizardryLayout(Image? chest)
    {
        const int margin = 8;
        const int gap = 6;
        const int width = 980;
        var frameWidth = width - 2 * margin;
        var color = GameRulesProvider.Current.DefaultColor;

        var optionsFrame = new OldStyleFramePanel
        {
            Left = margin,
            Top = margin,
            Width = frameWidth,
            Height = 128,
            BackColor = Color.Black
        };

        optionsFrame.Controls.Add(new Label
        {
            Left = 12,
            Top = 8,
            Width = frameWidth - 24,
            Height = 36,
            Text = "A CHEST!  YOU MAY:",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = color,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        });

        optionsFrame.Controls.Add(new Label
        {
            Left = 12,
            Top = 48,
            Width = frameWidth - 24,
            Height = 72,
            Text = "O)PEN   C)AST FIND TRAPS   L)EAVE ALONE" + Environment.NewLine + "I)NSPECT   B)ACK",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = color,
            Font = new Font("Consolas", 18f, FontStyle.Bold)
        });

        var stripTop = margin + optionsFrame.Height + gap;
        var strip = new Panel
        {
            Left = margin,
            Top = stripTop,
            Width = frameWidth,
            Height = 176,
            BackColor = Color.Black
        };

        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(strip, true);
        strip.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            EncounterForm.DrawCorridorFrame(g, strip.ClientRectangle);
            if (chest != null)
            {
                const int maxHeight = 64;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                var scale = Math.Min(maxHeight / (float)chest.Height, 160f / chest.Width);
                var w = (int)(chest.Width * scale);
                var h = (int)(chest.Height * scale);
                g.DrawImage(chest, new Rectangle((strip.Width - w) / 2, (strip.Height - h) / 2 + 20, w, h));
            }

            OldStyleFrame.Draw(g, strip.ClientRectangle, color);
        };

        Controls.Add(optionsFrame);
        Controls.Add(strip);
        ClientSize = new Size(width, stripTop + strip.Height + margin);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _pump = ViewerControlPump.Start(this, Vocabulary, HandleChoiceKey);

        _publish?.Invoke(new ViewerPrompt(
            "choice",
            "Treasure chest: O)pen, C)ast Find Traps, L)eave, I)nspect, B)ack",
            null,
            new[]
            {
                new ViewerPromptOption("open", "Open"),
                new ViewerPromptOption("cast", "Cast Find Traps"),
                new ViewerPromptOption("leave", "Leave"),
                new ViewerPromptOption("inspect", "Inspect"),
                new ViewerPromptOption("back", "Back")
            }));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pump?.Dispose();
        _publish?.Invoke(null);
        base.OnFormClosed(e);
    }

    private void CloseWith(LairChestChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void HandleChoiceKey(Keys key)
    {
        if (key == Keys.O)
            CloseWith(LairChestChoice.Open);
        else if (key == Keys.C)
            CloseWith(LairChestChoice.CastFindTraps);
        else if (key == Keys.L)
            CloseWith(LairChestChoice.LeaveAlone);
        else if (key == Keys.I)
            CloseWith(LairChestChoice.Inspect);
        else if (key == Keys.B || key == Keys.Escape)
            CloseWith(LairChestChoice.Back);
    }

    internal static Image? ResolveChestImage()
    {
        var wizardryOnly = GameRulesProvider.Current.EffectiveMonsterSourceOptions == SourceOptions.OnlyWizardry;

        var imageFileNames = wizardryOnly
            ? new[]
            {
                "TreasureChest.png",
                "TreasureChest.webp",
                "dividing-treasure.webp"
            }
            : new[]
            {
                "TreasureChest_Adnd.png",
                "TreasureChest_adnd.png",
                "TreasureChest_Adnd.webp",
                "TreasureChest_adnd.webp",
                "FindingTreasure.webp",
                "dividing-treasure.webp"
            };

        foreach (var imageFileName in imageFileNames)
        {
            var candidates = new[]
            {
                Path.Combine("Assets", "ScenPictures", imageFileName),
                Path.Combine("..", "..", "..", "Assets", "ScenPictures", imageFileName),
                Path.Combine("..", "..", "..", "..", "Adnd.Game", "Assets", "ScenPictures", imageFileName)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath))
                    continue;

                try
                {
                    using var src = Image.FromFile(fullPath);
                    return new Bitmap(src);
                }
                catch (Exception ex) when (ex is ExternalException || ex is OutOfMemoryException || ex is ArgumentException)
                {
                    // Try next candidate if this file exists but cannot be decoded by System.Drawing.
                }
            }
        }

        return null;
    }
}
