using Adnd.Core.Monsters;
using Adnd.Core.Characters;

namespace Adnd.Core.Combat.Sessions;

public sealed class MonsterInstance
{
    private readonly Dictionary<MonsterStatus, int> _statusDurations = new();

    public MonsterInstance(Monster template, int index, string groupId = "default")
    {
        Template = template;
        Index = index;
        GroupId = groupId;
        Name = template.Name;
        InstanceMonsterType = template.Type;

        // Roll HP based on HitDice (1d8 per hit die)
        var rolledHitPoints = RollHitPoints(template.HitDice, template.HitDiceType, template.ExtraHitPoints);
        MaxHitPoints = rolledHitPoints;
        CurrentHitPoints = rolledHitPoints;
        BaseArmorClass = template.ArmorClass;
    }

    private static int RollHitPoints(int hitDice, int hitDiceType = 8, int extraHitPoints = 0)
    {
        if (hitDice <= 0)
            return 1; // Minimum 1 HP

        // Default to d8 if hitDiceType is not specified or is 0
        if (hitDiceType <= 0)
            hitDiceType = 8;

        return DiceRoller.Roll(hitDice, hitDiceType) + extraHitPoints;
    }

    public Monster Template { get; }
    public int Index { get; }
    public string GroupId { get; }
    public string Name { get; }
    public MonsterType InstanceMonsterType { get; }
    public int MaxHitPoints { get; }
    public int CurrentHitPoints { get; set; }
    public int BaseArmorClass { get; }
    public int ArmorClassModifier { get; private set; }
    public int ArmorClass => BaseArmorClass + ArmorClassModifier;
    public bool IsAlive => CurrentHitPoints > 0;

    public string DisplayName => $"{Name} #{Index}";
    public string DisplayNameWithGroup => $"{Name} #{Index} (Group {GroupId})";

    public bool HasStatus(MonsterStatus status)
    {
        return _statusDurations.TryGetValue(status, out var rounds) && rounds > 0;
    }

    public int GetStatusRounds(MonsterStatus status)
    {
        return _statusDurations.TryGetValue(status, out var rounds) ? Math.Max(0, rounds) : 0;
    }

    public void SetStatus(MonsterStatus status, int rounds)
    {
        if (rounds <= 0)
        {
            _statusDurations.Remove(status);
            return;
        }

        _statusDurations[status] = rounds;
    }

    public int TickStatus(MonsterStatus status)
    {
        if (!_statusDurations.TryGetValue(status, out var rounds) || rounds <= 0)
            return 0;

        rounds -= 1;
        if (rounds <= 0)
        {
            _statusDurations.Remove(status);
            return 0;
        }

        _statusDurations[status] = rounds;
        return rounds;
    }

    public void AdjustArmorClass(int delta)
    {
        ArmorClassModifier += delta;
    }
}
