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

// Per-step poison and disease effects, exit-day progression and the full moon.
public sealed partial class MazeForm
{
    private void ApplyPoisonDamageForStep()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAllCached().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var members = party.Members.Where(roster.ContainsKey).Select(name => roster[name]).ToList();
        var effectsBeforeStep = GetStepEffects(members);
        if (!_stepEffects.SetEquals(effectsBeforeStep))
        {
            _effectsNotificationChoice = DialogResult.OK;
            _stepsSinceEffectsNotification = 0;
        }
        _stepsSinceEffectsNotification++;
        var messages = new List<string>();
        var changed = false;

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            var wasLycanthropeDamageTriggered = c.LycanthropyTriggeredByDamage;

            if (c.CurrentHitPoints > 0 && c.CurrentHitPoints < c.MaxHitPoints && !c.HasStatus(CharacterStatus.Dead)
                && c.Inventory.Any(i => string.Equals(i.Name, "Amulet of Werdna", StringComparison.OrdinalIgnoreCase)))
            {
                c.CurrentHitPoints++;
                changed = true;
            }

            if (c.MummyRotRaiseWindowRoundsRemaining > 0
                && (c.HasStatus(CharacterStatus.Dead) || c.HasStatus(CharacterStatus.Ashes) || c.CurrentHitPoints <= 0))
            {
                var remainingBefore = c.MummyRotRaiseWindowRoundsRemaining;
                var remainingAfter = c.TickMummyRotRaiseWindowRound();
                if (remainingAfter != remainingBefore)
                    changed = true;

                if (remainingAfter <= 0)
                    messages.Add($"{c.Name}'s mummy-rot revival window expires. Raise Dead can no longer restore this character.");
            }

