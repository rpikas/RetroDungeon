using Adnd.Core.Characters;

namespace Adnd.Core.Spells;

public static class SpellProgression
{
    public static List<(SpellClass SpellClass, List<int> SlotsPerDay)> GetSpellcastingTracks(CharacterClass characterClass, int level)
    {
        var tracks = new List<(SpellClass SpellClass, List<int> SlotsPerDay)>();

        switch (characterClass)
        {
            case CharacterClass.MagicUser:
                tracks.Add((SpellClass.MagicUser, GetSlotsFromTable(MagicUserSlots, SpellClass.MagicUser, level)));
                break;
            case CharacterClass.Illusionist:
                tracks.Add((SpellClass.Illusionist, GetSlotsFromTable(IllusionistSlots, SpellClass.Illusionist, level)));
                break;
            case CharacterClass.Cleric:
                tracks.Add((SpellClass.Cleric, GetSlotsFromTable(ClericSlots, SpellClass.Cleric, level)));
                break;
            case CharacterClass.Druid:
                tracks.Add((SpellClass.Druid, GetSlotsFromTable(DruidSlots, SpellClass.Druid, level)));
                break;
            case CharacterClass.Ranger:
                // AD&D 1E: Ranger gets MU spells from level 8 and Cleric spells from level 9.
                tracks.Add((SpellClass.MagicUser, GetSlotsFromTable(RangerMagicUserSlots, SpellClass.MagicUser, level)));
                tracks.Add((SpellClass.Cleric, GetSlotsFromTable(RangerClericSlots, SpellClass.Cleric, level)));
                break;
            case CharacterClass.Paladin:
                // AD&D 1E: Paladin first spells at level 9.
                tracks.Add((SpellClass.Cleric, GetSlotsFromTable(PaladinClericSlots, SpellClass.Cleric, level)));
                break;
            case CharacterClass.Bard:
                // AD&D 1E: Bard gets druid spells from level 8.
                tracks.Add((SpellClass.Druid, GetSlotsFromTable(BardDruidSlots, SpellClass.Druid, level)));
                break;
        }

        return tracks;
    }

    public static bool TryGetSpellClass(CharacterClass characterClass, out SpellClass spellClass)
    {
        switch (characterClass)
        {
            case CharacterClass.MagicUser:
                spellClass = SpellClass.MagicUser;
                return true;
            case CharacterClass.Illusionist:
                spellClass = SpellClass.Illusionist;
                return true;
            case CharacterClass.Cleric:
                spellClass = SpellClass.Cleric;
                return true;
            case CharacterClass.Druid:
                spellClass = SpellClass.Druid;
                return true;
            default:
                spellClass = default;
                return false;
        }
    }

    public static int GetMaxSpellLevel(SpellClass spellClass)
    {
        return spellClass == SpellClass.MagicUser ? 9 : 7;
    }

    public static List<int> GetSlotsPerDay(SpellClass spellClass, int level)
    {
        if (level < 1)
            level = 1;

        var table = spellClass switch
        {
            SpellClass.MagicUser => MagicUserSlots,
            SpellClass.Illusionist => IllusionistSlots,
            SpellClass.Cleric => ClericSlots,
            SpellClass.Druid => DruidSlots,
            _ => MagicUserSlots
        };

        var row = table.TryGetValue(level, out var exact)
            ? exact
            : table[table.Keys.Max()];

        return new List<int>(row);
    }

    private static List<int> GetSlotsFromTable(Dictionary<int, int[]> table, SpellClass spellClass, int level)
    {
        if (level < 1)
            level = 1;

        if (table.Count == 0)
            return Enumerable.Repeat(0, GetMaxSpellLevel(spellClass)).ToList();

        var minLevel = table.Keys.Min();
        if (level < minLevel)
            return Enumerable.Repeat(0, GetMaxSpellLevel(spellClass)).ToList();

        var row = table.TryGetValue(level, out var exact)
            ? exact
            : table[table.Keys.Max()];

        return new List<int>(row);
    }

