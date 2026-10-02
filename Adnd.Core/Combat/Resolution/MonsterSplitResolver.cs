using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Combat.Resolution;

internal enum JellySplitTrigger
{
    Slashing,
    Lightning
}

internal static class MonsterSplitResolver
{
    internal static bool TryResolveSplit(
        CombatSession? session,
        MonsterInstance? target,
        int actualDamage,
        JellySplitTrigger trigger,
        Action<string> addEvent)
    {
        if (session == null || target?.Template == null || actualDamage <= 0)
            return false;

        if (!HasSplitAbility(target, trigger))
            return false;

        var remainingHp = Math.Max(1, target.CurrentHitPoints);
        var firstHp = Math.Max(1, (remainingHp + 1) / 2);
        var secondHp = Math.Max(1, remainingHp / 2);

        target.CurrentHitPoints = 0;

        var splitTemplateA = CreateSplitTemplate(target.Template, firstHp);
        var splitTemplateB = CreateSplitTemplate(target.Template, secondHp);

        var nextIndex = session.Monsters
            .Where(m => string.Equals(m.GroupId, target.GroupId, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Index)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var splitA = new MonsterInstance(splitTemplateA, nextIndex++, target.GroupId)
        {
            IsInLair = target.IsInLair
        };

        var splitB = new MonsterInstance(splitTemplateB, nextIndex, target.GroupId)
        {
            IsInLair = target.IsInLair
        };

        session.Monsters.Add(splitA);
        session.Monsters.Add(splitB);

        var triggerText = trigger == JellySplitTrigger.Slashing ? "slashing" : "lightning";
        addEvent($"{target.DisplayName} splits from {triggerText} damage into two smaller jellies!");
        addEvent($"{splitA.DisplayName} forms with {splitA.CurrentHitPoints} HP and attacks for 1d6 damage.");
        addEvent($"{splitB.DisplayName} forms with {splitB.CurrentHitPoints} HP and attacks for 1d6 damage.");

        return true;
    }

    private static bool HasSplitAbility(MonsterInstance target, JellySplitTrigger trigger)
    {
        foreach (var ability in target.Template.SpecialAbilities)
        {
            if (!string.Equals(ability.Name?.Trim(), "Split", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = ability.Description?.Trim() ?? string.Empty;
            if (trigger == JellySplitTrigger.Slashing
                && text.Contains("slashing", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trigger == JellySplitTrigger.Lightning
                && (text.Contains("lightning", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("electr", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static Monster CreateSplitTemplate(Monster original, int hp)
    {
        return new Monster
        {
            Name = $"{original.Name} (Small)",
            Type = original.Type,
            TypeName = original.TypeName,
            ClimateTerain = original.ClimateTerain,
            Frequency = original.Frequency,
            ActivityCycle = original.ActivityCycle,
            Intelligence = original.Intelligence,
            Alignment = original.Alignment,
            NumberOfAppearancesMin = 1,
            NumberOfAppearancesMax = 1,
            ArmorClass = original.ArmorClass,
            MovementRate = original.MovementRate,
            Movement = original.Movement,
            HitDice = 1,
            HitDiceType = 1,
            ExtraHitPoints = Math.Max(0, hp - 1),
            THAC0 = original.THAC0,
            NumberOfAttacks = 1,
            MagicResistance = original.MagicResistance,
            MagicResistancePercent = original.MagicResistancePercent,
            InLairPercent = original.InLairPercent,
            Size = MonsterSize.Small,
            Source = original.Source,
            SavingThrows = original.SavingThrows,
            Morale = original.Morale,
            BaseXPValue = original.BaseXPValue,
            XPValuePerHitPoint = original.XPValuePerHitPoint,
            TreasureType = "None",
            IndividualTreasure = "None",
            Attacks = new List<MonsterAttack>
            {
                new() { Name = "Acid Touch", NumberOfAttacks = 1, Damage = "1d6" }
            },
            SpecialAttacks = original.SpecialAttacks
                .Select(a => new MonsterSpecialAbility { Name = a.Name, Description = a.Description })
                .ToList(),
            SpecialDefenses = original.SpecialDefenses
                .Select(d => new MonsterSpecialAbility { Name = d.Name, Description = d.Description })
                .ToList(),
            SpecialAbilities = original.SpecialAbilities
                .Where(a => !string.Equals(a.Name?.Trim(), "Split", StringComparison.OrdinalIgnoreCase))
                .Select(a => new MonsterSpecialAbility { Name = a.Name, Description = a.Description })
                .ToList()
        };
    }
}
