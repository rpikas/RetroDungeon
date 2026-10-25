using Adnd.Core.Assassination;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private static bool HasAnySpecialAbility(MonsterInstance monster, params string[] abilityNames)
    {
        return GetMonsterAbilityNames(monster)
            .Any(name => abilityNames.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ApplyMonsterRegeneration(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasMonsterRegeneration(monster))
            return;

        if (session != null && session.GetMonsterWoundingActiveCount(monster) > 0)
            return;

        var regenCap = Math.Max(0, monster.MaxHitPoints - (session?.GetMonsterWoundingUnhealableDamage(monster) ?? 0));
        if (!monster.IsAlive || monster.CurrentHitPoints >= regenCap)
            return;

        var perRound = GetMonsterRegenerationPerRound(monster);
        if (perRound <= 0)
            perRound = 1;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Min(regenCap, monster.CurrentHitPoints + perRound);
        var healed = monster.CurrentHitPoints - before;

        if (healed > 0)
            events.Add(new CombatEvent($"{monster.DisplayName} regenerates {healed} HP. HP {before}->{monster.CurrentHitPoints}."));
    }

    public static bool HasMonsterRegeneration(MonsterInstance monster)
        => GetMonsterAbilityNames(monster)
            .Any(n => n != null && n.TrimStart().StartsWith("Regeneration", StringComparison.OrdinalIgnoreCase));

    private static int GetMonsterRegenerationPerRound(MonsterInstance monster)
    {
        if (monster?.Template?.SpecialDefenses == null)
            return 1;

        foreach (var defense in monster.Template.SpecialDefenses)
        {
            var defenseName = defense?.Name?.Trim() ?? string.Empty;
            if (!defenseName.StartsWith("Regeneration", StringComparison.OrdinalIgnoreCase))
                continue;

            var nameMatch = Regex.Match(defenseName, @"(\d+)\s*(?:hp|hit\s*points?)", RegexOptions.IgnoreCase);
            if (nameMatch.Success && int.TryParse(nameMatch.Groups[1].Value, out var fromName) && fromName > 0)
                return fromName;

            var description = defense.Description ?? string.Empty;
            var m = Regex.Match(description, @"regenerates\s+(?<n>\d+)\s*(?:hit\s*points|hp)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups["n"].Value, out var parsed) && parsed > 0)
                return parsed;
        }

        return 1;
    }

    private static void ApplySwordOfWoundingOngoingDamage(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (session == null || monster?.Template == null || !monster.IsAlive)
            return;

        var activeWounds = session.GetMonsterWoundingActiveCount(monster);
        if (activeWounds <= 0)
            return;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - activeWounds);
        var actual = before - monster.CurrentHitPoints;

        session.AddMonsterWoundingUnhealableDamage(monster, actual);
        var remainingWounds = session.TickMonsterWounding(monster);

        events.Add(new CombatEvent($"{monster.DisplayName} bleeds from Sword of Wounding for {actual} damage ({activeWounds} wound stack(s)). HP {before}->{monster.CurrentHitPoints}."));

        if (remainingWounds <= 0)
            events.Add(new CombatEvent($"Sword of Wounding bleed effect ends on {monster.DisplayName}."));

        if (monster.CurrentHitPoints <= 0)
            events.Add(new CombatEvent($"{monster.DisplayName} succumbs to Sword of Wounding blood loss."));
    }

    private bool TryResolveConfusedMonsterTurn(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var roundsRemaining = monster.GetStatusRounds(MonsterStatus.Confused);
        if (roundsRemaining <= 0)
            return false;

        // Save vs spell each round at -2.
        var saveTarget = monster.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = _dice.Roll(20);
        var effectiveRoll = Math.Max(1, saveRoll - 2);

        if (saveTarget > 0 && effectiveRoll >= saveTarget)
        {
            monster.SetStatus(MonsterStatus.Confused, 0);
            events.Add(new CombatEvent($"{monster.DisplayName} shakes off confusion (save {saveRoll}-2={effectiveRoll} vs {saveTarget})."));
            return false;
        }

        var actionRoll = _dice.Roll(100);
        if (actionRoll <= 10)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} wanders away in confusion and cannot act."));
            TickConfusedDuration(monster, events);
            return true;
        }

        if (actionRoll <= 60)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} stands confused and does nothing."));
            TickConfusedDuration(monster, events);
            return true;
        }

        if (actionRoll <= 80)
        {
            ResolveConfusedAttackNearestCreature(session, monster, events);
            TickConfusedDuration(monster, events);
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} turns on the druid's side in confusion!"));
        TickConfusedDuration(monster, events);
        return false;
    }

    private void TickConfusedDuration(MonsterInstance monster, List<CombatEvent> events)
    {
        var remaining = monster.TickStatus(MonsterStatus.Confused);
        if (remaining <= 0)
            events.Add(new CombatEvent($"{monster.DisplayName} is no longer confused."));
    }

    private void ResolveConfusedAttackNearestCreature(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var targetMonster = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, monster))
            .OrderBy(m => string.Equals(m.GroupId, monster.GroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.CurrentHitPoints)
            .FirstOrDefault();

        if (targetMonster == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} lashes out wildly but finds no nearby creature."));
            return;
        }

        ResolveMonsterAttackAgainstMonster(
            attacker: monster,
            target: targetMonster,
            events: events,
            missText: $"{monster.DisplayName} attacks nearest creature {targetMonster.DisplayName} in confusion but misses.",
            hitText: $"{monster.DisplayName} attacks nearest creature {targetMonster.DisplayName} in confusion",
            slainText: $"{targetMonster.DisplayName} is slain by the confused attack!");
    }

    private void ResolveCharmedMonsterTurn(CombatSession session, MonsterInstance charmedMonster, List<CombatEvent> events)
    {
        var hostileMonsters = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, charmedMonster))
            .Where(m => !m.HasStatus(MonsterStatus.Charmed))
            .OrderBy(m => string.Equals(m.GroupId, charmedMonster.GroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.CurrentHitPoints)
            .ToList();

        if (hostileMonsters.Count == 0)
        {
            events.Add(new CombatEvent($"{charmedMonster.DisplayName} is charmed and does nothing."));
            return;
        }

        var target = hostileMonsters[0];
        ResolveMonsterAttackAgainstMonster(
            attacker: charmedMonster,
            target: target,
            events: events,
            missText: $"{charmedMonster.DisplayName} fights for the Bard and misses {target.DisplayName}.",
            hitText: $"{charmedMonster.DisplayName} fights for the Bard and hits {target.DisplayName}",
            slainText: $"{target.DisplayName} is slain by the charmed ally!");
    }

    private void ResolveCharmedCharacterTurn(CombatSession session, Character charmedCharacter, List<CombatEvent> events)
    {
        var partyTargets = session.ExposedParty
            .Where(IsAlive)
            .Where(c => !ReferenceEquals(c, charmedCharacter))
            .ToList();

        if (partyTargets.Count == 0)
        {
            events.Add(new CombatEvent($"{charmedCharacter.Name} is charmed and has no ally to attack."));
            return;
        }

        var target = partyTargets[_dice.Roll(partyTargets.Count) - 1];
        ResolveCharacterAttackAgainstCharacter(
            attacker: charmedCharacter,
            target: target,
            events: events);
    }

    private void ResolveCharacterAttackAgainstCharacter(Character attacker, Character target, List<CombatEvent> events)
    {
        var mainHand = attacker.Equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) ? equipped : null;
        var attackName = mainHand?.Name ?? "Unarmed strike";
        var damageExpression = !string.IsNullOrWhiteSpace(mainHand?.Damage) ? mainHand!.Damage : "1d2";

        var thac0Modifier = attacker.Thac0StrengthModifier;
        if (mainHand != null)
            thac0Modifier += Math.Max(0, mainHand.ToHitBonus);

        var needed = Math.Clamp((attacker.Thac0 - thac0Modifier) - target.ArmorClass, 1, 20);
        var roll = _dice.Roll(20);
        if (roll < needed)
        {
            events.Add(new CombatEvent($"{attacker.Name} is charmed and attacks {target.Name} with {attackName}, but misses (rolled {roll} vs {needed})."));
            return;
        }

        var damage = RollDamage(damageExpression)
                     + attacker.DamageStrengthModifier
                     + (mainHand == null ? 0 : attacker.MainHandItemDamageModifier);
        damage = Math.Max(1, damage);

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actualDamage = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actualDamage, events);

        events.Add(new CombatEvent($"{attacker.Name} is charmed and attacks {target.Name} with {attackName} for {actualDamage} damage. HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0 && !target.HasStatus(CharacterStatus.Dead))
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain by the charmed attack!"));
        }
    }

    private void ResolveMonsterAttackAgainstMonster(
        MonsterInstance attacker,
        MonsterInstance target,
        List<CombatEvent> events,
        string missText,
        string hitText,
        string slainText)
    {
        var attack = attacker.Template.Attacks.FirstOrDefault()
                     ?? new MonsterAttack { Name = "Claw", NumberOfAttacks = 1, Damage = "1d4" };

        var thac0 = GetMonsterThac0(attacker);
        var needed = Math.Clamp(thac0 - target.ArmorClass, 1, 20);
        var roll = _dice.Roll(20);

        if (roll < needed)
        {
            events.Add(new CombatEvent(missText));
            return;
        }

        var damage = RollDamage(string.IsNullOrWhiteSpace(attack.Damage) ? "1d4" : attack.Damage);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeMonsterIfAsleepAfterDamage(target, actual, events);

        events.Add(new CombatEvent($"{hitText} for {actual} damage. HP {before}->{target.CurrentHitPoints}."));
        if (!target.IsAlive)
            events.Add(new CombatEvent(slainText));
    }
}
