namespace Adnd.Core.Characters;

public static class RaceClassLevelLimits
{
    public static int GetMaxLevel(Character character, CharacterClass cls)
    {
        if (character == null)
            return int.MaxValue;

        var abilities = character.Abilities ?? new AbilityScores();

        if (character.Race == Race.Human)
            return int.MaxValue;

        return character.Race switch
        {
            Race.Dwarf => GetDwarfMaxLevel(cls, abilities),
            Race.Elf => GetElfMaxLevel(cls, abilities),
            Race.Gnome => GetGnomeMaxLevel(cls, abilities),
            Race.HalfElf => GetHalfElfMaxLevel(cls, abilities),
            Race.Halfling => GetHalflingMaxLevel(cls, abilities),
            Race.HalfOrc => GetHalfOrcMaxLevel(cls, abilities),
            _ => int.MaxValue
        };
    }

    public static int ApplyCap(Character character, CharacterClass cls, int level)
    {
        var capped = Math.Max(1, level);
        var max = GetMaxLevel(character, cls);
        if (max == int.MaxValue)
            return capped;

        return Math.Min(capped, Math.Max(1, max));
    }

    private static int GetDwarfMaxLevel(CharacterClass cls, AbilityScores a)
    {
        return cls switch
        {
            CharacterClass.Cleric => 8,
            CharacterClass.Fighter => a.Strength >= 18 ? 9 : a.Strength >= 17 ? 8 : 7,
            CharacterClass.Assassin => 9,
            CharacterClass.Thief => int.MaxValue,
            _ => 1
        };
    }

    private static int GetElfMaxLevel(CharacterClass cls, AbilityScores a)
    {
        return cls switch
        {
            CharacterClass.Cleric => 7,
            CharacterClass.Fighter => a.Strength >= 18 ? 7 : a.Strength >= 17 ? 6 : 5,
            CharacterClass.MagicUser => a.Intelligence >= 18 ? 11 : a.Intelligence >= 17 ? 10 : 9,
            CharacterClass.Assassin => 10,
            CharacterClass.Thief => int.MaxValue,
            CharacterClass.Illusionist => int.MaxValue,
            _ => 1
        };
    }

    private static int GetGnomeMaxLevel(CharacterClass cls, AbilityScores a)
    {
        return cls switch
        {
            CharacterClass.Cleric => 7,
            CharacterClass.Fighter => a.Strength >= 18 ? 6 : 5,
            CharacterClass.Assassin => 8,
            CharacterClass.Thief => int.MaxValue,
            CharacterClass.Illusionist => a.Intelligence >= 18 && a.Dexterity >= 18
                ? 7
                : a.Intelligence >= 17 && a.Dexterity >= 17
                    ? 6
                    : 5,
            _ => 1
        };
    }

    private static int GetHalfElfMaxLevel(CharacterClass cls, AbilityScores a)
    {
        return cls switch
        {
            CharacterClass.Cleric => 5,
            CharacterClass.Druid => int.MaxValue,
            CharacterClass.Fighter => a.Strength >= 18 ? 8 : a.Strength >= 17 ? 7 : 6,
            CharacterClass.Ranger => 8,
            CharacterClass.MagicUser => a.Intelligence >= 18 ? 8 : a.Intelligence >= 17 ? 7 : 6,
            CharacterClass.Assassin => 11,
            CharacterClass.Thief => int.MaxValue,
            _ => 1
        };
    }

    private static int GetHalflingMaxLevel(CharacterClass cls, AbilityScores a)
    {
        return cls switch
        {
            CharacterClass.Fighter => a.Strength >= 18 ? 6 : a.Strength >= 17 ? 5 : 4,
            CharacterClass.Thief => int.MaxValue,
            _ => 1
        };
    }

    private static int GetHalfOrcMaxLevel(CharacterClass cls, AbilityScores a)
    {
        return cls switch
        {
            CharacterClass.Cleric => 4,
            CharacterClass.Fighter => 10,
            CharacterClass.Assassin => int.MaxValue,
            CharacterClass.Thief => a.Dexterity >= 18 ? 8 : a.Dexterity >= 17 ? 7 : 6,
            _ => 1
        };
    }
}
