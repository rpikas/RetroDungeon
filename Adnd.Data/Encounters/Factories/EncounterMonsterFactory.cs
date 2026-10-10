using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;
using Adnd.Data.Monsters;
using System.IO;
using System.Text.RegularExpressions;
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
        return CreateGroup(monsterName, count, "default", forcedHumanAlignment: null, dungeonLevel: null);
    }

    public List<MonsterInstance> CreateGroup(string monsterName, int count, string groupId)
    {
        return CreateGroup(monsterName, count, groupId, forcedHumanAlignment: null, dungeonLevel: null);
    }

    public List<MonsterInstance> CreateGroup(string monsterName, int count, string groupId, int? dungeonLevel)
    {
        return CreateGroup(monsterName, count, groupId, forcedHumanAlignment: null, dungeonLevel: dungeonLevel);
    }

    public List<MonsterInstance> CreateGroupWithDragonHitPointsPerDie(
        string monsterName,
        IReadOnlyList<int> hitPointsPerDiePerMonster,
        string groupId,
        int? dungeonLevel)
    {
        if (hitPointsPerDiePerMonster == null || hitPointsPerDiePerMonster.Count == 0)
            return CreateGroup(monsterName, 1, groupId, forcedHumanAlignment: null, dungeonLevel: dungeonLevel);

        var templatePool = _monsterRepository.GetAll().ToList();
        var template = TryResolvePiercerVariant(templatePool, monsterName)
            ?? FindMonsterByName(templatePool, monsterName)
            ?? templatePool.FirstOrDefault(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase))
            ?? BuildFallback(monsterName);

        var lairTemplate = CloneMonster(template);
        var inLairRoll = _random.Next(1, 101);
        var inLairChance = Math.Clamp(lairTemplate.InLairPercent, 0, 100);
        var isInLair = inLairRoll <= inLairChance;

        RuleApplicationInfo.Publish(
            lairTemplate.Name,
            "MM",
            $"{lairTemplate.Name} %InLair={inLairChance}%. ",
            "",
            "1",
            "100",
            inLairRoll.ToString(),
            $" {(isInLair ? "Group in lair" : "Group not in lair")}.");

        var list = new List<MonsterInstance>(hitPointsPerDiePerMonster.Count);
        for (int i = 0; i < hitPointsPerDiePerMonster.Count; i++)
        {
            var cloned = CloneMonster(template);
            ApplyDragonAgeHitPointProfile(cloned, dungeonLevel, hitPointsPerDiePerMonster[i]);

            list.Add(new MonsterInstance(cloned, i + 1, groupId)
            {
                IsInLair = isInLair
            });
        }

        return list;
    }

    private List<MonsterInstance> CreateGroup(string monsterName, int count, string groupId, string? forcedHumanAlignment, int? dungeonLevel)
    {
        count = Math.Max(1, count);
        if (count > GameRulesProvider.Current.MaxSizeEncounter)
        {
            count = _random.Next(1, GameRulesProvider.Current.MaxSizeEncounter + 1);
        }

        var allMonsters = _monsterRepository.GetAll().ToList();
        var template = TryResolvePiercerVariant(allMonsters, monsterName)
            ?? FindMonsterByName(allMonsters, monsterName)
            ?? allMonsters.FirstOrDefault(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase));

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

        string? encounterHumanAlignment = null;
        if (IsHumanCharacterTemplate(template))
        {
            encounterHumanAlignment = forcedHumanAlignment;
            if (string.IsNullOrWhiteSpace(encounterHumanAlignment))
            {
                var roll = _random.Next(1, 4);
                encounterHumanAlignment = roll switch
                {
                    1 => "Neutral",
                    2 => "Lawful Good",
                    _ => "Chaotic Evil"
                };

                RuleApplicationInfo.Publish(
                    "Human Encounter Alignment",
                    "DMG",
                    "Human encounter alignment roll",
                    "All encountered human characters in this encounter use one shared alignment: Neutral, Lawful Good, or Chaotic Evil.",
                    "1",
                    "3",
                    roll.ToString(),
                    $"Alignment selected: {encounterHumanAlignment}.");
            }
        }

        var encounterTemplate = CloneMonster(template);
        var forcedDragonPairHitPointsPerDie = TryGetForcedDragonPairHitPointsPerDie(encounterTemplate, count, dungeonLevel);
        if (forcedDragonPairHitPointsPerDie == null)
            ApplyDragonAgeHitPointProfile(encounterTemplate, dungeonLevel);

        var lairTemplate = CloneMonster(encounterTemplate);
        if (!string.IsNullOrWhiteSpace(encounterHumanAlignment))
            lairTemplate.Alignment = encounterHumanAlignment;

        var inLairRoll = _random.Next(1, 101);
        var inLairChance = Math.Clamp(lairTemplate.InLairPercent, 0, 100);
        var isInLair = inLairRoll <= inLairChance;

        RuleApplicationInfo.Publish(
            lairTemplate.Name,
            "MM",
          //  $"Monster Lair % Determine if {lairTemplate.Name} group ({groupId}) is in lair",
            $"{lairTemplate.Name} %InLair={inLairChance}%. ",
     //       "Roll 1d100 once per encounter group. If result is less than or equal to %InLair, the group is in lair.",
            "",
            "1",
            "100",
            inLairRoll.ToString(),
            $" {(isInLair ? "Group in lair" : "Group not in lair")}.");
    //    $"%InLair={inLairChance}%. {(isInLair ? "Group in lair" : "Group not in lair")}.");

        var list = new List<MonsterInstance>(count);
        for (int i = 1; i <= count; i++)
        {
            var cloned = CloneMonster(encounterTemplate);
            if (forcedDragonPairHitPointsPerDie != null)
            {
                var index = Math.Clamp(i - 1, 0, forcedDragonPairHitPointsPerDie.Count - 1);
                ApplyDragonAgeHitPointProfile(cloned, dungeonLevel, forcedDragonPairHitPointsPerDie[index]);
            }

            if (!string.IsNullOrWhiteSpace(encounterHumanAlignment))
                cloned.Alignment = encounterHumanAlignment;

            list.Add(new MonsterInstance(cloned, i, groupId)
            {
                IsInLair = isInLair
            });
        }

        if (isInLair && string.Equals(encounterTemplate.Name, "Giant Ant", StringComparison.OrdinalIgnoreCase))
        {
            var warriorTemplate = FindMonsterByName(allMonsters, "Giant Warrior Ant");
            if (warriorTemplate != null)
            {
                var warrior = CloneMonster(warriorTemplate);
                list.Add(new MonsterInstance(warrior, list.Count + 1, groupId)
                {
                    IsInLair = true
                });

                RuleApplicationInfo.Publish(
                    "Giant Ant",
                    "MM",
                    "Giant ants in lair encounter support from giant warrior ant",
                    "When giant ants are encountered in lair, one giant warrior ant is also present.",
                    "-",
                    "-",
                    "-",
                    $"Added 1 Giant Warrior Ant to group {groupId}.");
            }
        }

        return list;
    }

    private Monster? TryResolvePiercerVariant(IReadOnlyList<Monster> monsters, string requestedName)
    {
        if (!string.Equals(requestedName?.Trim(), "Piercer", StringComparison.OrdinalIgnoreCase))
            return null;

        var variants = monsters
            .Where(m => string.Equals(m.Name, "Piercer Small", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Name, "Piercer Medium", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Name, "Piercer Large", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Name, "Piercer Huge", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (variants.Count == 0)
            return null;

        var selected = variants[_random.Next(variants.Count)];
        RuleApplicationInfo.Publish($"Encounter roll: '{requestedName}' resolved to variant '{selected.Name}'.");
        return selected;
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

    public List<MonsterInstance> CreateMultipleGroups(List<(string monsterName, int count)> groups, int? dungeonLevel = null)
    {
        string? sharedHumanAlignment = null;
        var includesHumanCharacters = groups.Any(g => IsLikelyHumanCharacterName(g.monsterName));
        if (includesHumanCharacters)
        {
            var roll = _random.Next(1, 4);
            sharedHumanAlignment = roll switch
            {
                1 => "Neutral",
                2 => "Lawful Good",
                _ => "Chaotic Evil"
            };

            RuleApplicationInfo.Publish(
                "Human Encounter Alignment",
                "DMG",
                "Shared alignment for encountered human groups",
                "When multiple human groups are encountered, all human characters share one alignment for the whole encounter.",
                "1",
                "3",
                roll.ToString(),
                $"Shared human alignment: {sharedHumanAlignment}.");
        }

        var allMonsters = new List<MonsterInstance>();
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var (monsterName, count) = groups[groupIndex];
            var groupId = $"Group{groupIndex + 1}";
            allMonsters.AddRange(CreateGroup(monsterName, count, groupId, sharedHumanAlignment, dungeonLevel));
        }
        return allMonsters;
    }

    private static readonly DragonAgeProfile VeryYoung = new("Very Young", 1);
    private static readonly DragonAgeProfile Young = new("Young", 2);
    private static readonly DragonAgeProfile SubAdult = new("Sub-adult", 3);
    private static readonly DragonAgeProfile YoungAdult = new("Young Adult", 4);
    private static readonly DragonAgeProfile Adult = new("Adult", 5);
    private static readonly DragonAgeProfile Old = new("Old", 6);
    private static readonly DragonAgeProfile VeryOld = new("Very Old", 7);
    private static readonly DragonAgeProfile Ancient = new("Ancient", 8);

    private sealed record DragonAgeProfile(string AgeCategory, int HitPointsPerDie);

    private static readonly Regex DragonAgeRelatedMagicUseRegex = new(
        @"^(?<chance>\d{1,3})\s*%\s*Level\s*Age\s*related\s*Magic\s*Use$",
        RegexOptions.IgnoreCase);

    private void ApplyDragonAgeHitPointProfile(Monster template, int? dungeonLevel, int? forcedHitPointsPerDie = null)
    {
        if (template == null)
            return;

        var isDragon = template.Type == MonsterType.Dragon
            || template.Name.Contains("Dragon", StringComparison.OrdinalIgnoreCase);
        if (!isDragon)
            return;

        var profile = forcedHitPointsPerDie.HasValue
            ? TryResolveDragonAgeProfileByHitPointsPerDie(forcedHitPointsPerDie.Value)
            : TryResolveDragonAgeProfile(template.Name, dungeonLevel);
        if (profile == null)
            return;

        var hitDice = Math.Max(0, template.HitDice);
        var fixedBonus = (profile.HitPointsPerDie - 1) * hitDice;
        template.HitDiceType = 1;
        template.ExtraHitPoints = Math.Max(0, template.ExtraHitPoints + fixedBonus);

        ApplyDragonAgeRelatedMagicUse(template, profile);

        RuleApplicationInfo.PublishLinked(
            "DMG",
            "Dragon Subtable",
            $"{template.Name} age category {profile.AgeCategory} => {profile.HitPointsPerDie} HP per die.");
    }

    private static void ApplyDragonAgeRelatedMagicUse(Monster template, DragonAgeProfile profile)
    {
        if (template?.SpecialAbilities == null || template.SpecialAbilities.Count == 0)
            return;

        foreach (var ability in template.SpecialAbilities)
        {
            if (ability == null || string.IsNullOrWhiteSpace(ability.Name))
                continue;

            var match = DragonAgeRelatedMagicUseRegex.Match(ability.Name.Trim());
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups["chance"].Value, out var parsedChance))
                parsedChance = 0;

            var chance = Math.Clamp(parsedChance, 0, 100);
            var spellLevel = Math.Clamp((profile.HitPointsPerDie + 1) / 2, 1, 4);
            ability.Name = $"{chance}% Level {spellLevel} Magic Use";
            ability.Description = $"{chance}% chance to use level {spellLevel} magic (dragon age-based).";

            EnsureAgeRelatedDragonSpellTier(template, spellLevel);

            RuleApplicationInfo.PublishLinked(
                "DMG",
                "Dragon Subtable",
                $"{template.Name} age-based magic use resolved to level {spellLevel} ({chance}% chance).");
        }
    }

    private static void EnsureAgeRelatedDragonSpellTier(Monster template, int spellLevel)
    {
        var spellAbilityName = $"Level {spellLevel} Magic-User spells";
        var hasTier = template.SpecialAbilities.Any(a =>
            string.Equals(a?.Name?.Trim(), spellAbilityName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a?.Name?.Trim(), $"Level {spellLevel} Mage spells", StringComparison.OrdinalIgnoreCase));

        if (hasTier)
            return;

        template.SpecialAbilities.Add(new MonsterSpecialAbility
        {
            Name = spellAbilityName,
            Description = spellAbilityName
        });
    }

    private DragonAgeProfile? TryResolveDragonAgeProfile(string dragonName, int? dungeonLevel)
    {
        if (string.IsNullOrWhiteSpace(dragonName) || !dungeonLevel.HasValue)
            return null;

        var normalized = dragonName.Trim();

        return dungeonLevel.Value switch
        {
            3 => VeryYoung,
            4 => normalized switch
            {
                "Black Dragon" => PickOne(Young, SubAdult),
                "Blue Dragon" => PickOne(VeryYoung, Young),
                "Brass Dragon" => PickOne(Young, SubAdult),
                "Bronze Dragon" => PickOne(VeryYoung, Young),
                "Copper Dragon" => PickOne(VeryYoung, Young),
                "Gold Dragon" => PickOne(VeryYoung, Young),
                "Green Dragon" => PickOne(VeryYoung, Young),
                "Red Dragon" => PickOne(VeryYoung, Young),
                "Silver Dragon" => PickOne(VeryYoung, Young),
                "White Dragon" => PickOne(Young, SubAdult),
                _ => null
            },
            5 => normalized switch
            {
                "Black Dragon" => PickOne(YoungAdult, Adult),
                "Blue Dragon" => PickOne(SubAdult, YoungAdult),
                "Brass Dragon" => PickOne(YoungAdult, Adult),
                "Bronze Dragon" => PickOne(SubAdult, YoungAdult),
                "Copper Dragon" => PickOne(SubAdult, YoungAdult),
                "Gold Dragon" => PickOne(SubAdult, YoungAdult),
                "Green Dragon" => PickOne(SubAdult, YoungAdult),
                "Red Dragon" => PickOne(SubAdult, YoungAdult),
                "Silver Dragon" => PickOne(SubAdult, YoungAdult),
                "White Dragon" => PickOne(YoungAdult, Adult),
                _ => null
            },
            7 => normalized switch
            {
                "Black Dragon" => Old,
                "Blue Dragon" => Old,
                "Brass Dragon" => VeryOld,
                "Bronze Dragon" => Old,
                "Copper Dragon" => Old,
                "Gold Dragon" => Old,
                "Green Dragon" => Old,
                "Red Dragon" => Old,
                "Silver Dragon" => Old,
                "White Dragon" => VeryOld,
                _ => null
            },
            8 => normalized switch
            {
                "Black Dragon" => Ancient,
                "Blue Dragon" => VeryOld,
                "Brass Dragon" => Ancient,
                "Bronze Dragon" => VeryOld,
                "Copper Dragon" => VeryOld,
                "Gold Dragon" => VeryOld,
                "Green Dragon" => VeryOld,
                "Red Dragon" => VeryOld,
                "Silver Dragon" => VeryOld,
                "White Dragon" => Ancient,
                _ => null
            },
            9 => normalized switch
            {
                "Black Dragon" => PickOne(Ancient, Old),
                "Blue Dragon" => Ancient,
                "Brass Dragon" => PickOne(Ancient, Old),
                "Bronze Dragon" => Ancient,
                "Copper Dragon" => Ancient,
                "Gold Dragon" => Ancient,
                "Green Dragon" => Ancient,
                "Red Dragon" => Ancient,
                "Silver Dragon" => Ancient,
                "White Dragon" => PickOne(Ancient, VeryOld),
                _ => null
            },
            10 => normalized switch
            {
                "Blue Dragon" => PickOne(Ancient, VeryOld),
                "Bronze Dragon" => PickOne(Ancient, VeryOld),
                "Copper Dragon" => PickOne(Ancient, VeryOld),
                "Gold Dragon" => PickOne(Ancient, Old),
                "Green Dragon" => PickOne(Ancient, VeryOld),
                "Red Dragon" => PickOne(Ancient, Old),
                "Silver Dragon" => PickOne(Ancient, Old),
                "Dragon, Chromatic" => Ancient,
                "Dragon, Platinum (Bahamut)" => Ancient,
                _ => null
            },
            _ => null
        };
    }

    private DragonAgeProfile PickOne(DragonAgeProfile first, DragonAgeProfile second)
    {
        return _random.Next(0, 2) == 0 ? first : second;
    }

    private static List<int>? TryGetForcedDragonPairHitPointsPerDie(Monster template, int count, int? dungeonLevel)
    {
        if (template == null || count != 2 || !dungeonLevel.HasValue)
            return null;

        var isDragon = template.Type == MonsterType.Dragon
            || template.Name.Contains("Dragon", StringComparison.OrdinalIgnoreCase);
        if (!isDragon)
            return null;

        var name = template.Name?.Trim() ?? string.Empty;

        if (dungeonLevel.Value == 9)
        {
            return name switch
            {
                "Black Dragon" => new List<int> { 8, 6 },
                "Brass Dragon" => new List<int> { 8, 6 },
                "White Dragon" => new List<int> { 8, 7 },
                _ => null
            };
        }

        if (dungeonLevel.Value == 10)
        {
            return name switch
            {
                "Blue Dragon" => new List<int> { 8, 7 },
                "Bronze Dragon" => new List<int> { 8, 7 },
                "Copper Dragon" => new List<int> { 8, 7 },
                "Gold Dragon" => new List<int> { 8, 6 },
                "Green Dragon" => new List<int> { 8, 7 },
                "Red Dragon" => new List<int> { 8, 6 },
                "Silver Dragon" => new List<int> { 8, 6 },
                _ => null
            };
        }

        return null;
    }

    private static DragonAgeProfile? TryResolveDragonAgeProfileByHitPointsPerDie(int hitPointsPerDie)
    {
        return hitPointsPerDie switch
        {
            1 => VeryYoung,
            2 => Young,
            3 => SubAdult,
            4 => YoungAdult,
            5 => Adult,
            6 => Old,
            7 => VeryOld,
            8 => Ancient,
            _ => null
        };
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
            THAC0 = 20,
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
            TypeName = source.TypeName,
            ClimateTerain = source.ClimateTerain,
            Frequency = source.Frequency,
            ActivityCycle = source.ActivityCycle,
            Intelligence = source.Intelligence,
            Alignment = source.Alignment,
            NumberOfAppearancesMin = source.NumberOfAppearancesMin,
            NumberOfAppearancesMax = source.NumberOfAppearancesMax,
            ArmorClass = source.ArmorClass,
            MovementRate = source.MovementRate,
            HitDice = source.HitDice,
            HitDiceType = source.HitDiceType,
            ExtraHitPoints = source.ExtraHitPoints,
            THAC0 = source.THAC0,
            NumberOfAttacks = source.NumberOfAttacks,
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
            SpecialAttacks = source.SpecialAttacks
                .Select(sa => new MonsterSpecialAbility
                {
                    Name = sa.Name,
                    Description = sa.Description
                })
                .ToList(),
            SpecialDefenses = source.SpecialDefenses
                .Select(sd => new MonsterSpecialAbility
                {
                    Name = sd.Name,
                    Description = sd.Description
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

    private static bool IsHumanCharacterTemplate(Monster monster)
    {
        if (monster == null)
            return false;

        return IsLikelyHumanCharacterName(monster.Name);
    }

    private static bool IsLikelyHumanCharacterName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Trim();
        // Keep fixed-alignment human entries (e.g. Bandit/Berserker/Brigand) as defined in JSON.
        // Shared encounter alignment is only for class-like human characters.
        return normalized.Contains("Magic-User", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Cleric", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Fighter", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Thief", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Assassin", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Paladin", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Ranger", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Illusionist", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Druid", StringComparison.OrdinalIgnoreCase);
    }
}