            if (c.RotGrubDeathRoundsRemaining > 0
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost)
                && c.CurrentHitPoints > 0)
            {
                c.RotGrubDeathRoundsRemaining = Math.Max(0, c.RotGrubDeathRoundsRemaining - 1);
                changed = true;

                if (c.RotGrubDeathRoundsRemaining <= 0)
                {
                    c.CurrentHitPoints = 0;
                    c.AddStatus(CharacterStatus.Dead);
                    messages.Add($"{c.Name} dies as rot grubs reach the heart.");
                }
                else
                {
                    messages.Add($"{c.Name} has {c.RotGrubDeathRoundsRemaining} round(s) before rot grub death unless cured.");
                }
            }

            var ringDrain = EquipmentManager.ApplyEquippedLifeDrain(c);
            if (ringDrain > 0)
            {
                changed = true;
                messages.Add($"{c.Name}'s cursed ring drains {ringDrain} HP. HP {c.CurrentHitPoints + ringDrain}->{c.CurrentHitPoints}.");
                if (c.HasStatus(CharacterStatus.Dead))
                    messages.Add($"{c.Name} dies from the ring's drain.");
            }

            if (c.CurrentHitPoints > 0
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Petrified)
                && c.CurrentHitPoints < c.MaxHitPoints
                && HasEquippedRegeneration(c))
            {
                var beforeRegen = c.CurrentHitPoints;
                c.CurrentHitPoints = Math.Min(c.MaxHitPoints, c.CurrentHitPoints + 1);
                var healed = c.CurrentHitPoints - beforeRegen;
                if (healed > 0)
                {
                    changed = true;
                    messages.Add($"{c.Name} regenerates {healed} HP while walking. HP {beforeRegen}->{c.CurrentHitPoints}.");
                }
            }

            if (c.HasStatus(CharacterStatus.Paralyzed)
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost)
                && c.CurrentHitPoints > 0)
            {
                var beforeRounds = c.ParalyzedRoundsRemaining;
                var afterRounds = c.TickParalysisRound();
                changed = true;

                if (afterRounds > 0)
                    messages.Add($"{c.Name} remains paralyzed ({afterRounds} round(s) remaining). Step consumed 1 round.");
                else if (beforeRounds > 0)
                    messages.Add($"{c.Name} is no longer paralyzed.");
            }

            if (wasLycanthropeDamageTriggered
                && c.HasStatus(CharacterStatus.LycanthropeTransformed)
                && c.CurrentHitPoints >= c.MaxHitPoints)
            {
                c.EndLycanthropeTransformation();
                changed = true;
                messages.Add($"{c.Name} recovers and returns from lycanthrope form.");
            }

            if (c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead) || !c.HasStatus(CharacterStatus.Poisoned))
                continue;

            var roll = _random.Next(1, 101);
            if (roll > 30)
                continue;

            var damage = _random.Next(1, 3);
            var before = c.CurrentHitPoints;
            c.CurrentHitPoints = Math.Max(0, c.CurrentHitPoints - damage);
            var actual = before - c.CurrentHitPoints;

            if (actual <= 0)
                continue;

            changed = true;
            messages.Add($"{c.Name} takes {actual} poison damage while walking. HP {before}->{c.CurrentHitPoints}.");

            if (c.CurrentHitPoints <= 0)
            {
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} dies from poison.");
            }
        }

        var effectsAfterStep = GetStepEffects(members);
        var effectsEnded = !effectsBeforeStep.SetEquals(effectsAfterStep);
        _stepEffects.Clear();
        _stepEffects.UnionWith(effectsAfterStep);

        if (changed)
        {
            foreach (var c in roster.Values)
                _characterRepository.Save(c);
        }

        if (effectsEnded)
        {
            _effectsNotificationChoice = DialogResult.OK;
            _stepsSinceEffectsNotification = 0;
        }

        if (messages.Count == 0
            || (!effectsEnded && (_effectsNotificationChoice == DialogResult.Ignore
                || (_effectsNotificationChoice == DialogResult.Retry && _stepsSinceEffectsNotification < 10))))
            return;

        var choice = ShowEffectsOnBoth(string.Join(Environment.NewLine, messages));
        if (effectsAfterStep.Count == 0)
            _effectsNotificationChoice = DialogResult.OK;
        else if (choice is DialogResult.Ignore or DialogResult.Retry)
            _effectsNotificationChoice = choice;
        _stepsSinceEffectsNotification = 0;
    }

    private static HashSet<(string Character, string Effect)> GetStepEffects(IEnumerable<Character> members)
    {
        var effects = new HashSet<(string Character, string Effect)>();
        foreach (var c in members)
        {
            if (c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes) || c.HasStatus(CharacterStatus.Lost))
                continue;

            if (c.RotGrubDeathRoundsRemaining > 0)
                effects.Add((c.Name, "RotGrubs"));
            if (c.HasStatus(CharacterStatus.Poisoned))
                effects.Add((c.Name, "Poison"));
            if (c.HasStatus(CharacterStatus.Paralyzed))
                effects.Add((c.Name, "Paralysis"));
            if (!c.HasStatus(CharacterStatus.Petrified) && c.CurrentHitPoints < c.MaxHitPoints && HasEquippedRegeneration(c))
                effects.Add((c.Name, "Regeneration"));
            if (EquipmentManager.GetEquippedLifeDrain(c) > 0)
                effects.Add((c.Name, "CursedRing"));
            if (c.LycanthropyTriggeredByDamage && c.HasStatus(CharacterStatus.LycanthropeTransformed))
                effects.Add((c.Name, "Lycanthropy"));
        }
        return effects;
    }

    private DialogResult ShowEffectsOnBoth(string text)
    {
        var oldStyle = GameRulesProvider.Current.UIOldStyle;
        using var form = new Form
        {
            Text = "Effects",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = oldStyle ? FormBorderStyle.None : FormBorderStyle.FixedDialog,
            ClientSize = new Size(820, 500),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            KeyPreview = true,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Padding = new Padding(4)
        };
        var frame = new OldStyleFramePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        var title = new Label
        {
            Text = "EFFECTS",
            Dock = DockStyle.Top,
            Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };
        var body = new TextBox
        {
            Text = text,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = form.BackColor,
            ForeColor = form.ForeColor,
            Font = new Font("Consolas", oldStyle ? 18f : 14f, FontStyle.Bold)
        };
        var choices = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = oldStyle ? 108 : 80 };
        foreach (var (label, result) in new[]
        {
            ("↵ Continue", DialogResult.OK),
            ("D)on't show again", DialogResult.Ignore),
            ("S)how only every 10 steps", DialogResult.Retry)
        })
        {
            choices.Controls.Add(new Button
            {
                Text = label,
                DialogResult = result,
                AutoSize = true,
                BackColor = Color.Black,
                ForeColor = form.ForeColor,
                FlatStyle = oldStyle ? FlatStyle.Flat : FlatStyle.Standard,
                Font = body.Font
            });
        }
        form.KeyDown += (_, e) =>
        {
            var result = e.KeyCode switch
            {
                Keys.D => DialogResult.Ignore,
                Keys.S => DialogResult.Retry,
                Keys.Enter or Keys.Escape => DialogResult.OK,
                _ => DialogResult.None
            };
            if (result == DialogResult.None)
                return;
            e.SuppressKeyPress = true;
            form.DialogResult = result;
            form.Close();
        };
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                e.IsInputKey = true;
        };
        frame.Controls.Add(body);
        frame.Controls.Add(choices);
        frame.Controls.Add(title);
        form.Controls.Add(frame);
        form.Shown += (_, _) => body.Focus();

        var options = new[]
        {
            new ViewerPromptOption("continue", "Continue"),
            new ViewerPromptOption("dontShowAgain", "Don't show again"),
            new ViewerPromptOption("everyTenSteps", "Show only every 10 steps")
        };
        var answers = new Dictionary<string, DialogResult>
        {
            ["continue"] = DialogResult.OK,
            ["dontShowAgain"] = DialogResult.Ignore,
            ["everyTenSteps"] = DialogResult.Retry
        };
        var outcome = ViewerDialog.RunModal(form, this, new ViewerPrompt("choice", text, null, options), answers, PublishTable);
        PublishToViewer();
        return outcome;
    }

    private void ApplyDiseaseProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (!c.HasStatus(CharacterStatus.Diseased)
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            if (c.RottingDiseaseMonthsRemaining > 0)
                continue;

            var beforeCon = c.Abilities.Constitution;
            c.Abilities.Constitution = Math.Max(0, c.Abilities.Constitution - 1);

            if (c.Abilities.Constitution <= 0)
            {
                c.CurrentHitPoints = 0;
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} loses 1 Constitution from disease ({beforeCon}->0) and dies.");
            }
            else
            {
                messages.Add($"{c.Name} loses 1 Constitution from disease ({beforeCon}->{c.Abilities.Constitution}).");
            }

            _characterRepository.Save(c);
        }

        ApplyInfestationProgressForDungeonExitDay();

        if (messages.Count > 0)
            SayOnBoth("Disease", string.Join(Environment.NewLine, messages));
    }

    private void ApplyInfestationProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.InfestationDeathDaysRemaining <= 0
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            c.InfestationDeathDaysRemaining = Math.Max(0, c.InfestationDeathDaysRemaining - 1);

            if (c.InfestationDeathDaysRemaining > 0)
            {
                _characterRepository.Save(c);
                continue;
            }

            c.CurrentHitPoints = 0;
            c.AddStatus(CharacterStatus.Dead);
            messages.Add($"{c.Name} dies from untreated gas spore infestation.");

            var spores = Random.Shared.Next(2, 9);
            messages.Add($"{c.Name}'s body erupts and spawns {spores} gas spore(s).");

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Infestation", string.Join(Environment.NewLine, messages));

        ApplyTickDiseaseProgressForDungeonExitDay();
    }

    private void ApplyTickDiseaseProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.TickDiseaseDeathDaysRemaining <= 0
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            c.TickDiseaseDeathDaysRemaining = Math.Max(0, c.TickDiseaseDeathDaysRemaining - 1);

            if (c.TickDiseaseDeathDaysRemaining > 0)
            {
                messages.Add($"{c.Name} suffers from Tick Disease ({c.TickDiseaseDeathDaysRemaining} day(s) until death unless cured).");
                _characterRepository.Save(c);
                continue;
            }

            c.CurrentHitPoints = 0;
            c.AddStatus(CharacterStatus.Dead);
            messages.Add($"{c.Name} dies from untreated Tick Disease.");
            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Tick Disease", string.Join(Environment.NewLine, messages));

        ApplyPoisonSporesRaiseWindowProgressForDungeonExitDay();

        ApplyRottingDiseaseProgressForDungeonExitDay();
    }

    private void ApplyPoisonSporesRaiseWindowProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.PoisonSporesRaiseWindowDaysRemaining <= 0)
                continue;

            if (!c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && c.CurrentHitPoints > 0)
            {
                continue;
            }

            var remaining = c.TickPoisonSporesRaiseWindowDay();
            if (remaining <= 0)
                messages.Add($"{c.Name}'s poison spores revival window closes. Resurrection/Raise Dead can no longer restore this character.");
            else
                messages.Add($"{c.Name}'s poison spores revival window: {remaining} day(s) remaining.");

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Poison Spores", string.Join(Environment.NewLine, messages));
    }

    private void ApplyRottingDiseaseProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.RottingDiseaseMonthsRemaining <= 0
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            c.RottingDiseaseDaysIntoCurrentMonth = Math.Max(0, c.RottingDiseaseDaysIntoCurrentMonth + 1);
            if (c.RottingDiseaseDaysIntoCurrentMonth < 30)
            {
                _characterRepository.Save(c);
                continue;
            }

            c.RottingDiseaseDaysIntoCurrentMonth = 0;
            c.RottingDiseaseMonthsRemaining = Math.Max(0, c.RottingDiseaseMonthsRemaining - 1);

            var beforeCha = c.Abilities.Charisma;
            c.Abilities.Charisma = Math.Max(0, c.Abilities.Charisma - 2);
            messages.Add($"{c.Name} loses 2 Charisma from Rotting Disease ({beforeCha}->{c.Abilities.Charisma}).");

            if (c.RottingDiseaseMonthsRemaining <= 0)
            {
                c.CurrentHitPoints = 0;
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} dies from untreated Rotting Disease.");
            }
            else
            {
                messages.Add($"{c.RottingDiseaseMonthsRemaining} month(s) remain until death unless cured.");
            }

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Rotting Disease", string.Join(Environment.NewLine, messages));
    }

    private void ApplyPartyAgingForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            c.AdvanceAgeByDays(1);
            _characterRepository.Save(c);
        }

        party.DungeonDaysElapsed = Math.Max(0, party.DungeonDaysElapsed) + 1;
        _partyRepository.Save(party);
    }

    private bool IsFullMoonNight()
    {
        var party = _partyRepository.Load();
        var dungeonDaysElapsed = Math.Max(0, party.DungeonDaysElapsed);
        // Every 30th day descending is a full moon night.
        var daysOnNextDescent = dungeonDaysElapsed + 1;
        return daysOnNextDescent % 30 == 0;
    }

    private void ShowFullMoonDialogRetro()
    {
        using var form = new Form
        {
            Text = "Full Moon",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(700, 150),
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
            Text = "FULL MOON",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 12,
            Top = 58,
            Width = framePanel.ClientSize.Width - 24,
            Height = 56,
            Text = "Tonight is a full moon as the party descends into the Proving Grounds.",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 13f, FontStyle.Bold)
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        form.MouseDown += (_, _) =>
        {
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        ViewerDialog.RunModal(form, this, new ViewerPrompt("info", "Full moon tonight.", null, new[]
        {
            new ViewerPromptOption("continue", "Continue")
        }), new Dictionary<string, DialogResult>
        {
            ["continue"] = DialogResult.OK
        }, PublishTable);

        PublishToViewer();
    }

    private static bool HasEquippedRegeneration(Character c)
    {
        return c.Equipment.Values
            .Where(item => item != null)
            .Any(item => item!.SpecialAbilities.Any(a =>
                string.Equals(a?.Trim(), "Regeneration (1)", StringComparison.OrdinalIgnoreCase)));
    }
}
