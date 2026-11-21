using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Wizardry;
using Adnd.Data.Characters;

namespace Adnd.Game;

/// <summary>Character creation used in the Training Grounds when the rules are set to Wizardry.</summary>
public class WizardryCharacterCreation
{
    private static readonly (string Label, char Key)[] StatKeys =
    {
        ("Strength", 'S'), ("I.Q.", 'I'), ("Piety", 'P'), ("Vitality", 'V'), ("Agility", 'A'), ("Luck", 'L')
    };

    private readonly CharacterRepository _repo;

    public WizardryCharacterCreation(CharacterRepository repo) => _repo = repo;

    public void Run()
    {
        Console.Clear();
        Console.WriteLine("=== CREATE WIZARDRY CHARACTER ===\n");

        var race = ChooseRace();
        if (race == null)
            return;

        var baseStats = WizardryRules.RaceBaseStats(race.Value);
        var bonus = WizardryRules.RollBonusPoints(Random.Shared);
        RuleApplicationInfo.Publish($"Wizardry creation: {WizardryRules.RaceName(race.Value)}, {bonus} bonus points rolled.");

        WizardryStats stats;
        List<WizardryClass> eligible;
        WizardryClass? picked;

        var alignment = ChooseAlignment();
        Console.Clear();

        while (true)
        {
            stats = AllocatePoints(race.Value, alignment, baseStats, bonus, out picked);
            eligible = WizardryRules.EligibleClasses(stats, alignment).ToList();
            if (eligible.Count > 0)
                break;

            Console.WriteLine("\nNo class accepts these stats. Assign the points again.");
            Console.ReadKey(true);
        }

        Console.CityPicture = GameRulesProvider.Current.ShowCityPictures ? "WizardrySelectingClass1.png" : null;
        WizardryClass cls;
        try { cls = picked is { } p && eligible.Contains(p) ? p : ChooseClass(eligible); }
        finally { Console.CityPicture = null; }
        var character = WizardryCharacterFactory.Create(race.Value, stats, alignment, cls, Random.Shared);

        Console.WriteLine();
        Console.WriteLine($"{WizardryRules.RaceName(race.Value)} {cls}, {alignment}");
        Console.WriteLine($"HP: {character.MaxHitPoints}, GP: {character.GoldPieces}, Age: {character.Age}\n");

        Console.Write("Save character? (Y/N): ");
        if (Console.ReadKey(true).Key == ConsoleKey.N)
        {
            Console.WriteLine("\nCharacter discarded.");
            return;
        }

        Console.WriteLine();
        character.Name = AskName();
        _repo.Save(character);
        Console.WriteLine("Character saved.");
    }

