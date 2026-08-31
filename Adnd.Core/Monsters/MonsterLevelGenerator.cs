using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.X86;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Monsters;

/// <summary>
/// This class generates a monster level based on the dungeon level and a random roll.
/* AI instructions: Paste in page 174 DMG table picture did not work, reformated to text first
I want a c-sharp method that takes as input level of dungeon.
Rolls 1d20 and returns level of monster that uses this tabel:
so for example if dungeon is level 1, roll 1-16 level of monster is 1,
roll 17-19 level of monster is 2, roll 20 then level of monster is 3.
but if dungeon level is for example 7, rolling 1 will return level 1 monster,
rolling 2-3 will return level 2 monsters.rolling 20 will return a level 9 monster.
When determining what level the monster should be, use this method: RollMonsterLevel
Equivalent Level Of The Dungeon   I       II      III     IV      V       VI      VII     VIII    IX      X
--------------------------------------------------------------------------------------------------------------
1st                               1–16    17–19   20      —       —       —       —       —       —       —
2nd–3rd                           1–12    13–16   17–18   19      20      —       —       —       —       —
4th                               1–5     6–10    11–16   17–18   19      20      —       —       —       —
5th                               1–3     4–6     7–12    13–16   17–18   19      20      —       —       —
6th                               1–2     3–4     5–6     7–12    13–16   17–18   19      20      —       —
7th                               1       2–3     4–5     6–10    11–14   15–16   17–18   19      20      —
8th                               1       2       3–4     5–7     8–10    11–14   15–16   17–18   19      20
9th                               1       2       3       4–5     6–8     9–12    13–16   17–18   19      20
10th–11th                         1       2       3       4       5–6     7–9     10–12   13–16   17–19   20
12th–13th                         1       2       3       4       5       6–7     8–9     10–12   13–18   19–20
14th–15th                         1       2       3       4       5       6       7–8     9–11    12–18   19–20
16th & down                       1       2       3       4       5       6       7       8–10    11–17   18–20


*/
/// </summary>

public static class MonsterLevelGenerator
{
    public record Range(int Min, int Max, int MonsterLevel);

    private static readonly Dictionary<int, List<Range>> Table =
        new()
        {
            [1] = new()
            {
                new Range(1,16,1),
                new Range(17,19,2),
                new Range(20,20,3)
            },

            [2] = new()
            {
                new Range(1,12,1),
                new Range(13,16,2),
                new Range(17,18,3),
                new Range(19,19,4),
                new Range(20,20,5)
            },

            [3] = new()
            {
                new Range(1,12,1),
                new Range(13,16,2),
                new Range(17,18,3),
                new Range(19,19,4),
                new Range(20,20,5)
            },

            [4] = new()
            {
                new Range(1,5,1),
                new Range(6,10,2),
                new Range(11,16,3),
                new Range(17,18,4),
                new Range(19,19,5),
                new Range(20,20,6)
            },

            [5] = new()
            {
                new Range(1,3,1),
                new Range(4,6,2),
                new Range(7,12,3),
                new Range(13,16,4),
                new Range(17,18,5),
                new Range(19,19,6),
                new Range(20,20,7)
            },

            [6] = new()
            {
                new Range(1,2,1),
                new Range(3,4,2),
                new Range(5,6,3),
                new Range(7,12,4),
                new Range(13,16,5),
                new Range(17,18,6),
                new Range(19,19,7),
                new Range(20,20,8)
            },

            [7] = new()
            {
                new Range(1,1,1),
                new Range(2,3,2),
                new Range(4,5,3),
                new Range(6,10,4),
                new Range(11,14,5),
                new Range(15,16,6),
                new Range(17,18,7),
                new Range(19,19,8),
                new Range(20,20,9)
            },

            [8] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,4,3),
                new Range(5,7,4),
                new Range(8,10,5),
                new Range(11,14,6),
                new Range(15,16,7),
                new Range(17,18,8),
                new Range(19,19,9),
                new Range(20,20,10)
            },

            [9] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,5,4),
                new Range(6,8,5),
                new Range(9,12,6),
                new Range(13,16,7),
                new Range(17,18,8),
                new Range(19,19,9),
                new Range(20,20,10)
            },

            [10] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,6,5),
                new Range(7,9,6),
                new Range(10,12,7),
                new Range(13,16,8),
                new Range(17,19,9),
                new Range(20,20,10)
            },

            [11] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,6,5),
                new Range(7,9,6),
                new Range(10,12,7),
                new Range(13,16,8),
                new Range(17,19,9),
                new Range(20,20,10)
            },

            [12] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,5,5),
                new Range(6,7,6),
                new Range(8,9,7),
                new Range(10,12,8),
                new Range(13,18,9),
                new Range(19,20,10)
            },

            [13] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,5,5),
                new Range(6,7,6),
                new Range(8,9,7),
                new Range(10,12,8),
                new Range(13,18,9),
                new Range(19,20,10)
            },

            [14] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,5,5),
                new Range(6,6,6),
                new Range(7,8,7),
                new Range(9,11,8),
                new Range(12,18,9),
                new Range(19,20,10)
            },

            [15] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,5,5),
                new Range(6,6,6),
                new Range(7,8,7),
                new Range(9,11,8),
                new Range(12,18,9),
                new Range(19,20,10)
            },

            [16] = new()
            {
                new Range(1,1,1),
                new Range(2,2,2),
                new Range(3,3,3),
                new Range(4,4,4),
                new Range(5,5,5),
                new Range(6,6,6),
                new Range(7,7,7),
                new Range(8,10,8),
                new Range(11,17,9),
                new Range(18,20,10)
            }
        };

    public static int RollMonsterLevel(int dungeonLevel)
    {
        if (!Table.ContainsKey(dungeonLevel))
            throw new ArgumentException("Dungeon level must be between 1 and 16.");
        int sidesOnDice = 20;
        int roll = Random.Shared.Next(1, sidesOnDice + 1); // 1d20

        foreach (var r in Table[dungeonLevel])
        {
            if (roll >= r.Min && roll <= r.Max)
            {
                //Publish( source,  page,  context,   rule,  numberOfDices,  sidesOnDices,  resultOfRoll, string consequenceOfRoll)

                RuleApplicationInfo.Publish("DMG", "174", "Roll to determine the monster level", MonsterLevelRuleDecription(dungeonLevel), 
                    "1", sidesOnDice.ToString(), roll.ToString(), $"Monster level {r.MonsterLevel}");
                return r.MonsterLevel;
            }
        }

        throw new InvalidOperationException("No monster level matched the roll.");
    }

    private static string MonsterLevelRuleDecription(int dungeonLevel)
    {
        if (!Table.TryGetValue(dungeonLevel, out var ranges) || ranges.Count == 0)
            return $"No rule found for dungeon level {dungeonLevel}.";

        static string FormatRange(Range range)
        {
            var interval = range.Min == range.Max
                ? range.Min.ToString()
                : $"{range.Min}-{range.Max}";

            return $"{interval}>Level {range.MonsterLevel}";
        }

        if (ranges.Count == 1)
            return FormatRange(ranges[0]);

        if (ranges.Count == 2)
            return $"{FormatRange(ranges[0])}. {FormatRange(ranges[1])}";

        var firstPart = string.Join(", ", ranges.Take(ranges.Count - 1).Select(FormatRange));
        var lastPart = FormatRange(ranges[^1]);
        return $"{firstPart}. {lastPart}";
    }
}



