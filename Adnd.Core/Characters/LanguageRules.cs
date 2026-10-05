using System;
using System.Collections.Generic;
using System.Linq;

namespace Adnd.Core.Characters;

public static class LanguageRules
{
    private static readonly Dictionary<Race, string[]> RacialLanguages = new()
    {
        [Race.Human] = new[] { "Common" },
        [Race.Dwarf] = new[] { "Common", "Dwarvish" },
        [Race.Elf] = new[] { "Common", "Elvish" },
        [Race.Gnome] = new[] { "Common", "Gnomish" },
        [Race.Halfling] = new[] { "Common", "Halfling" },
        [Race.HalfElf] = new[] { "Common", "Elvish" },
        [Race.HalfOrc] = new[] { "Common", "Orcish" }
    };

    private static readonly string[] AdditionalLanguagePool =
    [
        "Dwarvish", "Elvish", "Gnomish", "Halfling", "Orcish", "Goblin", "Hobgoblin", "Gnoll", "Kobold",
        "Ogrish", "Lizardman", "Troll", "Giant", "Dragon", "Pixie", "Sprite", "Centaur"
    ];

    public static int GetAdditionalLanguageCount(int intelligence)
    {
        intelligence = Math.Clamp(intelligence, 3, 18);
        return intelligence switch
        {
            <= 7 => 0,
            <= 9 => 1,
            <= 11 => 2,
            <= 13 => 3,
            <= 16 => 4,
            17 => 5,
            _ => 7
        };
    }

    public static List<string> BuildBaseKnownLanguages(Character character)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Common",
            character.Alignment.ToDisplayString()
        };

        if (RacialLanguages.TryGetValue(character.Race, out var racial))
        {
            foreach (var lang in racial)
                set.Add(lang);
        }

        var classes = character.Classes ?? new List<CharacterClass>();
        if (classes.Contains(CharacterClass.Druid))
            set.Add("Druidic");
        if (classes.Contains(CharacterClass.Thief))
            set.Add("Thieves' Cant");

        return set.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> GetAdditionalLanguageChoices(Character character)
    {
        var known = BuildBaseKnownLanguages(character);
        var set = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        return AdditionalLanguagePool.Where(l => !set.Contains(l)).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> PickRandomAdditionalLanguages(Character character, int count)
    {
        var choices = GetAdditionalLanguageChoices(character);
        if (count <= 0 || choices.Count == 0)
            return new List<string>();

        var picks = new List<string>();
        for (int i = 0; i < count && choices.Count > 0; i++)
        {
            var idx = Random.Shared.Next(0, choices.Count);
            picks.Add(choices[idx]);
            choices.RemoveAt(idx);
        }

        return picks;
    }

    public static List<string> CombineKnownLanguages(Character character, IEnumerable<string> additional)
    {
        var known = new HashSet<string>(BuildBaseKnownLanguages(character), StringComparer.OrdinalIgnoreCase);
        foreach (var lang in additional ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(lang))
                known.Add(lang.Trim());
        }

        return known.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