    private static readonly Dictionary<int, int[]> MagicUserSlots = new()
{
    { 1,  new[] {1,0,0,0,0,0,0,0,0} },
    { 2,  new[] {2,0,0,0,0,0,0,0,0} },
    { 3,  new[] {2,1,0,0,0,0,0,0,0} },
    { 4,  new[] {3,2,0,0,0,0,0,0,0} },
    { 5,  new[] {4,2,1,0,0,0,0,0,0} },
    { 6,  new[] {4,2,2,0,0,0,0,0,0} },
    { 7,  new[] {4,3,2,1,0,0,0,0,0} },
    { 8,  new[] {4,3,3,2,0,0,0,0,0} },
    { 9,  new[] {4,3,3,2,1,0,0,0,0} },
    { 10, new[] {4,4,3,2,2,0,0,0,0} },
    { 11, new[] {4,4,4,3,2,1,0,0,0} },
    { 12, new[] {4,4,4,3,3,2,0,0,0} },
    { 13, new[] {4,4,4,4,3,2,1,0,0} },
    { 14, new[] {4,4,4,4,3,3,2,0,0} },
    { 15, new[] {4,4,4,4,4,3,2,1,0} },
    { 16, new[] {4,4,4,4,4,3,3,2,0} },
    { 17, new[] {4,4,4,4,4,4,3,2,1} },
    { 18, new[] {4,4,4,4,4,4,3,3,2} },
    { 19, new[] {4,4,4,4,4,4,4,3,2} },
    { 20, new[] {4,4,4,4,4,4,4,3,3} },

    // DMG High-Level Progression (21–29)
    { 21, new[] {5,4,4,4,4,4,4,3,3} },
    { 22, new[] {5,5,4,4,4,4,4,3,3} },
    { 23, new[] {5,5,5,4,4,4,4,3,3} },
    { 24, new[] {5,5,5,5,4,4,4,3,3} },
    { 25, new[] {5,5,5,5,5,4,4,3,3} },
    { 26, new[] {5,5,5,5,5,5,4,3,3} },
    { 27, new[] {5,5,5,5,5,5,5,3,3} },
    { 28, new[] {5,5,5,5,5,5,5,4,3} },
    { 29, new[] {5,5,5,5,5,5,5,4,4} },
   
    // Levels 30–40 (hard cap)
};

    private static readonly Dictionary<int, int[]> IllusionistSlots = new()
{
    { 1,  new[] {1,0,0,0,0,0,0} },
    { 2,  new[] {2,0,0,0,0,0,0} },
    { 3,  new[] {2,1,0,0,0,0,0} },
    { 4,  new[] {3,2,0,0,0,0,0} },
    { 5,  new[] {4,2,1,0,0,0,0} },
    { 6,  new[] {4,2,2,0,0,0,0} },
    { 7,  new[] {4,3,2,1,0,0,0} },
    { 8,  new[] {4,3,3,2,0,0,0} },
    { 9,  new[] {4,3,3,2,1,0,0} },
    { 10, new[] {4,4,3,2,2,0,0} },
    { 11, new[] {4,4,4,3,2,1,0} },
    { 12, new[] {4,4,4,3,3,2,0} },
    { 13, new[] {4,4,4,4,3,2,1} },
    { 14, new[] {5,4,4,4,3,2,1} },

    // Levels 15–40 (Illusionist progression caps at level 14)
};

    private static readonly Dictionary<int, int[]> ClericSlots = new()
{
    { 1,  new[] {1,0,0,0,0,0,0} },
    { 2,  new[] {2,0,0,0,0,0,0} },
    { 3,  new[] {2,1,0,0,0,0,0} },
    { 4,  new[] {3,2,0,0,0,0,0} },
    { 5,  new[] {3,3,1,0,0,0,0} },
    { 6,  new[] {3,3,2,0,0,0,0} },
    { 7,  new[] {3,3,2,1,0,0,0} },
    { 8,  new[] {3,3,3,2,0,0,0} },
    { 9,  new[] {4,3,3,2,1,0,0} },
    { 10, new[] {4,4,3,2,2,0,0} },
    { 11, new[] {5,4,3,2,2,1,0} },
    { 12, new[] {5,4,4,3,2,2,0} },
    { 13, new[] {6,4,4,3,3,2,1} },
    { 14, new[] {6,5,4,3,3,2,1} },
    { 15, new[] {6,5,5,4,3,3,2} },
    { 16, new[] {6,6,5,4,4,3,2} },
    { 17, new[] {7,6,5,4,4,3,2} },
    { 18, new[] {7,6,6,5,4,4,3} },
    { 19, new[] {7,7,6,5,5,4,3} },
    { 20, new[] {7,7,7,6,5,5,4} },

    // DMG High-Level Cleric progression (21–29)
    { 21, new[] {8,7,7,6,5,5,4} },
    { 22, new[] {8,8,7,6,5,5,4} },
    { 23, new[] {8,8,8,6,5,5,4} },
    { 24, new[] {8,8,8,7,5,5,4} },
    { 25, new[] {8,8,8,7,6,5,4} },
    { 26, new[] {8,8,8,7,6,6,4} },
    { 27, new[] {8,8,8,7,6,6,5} },
    { 28, new[] {8,8,8,7,6,6,5} },
    { 29, new[] {8,8,8,7,6,6,5} },

    // Level 30 = capped at 29
    { 30, new[] {8,8,8,7,6,6,5} }
};