    /// <summary>Changes the class of an existing character: stats go back to the racial base plus new bonus points.</summary>
    public void ChangeClass(Character character)
    {
        Console.Clear();
        Console.WriteLine($"=== CHANGE CLASS: {character.Name} ===\n");
        Console.WriteLine("Level returns to 1 and XP to 0 (HP and known spells are kept),");
        Console.WriteLine("stats return to racial base plus new bonus points, and the");
        Console.WriteLine("character ages 252, 304 or 356 weeks.\n");
        Console.Write("Continue? (Y/N): ");
        if (Console.ReadKey(true).Key != ConsoleKey.Y)
            return;

        var baseStats = WizardryRules.RaceBaseStats(character.Race);
        var bonus = WizardryRules.RollBonusPoints(Random.Shared);
        var alignment = WizardryClassChange.AlignmentOf(character);
        RuleApplicationInfo.Publish($"Wizardry class change: {character.Name} has {bonus} bonus points to assign.");

        WizardryStats stats;
        List<WizardryClass> targets;
        WizardryClass? picked;
        while (true)
        {
            stats = AllocatePoints(character.Race,             alignment, baseStats, bonus, out picked, classChange: true);
            targets = WizardryClassChange.EligibleTargets(character, stats).ToList();
            if (targets.Count > 0)
                break;

            Console.WriteLine("\nNo other class accepts these stats. Assign the points again.");
            Console.ReadKey(true);
        }

        Console.Clear();
        var newClass = picked is { } chosen && targets.Contains(chosen) ? chosen : ChooseClass(targets);
        var weeks = WizardryClassChange.Apply(character, newClass, stats, Random.Shared);
        _repo.Save(character);

        RuleApplicationInfo.Publish($"Wizardry class change: {character.Name} became a {newClass}, level 1, aged {weeks} weeks.");
        Console.WriteLine($"\n{character.Name} is now a level 1 {newClass} and aged {weeks} weeks.");
        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private static Race? ChooseRace()
    {
        Console.WriteLine("Choose Race:");
        Console.WriteLine($"   {"",-8} STR  IQ PIE VIT AGI LUC");
        for (var i = 0; i < WizardryRules.Races.Length; i++)
        {
            var r = WizardryRules.Races[i];
            var s = WizardryRules.RaceBaseStats(r);
            Console.WriteLine($"{(char)('A' + i)}) {WizardryRules.RaceName(r),-8} {s.Strength,3} {s.Iq,3} {s.Piety,3} {s.Vitality,3} {s.Agility,3} {s.Luck,3}");
        }

        Console.Write("Race (Enter to cancel): ");
        var index = InputHelper.ReadLetterIndex(WizardryRules.Races.Length);
        Console.WriteLine();
        return index.HasValue ? WizardryRules.Races[index.Value] : null;
    }

    private static WizardryStats AllocatePoints(Race race, WizardryAlignment alignment,     WizardryStats baseStats, int bonus, out WizardryClass? picked, bool classChange = false)
    {
        var values = new[] { baseStats.Strength, baseStats.Iq, baseStats.Piety, baseStats.Vitality, baseStats.Agility, baseStats.Luck };
        var floor = (int[])values.Clone();
        var left = bonus;

        var pointer = 0;
        var message = "";
        WizardryClass? selected = null;

        bool Done() => left == 0 || values.All(v => v >= WizardryRules.MaxStat);

        // Clicks on the +/- buttons, the classes and the confirm button are typed as keys the loop below understands.
        bool OnClick(string text, int column)
        {
            if (text.Contains("[ CONFIRM ]", StringComparison.Ordinal))
            {
                if (Done())
                    TermConsole.EnqueueText("*");
                return true;
            }

            for (var i = 0; i < StatKeys.Length; i++)
            {
                if (!text.Contains($"│ {StatKeys[i].Label.ToUpperInvariant(),9}", StringComparison.Ordinal))
                    continue;

                if (column >= 15 && column <= 19)
                    TermConsole.EnqueueText($"{i + 1}-");
                else if (column >= 20 && column <= 24)
                    TermConsole.EnqueueText($"{i + 1}+");
                return true;
            }

            var cls = System.Text.RegularExpressions.Regex.Match(text, @"││[► ]([A-H])\) ");
            if (cls.Success && column >= 24)
            {
                if (Done())
                    TermConsole.EnqueueText(cls.Groups[1].Value);
                return true;
            }

            return false;
        }

        TermConsole.ClickHandler = OnClick;
        try
        {
            while (true)
            {
                var current = new WizardryStats(values[0], values[1], values[2], values[3], values[4], values[5]);
                var classes = WizardryRules.EligibleClasses(current, alignment, classChange).ToList();
                if (selected.HasValue && (!Done() || !classes.Contains(selected.Value)))
                    selected = null;

                DrawAllocation(race, alignment, values, floor, pointer, left, classes, selected, Done(), message);
                message = "";

                var info = Console.ReadKey(true);
                switch (info.Key)
                {
                    case ConsoleKey.Enter:
                        pointer = (pointer + 1) % values.Length;
                        continue;
                    case ConsoleKey.UpArrow:
                        TryChange(+1);
                        continue;
                    case ConsoleKey.DownArrow:
                        TryChange(-1);
                        continue;
                    case ConsoleKey.Escape:
                        if (Done())
                        {
                            picked = selected;
                            return current;
                        }

                        message = "SPEND ALL BONUS POINTS FIRST.";
                        continue;
                }

                var ch = info.KeyChar;
                if (ch is >= '1' and <= '6')
                    pointer = ch - '1';
                else if (ch == '+')
                    TryChange(+1);
                else if (ch == '-')
                    TryChange(-1);
                else if (ch == '*' && Done())
                {
                    picked = selected;
                    return current;
                }
                else if (Done() && char.ToUpperInvariant(ch) is >= 'A' and <= 'H')
                {
                    var i = char.ToUpperInvariant(ch) - 'A';
                    if (i < classes.Count)
                        selected = classes[i];
                }
            }
        }
        finally
        {
            TermConsole.ClickHandler = null;
        }

        void TryChange(int delta)
        {
            if (delta > 0 && left > 0 && values[pointer] < WizardryRules.MaxStat)
            {
                values[pointer]++;
                left--;
            }
            else if (delta < 0 && values[pointer] > floor[pointer])
            {
                values[pointer]--;
                left++;
            }
        }
    }

    private static void DrawAllocation(Race race, WizardryAlignment alignment, int[] values, int[] floor, int pointer, int left, List<WizardryClass> classes, WizardryClass? selected, bool done, string message)
    {
        const int leftWidth = 22;
        const int rightWidth = 24;

        Console.Clear();
        Console.WriteLine($"┌{new string('─', leftWidth + rightWidth + 1)}┐");
        Console.WriteLine($"│ {"NEW CHARACTER",-17} L  1  {alignment.ToString()[0]}-???  {WizardryRules.RaceName(race).ToUpperInvariant(),-10}{"",-5}│");
        Console.WriteLine($"└{new string('─', leftWidth + rightWidth + 1)}┘");
        Console.WriteLine();
        Console.WriteLine(done ? "ALL POINTS DISTRIBUTED." : $"POINTS LEFT TO DISTRIBUTE: {left}");
        Console.WriteLine();

        var rows = new List<(string Left, string Right)>();
        for (var i = 0; i < 8; i++)
        {
            string leftText;
            if (i < StatKeys.Length)
            {
                var minus = values[i] > floor[i] ? "[-]" : " - ";
                var plus = left > 0 && values[i] < WizardryRules.MaxStat ? "[+]" : " + ";
                leftText = $"{StatKeys[i].Label.ToUpperInvariant(),9}{(i == pointer ? "◄" : " ")}{values[i],2}  {minus} {plus}";
            }
            else if (i == 7)
                leftText = $"{"BONUS",9} {left,2}";
            else
                leftText = "";

            var rightText = i < classes.Count
                ? $"{(classes[i] == selected ? "►" : " ")}{(char)('A' + i)}) {classes[i].ToString().ToUpperInvariant()}"
                : "";
            rows.Add((leftText, rightText));
        }

        Console.WriteLine($"┌{new string('─', leftWidth)}┐┌{new string('─', rightWidth)}┐");
        foreach (var (l, r) in rows)
            Console.WriteLine($"│ {l.PadRight(leftWidth - 1)}││{r.PadRight(rightWidth)}│");
        Console.WriteLine($"└{new string('─', leftWidth)}┘└{new string('─', rightWidth)}┘");

        Console.WriteLine();
        if (done)
        {
            Console.WriteLine("CLICK OR PRESS A CLASS LETTER TO SELECT IT.");
            Console.WriteLine();
            Console.WriteLine("[ CONFIRM ]");
            Console.WriteLine();
        }

        Console.WriteLine("↵ MOVES POINTER, ↑ AND ↓ (OR + AND -) ALTER SCORES. PRESS (ESC) WHEN FINISHED.");
        if (message.Length > 0)
            Console.WriteLine(message);
    }

    private static WizardryAlignment ChooseAlignment()
    {
        Console.Clear();
        Console.WriteLine("Choose Alignment:");
        var values = Enum.GetValues<WizardryAlignment>();
        for (var i = 0; i < values.Length; i++)
            Console.WriteLine($"{(char)('A' + i)}) {values[i]}");

        Console.Write("Alignment: ");
        var index = InputHelper.ReadLetterIndex(values.Length);
        return values[index ?? 0];
    }

    private static WizardryClass ChooseClass(List<WizardryClass> eligible)
    {
        Console.WriteLine("\nChoose Class:");
        for (var i = 0; i < eligible.Count; i++)
            Console.WriteLine($"{(char)('A' + i)}) {eligible[i]}");

        Console.Write("Class: ");
        var index = InputHelper.ReadLetterIndex(eligible.Count);
        return eligible[index ?? 0];
    }

    private string AskName()
    {
        var existing = _repo.GetAll().Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            Console.Write("Name: ");
            var name = (Console.ReadLine() ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                Console.WriteLine("Name is mandatory. Please enter a name.");
                continue;
            }
            if (name.Length > 15)
                name = name[..15];

            if (!existing.Contains(name))
                return name;

            Console.WriteLine("A character with that name already exists. Please enter another name.");
        }
    }
}
