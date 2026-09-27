using System.Collections.Generic;

namespace Adnd.Core.Characters;

public static class ClassRestrictions
{
    // Return allowed classes based on raw ability scores and race.
    // Thresholds follow common AD&D-style heuristics and can be tuned.
    public static List<CharacterClass> GetAllowedClasses(AbilityScores a, Race race)
    {
        var list = new List<CharacterClass>();

        // Fighter always allowed
        list.Add(CharacterClass.Fighter);

        // Paladin: human only, Str >=12, Cha >=17
        if (race == Race.Human && a.Strength >= 12 && a.Charisma >= 17)
            list.Add(CharacterClass.Paladin);

        // Ranger: Str >=13, Dex >=13, Wis >=13 (race restriction applied below)
        if (a.Strength >= 13 && a.Dexterity >= 13 && a.Wisdom >= 13)
            list.Add(CharacterClass.Ranger);

        // Cleric: Wis >= 9
        if (a.Wisdom >= 9)
            list.Add(CharacterClass.Cleric);

        // Druid: Wis >= 15, Dex >= 12
        if (a.Wisdom >= 15 && a.Dexterity >= 12)
            list.Add(CharacterClass.Druid);

        // Magic-user: Int >= 9
        if (a.Intelligence >= 9)
            list.Add(CharacterClass.MagicUser);

        // Illusionist: Int >= 12
        if (a.Intelligence >= 12)
            list.Add(CharacterClass.Illusionist);

        // Thief: Dex >= 9
        if (a.Dexterity >= 9)
            list.Add(CharacterClass.Thief);

        // Assassin: Dex >= 13 (and thief-like skills)
        if (a.Dexterity >= 13)
            list.Add(CharacterClass.Assassin);

        // Monk: human only, Str >=15, Dex >=15, Con >=15
        if (race == Race.Human && a.Strength >= 15 && a.Dexterity >= 15 && a.Constitution >= 15)
            list.Add(CharacterClass.Monk);

        // Bard: Cha >= 15, Dex >= 13
        if (a.Charisma >= 15 && a.Dexterity >= 13)
            list.Add(CharacterClass.Bard);

        // Enforce per-race allowed single-class choices from the configured class/race table.
        var allowedForRace = new HashSet<CharacterClass>();
        switch (race)
        {
            case Race.Human:
                // Humans may choose any class (subject to ability minima)
                foreach (CharacterClass cc in System.Enum.GetValues(typeof(CharacterClass)))
                    allowedForRace.Add(cc);
                break;
            case Race.Elf:
                allowedForRace = new HashSet<CharacterClass>
                {
                    CharacterClass.Fighter,
                    CharacterClass.MagicUser,
                    CharacterClass.Thief,
                    CharacterClass.Assassin
                };
                break;
            case Race.HalfElf:
                allowedForRace = new HashSet<CharacterClass>
                {
                    CharacterClass.Cleric,
                    CharacterClass.Druid,
                    CharacterClass.Fighter,
                    CharacterClass.Ranger,
                    CharacterClass.MagicUser,
                    CharacterClass.Thief,
                    CharacterClass.Assassin
                };
                break;
            case Race.Dwarf:
                allowedForRace = new HashSet<CharacterClass>
                {
                    CharacterClass.Fighter,
                    CharacterClass.Thief,
                    CharacterClass.Assassin
                };
                break;
            case Race.Halfling:
                allowedForRace = new HashSet<CharacterClass>
                {
                    CharacterClass.Fighter,
                    CharacterClass.Thief
                };
                break;
            case Race.Gnome:
                allowedForRace = new HashSet<CharacterClass>
                {
                    CharacterClass.Fighter,
                    CharacterClass.Illusionist,
                    CharacterClass.Thief,
                    CharacterClass.Assassin
                };
                break;
            case Race.HalfOrc:
                allowedForRace = new HashSet<CharacterClass>
                {
                    CharacterClass.Fighter,
                    CharacterClass.Cleric,
                    CharacterClass.Thief,
                    CharacterClass.Assassin
                };
                break;
            default:
                foreach (CharacterClass cc in System.Enum.GetValues(typeof(CharacterClass)))
                    allowedForRace.Add(cc);
                break;
        }

        // Remove any classes that are not permitted for this race
        list.RemoveAll(c => !allowedForRace.Contains(c));

        return list;
    }


    // Return allowed multiclass combinations for the race, filtered by ability-allowed classes.
    // Each multiclass option is a list of two or three CharacterClass values.
    public static List<List<CharacterClass>> GetAllowedMulticlasses(Race race, List<CharacterClass> allowedByAbilities)
    {
        var result = new List<List<CharacterClass>>();

        bool IsAllowed(params CharacterClass[] classes)
        {
            foreach (var c in classes)
                if (!allowedByAbilities.Contains(c))
                    return false;
            return true;
        }

        switch (race)
        {
            case Race.Elf:
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.MagicUser))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.MagicUser });
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Thief });
                if (IsAllowed(CharacterClass.MagicUser, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.MagicUser, CharacterClass.Thief });
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.MagicUser, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.MagicUser, CharacterClass.Thief });
                break;

            case Race.HalfElf:
                // Half-elf options include the common cleric, fighter, magic-user, thief, and ranger pairings.
                if (IsAllowed(CharacterClass.Cleric, CharacterClass.Fighter))
                    result.Add(new List<CharacterClass> { CharacterClass.Cleric, CharacterClass.Fighter });
                if (IsAllowed(CharacterClass.Cleric, CharacterClass.MagicUser))
                    result.Add(new List<CharacterClass> { CharacterClass.Cleric, CharacterClass.MagicUser });
                if (IsAllowed(CharacterClass.Cleric, CharacterClass.Ranger))
                    result.Add(new List<CharacterClass> { CharacterClass.Cleric, CharacterClass.Ranger });
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.MagicUser))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.MagicUser });
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Thief });
                if (IsAllowed(CharacterClass.MagicUser, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.MagicUser, CharacterClass.Thief });
                // The fighter/magic-user/thief triple is retained as an explicit half-elf option.
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.MagicUser, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.MagicUser, CharacterClass.Thief });
                break;

            case Race.Dwarf:
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Thief });
                break;

            case Race.Halfling:
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Thief });
                break;

            case Race.Gnome:
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Illusionist))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Illusionist });
                if (IsAllowed(CharacterClass.Illusionist, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Illusionist, CharacterClass.Thief });
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Illusionist, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Illusionist, CharacterClass.Thief });
                break;

            case Race.HalfOrc:
                // Optional UA: Half-orc multiclass combinations
                // Keep to these listed pairings for this ruleset.
                if (IsAllowed(CharacterClass.Cleric, CharacterClass.Fighter))
                    result.Add(new List<CharacterClass> { CharacterClass.Cleric, CharacterClass.Fighter });
                if (IsAllowed(CharacterClass.Cleric, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Cleric, CharacterClass.Thief });
                if (IsAllowed(CharacterClass.Fighter, CharacterClass.Thief))
                    result.Add(new List<CharacterClass> { CharacterClass.Fighter, CharacterClass.Thief });
                break;

            case Race.Human:
            default:
                // Humans do not multiclass in AD&D 1e in this implementation
                break;
        }

        return result;
    }
}