    private static readonly Dictionary<int, int[]> DruidSlots = new()
{
    { 1,  new[] {1,0,0,0,0,0,0} },
    { 2,  new[] {2,0,0,0,0,0,0} },
    { 3,  new[] {2,1,0,0,0,0,0} },
    { 4,  new[] {2,2,0,0,0,0,0} },
    { 5,  new[] {3,2,1,0,0,0,0} },
    { 6,  new[] {3,3,2,0,0,0,0} },
    { 7,  new[] {3,3,2,1,0,0,0} },
    { 8,  new[] {3,3,3,2,0,0,0} },
    { 9,  new[] {4,3,3,2,1,0,0} },
    { 10, new[] {4,4,3,2,2,0,0} },
    { 11, new[] {5,4,3,2,2,1,0} },
    { 12, new[] {5,4,4,3,2,2,0} },
    { 13, new[] {6,4,4,3,3,2,1} },
    { 14, new[] {6,5,4,3,3,2,1} },
    { 15, new[] {6,5,5,4,3,3,2} },
    { 16, new[] {6,6,5,4,4,3,2} },
    { 17, new[] {7,6,5,4,4,3,2} },
    { 18, new[] {7,6,6,5,4,4,3} },
    { 19, new[] {7,7,6,5,5,4,3} },
    { 20, new[] {7,7,7,6,5,5,4} },

    // DMG High-Level Druid progression (21–29)
    { 21, new[] {7,7,7,6,5,5,4} },

};


    // Ranger gets Magic-User spells from level 8
    private static readonly Dictionary<int, int[]> RangerMagicUserSlots = new()
{
    { 8,  new[] {1,0,0,0,0,0,0,0,0} },
    { 9,  new[] {2,0,0,0,0,0,0,0,0} },
    { 10, new[] {2,1,0,0,0,0,0,0,0} },
    { 11, new[] {2,1,1,0,0,0,0,0,0} },
    { 12, new[] {2,2,1,0,0,0,0,0,0} },
    { 13, new[] {2,2,1,1,0,0,0,0,0} },
    { 14, new[] {3,2,1,1,0,0,0,0,0} },
    { 15, new[] {3,2,2,1,0,0,0,0,0} },
    { 16, new[] {3,3,2,1,0,0,0,0,0} },

    // 17–30 = capped
    { 17, new[] {3,3,2,1,0,0,0,0,0} },
};


    // Ranger gets Cleric spells from level 9
    private static readonly Dictionary<int, int[]> RangerClericSlots = new()
{
    { 9,  new[] {1,0,0,0,0,0,0} },
    { 10, new[] {2,0,0,0,0,0,0} },
    { 11, new[] {2,1,0,0,0,0,0} },
    { 12, new[] {2,1,1,0,0,0,0} },
    { 13, new[] {2,2,1,0,0,0,0} },
    { 14, new[] {3,2,1,0,0,0,0} },
    { 15, new[] {3,2,2,0,0,0,0} },
    { 16, new[] {3,3,2,0,0,0,0} },
    { 17, new[] {3,3,2,1,0,0,0} },

    // 18–30 = capped
    { 18, new[] {3,3,2,1,0,0,0} },
};


    // Paladin first spells at level 9
    private static readonly Dictionary<int, int[]> PaladinClericSlots = new()
{
    { 9,  new[] {1,0,0,0,0,0,0} },
    { 10, new[] {1,1,0,0,0,0,0} },
    { 11, new[] {2,1,0,0,0,0,0} },
    { 12, new[] {2,1,1,0,0,0,0} },
    { 13, new[] {2,2,1,0,0,0,0} },
    { 14, new[] {3,2,1,0,0,0,0} },
    { 15, new[] {3,2,2,0,0,0,0} },
    { 16, new[] {3,3,2,0,0,0,0} },
    { 17, new[] {3,3,2,1,0,0,0} },

    // 18–30 = capped
    { 18, new[] {3,3,2,1,0,0,0} },

};


    // Bard gets druid spells from level 8

    private static readonly Dictionary<int, int[]> BardDruidSlots = new()
{
    { 8,  new[] {1,0,0,0,0,0,0} },
    { 9,  new[] {2,0,0,0,0,0,0} },
    { 10, new[] {2,1,0,0,0,0,0} },
    { 11, new[] {2,1,1,0,0,0,0} },
    { 12, new[] {2,2,1,0,0,0,0} },
    { 13, new[] {3,2,1,0,0,0,0} },
    { 14, new[] {3,2,2,0,0,0,0} },
    { 15, new[] {3,3,2,0,0,0,0} },
    { 16, new[] {3,3,2,1,0,0,0} },

    // 17–30 = capped
    { 17, new[] {3,3,2,1,0,0,0} }
};
}
