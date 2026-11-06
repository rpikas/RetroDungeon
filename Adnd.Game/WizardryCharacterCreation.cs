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

        var alignment = ChooseAlignment();
        Console.Clear();

        while (true)
        {
            stats = AllocatePoints(race.Value, alignment, baseStats, bonus);
            eligible = WizardryRules.EligibleClasses(stats, alignment).ToList();
            if (eligible.Count > 0)
                break;

            Console.WriteLine("\nNo class accepts these stats. Assign the points again.");
            Console.ReadKey(true);
        }

        var cls = ChooseClass(eligible);
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

    private static WizardryStats AllocatePoints(Race race, WizardryAlignment alignment, WizardryStats baseStats, int bonus)
    {
        var values = new[] { baseStats.Strength, baseStats.Iq, baseStats.Piety, baseStats.Vitality, baseStats.Agility, baseStats.Luck };
        var floor = (int[])values.Clone();
        var left = bonus;

        var pointer = 0;
        var message = "";

        while (true)
        {
            var current = new WizardryStats(values[0], values[1], values[2], values[3], values[4], values[5]);
            var classes = Enum.GetValues<WizardryClass>()
                .Where(c => WizardryRules.MeetsRequirements(c, current, alignment))
                .ToList();

            DrawAllocation(race, alignment, values, pointer, left, classes, message);
            message = "";

            var key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.Enter:
                    pointer = (pointer + 1) % values.Length;
                    break;
                case ConsoleKey.UpArrow:
                    if (left > 0 && values[pointer] < WizardryRules.MaxStat)
                    {
                        values[pointer]++;
                        left--;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (values[pointer] > floor[pointer])
                    {
                        values[pointer]--;
                        left++;
                    }
                    break;
                case ConsoleKey.Escape:
                    if (left == 0 || values.All(v => v >= WizardryRules.MaxStat))
                        return current;

                    message = "SPEND ALL BONUS POINTS FIRST.";
                    break;
            }
        }
    }

    private static void DrawAllocation(Race race, WizardryAlignment alignment, int[] values, int pointer, int left, List<WizardryClass> classes, string message)
    {
        const int leftWidth = 22;
        const int rightWidth = 24;

        Console.Clear();
        Console.WriteLine($"┌{new string('─', leftWidth + rightWidth + 1)}┐");
        Console.WriteLine($"│ {"NEW CHARACTER",-17} L  1  {alignment.ToString()[0]}-???  {WizardryRules.RaceName(race).ToUpperInvariant(),-10}{"",-5}│");
        Console.WriteLine($"└{new string('─', leftWidth + rightWidth + 1)}┘");
        Console.WriteLine();

        var rows = new List<(string Left, string Right)>();
        for (var i = 0; i < 8; i++)
        {
            string leftText;
            if (i < StatKeys.Length)
                leftText = $"{StatKeys[i].Label.ToUpperInvariant(),9}{(i == pointer ? "◄" : " ")}{values[i],2}";
            else if (i == 7)
                leftText = $"{"BONUS",9} {left,2}";
            else
                leftText = "";

            var rightText = i < classes.Count ? $"{(char)('A' + i)}) {classes[i].ToString().ToUpperInvariant()}" : "";
            rows.Add((leftText, rightText));
        }

        Console.WriteLine($"┌{new string('─', leftWidth)}┐┌{new string('─', rightWidth)}┐");
        foreach (var (l, r) in rows)
            Console.WriteLine($"│ {l.PadRight(leftWidth - 1)}││ {r.PadRight(rightWidth - 1)}│");
        Console.WriteLine($"└{new string('─', leftWidth)}┘└{new string('─', rightWidth)}┘");

        Console.WriteLine();
        Console.WriteLine("↵ MOVES POINTER, ↑ AND ↓ ALTER SCORES. PRESS (ESC) WHEN FINISHED.");
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
                name = "Unknown";
            if (name.Length > 15)
                name = name[..15];

            if (!existing.Contains(name) || name == "Unknown")
                return name;

            Console.WriteLine("A character with that name already exists.");
        }
    }
}
