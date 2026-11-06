using Adnd.Core.Characters;

namespace Adnd.Core.Wizardry;

public enum WizardryClass
{
    Fighter,
    Mage,
    Priest,
    Thief,
    Bishop,
    Samurai,
    Lord,
    Ninja
}

public enum WizardryAlignment
{
    Good,
    Neutral,
    Evil
}

public readonly record struct WizardryStats(int Strength, int Iq, int Piety, int Vitality, int Agility, int Luck)
{
    public int Total => Strength + Iq + Piety + Vitality + Agility + Luck;
}

/// <summary>Wizardry character data: race base stats, class requirements, XP tables and creation rolls.</summary>
public static class WizardryRules
{
    public const int MaxStat = 18;
    // Level 1 characters always start with 8 HP whatever the class; hit dice apply from level 2.
    public const int StartingHitPoints = 8;
    public const int MinStartingAge = 18;
    public const int StartingAgeSpread = 6;
    public const int StartingGoldBase = 89;
    public const int StartingGoldDie = 100;

    public static readonly Race[] Races = { Race.Human, Race.Elf, Race.Dwarf, Race.Gnome, Race.Halfling };

    // Halfling is the Wizardry hobbit.
    public static string RaceName(Race race) => race == Race.Halfling ? "Hobbit" : race.ToString();

    public static WizardryStats RaceBaseStats(Race race) => race switch
    {
        Race.Human => new WizardryStats(8, 8, 5, 8, 8, 9),
        Race.Elf => new WizardryStats(7, 10, 10, 6, 9, 6),
        Race.Dwarf => new WizardryStats(10, 7, 10, 10, 5, 6),
        Race.Gnome => new WizardryStats(7, 7, 10, 8, 10, 7),
        Race.Halfling => new WizardryStats(5, 7, 7, 6, 10, 12),
        _ => throw new ArgumentOutOfRangeException(nameof(race), race, "Not a Wizardry race.")
    };

    // 0 means no minimum for that stat.
    public static WizardryStats ClassMinimums(WizardryClass cls) => cls switch
    {
        WizardryClass.Priest => new WizardryStats(0, 0, 11, 0, 0, 0),
        WizardryClass.Fighter => new WizardryStats(11, 0, 0, 0, 0, 0),
        WizardryClass.Lord => new WizardryStats(15, 12, 12, 15, 14, 15),
        WizardryClass.Mage => new WizardryStats(0, 11, 0, 0, 0, 0),
        WizardryClass.Ninja => new WizardryStats(17, 17, 17, 17, 17, 17),
        WizardryClass.Samurai => new WizardryStats(15, 11, 10, 14, 10, 0),
        WizardryClass.Thief => new WizardryStats(0, 0, 0, 0, 11, 0),
        WizardryClass.Bishop => new WizardryStats(0, 12, 12, 0, 0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(cls))
    };

    public static bool AlignmentAllowed(WizardryClass cls, WizardryAlignment alignment) => cls switch
    {
        WizardryClass.Priest or WizardryClass.Bishop => alignment != WizardryAlignment.Neutral,
        WizardryClass.Lord => alignment == WizardryAlignment.Good,
        WizardryClass.Ninja => alignment == WizardryAlignment.Evil,
        WizardryClass.Samurai => alignment != WizardryAlignment.Evil,
        WizardryClass.Thief => alignment != WizardryAlignment.Good,
        _ => true
    };

    public static bool MeetsStatRequirements(WizardryClass cls, WizardryStats stats)
    {
        var min = ClassMinimums(cls);
        return stats.Strength >= min.Strength
            && stats.Iq >= min.Iq
            && stats.Piety >= min.Piety
            && stats.Vitality >= min.Vitality
            && stats.Agility >= min.Agility
            && stats.Luck >= min.Luck;
    }

    public static string AlignmentText(WizardryClass cls) => cls switch
    {
        WizardryClass.Priest or WizardryClass.Bishop => "Good or Evil",
        WizardryClass.Lord => "Good",
        WizardryClass.Ninja => "Evil",
        WizardryClass.Samurai => "Good or Neutral",
        WizardryClass.Thief => "Neutral or Evil",
        _ => "Any"
    };

    public static bool MeetsRequirements(WizardryClass cls, WizardryStats stats, WizardryAlignment alignment)
        => MeetsStatRequirements(cls, stats) && AlignmentAllowed(cls, alignment);

