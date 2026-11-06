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

// Camp menu, party reordering and inspection, text and number prompts.
public sealed partial class MazeForm
{
    private void PruneUnavailablePartyMembers()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var filtered = party.Members
            .Where(name =>
                roster.TryGetValue(name, out var c)
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost)
                && !c.HasStatus(CharacterStatus.Out))
            .ToList();

        if (filtered.Count == party.Members.Count)
            return;

        party.Members = filtered;

        if (party.CurrentShopperIndex >= party.Members.Count)
            party.CurrentShopperIndex = party.Members.Count > 0 ? party.Members.Count - 1 : -1;

        _partyRepository.Save(party);
    }

    private void ShowCampDialog()
    {
        while (true)
        {
            var party = _partyRepository.Load();
            var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var activeMembers = party.Members.Where(m => roster.ContainsKey(m)).ToList();

            if (activeMembers.Count == 0)
            {
                SayOnBoth("Camp", "No party members to camp with.");
                RefreshMazeAfterCamp();
                return;
            }

            using var form = new Form();
            form.Text = "Camp";
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size(300, 280);
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;
            form.BackColor = Color.Black;
            form.ForeColor = GameRulesProvider.Current.DefaultColor;
            form.KeyPreview = true;

            // Same look as Assets\ScenPictures\Camp.png: a small title box above a large menu box.
            var titleRect = new Rectangle((form.ClientSize.Width - 170) / 2, 6, 170, 62);
            var menuRect = new Rectangle(6, 84, form.ClientSize.Width - 12, 190);
            form.Paint += (_, e) =>
            {
                OldStyleFrame.Draw(e.Graphics, titleRect, GameRulesProvider.Current.DefaultColor);
                OldStyleFrame.Draw(e.Graphics, menuRect, GameRulesProvider.Current.DefaultColor);
            };

            var titleLabel = new Label
            {
                Left = titleRect.Left + OldStyleFrame.Thickness,
                Top = titleRect.Top + OldStyleFrame.Thickness,
                Width = titleRect.Width - 2 * OldStyleFrame.Thickness,
                Height = titleRect.Height - 2 * OldStyleFrame.Thickness,
                Text = "CAMP",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 20f, FontStyle.Bold)
            };

            var menuLabel = new Label
            {
                Left = menuRect.Left + 30,
                Top = menuRect.Top + 19,
                Width = menuRect.Width - 60,
                Height = menuRect.Height - 38,
                Text = "#)INSPECT\nR)EORDER\nE)QUIP\nL↵EAVE",
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 24f, FontStyle.Bold)
            };

            int? quickInspectIndex = null;
            var startWithEquip = false;

            form.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.R)
                {
                    form.DialogResult = DialogResult.No;
                    form.Close();
                }
                else if (e.KeyCode == Keys.E)
                {
                    startWithEquip = true;
                    form.DialogResult = DialogResult.Yes;
                    form.Close();
                }

                var selectedIndex = GetCampInspectIndexFromKey(e.KeyCode);
                if (selectedIndex > 0 && selectedIndex <= activeMembers.Count)
                {
                    quickInspectIndex = selectedIndex - 1;
                    form.DialogResult = DialogResult.Yes;
                    form.Close();
                }
                else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                {
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                }
            };

            form.Controls.Add(titleLabel);
            form.Controls.Add(menuLabel);

            // The camp menu on the table as well: pressing Camp from the viewer used to open this and stop dead,
            // which made the whole of camp -- reordering, inspecting, equipping, memorising -- unreachable from
            // there. Same three answers, same results, either surface.
            var campPrompt = new ViewerPrompt("choice", "The party camps.", null, new[]
            {
                new ViewerPromptOption("reorder", "Reorder the party"),
                new ViewerPromptOption("inspect", "Inspect a member"),
                new ViewerPromptOption("leave", "Leave camp"),
            });


            var campAnswers = new Dictionary<string, DialogResult>
            {
                ["reorder"] = DialogResult.No,
                ["inspect"] = DialogResult.Yes,
                ["leave"] = DialogResult.Cancel,
            };
            var choice = ViewerDialog.RunModal(form, this, campPrompt, campAnswers, PublishTable);

            if (choice == DialogResult.Yes)
            {
                if (quickInspectIndex.HasValue
                    && quickInspectIndex.Value >= 0
                    && quickInspectIndex.Value < activeMembers.Count)
                {
                    var selectedCharacter = roster[activeMembers[quickInspectIndex.Value]];
                    OpenCampInspectDialog(selectedCharacter, activeMembers, startWithEquip);
                }
                else
                {
                    InspectPartyMemberFromCamp(activeMembers, roster, startWithEquip);
                }

                continue;
            }

            if (choice == DialogResult.No)
            {
                ShowReorderCampDialog(party, activeMembers, roster);
                continue;
            }

            RefreshMazeAfterCamp();
            return;
        }
    }

    private void RefreshMazeAfterCamp()
    {
        Invalidate();
        PublishToViewer();
    }

    private List<string>? RunOldStyleReorderDialog(List<string> activeMembers, Dictionary<string, Character> roster)
    {
        var picked = new List<int>();
        List<string>? result = null;

        using var form = new Form { Text = "Reorder Party", StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            TabStop = false,
            WordWrap = false,
            ScrollBars = ScrollBars.Vertical
        };
        form.Controls.Add(text);

        string Row(int number, Character c) => $"{number}. {c.Name,-12} {c.GetClassesDisplayText("/"),-26} HP {c.CurrentHitPoints + "/" + c.MaxHitPoints,-7} AC {c.ArmorClass,2}";

        void Refresh()
        {
            var lines = new List<string> { "TYPE MEMBER NUMBERS IN NEW ORDER", "", "OLD ORDER" };
            for (var i = 0; i < activeMembers.Count; i++)
                lines.Add((picked.Contains(i) ? "*" : " ") + Row(i + 1, roster[activeMembers[i]]));

            lines.Add("");
            lines.Add("NEW ORDER");
            if (picked.Count == 0)
                lines.Add(" (EMPTY)");
            for (var i = 0; i < picked.Count; i++)
                lines.Add(" " + Row(i + 1, roster[activeMembers[picked[i]]]));

            text.Lines = lines.ToArray();
        }

        Refresh();
        OldStyleDialog.Apply(form, text, selectLabel: "CONFIRM    1-9 PICK    BKSP UNDO");
        form.Width = Math.Min(Screen.FromControl(this).WorkingArea.Width, form.Width + 240);
        form.PerformLayout();
        var completeOrderHeight = (6 + 2 * activeMembers.Count) * text.Font.Height;
        var extraHeight = Math.Max(0, completeOrderHeight - text.ClientSize.Height);
        form.Height = Math.Min(Screen.FromControl(this).WorkingArea.Height, form.Height + extraHeight);

        form.KeyDown += (_, e) =>
        {
            var digit = e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9 ? e.KeyCode - Keys.D1
                : e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9 ? e.KeyCode - Keys.NumPad1
                : -1;
            if (digit >= 0 && digit < activeMembers.Count && !picked.Contains(digit))
            {
                picked.Add(digit);
                Refresh();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Back && picked.Count > 0)
            {
                picked.RemoveAt(picked.Count - 1);
                Refresh();
                e.Handled = e.SuppressKeyPress = true;
            }
        };

        form.FormClosing += (_, e) =>
        {
            if (form.DialogResult != DialogResult.OK)
                return;

            if (picked.Count != activeMembers.Count)
            {
                e.Cancel = true;
                form.DialogResult = DialogResult.None;
                return;
            }

            result = picked.Select(i => activeMembers[i]).ToList();
        };

        return ViewerDialog.RunBlocked(form, this, "Reordering the party", PublishTable) == DialogResult.OK ? result : null;
    }

    private void ShowReorderCampDialog(Adnd.Data.Party.Party party, List<string> activeMembers, Dictionary<string, Character> roster)
    {
        if (activeMembers.Count < 2)
        {
            SayOnBoth("Camp", "Need at least two party members to reorder.");
            return;
        }

        if (GameRulesProvider.Current.UIOldStyle)
        {
            var oldStyleResult = RunOldStyleReorderDialog(activeMembers, roster);
            PublishToViewer();
            if (oldStyleResult == null || oldStyleResult.SequenceEqual(activeMembers, StringComparer.OrdinalIgnoreCase))
                return;

            party.Members = oldStyleResult;
            _partyRepository.Save(party);
            SayOnBoth("Camp", "Party order updated.");
            Invalidate();
            return;
        }

        List<string>? reorderedResult = null;
        var picked = new List<int>();
        var remaining = Enumerable.Range(0, activeMembers.Count).ToList();

        using var form = new Form();
        form.Text = "Camp - Reorder Party";
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(980, 215);
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.KeyPreview = true;

        var instructions = new Label
        {
            Left = 12,
            Top = 10,
            Width = 956,
            Height = 24,
            Text = "Select a member and use > to add to new order, < to remove. Enter = confirm, Esc = cancel"
        };

        var oldLabel = new Label
        {
            Left = 12,
            Top = 38,
            Width = 430,
            Height = 24,
            Text = "Old order"
        };

        var newLabel = new Label
        {
            Left = 538,
            Top = 38,
            Width = 430,
            Height = 24,
            Text = "New order"
        };

        var oldList = new ListBox
        {
            Left = 12,
            Top = 62,
            Width = 430,
            Height = 110,
            Font = new Font("Consolas", 10f)
        };

        var newList = new ListBox
        {
            Left = 538,
            Top = 62,
            Width = 430,
            Height = 110,
            Font = new Font("Consolas", 10f)
        };

        var moveRightBtn = new Button
        {
            Text = ">",
            Left = 460,
            Top = 82,
            Width = 60,
            Height = 30
        };

        var moveLeftBtn = new Button
        {
            Text = "<",
            Left = 460,
            Top = 122,
            Width = 60,
            Height = 30
        };

        var confirmBtn = new Button
        {
            Text = "Confirm",
            Left = 812,
            Top = 178,
            Width = 75,
            DialogResult = DialogResult.None
        };

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Left = 893,
            Top = 178,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        string Row(Character c, int row)
        {
            var cls = c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(x => x.ToDisplayString())) : c.Class.ToDisplayString();
            var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
            return $"{row,2}. {c.Name,-12} {cls,-24} HP {hp,-7} AC {c.ArmorClass,2}";
        }

        void RefreshLists()
        {
            oldList.Items.Clear();
            newList.Items.Clear();

            for (int i = 0; i < remaining.Count; i++)
            {
                var c = roster[activeMembers[remaining[i]]];
                oldList.Items.Add(Row(c, i + 1));
            }

            for (int i = 0; i < picked.Count; i++)
            {
                var c = roster[activeMembers[picked[i]]];
                newList.Items.Add(Row(c, i + 1));
            }
        }

        void MoveRight()
        {
            if (oldList.SelectedIndex < 0 || oldList.SelectedIndex >= remaining.Count)
                return;

            var idx = remaining[oldList.SelectedIndex];
            remaining.RemoveAt(oldList.SelectedIndex);
            picked.Add(idx);
            RefreshLists();
        }

        void MoveLeft()
        {
            if (newList.SelectedIndex < 0 || newList.SelectedIndex >= picked.Count)
                return;

            var idx = picked[newList.SelectedIndex];
            picked.RemoveAt(newList.SelectedIndex);
            remaining.Add(idx);
            remaining.Sort();
            RefreshLists();
        }

        void TryConfirm()
        {
            if (picked.Count != activeMembers.Count)
            {
                ViewerMessage.Say(form, "Reorder Party", "Move all members into New order before confirming.", PublishTable);
                return;
            }

            reorderedResult = picked.Select(i => activeMembers[i]).ToList();
            form.DialogResult = DialogResult.OK;
            form.Close();
        }

        moveRightBtn.Click += (_, _) => MoveRight();
        moveLeftBtn.Click += (_, _) => MoveLeft();
        confirmBtn.Click += (_, _) => TryConfirm();

        form.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.Cancel;
                form.Close();
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                TryConfirm();
                e.Handled = true;
                return;
            }
        };

        form.Controls.Add(instructions);
        form.Controls.Add(oldLabel);
        form.Controls.Add(newLabel);
        form.Controls.Add(oldList);
        form.Controls.Add(newList);
        form.Controls.Add(moveRightBtn);
        form.Controls.Add(moveLeftBtn);
        form.Controls.Add(confirmBtn);
        form.Controls.Add(cancelBtn);
        form.CancelButton = cancelBtn;

        RefreshLists();

        // Dragging names between two lists is not a thing the table can offer, so it says where to do it and
        // holds the table's commands until this closes -- otherwise they would fall through and walk the party
        // around underneath an open dialog.
        var result = ViewerDialog.RunBlocked(form, this, "Reordering the party", PublishTable);
        PublishToViewer();

        if (result != DialogResult.OK || reorderedResult == null)
            return;

        if (reorderedResult.SequenceEqual(activeMembers, StringComparer.OrdinalIgnoreCase))
            return;

        party.Members = reorderedResult;
        _partyRepository.Save(party);
        SayOnBoth("Camp", "Party order updated.");
        Invalidate();
    }

    public void ShowCharaterSheet(Character character)
    {
        using var sheet = new Adnd.Game.Windows.CharacterForm(character);
        sheet.ShowDialog(this);
        PublishToViewer();
    }

    private void InspectPartyMemberFromCamp(List<string> activeMembers, Dictionary<string, Character> roster, bool startWithEquip = false)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(startWithEquip ? "Choose party member to equip:" : "Choose party member to inspect:");
        prompt.AppendLine();

        for (int i = 0; i < activeMembers.Count; i++)
        {
            var c = roster[activeMembers[i]];
            var cls = c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(x => x.ToDisplayString())) : c.Class.ToDisplayString();
            prompt.AppendLine($"{i + 1}. {c.Name} ({cls})");
        }

        var selected = PromptForNumber("Camp - Inspect", prompt.ToString(), 1, activeMembers.Count);
        if (!selected.HasValue)
            return;

        var selectedCharacter = roster[activeMembers[selected.Value - 1]];

        OpenCampInspectDialog(selectedCharacter, activeMembers, startWithEquip);
    }

    private void OpenCampInspectDialog(Character selectedCharacter, List<string> activeMembers, bool startWithEquip = false)
    {
     /*   if (!GameRulesProvider.Current.UIOldStyle)
        {
            using var sheet = new Adnd.Game.Windows.CharacterForm(selectedCharacter);
            sheet.ShowDialog(this);
            PublishToViewer();
            return;
        }
     */
        // Handed the maze's publisher, so the whole screen -- menu, item lists, spell lists -- is answerable from
        // the table too.
        using var inspect = new CampCharacterInspectForm(selectedCharacter.Name, activeMembers, PublishTable)
        {
            StartWithEquip = startWithEquip,
            LocationInfoProvider = () => $"You are at {PositionText} on dungeon level {_currentDungeonLevel}, facing {CompassHeading()}.",
            MalorTeleport = TryMalorTeleport
        };
        inspect.ShowDialog(this);
        LoadLightFromParty();
        if (inspect.ReturnToTownRequested)
        {
            LeaveDungeonForTown();
            return;
        }
        PublishToViewer();   // back to the maze's own question
//        MessageBox.Show(this, selectedCharacter.ToString(), $"Inspect - {selectedCharacter.Name}", MessageBoxButtons.OK, MessageBoxIcon.None);
    }

    private static int GetCampInspectIndexFromKey(Keys key)
    {
        return key switch
        {
            Keys.D1 or Keys.NumPad1 => 1,
            Keys.D2 or Keys.NumPad2 => 2,
            Keys.D3 or Keys.NumPad3 => 3,
            Keys.D4 or Keys.NumPad4 => 4,
            Keys.D5 or Keys.NumPad5 => 5,
            Keys.D6 or Keys.NumPad6 => 6,
            _ => 0
        };
    }

    private int? PromptForNumber(string title, string text, int min, int max)
    {
        // One option per number for the table. Both callers -- choosing who to inspect in camp, choosing a floor
        // in the elevator -- already list the choices numbered in the text, so the labels come from there.
        var labels = new List<string>();
        for (var n = min; n <= max; n++)
            labels.Add(ViewerDialog.LabelFor(text, n));

        var input = PromptForText(title, text, labels, min);
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (!int.TryParse(input.Trim(), out var value))
            return null;

        if (value < min || value > max)
            return null;

        return value;
    }

    private void ShowDungeonElevatorDialog()
    {
        var usedPrivateElevator = _thinWalls && _currentDungeonLevel >= 4 && _position == PrivateElevatorSquare;
        // The lift from Level 1 only serves Levels 1-4; the private express elevator (E2) serves Levels 4-9.
        var minLevel = usedPrivateElevator ? 4 : MinDungeonLevel;
        var maxLevel = !_thinWalls ? MaxDungeonLevel : usedPrivateElevator ? 9 : 4;

        // Proving Grounds floors are marked with buttons A, B, C... in Wizardry; the maze keeps numbers.
        var letters = _thinWalls && IsPlayingWizardry;
        int? selected;
        if (letters)
        {
            string Letter(int n) => ((char)('A' + n - MinDungeonLevel)).ToString();
            var text = $"THERE ARE BUTTONS HERE MARKED {Letter(minLevel)} THROUGH {Letter(maxLevel)}."
                + Environment.NewLine + Environment.NewLine + "PRESS ONE (↵ TO LEAVE)"
                + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, Enumerable.Range(minLevel, maxLevel - minLevel + 1).Select(n => $"{Letter(n)}. Level {n}"));
            var labels = Enumerable.Range(minLevel, maxLevel - minLevel + 1).Select(n => $"Button {Letter(n)}").ToList();
            var answer = PromptForText("Elevator", text, labels, minLevel)?.Trim();
            selected = null;
            if (!string.IsNullOrEmpty(answer))
            {
                var level = answer.Length == 1 && char.IsLetter(answer[0])
                    ? char.ToUpperInvariant(answer[0]) - 'A' + MinDungeonLevel
                    : int.TryParse(answer, out var parsed) ? parsed : 0;
                if (level >= minLevel && level <= maxLevel)
                    selected = level;
            }
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("Choose dungeon level:");
            sb.AppendLine();
            for (var n = minLevel; n <= maxLevel; n++)
                sb.AppendLine($"{n}. Level {n}");

            sb.AppendLine();
            sb.Append($"Current level: {_currentDungeonLevel}");

            selected = PromptForNumber("Elevator", sb.ToString(), minLevel, maxLevel);
        }

        if (!selected.HasValue)
            return;

        _currentDungeonLevel = selected.Value;
        BuildMazeForLevel(_currentDungeonLevel);
        LoadEncounterLocationsForLevel(_currentDungeonLevel);

        if (!_fullMoonShownForThisDungeonTrip && IsFullMoonNight())
        {
            ShowFullMoonDialogRetro();
            _fullMoonShownForThisDungeonTrip = true;
        }

        ApplyLycanthropyStateForDungeonDescent();

        // Elevator landing.
        _position = usedPrivateElevator && _currentDungeonLevel == 4 ? PrivateElevatorSquare : ElevatorPosition;
        UpdateLighting();

        Invalidate();
        PublishToViewer();
    }

    private void ApplyLycanthropyStateForDungeonDescent()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var isFullMoon = IsFullMoonNight();
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            c.AdvanceLycanthropyByOneDay();

            if (!c.CanBecomeLycanthropeNow())
            {
                _characterRepository.Save(c);
                continue;
            }

            if (isFullMoon && c.TriggerLycanthropeTransformationByFullMoon())
            {
                messages.Add($"{c.Name} transforms under the full moon ({c.LycanthropySourceType}).");
            }

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Lycanthropy", string.Join(Environment.NewLine, messages));
    }

    /// <param name="tableOptions">
    /// The answers to offer on the table, when this prompt is really a numbered choice. Null for genuinely free
    /// text -- naming something -- where the table has nothing sensible to click and the keyboard is the only
    /// way; the player is told that rather than left looking at a board that appears hung.
    /// </param>
    /// <param name="firstNumber">What <paramref name="tableOptions"/>[0] is called, usually 1.</param>
    private string? PromptForText(string title, string text, IReadOnlyList<string>? tableOptions = null,
                                  int firstNumber = 1)
    {
        using var form = new Form();
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(520, 320);
        form.MinimizeBox = false;
        form.MaximizeBox = false;

        var oldStyleChoice = GameRulesProvider.Current.UIOldStyle && tableOptions != null;
        Control label = oldStyleChoice
            ? new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = text
            }
            : new Label
            {
                Left = 12,
                Top = 12,
                Width = 496,
                Height = 220,
                AutoSize = false,
                Text = text
            };

        var input = new TextBox
        {
            Left = 12,
            Top = 244,
            Width = 496
        };

        var ok = new Button
        {
            Text = "OK",
            Left = 352,
            Top = 278,
            Width = 75,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Left = 433,
            Top = 278,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        form.Controls.Add(label);
        form.Controls.Add(input);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (oldStyleChoice)
        {
            OldStyleDialog.Apply(form, label, input);
            form.Shown += (_, _) => input.Focus();
        }

        if (tableOptions is null)
        {
            // Genuinely free text. Nothing for the table to click, so it is told where to type.
            var typed = ViewerDialog.RunBlocked(form, this, title, PublishTable) == DialogResult.OK
                ? input.Text
                : null;
            PublishToViewer();
            return typed;
        }

        var outcome = ViewerDialog.RunPick(form, this, title, null, tableOptions, PublishTable);
        PublishToViewer();

        if (outcome.Picked.HasValue)
            return (firstNumber + outcome.Picked.Value).ToString();

        return outcome.Result == DialogResult.OK ? input.Text : null;
    }
}
