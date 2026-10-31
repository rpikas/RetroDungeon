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

// Rolling what is encountered: DMG tables, dragons, characters and source filtering.
public sealed partial class MazeForm
{
    private int ResolveEncounterGroupCount(string monsterName, int? tableCount)
    {
        if (tableCount.HasValue && tableCount.Value > 0)
            return tableCount.Value;

        var monster = FindMonsterByName(_monsterRepository.GetAll(), monsterName);
        if (monster != null)
        {
            var min = Math.Max(1, monster.NumberOfAppearancesMin);
            var max = Math.Max(1, monster.NumberOfAppearancesMax);
            if (max < min)
                (min, max) = (max, min);

            var rolled = _random.Next(min, max + 1);
            RuleApplicationInfo.Publish(
                "Adnd",
                "Monster data",
                $"Roll count for {monsterName}",
                "When MonsterLevels CountMin/CountMax is unavailable, roll from monster NumberOfAppearancesMin-NumberOfAppearancesMax.",
                "1",
                (max - min + 1).ToString(),
                (rolled - min + 1).ToString(),
                $"{monsterName} count {rolled} (range {min}-{max}).");
            return rolled;
        }

        var fallback = _random.Next(1, 7);
        RuleApplicationInfo.Publish(
            "Adnd",
            "Fallback",
            $"Roll count for {monsterName}",
            "No monster template found; fallback count roll 1d6.",
            "1",
            "6",
            fallback.ToString(),
            $"{monsterName} count {fallback}.");
        return fallback;
    }

    private static List<int>? ResolveDragonHitPointsPerDieProfile(string monsterName, int encounterMonsterLevel, int monsterCount)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return null;

        if (!monsterName.Contains("Dragon", StringComparison.OrdinalIgnoreCase))
            return null;

        if (monsterCount <= 0)
            return null;

        if (encounterMonsterLevel == 9 && monsterCount >= 2)
        {
            if (monsterName.Equals("Black Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Brass Dragon", StringComparison.OrdinalIgnoreCase))
            {
                return new List<int> { 8, 6 };
            }

            if (monsterName.Equals("White Dragon", StringComparison.OrdinalIgnoreCase))
                return new List<int> { 8, 7 };
        }

        if (encounterMonsterLevel == 10 && monsterCount >= 2)
        {
            if (monsterName.Equals("Gold Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Red Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Silver Dragon", StringComparison.OrdinalIgnoreCase))
            {
                return new List<int> { 8, 6 };
            }

            if (monsterName.Equals("Blue Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Bronze Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Copper Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Green Dragon", StringComparison.OrdinalIgnoreCase))
            {
                return new List<int> { 8, 7 };
            }
        }

