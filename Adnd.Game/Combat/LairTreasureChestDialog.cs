using Adnd.Game.Viewer;
using Adnd.Core.Config;
using System.Drawing;
using System.Windows.Forms;

namespace Adnd.Game.Combat;

internal enum LairChestChoice
{
    Open,
    CastFindTraps,
    LeaveAlone,
    Inspect
}

internal sealed class LairTreasureChestDialog : Form
{
    private static readonly IReadOnlyDictionary<string, Keys> Vocabulary = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = Keys.O,
        ["cast"] = Keys.C,
        ["leave"] = Keys.L,
        ["inspect"] = Keys.I,
    };

    private readonly Action<ViewerPrompt?>? _publish;
    private ViewerControlPump? _pump;

    public LairChestChoice Choice { get; private set; } = LairChestChoice.LeaveAlone;

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

        var frame = new Panel
        {
            Left = 4,
            Top = 4,
            Width = ClientSize.Width - 8,
            Height = ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
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

        var path = ResolveChestImagePath();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            using var src = Image.FromFile(path);
            picture.Image = new Bitmap(src);
        }

        var options = new Label
        {
            Left = 0,
            Top = 260,
            Width = frame.Width,
            Height = 140,
            Text = "O)PEN   C)AST SPELL FIND TRAPS\nL)EAVE ALONE   I)NSPECT",
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 18f, FontStyle.Bold)
        };

        frame.Controls.Add(title);
        frame.Controls.Add(picture);
        frame.Controls.Add(options);
        Controls.Add(frame);

        KeyDown += (_, e) => HandleChoiceKey(e.KeyCode);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _pump = ViewerControlPump.Start(this, Vocabulary, HandleChoiceKey);

        _publish?.Invoke(new ViewerPrompt(
            "choice",
            "Treasure chest: O)pen, C)ast Find Traps, L)eave alone, I)nspect",
            null,
            new[]
            {
                new ViewerPromptOption("open", "Open"),
                new ViewerPromptOption("cast", "Cast Find Traps"),
                new ViewerPromptOption("leave", "Leave alone"),
                new ViewerPromptOption("inspect", "Inspect")
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
        else if (key == Keys.L || key == Keys.Escape)
            CloseWith(LairChestChoice.LeaveAlone);
        else if (key == Keys.I)
            CloseWith(LairChestChoice.Inspect);
    }

    private static string? ResolveChestImagePath()
    {
        var candidates = new[]
        {
            Path.Combine("Assets", "ScenPictures", "TreasureChest.png"),
            Path.Combine("..", "..", "..", "Assets", "ScenPictures", "TreasureChest.png"),
            Path.Combine("..", "..", "..", "..", "Adnd.Game", "Assets", "ScenPictures", "TreasureChest.png")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
