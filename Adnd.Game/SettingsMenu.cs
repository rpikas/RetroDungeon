using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;

namespace Adnd.Game;

public class SettingsMenu
{
    public void Show()
    {
        Console.RulesetPictures = true;
        try { ShowCore(); }
        finally { Console.RulesetPictures = false; }
    }

    private string? _notice;

    private void ShowCore()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== SETTINGS ===\n");

            var rules = GameRulesProvider.Current;
            var properties = GetEditableProperties();

            Console.WriteLine("-- Game system (click a picture, or W = Wizardry / D = AD&D) --");
            for (int i = 0; i < properties.Length; i++)
            {
                if (i == SystemProperties.Length)
                    Console.WriteLine("\n-- Other settings --");

                var p = properties[i];
                var value = p.GetValue(rules);
                Console.WriteLine($"{i + 1}. {p.Name} = {FormatValue(value)}");
            }

            if (_notice != null)
            {
                Console.WriteLine($"\n{_notice}");
                _notice = null;
            }

            Console.WriteLine("\nChoose setting # to edit (Enter to leave)");
            Console.WriteLine("L↵eave");

            Console.Write("Choose #: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                break;

            var preset = input.Trim().ToUpperInvariant();
            if (preset == "W" || preset == "D")
            {
                var target = preset == "W" ? Ruleset.Wizardry : Ruleset.ADND;
                var previous = rules.Rules;
                rules.ApplySystemPreset(target);
                GameRulesProvider.Current = rules;
                GameRulesProvider.Save();
                _notice = $"Now playing {target}: rules, items and monsters set to {target}." + SwitchParty(previous, target);
                RuleApplicationInfo.Publish($"Settings changed: rules, items and monsters set to {target}.");
                continue;
            }

            if (int.TryParse(input.Trim(), out int selected) && selected >= 1 && selected <= properties.Length)
            {
                var property = properties[selected - 1];
                var beforeValue = property.GetValue(rules);

                EditProperty(rules, property);

                var afterValue = property.GetValue(rules);
                GameRulesProvider.Current = rules;
                GameRulesProvider.Save();

                if (!Equals(beforeValue, afterValue))
                {
                    if (property.Name == nameof(GameRules.Rules))
                        _notice = SwitchParty((Ruleset)beforeValue!, rules.Rules).Trim();

                    RuleApplicationInfo.Publish(
                        $"Settings changed: {property.Name} = {FormatValue(beforeValue)}; now {FormatValue(afterValue)}.");
                }
            }
        }
    }

    // Each ruleset remembers its own party: the current party is stored under the ruleset being left, and the one
    // last used with the new ruleset is restored (members that no longer exist or belong elsewhere are skipped).
    private static string SwitchParty(Ruleset from, Ruleset to)
    {
        if (from == to)
            return "";

        var partyRepo = new Adnd.Data.Party.PartyRepository("Data/Party");
        var roster = new Adnd.Data.Characters.CharacterRepository("Data/Characters").GetAll()
            .ToDictionary(c => c.Name, c => c.Ruleset, StringComparer.OrdinalIgnoreCase);

        var current = partyRepo.Load();
        new Adnd.Data.Party.PartyRepository(RememberedPartyFile(from)).Save(current);

        var restored = new Adnd.Data.Party.PartyRepository(RememberedPartyFile(to)).Load();
        restored.Members = restored.Members.Where(m => roster.TryGetValue(m, out var r) && r == to).ToList();
        restored.CurrentShopperIndex = -1;
        partyRepo.Save(restored);

        return restored.Members.Count > 0
            ? $" Restored the {to} party: {string.Join(", ", restored.Members)}."
            : $" No saved {to} party yet.";
    }

    private static string RememberedPartyFile(Ruleset ruleset) =>
        System.IO.Path.Combine("Data", "Party", $"party-{ruleset.ToString().ToLowerInvariant()}.json");

    private static readonly string[] SystemProperties =
    {
        nameof(GameRules.Rules),
        nameof(GameRules.ItemSourceOptions),
        nameof(GameRules.BoltacItemSourceFilter),
        nameof(GameRules.MonsterSourceOptions),
    };

    private static PropertyInfo[] GetEditableProperties()
    {
        var all = typeof(GameRules)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => !string.Equals(p.Name, nameof(GameRules.DefaultColor), StringComparison.Ordinal))
            .Where(p => !string.Equals(p.Name, nameof(GameRules.InProvingGrounds), StringComparison.Ordinal))
            .ToArray();

        return SystemProperties.Select(n => all.First(p => p.Name == n))
            .Concat(all.Where(p => !SystemProperties.Contains(p.Name)).OrderBy(p => p.Name))
            .ToArray();
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return "(null)";

        if (value is bool b)
            return b ? "yes" : "no";

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return value.ToString() ?? "(null)";
    }

    private static void EditProperty(GameRules rules, PropertyInfo property)
    {
        Console.WriteLine();
        Console.WriteLine($"Editing {property.Name}");

        if (property.PropertyType == typeof(Color)
            && string.Equals(property.Name, nameof(GameRules.ForegroundColor), StringComparison.Ordinal))
        {
            EditForegroundColor(rules, property);
            return;
        }

        var type = property.PropertyType;
        var currentValue = property.GetValue(rules);

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            for (int i = 0; i < values.Length; i++)
                Console.WriteLine($"{i + 1}. {values[i]}");

            Console.Write("Choose #: ");
            var selected = InputHelper.ReadNumber(1, values.Length);
            if (selected.HasValue)
                property.SetValue(rules, values[selected.Value - 1]);

            return;
        }

        Console.Write($"New value (current: {FormatValue(currentValue)}): ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            return;

        if (TryConvert(input.Trim(), type, out var converted))
            property.SetValue(rules, converted);
    }

    private static void EditForegroundColor(GameRules rules, PropertyInfo property)
    {
        var choices = new[]
        {
            Color.Green,
            Color.Red,
            Color.Blue,
            Color.Yellow,
            Color.Cyan,
            Color.Magenta,
            Color.White
        };

        var current = property.GetValue(rules);
        Console.WriteLine($"Current: {FormatValue(current)}");

        for (int i = 0; i < choices.Length; i++)
            Console.WriteLine($"{i + 1}. {choices[i].Name}");

        Console.Write("Choose #: ");
        var selected = InputHelper.ReadNumber(1, choices.Length);
        if (selected.HasValue)
            property.SetValue(rules, choices[selected.Value - 1]);
    }

    private static bool TryConvert(string input, Type type, out object? value)
    {
        if (type == typeof(string))
        {
            value = input;
            return true;
        }

        if (type == typeof(bool))
        {
            if (bool.TryParse(input, out var b))
            {
                value = b;
                return true;
            }

            if (string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(input, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "n", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            if (input == "1")
            {
                value = true;
                return true;
            }

            if (input == "0")
            {
                value = false;
                return true;
            }
        }

        if (type == typeof(int) && int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            value = i;
            return true;
        }

        if (type == typeof(double) && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            value = d;
            return true;
        }

        if (type == typeof(float) && float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
        {
            value = f;
            return true;
        }

        if (type == typeof(decimal) && decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
        {
            value = m;
            return true;
        }

        if (type == typeof(long) && long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            value = l;
            return true;
        }

        value = null;
        return false;
    }
}
