using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private bool TryResolveLevel9Powers(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        TickConstriction(session, monster, events);

        if (TryResolveStormGiantLightning(session, monster, events))
            return true;
        if (TryResolvePitFiendPowers(session, monster, events))
            return true;
        return TryResolveStoneGolemSlow(session, monster, events);
    }

    private void TickConstriction(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        foreach (var entry in _monsterHolds.Where(h => ReferenceEquals(h.Value.Monster, monster) && h.Value.Kind == "Constrict").ToList())
        {
            var victim = session.ExposedParty.FirstOrDefault(c => string.Equals(c.Name, entry.Key, StringComparison.OrdinalIgnoreCase));
            if (victim == null || !IsAlive(victim))
            {
                _monsterHolds.Remove(entry.Key);
                continue;
            }

            var damage = _dice.Roll(7) + 1;
            victim.CurrentHitPoints = Math.Max(0, victim.CurrentHitPoints - damage);
            events.Add(new CombatEvent($"{monster.DisplayName}'s tail crushes {victim.Name} for {damage} damage. HP {victim.CurrentHitPoints}/{victim.MaxHitPoints}."));
            if (victim.CurrentHitPoints <= 0)
            {
                KillCharacter(victim);
                _monsterHolds.Remove(entry.Key);
                events.Add(new CombatEvent($"{victim.Name} is crushed to death!"));
            }
        }
    }

    private void TryApplyPitFiendTail(MonsterInstance monster, Character target, string attackName, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Pit Fiend Constrict")
            || !string.Equals(attackName, "Tail", StringComparison.OrdinalIgnoreCase)
            || !IsAlive(target) || _monsterHolds.ContainsKey(target.Name))
            return;

        _monsterHolds[target.Name] = new MonsterHold { Monster = monster, Kind = "Constrict", RoundsLeft = int.MaxValue };
        events.Add(new CombatEvent($"{monster.DisplayName}'s tail coils around {target.Name}, holding them fast (2-8 constriction damage per round until the pit fiend dies)."));
    }

    private bool TryResolvePitFiendPowers(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var canGate = HasSpecialAbility(monster, "Pit Fiend Gate") && !_nightHagGateAttempted.Contains(monster) && MonsterTemplateLookup != null;
        var canSymbol = HasSpecialAbility(monster, "Symbol of Pain") && !_level8OnceUsed.Contains(monster);
        if ((!canGate && !canSymbol) || _dice.Roll(3) != 1)
            return false;

        if (canSymbol && (!canGate || _dice.Roll(2) == 1))
        {
            var party = session.ExposedParty.Where(IsAlive).ToList();
            if (party.Count == 0)
                return false;

            _level8OnceUsed.Add(monster);
            events.Add(new CombatEvent($"{monster.DisplayName} traces a symbol of pain!"));
            foreach (var target in party)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists the symbol of pain (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                if (target.IsMonkImmuneToDiseaseSlowHaste())
                    continue;

                target.AddStatus(CharacterStatus.Slowed);
                events.Add(new CombatEvent($"{target.Name} is wracked by pain and fights at a severe penalty (save {saveRoll} vs {saveTarget})."));
            }

            return true;
        }

        _nightHagGateAttempted.Add(monster);
        var kindRoll = _dice.Roll(100);
        var barbed = _dice.Roll(100) <= 50;
        var percent = barbed ? 60 : 70;
        RuleApplicationInfo.Publish("AD&D", "Pit Fiend Gate", $"{monster.DisplayName} attempts to gate in devils",
            "Gates in 1-3 barbed devils (60% success) or another pit fiend (70% success).",
            "1", "100", kindRoll.ToString(), kindRoll <= percent ? "Gate opens." : "Gate fails.");
        if (kindRoll > percent)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in devils but fails (roll {kindRoll} vs {percent}%)."));
            return true;
        }

        var template = MonsterTemplateLookup!(barbed ? "Devil, Barbed" : "Devil, Pit Fiend");
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var gateGroup = $"gate-{monster.GroupId}-{_dice.Roll(10000)}";
        var count = barbed ? _dice.Roll(3) : 1;
        for (var i = 1; i <= count; i++)
        {
            var summoned = new MonsterInstance(template, i, gateGroup);
            session.Monsters.Add(summoned);
            events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
        }

        return true;
    }

    private bool TryResolveStormGiantLightning(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var canBolt = HasSpecialAbility(monster, "Storm Lightning Bolt") && !_level8OnceUsed.Contains(monster);
        var callUses = _pyroBreathCount.GetValueOrDefault(monster);
        var canCall = HasSpecialAbility(monster, "Call Lightning") && callUses < 3 && session.RoundNumber >= 2;
        if ((!canBolt && !canCall) || _dice.Roll(2) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var isBolt = canBolt;
        if (isBolt)
            _level8OnceUsed.Add(monster);
        else
            _pyroBreathCount[monster] = callUses + 1;

        var diceCount = isBolt ? 8 : 10 + _dice.Roll(6) - 1;
        var sides = isBolt ? 8 : 6;
        var name = isBolt ? "lightning bolt (8d8)" : "called lightning";
        var count = Math.Min(party.Count, isBolt ? _dice.Roll(3) : 1);
        events.Add(new CombatEvent($"{monster.DisplayName} hurls a {name}!"));
        for (var i = 0; i < count; i++)
        {
            var target = party[_dice.Roll(party.Count) - 1];
            party.Remove(target);
            var damage = _dice.RollMany(sides, diceCount);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);
            var saved = saveRoll >= saveTarget;
            if (saved)
                damage /= 2;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, before - damage);
            WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
            events.Add(new CombatEvent($"{target.Name} is struck for {before - target.CurrentHitPoints} damage ({diceCount}d{sides}, save {saveRoll} vs {saveTarget}{(saved ? ", half" : string.Empty)}). HP {before}->{target.CurrentHitPoints}."));
            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private bool TryResolveStoneGolemSlow(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Golem Slow") || session.RoundNumber % 2 != 0)
            return false;

        var targets = session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Slowed) && !c.IsMonkImmuneToDiseaseSlowHaste()).ToList();
        if (targets.Count == 0)
            return false;

        var target = targets[_dice.Roll(targets.Count) - 1];
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s slow spell (save {saveRoll} vs {saveTarget})."));
            return false;
        }

        target.AddStatus(CharacterStatus.Slowed);
        events.Add(new CombatEvent($"{monster.DisplayName} casts slow on {target.Name} (save {saveRoll} vs {saveTarget})."));
        return false;
    }
}
