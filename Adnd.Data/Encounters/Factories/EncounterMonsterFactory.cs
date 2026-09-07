using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;
using Adnd.Data.Monsters;
using System.IO;
using System.Text.Json;

namespace Adnd.Data.Encounters.Factories;

public sealed class EncounterMonsterFactory
{
    private readonly MonsterRepository _monsterRepository;
    private readonly Random _random = new();

    private static int ParsePercent(string? raw, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        var trimmed = raw.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
            trimmed = trimmed[..^1].Trim();

        if (!int.TryParse(trimmed, out var parsed))
            return defaultValue;

        return Math.Clamp(parsed, 0, 100);
    }

    public EncounterMonsterFactory(MonsterRepository? monsterRepository = null)
    {
        _monsterRepository = monsterRepository ?? new MonsterRepository();
    }

    private Monster? GetRandomMonsterByJsonType(string desiredType)
    {
        if (!Directory.Exists(MonsterDataPaths.MonsterJsonFolder))
            return null;

        var matches = new List<Adnd.Data.Monsters.MonsterJsonModel>();

        foreach (var file in Directory.GetFiles(MonsterDataPaths.MonsterJsonFolder, "*.json"))
        {
            var jsonText = File.ReadAllText(file);

            try
            {
                var levelModel = JsonSerializer.Deserialize<Adnd.Data.Monsters.MonsterLevelJsonModel>(jsonText);
                if (levelModel?.Monsters != null && levelModel.Monsters.Count > 0)
                {
                    matches.AddRange(levelModel.Monsters.Where(m => string.Equals(m.Type?.Trim(), desiredType, StringComparison.OrdinalIgnoreCase)));
                    continue;
                }

                var single = JsonSerializer.Deserialize<Adnd.Data.Monsters.MonsterJsonModel>(jsonText);
                if (single != null && string.Equals(single.Type?.Trim(), desiredType, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(single);
                }
            }
            catch
            {
                // ignore malformed files
            }
        }

        if (matches.Count == 0)
            return null;

        var pick = matches[_random.Next(matches.Count)];
        return MonsterImporter.Convert(pick);
    }

    public List<MonsterInstance> CreateGroup(string monsterName, int count)
    {
        return CreateGroup(monsterName, count, "default");
    }

    public List<MonsterInstance> CreateGroup(string monsterName, int count, string groupId)
    {
        count = Math.Max(1, count);
        if (count > GameRulesProvider.Current.MaxSizeEncounter)
        {
            count = _random.Next(1, GameRulesProvider.Current.MaxSizeEncounter + 1);
        }

        var template = _monsterRepository
            .GetAll()
            .FirstOrDefault(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            var lower = monsterName.ToLowerInvariant();

            if (lower.Contains("demon") && lower.Contains("prince"))
            {
                template = GetRandomMonsterByJsonType("Demon Prince");
            }
            else if (lower.Contains("devil") && (lower.Contains("arch") || lower.Contains("archfiend")))
            {
                template = GetRandomMonsterByJsonType("Devil, Archfiend");
            }
        }

        template ??= BuildFallback(monsterName);

        var lairTemplate = CloneMonster(template);
        var inLairRoll = _random.Next(1, 101);
        var inLairChance = Math.Clamp(lairTemplate.InLairPercent, 0, 100);
        var isInLair = inLairRoll <= inLairChance;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Monster Lair %",
            $"Determine if {lairTemplate.Name} group ({groupId}) is in lair",
            "Roll 1d100 once per encounter group. If result is less than or equal to %InLair, the group is in lair.",
            "1",
            "100",
            inLairRoll.ToString(),
            $"%InLair={inLairChance}%. {(isInLair ? "Group in lair" : "Group not in lair")}.");

        var list = new List<MonsterInstance>(count);
        for (int i = 1; i <= count; i++)
        {
            var cloned = CloneMonster(template);
            list.Add(new MonsterInstance(cloned, i, groupId)
            {
                IsInLair = isInLair
            });
        }

        return list;
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

    public List<MonsterInstance> CreateMultipleGroups(List<(string monsterName, int count)> groups)
    {
        var allMonsters = new List<MonsterInstance>();
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var (monsterName, count) = groups[groupIndex];
            var groupId = $"Group{groupIndex + 1}";
            allMonsters.AddRange(CreateGroup(monsterName, count, groupId));
        }
        return allMonsters;
    }



    private static Monster BuildFallback(string monsterName)
    {
        var xp = monsterName.ToLowerInvariant() switch
        {
            "skeleton" => 25,
            "goblin" => 20,
            _ => 15
        };

        return new Monster
        {
            Name = monsterName,
            ArmorClass = 9,
            HitDice = 1,
            HitPoints = 6,
            BaseXPValue = xp,
            XPValuePerHitPoint = 1,
            TreasureType = "None",
            Attacks =
            {
                new MonsterAttack
                {
                    Name = "Attack",
                    NumberOfAttacks = 1,
                    Damage = "1d6"
                }
            }
        };
    }

    private static Monster CloneMonster(Monster source)
    {
        return new Monster
        {
            Name = source.Name,
            Type = source.Type,
            ArmorClass = source.ArmorClass,
            HitDice = source.HitDice,
            HitDiceType = source.HitDiceType,
            ExtraHitPoints = source.ExtraHitPoints,
            Size = source.Size,
            HitPoints = source.HitPoints,
            MagicResistance = source.MagicResistance,
            MagicResistancePercent = source.MagicResistancePercent,
            InLairPercent = source.InLairPercent,
            Movement = source.Movement,
            Morale = source.Morale,
            SavingThrows = source.SavingThrows,
            BaseXPValue = source.BaseXPValue,
            XPValuePerHitPoint = source.XPValuePerHitPoint,
            TreasureType = source.TreasureType,
            IndividualTreasure = source.IndividualTreasure,
            TreasureChanceOverride = source.TreasureChanceOverride,
            Source = source.Source, // Important: Copy the Source property!
            DungeonLevel = source.DungeonLevel, // Also copy DungeonLevel
            Attacks = source.Attacks
                .Select(a => new MonsterAttack
                {
                    Name = a.Name,
                    NumberOfAttacks = a.NumberOfAttacks,
                    Damage = a.Damage
                })
                .ToList(),
            SpecialAbilities = source.SpecialAbilities
                .Select(sa => new MonsterSpecialAbility
                {
                    Name = sa.Name,
                    Description = sa.Description
                })
                .ToList()
        };
    }
}
