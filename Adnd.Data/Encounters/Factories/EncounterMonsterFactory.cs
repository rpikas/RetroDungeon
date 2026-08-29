using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;
using Adnd.Data.Monsters;

namespace Adnd.Data.Encounters.Factories;

public sealed class EncounterMonsterFactory
{
    private readonly MonsterRepository _monsterRepository;

    public EncounterMonsterFactory(MonsterRepository? monsterRepository = null)
    {
        _monsterRepository = monsterRepository ?? new MonsterRepository();
    }

    public List<MonsterInstance> CreateGroup(string monsterName, int count)
    {
        return CreateGroup(monsterName, count, "default");
    }

    public List<MonsterInstance> CreateGroup(string monsterName, int count, string groupId)
    {
        count = Math.Max(1, count);

        var template = _monsterRepository
            .GetAll()
            .FirstOrDefault(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase))
            ?? BuildFallback(monsterName);

        var list = new List<MonsterInstance>(count);
        for (int i = 1; i <= count; i++)
            list.Add(new MonsterInstance(CloneMonster(template), i, groupId));

        return list;
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
            HitPoints = source.HitPoints,
            Movement = source.Movement,
            Morale = source.Morale,
            SavingThrows = source.SavingThrows,
            BaseXPValue = source.BaseXPValue,
            XPValuePerHitPoint = source.XPValuePerHitPoint,
            TreasureType = source.TreasureType,
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
