using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Dungeon;
using Adnd.Core.Monsters;
using Adnd.Data.Characters;
using Adnd.Data.Dungeon;
using Adnd.Data.Encounters;
using Adnd.Data.Monsters;
using Adnd.Data.Party;
using Adnd.Game.Combat;
using Adnd.Game.Viewer;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using static Adnd.Core.Dices.DiceFormulas;

namespace Adnd.Game;

// Dialogs answerable from both the keyboard and the viewer table, and the elevator.
public sealed partial class MazeForm
{
    /// <summary>
    /// Where a dialog sends its question so the table can answer it. Handed to the camp screens, which have no
    /// business knowing about snapshots or bridges -- they just need somewhere to put a question.
    /// </summary>
    private void PublishTable(ViewerPrompt? prompt) => PublishToViewer(prompt: prompt);

    /// <summary>
    /// Says something the player must acknowledge, on the keyboard AND on the table, then puts the maze's own
    /// question back up. These were plain MessageBoxes, which a viewer can neither read nor dismiss.
    /// </summary>
    private void SayOnBoth(string title, string text)
    {
        ViewerMessage.Say(this, title, text, PublishTable);
        PublishToViewer();
    }

    /// <summary>
    /// Asks a yes/no question on the keyboard AND on the table.
    ///
    /// Built rather than borrowed from MessageBox because a MessageBox cannot be answered from the viewer at
    /// all: "Stairs up, take them?" is the last thing between a party and the surface, and from the table it
    /// was an unanswerable stop right at the exit.
    /// </summary>
    private DialogResult AskOnBoth(string title, string question)
    {
        var questionText = string.IsNullOrWhiteSpace(question) ? "(Y/N)?" : question.ToUpperInvariant();
        var questionFont = new Font("Consolas", 18f, FontStyle.Bold);
        var questionSize = TextRenderer.MeasureText(questionText, questionFont, new Size(800, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        var questionHeight = Math.Max(80, questionSize.Height + 12);

        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(820, 110 + questionHeight),
        };

        var framePanel = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = string.IsNullOrWhiteSpace(title) ? "QUESTION" : title.ToUpperInvariant(),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var questionLabel = new Label
        {
            Left = 0,
            Top = 52,
            Width = framePanel.ClientSize.Width,
            Height = questionHeight,
            Text = questionText,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = questionFont
        };

        var optionsLabel = new Label
        {
            Left = 0,
            Top = 52 + questionHeight,
            Width = framePanel.ClientSize.Width,
            Height = 34,
            Text = "Y)ES    N)O",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = questionLabel.Font
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Y)
            {
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.N || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(questionLabel);
        framePanel.Controls.Add(optionsLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", question, null, new[]
        {
            new ViewerPromptOption("yes", "Yes"),
            new ViewerPromptOption("no", "No"),
        });

        var answers = new Dictionary<string, DialogResult>
        {
            ["yes"] = DialogResult.Yes,
            ["no"] = DialogResult.No,
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    /// <summary>
    /// A wide one-line question in the classic look (see the "WILL YOU SEARCH (Y/N) ?" picture): a black bar with a
    /// thick white rounded frame and the question centered in it.
    /// </summary>
    private DialogResult AskOneLineOnBoth(string title, string line, string tableQuestion)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = Color.White,
            KeyPreview = true,
            ClientSize = new Size(760, 64),
        };

        using var font = new Font("Consolas", 24f, FontStyle.Bold);
        form.Paint += (_, e) =>
        {
            OldStyleFrame.Draw(e.Graphics, form.ClientSize, Color.White);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var brush = new SolidBrush(Color.White);
            e.Graphics.DrawString(line, font, brush, new RectangleF(0, 0, form.ClientSize.Width, form.ClientSize.Height), format);
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Y)
            {
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.N || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        var prompt = new ViewerPrompt("choice", tableQuestion, null, new[]
        {
            new ViewerPromptOption("yes", "Yes"),
            new ViewerPromptOption("no", "No"),
        });

        var answers = new Dictionary<string, DialogResult>
        {
            ["yes"] = DialogResult.Yes,
            ["no"] = DialogResult.No,
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    /// <summary>
    /// The stairs question in the classic look (see Assets\ScenPictures\StairsUp.png): a compact black box with a
    /// white double border and two centered lines, "STAIRS UP" and "TAKE THEM (Y/N) ?".
    /// </summary>
    private DialogResult AskStairsOnBoth(bool up)
    {
        var line1 = up ? "STAIRS UP" : "STAIRS DOWN";
        const string line2 = "TAKE THEM (Y/N) ?";

        using var form = new Form
        {
            Text = "Stairs",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = Color.White,
            KeyPreview = true,
            ClientSize = new Size(640, 130),
        };

        using var font = new Font("Consolas", 28f, FontStyle.Bold);
        form.Paint += (_, e) =>
        {
            var g = e.Graphics;
            OldStyleFrame.Draw(g, form.ClientSize, Color.White);

            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var half = form.ClientSize.Height / 2f;
            using var brush = new SolidBrush(Color.White);
            g.DrawString(line1, font, brush, new RectangleF(0, 12, form.ClientSize.Width, half - 12), format);
            g.DrawString(line2, font, brush, new RectangleF(0, half - 4, form.ClientSize.Width, half - 8), format);
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Y)
            {
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.N || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        var prompt = new ViewerPrompt("choice", up ? "Stairs up, take them?" : "Stairs down, take them?", null, new[]
        {
            new ViewerPromptOption("yes", "Yes"),
            new ViewerPromptOption("no", "No"),
        });

        var answers = new Dictionary<string, DialogResult>
        {
            ["yes"] = DialogResult.Yes,
            ["no"] = DialogResult.No,
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    private DialogResult AskNeutralEncounterOnBoth(string encounterName, bool ignoreAlignment = false)
    {
        using var form = new Form
        {
            Text = ignoreAlignment ? "Encounter" : "Neutral Encounter",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(780, 160),
        };

        var framePanel = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = ignoreAlignment ? "ENCOUNTER" : "NEUTRAL ENCOUNTER",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 12,
            Top = 50,
            Width = framePanel.ClientSize.Width - 24,
            Height = 62,
            Text = $"A group of {(ignoreAlignment ? string.Empty : "neutral ")}{encounterName} is approaching.{Environment.NewLine}Do you L)eave them alone or A)ttack?",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", $"{(ignoreAlignment ? string.Empty : "Neutral ")}{encounterName} approach. Leave alone or attack?", null, new[]
        {
            new ViewerPromptOption("leave", "Leave alone"),
            new ViewerPromptOption("attack", "Attack")
        });

        var answers = new Dictionary<string, DialogResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["leave"] = DialogResult.No,
            ["attack"] = DialogResult.Yes
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    private DialogResult AskFriendlyEncounterOnBoth(string encounterName)
    {
        using var form = new Form
        {
            Text = "Friendly Encounter",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(780, 160),
        };

        var framePanel = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = "FRIENDLY ENCOUNTER",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 12,
            Top = 50,
            Width = framePanel.ClientSize.Width - 24,
            Height = 62,
            Text = $"A group of friendly {encounterName} is approaching.{Environment.NewLine}Do you L)eave them alone or A)ttack?",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", $"Friendly {encounterName} approach. Leave alone or attack?", null, new[]
        {
            new ViewerPromptOption("leave", "Leave alone"),
            new ViewerPromptOption("attack", "Attack")
        });

        var answers = new Dictionary<string, DialogResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["leave"] = DialogResult.No,
            ["attack"] = DialogResult.Yes
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    private void ShowElevatorDialog()
    {
        var selected = PromptElevatorLevelOnBoth();
        if (!selected.HasValue)
            return;

        var targetLevel = selected.Value;
        if (targetLevel == _currentDungeonLevel)
            return;

        _currentDungeonLevel = targetLevel;
        BuildMazeForLevel(_currentDungeonLevel);
        LoadEncounterLocationsForLevel(_currentDungeonLevel);
        _position = ElevatorPosition;
        _direction = Direction.North;
        UpdateLighting();
        // Publish here rather than waiting for the next keypress, so the table swaps levels as the
        // elevator doors close. The viewer clears the old level when it sees the new number.
        PublishToViewer();
    }

    private static string ElevatorLabel(int level, bool letters) =>
        letters ? ((char)('A' + level - MinDungeonLevel)).ToString() : level.ToString();

    private static bool TryParseElevatorInput(string text, bool letters, out int level)
    {
        text = text.Trim();
        level = 0;
        if (letters && text.Length == 1 && char.IsLetter(text[0]))
            level = char.ToUpperInvariant(text[0]) - 'A' + MinDungeonLevel;
        else if (!letters && !int.TryParse(text, out level))
            return false;

        return level >= MinDungeonLevel && level <= MaxDungeonLevel;
    }

    private int? PromptElevatorLevelOnBoth()
    {
        var wizardryLetters = IsPlayingWizardry;
        using var form = new Form
        {
            Text = "Elevator",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(520, 620)
        };

        var framePanel = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = "ELEVATOR",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var questionLabel = new Label
        {
            Left = 0,
            Top = 52,
            Width = framePanel.ClientSize.Width,
            Height = 48,
            Text = wizardryLetters ? $"THERE ARE BUTTONS HERE MARKED {ElevatorLabel(MinDungeonLevel, true)} THROUGH {ElevatorLabel(MaxDungeonLevel, true)}.{Environment.NewLine}PRESS ONE (↵ TO LEAVE)" : $"CURRENT LEVEL {_currentDungeonLevel}  ↵  CHOOSE ({MinDungeonLevel}-{MaxDungeonLevel})",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 13f, FontStyle.Bold)
        };

        var floorsLabel = new Label
        {
            Left = 0,
            Top = 104,
            Width = framePanel.ClientSize.Width,
            Height = 364,
            Text = string.Join(Environment.NewLine, Enumerable.Range(MinDungeonLevel, MaxDungeonLevel - MinDungeonLevel + 1).Select(n => wizardryLetters ? $"{ElevatorLabel(n, true)}. Level {n}" : $"{n}. Level {n}")),
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 13f, FontStyle.Bold)
        };

        var input = new TextBox
        {
            Left = (framePanel.ClientSize.Width - 90) / 2,
            Top = 486,
            Width = 90,
            MaxLength = 2,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Consolas", 16f, FontStyle.Bold)
        };

        var hintLabel = new Label
        {
            Left = 0,
            Top = 528,
            Width = framePanel.ClientSize.Width,
            Height = 24,
            Text = "ENTER=CONFIRM   ESC=CANCEL",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 10f, FontStyle.Bold)
        };

        int? selected = null;

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.Cancel;
                form.Close();
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (TryParseElevatorInput(input.Text, wizardryLetters, out var parsed))
                {
                    selected = parsed;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }

                return;
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(questionLabel);
        framePanel.Controls.Add(floorsLabel);
        framePanel.Controls.Add(input);
        framePanel.Controls.Add(hintLabel);
        form.Controls.Add(framePanel);

        var labels = Enumerable.Range(MinDungeonLevel, MaxDungeonLevel - MinDungeonLevel + 1)
            .Select(n => $"Level {n}")
            .ToList();

        var outcome = ViewerDialog.RunPick(form, this, "Elevator", null, labels, PublishTable);
        PublishToViewer();

        if (outcome.Picked.HasValue)
            return outcome.Picked.Value + 1;

        if (outcome.Result == DialogResult.OK)
        {
            if (selected.HasValue)
                return selected.Value;

            // Viewer choice path defaults to the first option if no keyboard value was entered.
            if (TryParseElevatorInput(input.Text, wizardryLetters, out var parsed))
                return parsed;
        }

        return null;
    }
}