    public static IEnumerable<WizardryClass> EligibleClasses(WizardryStats stats, WizardryAlignment alignment)
        => Enum.GetValues<WizardryClass>().Where(c => MeetsRequirements(c, stats, alignment));

    // XP needed to advance from level 1..12 to the next level; index 0 is level 1.
    private static readonly Dictionary<WizardryClass, int[]> XpToAdvance = new()
    {
        [WizardryClass.Priest] = new[] { 1050, 1810, 3120, 5379, 9274, 15989, 27567, 47529, 81946, 141286, 243596, 419993 },
        [WizardryClass.Fighter] = new[] { 1000, 1724, 2972, 5124, 8834, 15231, 26260, 45275, 78060, 134586, 232044, 400075 },
        [WizardryClass.Lord] = new[] { 1300, 2280, 4000, 7017, 12310, 21596, 37887, 66468, 116610, 204578, 358908, 629663 },
        [WizardryClass.Mage] = new[] { 1100, 1896, 3268, 5634, 9713, 16746, 28872, 49779, 85825, 147974, 255127, 439874 },
        [WizardryClass.Ninja] = new[] { 1450, 2543, 4461, 7826, 13729, 24085, 42254, 74129, 130050, 228157, 400275, 702236 },
        [WizardryClass.Samurai] = new[] { 1200, 2105, 3677, 6477, 11363, 19935, 34973, 61356, 107642, 188845, 331307, 581240 },
        [WizardryClass.Thief] = new[] { 900, 1551, 2674, 4610, 7948, 13703, 23625, 40732, 70227, 121081, 208760, 359931 },
        [WizardryClass.Bishop] = new[] { 1200, 2105, 3677, 6477, 11363, 19935, 34973, 61356, 107642, 188845, 331307, 581240 }
    };

    // XP needed for every level from 13 onward.
    private static readonly Dictionary<WizardryClass, int> XpPerLevelBeyond12 = new()
    {
        [WizardryClass.Priest] = 304132,
        [WizardryClass.Fighter] = 289709,
        [WizardryClass.Lord] = 475008,
        [WizardryClass.Mage] = 318529,
        [WizardryClass.Ninja] = 529756,
        [WizardryClass.Samurai] = 428479,
        [WizardryClass.Thief] = 260639,
        [WizardryClass.Bishop] = 428479
    };

    /// <summary>XP the character must earn while at <paramref name="level"/> to reach the next level.</summary>
    public static int XpToNextLevel(WizardryClass cls, int level)
    {
        if (level < 1)
            level = 1;

        var table = XpToAdvance[cls];
        return level <= table.Length ? table[level - 1] : XpPerLevelBeyond12[cls];
    }

    /// <summary>Total XP at which a character of the class reaches <paramref name="level"/>.</summary>
    public static long TotalXpForLevel(WizardryClass cls, int level)
    {
        long total = 0;
        for (var l = 1; l < level; l++)
            total += XpToNextLevel(cls, l);

        return total;
    }

    public static int LevelForXp(WizardryClass cls, long xp)
    {
        var level = 1;
        while (xp >= TotalXpForLevel(cls, level + 1))
            level++;

        return level;
    }

    public static int HitDieSides(WizardryClass cls) => cls switch
    {
        WizardryClass.Fighter or WizardryClass.Lord => 10,
        WizardryClass.Priest or WizardryClass.Samurai => 8,
        WizardryClass.Thief or WizardryClass.Bishop or WizardryClass.Ninja => 6,
        _ => 4
    };

    /// <summary>Hit dice rolled when re-rolling max HP at the given level; samurai roll one extra.</summary>
    public static int HitDiceAtLevel(WizardryClass cls, int level) => cls == WizardryClass.Samurai ? level + 1 : level;

    /// <summary>Per-die hit point modifier from vitality.</summary>
    public static int VitalityHitPointModifier(int vitality) => vitality switch
    {
        <= 3 => -2,
        4 or 5 => -1,
        16 => 1,
        17 => 2,
        >= 18 => 3,
        _ => 0
    };

    /// <summary>Bonus points to distribute at creation: 1d4+6, with up to two 1-in-11 chances of +10 (second only while under 20).</summary>
    public static int RollBonusPoints(Random random)
    {
        var points = random.Next(1, 5) + 6;
        if (random.Next(11) == 0)
        {
            points += 10;
            if (points < 20 && random.Next(11) == 0)
                points += 10;
        }

        return points;
    }

    public static int RollStartingGold(Random random) => random.Next(1, StartingGoldDie + 1) + StartingGoldBase;
}
