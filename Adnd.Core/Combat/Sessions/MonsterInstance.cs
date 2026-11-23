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

        var options = template.HitDiceOptions;
        InstanceHitDice = options is { Count: > 0 }
            ? options[Random.Shared.Next(options.Count)]
            : template.HitDice;

        // Roll HP based on HitDice (1d8 per hit die)
        var rolledHitPoints = RollHitPoints(InstanceHitDice, template.HitDiceType, template.ExtraHitPoints);
        MaxHitPoints = rolledHitPoints;
        CurrentHitPoints = rolledHitPoints;
        BaseArmorClass = template.ArmorClass;
        IsInLair = true;
        IsIdentified = string.IsNullOrWhiteSpace(template.UnidentifiedName);
    }

    private static int RollHitPoints(int hitDice, int hitDiceType = 8, int extraHitPoints = 0)
    {
        if (hitDice <= 0)
            return 1; // Minimum 1 HP

        // Default to d8 if hitDiceType is not specified or is 0
        if (hitDiceType <= 0)
            hitDiceType = 8;

    //    return DiceRoller.Roll(hitDice, hitDiceType, " Rolling hit points") + extraHitPoints;
        return DiceRoller.Roll(hitDice, hitDiceType, extraHitPoints,"Hit points");
    }

    public Monster Template { get; }
    public int InstanceHitDice { get; }
    public int Index { get; }
    public string GroupId { get; }
    public string Name { get; }
    public MonsterType InstanceMonsterType { get; }
    public int MaxHitPoints { get; }
    public int CurrentHitPoints { get; set; }
    public int BaseArmorClass { get; }
    public int ArmorClassModifier { get; private set; }
    public int ArmorClass => BaseArmorClass + ArmorClassModifier;
    public int Thac0Modifier { get; private set; }
    public bool IsInLair { get; set; }
    public bool HasRevealedLairTreasure { get; set; }
    public bool HasFled { get; set; }
    public bool IsAlive => CurrentHitPoints > 0;

    /// <summary>Wizardry monsters stay unidentified (shown by their Unidentified name) until the party identifies the group.</summary>
    public bool IsIdentified { get; set; }

    public bool CanBeUnidentified => !string.IsNullOrWhiteSpace(Template.UnidentifiedName)
        && !string.Equals(Template.UnidentifiedName.Trim(), Name?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The name the party sees: the real name once identified, otherwise the Unidentified name.</summary>
    public string ShownName => IsIdentified || !CanBeUnidentified ? Name : Template.UnidentifiedName.Trim();

    public string DisplayName => $"{ShownName} #{Index}";
    public string DisplayNameWithGroup => $"{ShownName} #{Index} (Group {GroupId})";

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

    public void AdjustThac0(int delta)
    {
        Thac0Modifier += delta;
    }
}