        return null;
    }

    private EncounterRoll? RollDungeonMonsterForLevelWithCountExcludingCharacter(int level)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var rolled = RollDungeonMonsterForLevelWithCount(level);
            if (rolled == null || string.IsNullOrWhiteSpace(rolled.MonsterName))
                continue;

            if (string.Equals(rolled.MonsterName, "No Encounter", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(rolled.MonsterName, "Adventurer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rolled.MonsterName, "Character", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return rolled;
        }

        return null;
    }

    private static string FormatEncounterMoralAxis(EncounterMoralAxis axis)
    {
        return axis switch
        {
            EncounterMoralAxis.Good => "Good",
            EncounterMoralAxis.Neutral => "Neutral",
            EncounterMoralAxis.Evil => "Evil",
            _ => "Unknown"
        };
    }

    private string? RollDungeonMonsterForLevelExcludingCharacter(int level)
        => RollDungeonMonsterForLevelWithCountExcludingCharacter(level)?.MonsterName;

    private void ResolveCharacterEncounter(int dungeonLevel, int? forcedCount = null)
    {
        var party = LoadEncounterParty();
        if (party.Count == 0)
        {
            SayOnBoth("Characters", "Encounter!\n\nCharacter\n\n(No active party members found)");
            return;
        }

        var count = forcedCount.HasValue && forcedCount.Value > 0
            ? forcedCount.Value
            : _random.Next(1, 5) + 1; // 1d4+1
        var classMonsters = RollCharacterEncounterClasses(count, dungeonLevel);
        if (classMonsters.Count == 0)
        {
            SayOnBoth("Characters", $"No class-based character templates were found for dungeon level {Math.Max(1, dungeonLevel)}.");
            return;
        }

        var classSummary = string.Join(", ", classMonsters.GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"));
        RuleApplicationInfo.Publish(
            "Adnd",
            "House Rule",
            "Character encounter composition",
            forcedCount.HasValue
                ? "Use table count for Character encounter, then roll class table once per encountered character. All encountered classes use current dungeon level."
                : "Roll 1d4+1 for count, then roll class table once per encountered character. All encountered classes use current dungeon level.",
            "1",
            "5",
            count.ToString(),
            classSummary);

        var grouped = classMonsters
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => (name: g.Key, count: g.Count()))
            .ToList();

        var outcome = _combatCoordinator.StartEncounterWithGroupCounts(
            this,
            grouped,
            party,
            _characterRepository,
            dungeonLevel,
            dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        if (outcome == CombatOutcome.Defeat)
            HandlePartyDefeatAtCurrentCell();
    }

    private List<string> RollCharacterEncounterClasses(int count, int dungeonLevel)
    {
        var result = new List<string>(count);
        if (count <= 0)
            return result;

        bool TryBuildOneSet()
        {
            result.Clear();
            for (int i = 0; i < count; i++)
            {
                string? mapped = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    mapped = MapCharacterClassRollToMonsterName(_random.Next(1, 101), dungeonLevel);
                    if (!string.IsNullOrWhiteSpace(mapped))
                        break;
                }

                if (string.IsNullOrWhiteSpace(mapped))
                {
                    result.Clear();
                    return false;
                }

                result.Add(mapped);
            }

            return true;
        }

        // Prefer mixed classes for multi-character encounters, while still allowing duplicates.
        var requireMixedClasses = count >= 2;
        if (!requireMixedClasses)
        {
            TryBuildOneSet();
            return result;
        }

        List<string>? fallback = null;
        for (int reroll = 0; reroll < 100; reroll++)
        {
            if (!TryBuildOneSet())
                return result;

            fallback ??= new List<string>(result);

            if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
                return result;
        }

        // Fallback if the available class pool at this level is too narrow.
        return fallback ?? result;
    }

    private string? MapCharacterClassRollToMonsterName(int roll, int dungeonLevel)
    {
        string className;
        if (roll <= 17)
            className = "Cleric";
        else if (roll <= 20)
            className = "Druid";
        else if (roll <= 60)
            className = "Fighter";
        else if (roll <= 62)
            className = "Paladin";
        else if (roll <= 65)
            className = "Ranger";
        else if (roll <= 86)
            className = "Magic-User";
        else if (roll <= 88)
            className = "Illusionist";
        else if (roll <= 98)
            className = "Thief";
        else if (roll == 99)
            className = "Assassin";
        else
            className = _random.Next(0, 2) == 0 ? "Monk" : "Bard";

        var candidates = BuildCharacterClassMonsterNameCandidates(className, dungeonLevel);
        var monsters = _monsterRepository.GetAll().ToList();

        foreach (var candidate in candidates)
        {
            var exact = monsters.FirstOrDefault(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact.Name;
        }

        return null;
    }

    private static List<string> BuildCharacterClassMonsterNameCandidates(string className, int dungeonLevel)
    {
        var level = Math.Max(1, dungeonLevel);
        var names = new List<string>
        {
            $"Lvl {level} {className}",
            $"Lvl {level} {className.Replace("-", " ", StringComparison.Ordinal)}"
        };

        if (string.Equals(className, "Magic-User", StringComparison.OrdinalIgnoreCase))
        {
            names.Add($"Lvl {level} Mage");
            names.Add($"Lvl {level} MagicUser");
        }
        else if (string.Equals(className, "Cleric", StringComparison.OrdinalIgnoreCase))
        {
            names.Add($"Lvl {level} Priest");
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string? RollDungeonMonsterForLevel(int level)
        => RollDungeonMonsterForLevelWithCount(level)?.MonsterName;

    private EncounterRoll? RollDungeonMonsterForLevelWithCount(int level)
    {
        var rolledMonsterLevel = RollMonsterLevelFromEncounterTable(level);
        if (rolledMonsterLevel <= 0)
            return null;

        // Primary flow: MonsterEncounter.json determines monster level,
        // then MonsterLevels.json (1d100) determines creature and count.
        var wizardryOnly = GameRulesProvider.Current.EffectiveMonsterSourceOptions == SourceOptions.OnlyWizardry;

        // The DMG tables name AD&D creatures, so they are bypassed when only Wizardry monsters may appear.
        var dmgRoll = wizardryOnly ? null : RollFromDmgEncounterTable(rolledMonsterLevel, level);
        if (dmgRoll != null)
            return dmgRoll;

        // Fallback when DMG table data is unavailable or cannot be mapped locally.
        var sourceOption = GameRulesProvider.Current.EffectiveMonsterSourceOptions;
        var allMonstersForSource = FilterMonstersBySource(_monsterRepository.GetAll().ToList(), sourceOption);
        var monstersForLevel = allMonstersForSource
            .Where(m => m.DungeonLevel == rolledMonsterLevel)
            .ToList();

        // A level with no Wizardry monsters borrows from the closest level that has some.
        if (monstersForLevel.Count == 0 && wizardryOnly && allMonstersForSource.Count > 0)
        {
            var closest = allMonstersForSource.Min(m => Math.Abs(m.DungeonLevel - rolledMonsterLevel));
            monstersForLevel = allMonstersForSource.Where(m => Math.Abs(m.DungeonLevel - rolledMonsterLevel) == closest).ToList();
        }

        if (monstersForLevel.Count == 0)
            return null;

        var filteredMonsters = monstersForLevel;

        var selected = SelectMonsterByFrequencyWeight(filteredMonsters, level, rolledMonsterLevel);
        if (selected == null)
            return null;

        var name = selected.Name;
        int? countOverride = null;
        if (selected.Type == MonsterType.Dragon)
        {
            if (rolledMonsterLevel == 3)
                name = RollLevel3DragonBySubtable();
            else if (rolledMonsterLevel == 4)
                name = RollLevel4DragonBySubtable();
            else if (rolledMonsterLevel == 5)
                name = RollLevel5DragonBySubtable();
            else if (rolledMonsterLevel == 7)
                name = RollLevel7DragonBySubtable();
            else if (rolledMonsterLevel == 8)
                name = RollLevel8DragonBySubtable();
            else if (rolledMonsterLevel == 9)
                name = RollLevel9DragonBySubtable(out countOverride);
            else if (rolledMonsterLevel == 10)
                name = RollLevel10DragonBySubtable(out countOverride);
        }

        return new EncounterRoll(name, countOverride, rolledMonsterLevel);
    }

    private int RollMonsterLevelFromEncounterTable(int dungeonLevel)
    {
        var tablePath = Path.Combine("Data", "Encounters", "MonsterEncounter.json");
        if (!File.Exists(tablePath))
            return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);

        using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
        if (!document.RootElement.TryGetProperty("monsterEncounterTable", out var tableRoot)
            || tableRoot.ValueKind != JsonValueKind.Object)
        {
            return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);
        }

        var levelKey = dungeonLevel.ToString();
        if (!tableRoot.TryGetProperty(levelKey, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);

        var roll = _random.Next(1, 21);

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("roll", out var rollEl)
                || !entry.TryGetProperty("monsterLevel", out var levelEl))
                continue;

            var range = rollEl.GetString();
            if (!TryParseRollRange(range, out var min, out var max))
                continue;

            if (roll < min || roll > max)
                continue;

            var monsterLevel = levelEl.GetInt32();
            RuleApplicationInfo.Publish(
                "DMG",
                "174",
                $"Roll monster level for dungeon level {dungeonLevel}",
                "",
    //           "Use MonsterEncounterTable: roll 1d20 and map to encounter monster level.",
                "1",
                "20",
                roll.ToString(),
                $"Monster level {monsterLevel}.");

            return monsterLevel;
        }

        return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);
    }

    private static bool TryParseRollRange(string? range, out int min, out int max)
    {
        min = 0;
        max = 0;
        if (string.IsNullOrWhiteSpace(range))
            return false;

        var trimmed = range.Trim();
        var dash = trimmed.IndexOf('-');
        if (dash <= 0 || dash >= trimmed.Length - 1)
        {
            if (!int.TryParse(trimmed, out var single))
                return false;

            min = single;
            max = single;
            return true;
        }

        var left = trimmed[..dash].Trim();
        var right = trimmed[(dash + 1)..].Trim();
        if (!int.TryParse(left, out min) || !int.TryParse(right, out max))
            return false;

        if (max < min)
        {
            var temp = min;
            min = max;
            max = temp;
        }

        return true;
    }

    public static string GetDMGpageForMonsterEncounterTable(int monsterLevel)
    {
        return monsterLevel switch
        {
            1 => "175_Level1",
            2 => "177_Level2",
            3 => "177_Level3",
            4 => "177_Level4",
            5 => "177_Level5",
            6 => "178_Level6",
            7 => "178_Level7",
            8 => "178_Level8",
            9 => "179_Level9",
            10 => "179_Level10",
            _ => "175-177"
        };
    }

    private EncounterRoll? RollFromDmgEncounterTable(int monsterLevel, int dungeonLevel)
    {
        var tablePath = Path.Combine("Data", "Encounters", "MonsterLevels.json");
        if (!File.Exists(tablePath))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
        if (!document.RootElement.TryGetProperty("MonsterLevels", out var allLevels))
            return null;

        var levelKey = $"Level{monsterLevel}";
        if (!allLevels.TryGetProperty(levelKey, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return null;

        var roll = _random.Next(1, 101);

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("DiceMin", out var minEl)
                || !entry.TryGetProperty("DiceMax", out var maxEl)
                || !entry.TryGetProperty("Creature", out var creatureEl))
            {
                continue;
            }

            var min = minEl.GetInt32();
            var max = maxEl.GetInt32();
            if (roll < min || roll > max)
                continue;

            var creature = creatureEl.GetString();
            var substituteBadgerWithHobgoblin = dungeonLevel >= 3
                && roll is >= 3 and <= 4
                && string.Equals(creature?.Trim(), "Badger", StringComparison.OrdinalIgnoreCase);

            if (substituteBadgerWithHobgoblin)
            {
                creature = "Hobgoblin";
                RuleApplicationInfo.Publish(
                    "DMG special case: dungeon level 3+ and encounter roll 03-04 replaces Badger with 2-8 Hobgoblins.");
            }
            string page = GetDMGpageForMonsterEncounterTable(monsterLevel);
            RuleApplicationInfo.Publish(
                "DMG",
                page,
                $"Encounter table level {monsterLevel})",
    //            $"Use encounter table Level{monsterLevel}; roll 1d100 and find matching DiceMin-DiceMax range.",
                $"",
                "1",
                "100",
                roll.ToString(), creature);
            var resolved = ResolveDmgCreatureToMonsterName(creature, monsterLevel, dungeonLevel, out var dragonCountOverride);
            int? countOverride = null;
            int? countMin = null;
            int? countMax = null;

            if (substituteBadgerWithHobgoblin)
            {
                countMin = 2;
                countMax = 8;
            }
            else if (entry.TryGetProperty("CountMin", out var countMinEl)
                && entry.TryGetProperty("CountMax", out var countMaxEl)
                && countMinEl.ValueKind == JsonValueKind.Number
                && countMaxEl.ValueKind == JsonValueKind.Number)
            {
                countMin = countMinEl.GetInt32();
                countMax = countMaxEl.GetInt32();
            }

            if (countMin.HasValue && countMax.HasValue)
            {
                var resolvedCountMin = countMin.Value;
                var resolvedCountMax = countMax.Value;

                if (resolvedCountMax < resolvedCountMin)
                    (resolvedCountMin, resolvedCountMax) = (resolvedCountMax, resolvedCountMin);

                resolvedCountMin = Math.Max(1, resolvedCountMin);
                resolvedCountMax = Math.Max(1, resolvedCountMax);
                countOverride = _random.Next(resolvedCountMin, resolvedCountMax + 1);

                //        public record DiceFormula(int DiceCount, int DiceSides, int Extra);
                //        public static DiceFormula GetDiceFormula(int min, int max)

                DiceFormulas.DiceFormula? diceFormula = DiceFormulas.GetDiceFormula(resolvedCountMin, resolvedCountMax);
                int NumberOfDices = diceFormula?.DiceCount ?? countOverride.Value;
                int numberOfSides = diceFormula?.DiceSides ?? 1;
                int extra = diceFormula?.Extra ?? (resolvedCountMin - 1);

                string numberOfSidesText = numberOfSides.ToString();
                if (extra > 0)
                    numberOfSidesText += "+" + extra.ToString();

                if (creature != "Human")
                {
                    //    public static void Publish(string source, string page, string context, string rule, string numberOfDices, 
                    //string sidesOnDices, string resultOfRoll, string consequenceOfRoll)
                    RuleApplicationInfo.Publish(
                    "DMG",//source
                    page,//page
                    //               $"Roll encounter count for '{creature}' (monster level {monsterLevel})",
                    $"Number of '{creature}s'",//context
                    "",//rule
                //    "Use CountMin-CountMax from MonsterLevels entry.",//rule
                    NumberOfDices.ToString(),//numberOfDices
                    numberOfSidesText,//sidesOnDices
                    countOverride.Value.ToString(),//resultOfRoll
                    countOverride.Value.ToString() + " " + creature + "s"//resultOfRoll
                   );
                }
            }

            if (dragonCountOverride.HasValue && dragonCountOverride.Value > 0)
                countOverride = dragonCountOverride;

            if (!string.IsNullOrWhiteSpace(resolved))
                return new EncounterRoll(resolved, countOverride, monsterLevel);

            break;
        }

        RuleApplicationInfo.Publish($"DMG encounter table Level{monsterLevel} roll did not map to a local monster.");
        return null;
    }

    private string? ResolveDmgCreatureToMonsterName(string? dmgCreature, int monsterLevel, int dungeonLevel, out int? dragonCountOverride)
    {
        dragonCountOverride = null;

        if (string.IsNullOrWhiteSpace(dmgCreature))
            return null;

        if (IsDragonEncounter(dmgCreature))
        {
            if (monsterLevel == 3)
                return RollLevel3DragonBySubtable();

            if (monsterLevel == 4)
                return RollLevel4DragonBySubtable();

            if (monsterLevel == 5)
                return RollLevel5DragonBySubtable();

            if (monsterLevel == 7)
                return RollLevel7DragonBySubtable();

            if (monsterLevel == 8)
                return RollLevel8DragonBySubtable();

            if (monsterLevel == 9)
                return RollLevel9DragonBySubtable(out dragonCountOverride);

            if (monsterLevel == 10)
                return RollLevel10DragonBySubtable(out dragonCountOverride);
        }

        if (string.Equals(dmgCreature.Trim(), "Human", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dmgCreature.Trim(), "Humans", StringComparison.OrdinalIgnoreCase))
        {
            var roll = _random.Next(1, 101);
            var result = roll switch
            {
                <= 25 => "Bandit",
                <= 30 => "Berserker",
                <= 45 => "Brigand",
                _ => "Character"
            };

            RuleApplicationInfo.Publish(
                "DMG",
                "175",
                "Resolve Humans encounter",
                "If Humans are encountered, roll 1d100: 01-25 Bandit, 26-30 Berserker, 31-45 Brigand, 46-100 Character.",
                "1",
                "100",
                roll.ToString(),
                $"Selected {result}.");

            return result;
        }

        if (IsCharacterEncounterEntry(dmgCreature))
            return "Character";

        if (IsDemonPrinceEncounter(dmgCreature))
        {
            var demonPrince = GetRandomDemonPrinceName();
            if (!string.IsNullOrWhiteSpace(demonPrince))
                return demonPrince;
        }

        var allMonsters = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd)
            .ToList();

        var raw = dmgCreature.Trim();
        var candidateNames = new List<string> { raw };

        var comma = raw.IndexOf(',');
        if (comma > 0 && comma < raw.Length - 1)
        {
            var left = raw[..comma].Trim();
            var right = raw[(comma + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                candidateNames.Add($"{right} {left}");
        }

        foreach (var candidate in candidateNames)
        {
            var match = FindMonsterByName(allMonsters, candidate);
            if (match != null)
                return match.Name;
        }

        return null;
    }

    private static bool IsDragonEncounter(string dmgCreature)
    {
        var trimmed = dmgCreature.Trim();
        return trimmed.StartsWith("Dragon", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCharacterEncounterEntry(string dmgCreature)
    {
        var trimmed = dmgCreature.Trim();
        return trimmed.StartsWith("Character", StringComparison.OrdinalIgnoreCase);
    }

    private string GetRandomHumanCharacterEncounterNameForLevel(int monsterLevel)
    {
        var level = Math.Max(1, monsterLevel);
        var candidates = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd)
            .Where(m => m.DungeonLevel == level)
            .Where(m => m.Type == MonsterType.Humanoid)
            .Where(m => m.Name.StartsWith($"Lvl {level} ", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return $"Lvl {level} Fighter";

        return candidates[_random.Next(candidates.Count)];
    }

    private static bool IsDemonPrinceEncounter(string dmgCreature)
    {
        var normalized = NormalizeMonsterName(dmgCreature);
        return normalized.Contains("demon", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("prince", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetRandomDemonPrinceName()
    {
        var demonPrinces = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd
                && string.Equals(m.TypeName?.Trim(), "Demon Prince", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (demonPrinces.Count == 0)
            return null;

        return demonPrinces[_random.Next(demonPrinces.Count)];
    }

    private string RollLevel3DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 28 => "Black Dragon",
            <= 62 => "Brass Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 3 dragon encounter",
            "If dragon is encountered on dungeon level 3, roll 1d100: 01-28 Black Dragon, 29-62 Brass Dragon, 63-100 White Dragon.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel8DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 13 => "Black Dragon",
            <= 24 => "Blue Dragon",
            <= 31 => "Brass Dragon",
            <= 35 => "Bronze Dragon",
            <= 43 => "Copper Dragon",
            <= 47 => "Gold Dragon",
            <= 62 => "Green Dragon",
            <= 78 => "Red Dragon",
            <= 82 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 8 dragon encounter",
            "If a level 8 dragon is encountered, roll 1d100: 01-13 Black, 14-24 Blue, 25-31 Brass, 32-35 Bronze, 36-43 Copper, 44-47 Gold, 48-62 Green, 63-78 Red, 79-82 Silver, 83-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel5DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 21 => "Blue Dragon",
            <= 29 => "Brass Dragon",
            <= 36 => "Bronze Dragon",
            <= 48 => "Copper Dragon",
            <= 52 => "Gold Dragon",
            <= 66 => "Green Dragon",
            <= 80 => "Red Dragon",
            <= 87 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 5 dragon encounter",
            "If a level 5 dragon is encountered, roll 1d100: 01-10 Black, 11-21 Blue, 22-29 Brass, 30-36 Bronze, 37-48 Copper, 49-52 Gold, 53-66 Green, 67-80 Red, 81-87 Silver, 88-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel7DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 21 => "Blue Dragon",
            <= 29 => "Brass Dragon",
            <= 36 => "Bronze Dragon",
            <= 48 => "Copper Dragon",
            <= 52 => "Gold Dragon",
            <= 66 => "Green Dragon",
            <= 80 => "Red Dragon",
            <= 87 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 7 dragon encounter",
            "If a level 7 dragon is encountered, roll 1d100: 01-10 Black, 11-21 Blue, 22-29 Brass, 30-36 Bronze, 37-48 Copper, 49-52 Gold, 53-66 Green, 67-80 Red, 81-87 Silver, 88-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel9DragonBySubtable(out int? countOverride)
    {
        countOverride = null;
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 22 => "Blue Dragon",
            <= 31 => "Brass Dragon",
            <= 34 => "Bronze Dragon",
            <= 42 => "Copper Dragon",
            <= 46 => "Gold Dragon",
            <= 62 => "Green Dragon",
            <= 78 => "Red Dragon",
            <= 82 => "Silver Dragon",
            _ => "White Dragon"
        };

        if (roll <= 10 || (roll >= 23 && roll <= 31) || roll >= 83)
            countOverride = 2;

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 9 dragon encounter",
            "If a level 9 dragon is encountered, roll 1d100: 01-10 Black, 11-22 Blue, 23-31 Brass, 32-34 Bronze, 35-42 Copper, 43-46 Gold, 47-62 Green, 63-78 Red, 79-82 Silver, 83-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel10DragonBySubtable(out int? countOverride)
    {
        countOverride = null;
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 20 => "Blue Dragon",
            <= 26 => "Bronze Dragon",
            <= 33 => "Copper Dragon",
            <= 35 => ResolveBestAvailableMonsterName("Tiamat", "Chromatic Dragon", "Red Dragon"),
            <= 40 => "Gold Dragon",
            <= 60 => "Green Dragon",
            <= 63 => ResolveBestAvailableMonsterName("Bahamut", "Platinum Dragon", "Gold Dragon"),
            <= 94 => "Red Dragon",
            _ => "Silver Dragon"
        };

        if ((roll >= 1 && roll <= 33)
            || (roll >= 36 && roll <= 60)
            || (roll >= 64 && roll <= 100))
        {
            countOverride = 2;
        }

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 10 dragon encounter",
            "If a level 10 dragon is encountered, roll 1d100: 01-20 Blue, 21-26 Bronze, 27-33 Copper, 34-35 Chromatic (Tiamat), 36-40 Gold, 41-60 Green, 61-63 Platinum (Bahamut), 64-94 Red, 95-100 Silver.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string ResolveBestAvailableMonsterName(params string[] candidates)
    {
        var allMonsters = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd)
            .ToList();

        foreach (var candidate in candidates)
        {
            var match = FindMonsterByName(allMonsters, candidate);
            if (match != null)
                return match.Name;
        }

        return candidates.Last();
    }

    private string RollLevel4DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 21 => "Blue Dragon",
            <= 29 => "Brass Dragon",
            <= 36 => "Bronze Dragon",
            <= 48 => "Copper Dragon",
            <= 52 => "Gold Dragon",
            <= 66 => "Green Dragon",
            <= 80 => "Red Dragon",
            <= 87 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 4 dragon encounter",
            "If a level 4 dragon is encountered, roll 1d100: 01-10 Black, 11-21 Blue, 22-29 Brass, 30-36 Bronze, 37-48 Copper, 49-52 Gold, 53-66 Green, 67-80 Red, 81-87 Silver, 88-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private static string NormalizeMonsterName(string value)
    {
        var normalized = value
            .ToLowerInvariant()
            .Replace("deamon", "demon", StringComparison.Ordinal)
            .Replace(',', ' ')
            .Replace('-', ' ');

        var chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return string.Join(" ", new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string MonsterNameSignature(string value)
    {
        var parts = NormalizeMonsterName(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        return string.Join(" ", parts);
    }

    private static Monster? FindMonsterByName(IEnumerable<Monster> monsters, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return null;

        var exact = monsters.FirstOrDefault(m => string.Equals(m.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var normalizedRequested = NormalizeMonsterName(requestedName);
        var signatureRequested = MonsterNameSignature(requestedName);

        foreach (var monster in monsters)
        {
            if (string.Equals(NormalizeMonsterName(monster.Name), normalizedRequested, StringComparison.OrdinalIgnoreCase))
                return monster;

            if (string.Equals(MonsterNameSignature(monster.Name), signatureRequested, StringComparison.OrdinalIgnoreCase))
                return monster;
        }

        return null;
    }

    private List<Monster> FilterMonstersBySource(List<Monster> monsters, SourceOptions sourceOption)
    {
        return sourceOption switch
        {
            SourceOptions.OnlyAdnd => monsters.Where(m => m.Source == Sources.Adnd).ToList(),
            SourceOptions.OnlyAdndDMGEncounterTable => monsters.Where(m => m.Source == Sources.Adnd).ToList(),
            SourceOptions.OnlyWizardry => monsters.Where(m => m.Source == Sources.Wizardry || m.Source == Sources.WizardryAndAdnd).ToList(),
            SourceOptions.AllButWizardry => monsters.Where(m => m.Source != Sources.Wizardry).ToList(),
            SourceOptions.All => monsters,
            _ => monsters
        };
    }

    private Monster? SelectMonsterByFrequencyWeight(List<Monster> monsters, int dungeonLevel, int rolledMonsterLevel)
    {
        // Define frequency weights (similar to rarity weights for items)
        var frequencyWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Common", 50 },
            { "Uncommon", 30 },
            { "Rare", 15 },
            { "Very Rare", 4 },
            { "Legendary", 1 },
            { "Not Implemented", 0 }

        };
        //filter out monsters with "Not Implemented" frequency
        monsters = monsters.Where(m => !string.Equals(m.Frequency, "Not Implemented", StringComparison.OrdinalIgnoreCase)).ToList();
        if (monsters.Count == 0)
            return null;
        // Calculate total weight
        var totalWeight = 0;
        var monsterWeights = new List<(Monster monster, int weight)>();

        foreach (var monster in monsters)
        {
            var weight = frequencyWeights.ContainsKey(monster.Frequency) 
                ? frequencyWeights[monster.Frequency] 
                : frequencyWeights["Common"]; // Default to Common if frequency not recognized

            monsterWeights.Add((monster, weight));
            totalWeight += weight;
        }

        if (totalWeight == 0)
            return monsters[_random.Next(monsters.Count)]; // Fallback to uniform random

        // Select random monster based on weight
        var randomValue = _random.Next(totalWeight);
        var cumulativeWeight = 0;

        foreach (var (monster, weight) in monsterWeights)
        {
            cumulativeWeight += weight;
            if (randomValue < cumulativeWeight)
            {
                RuleApplicationInfo.Publish(
                    "Adnd",
                    "House Rule",
                    $"Roll encounter creature for dungeon level {dungeonLevel} (monster level {rolledMonsterLevel})",
                    "Roll 1dN where N is total frequency weight after source filtering; choose creature by cumulative weight.",
                    "1",
                    totalWeight.ToString(),
                    (randomValue + 1).ToString(),
                    $"Selected {monster.Name} (weight {weight} of total {totalWeight}).");
                return monster;
            }
        }

        // Fallback (should never reach here)
        return monsters[_random.Next(monsters.Count)];
    }
}
