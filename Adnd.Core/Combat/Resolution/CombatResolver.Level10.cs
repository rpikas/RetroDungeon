using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private readonly Dictionary<MonsterInstance, int> _ironGolemBreathRound = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);

    private bool TryResolveLevel10Powers(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (TryResolveBeholderRays(session, monster, events))
            return true;
        if (TryResolveIronGolemBreath(session, monster, events))
            return true;
        if (TryResolveLichFear(session, monster, events))
            return false;
        if (TryResolvePrinceGate(session, monster, events))
            return true;
        if (TryResolvePrinceLightning(session, monster, events))
            return true;
        if (TryResolveWandOfOrcus(session, monster, events))
            return true;
        if (TryResolveDemogorgonGaze(session, monster, events))
            return true;
        if (TryResolveArchDevilGaze(session, monster, events))
            return false;
        if (TryResolveArchDevilSymbol(session, monster, events))
            return true;
        return TryResolveArchDevilSummon(session, monster, events);
    }

    private bool SaveVsSpell(Character target, int modifier = 0, SaveThrowType type = SaveThrowType.Spell)
    {
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, type));
        return _dice.Roll(20) + modifier >= saveTarget;
    }

    private bool TryResolveBeholderRays(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Beholder Eye Rays"))
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var eyes = Math.Min(10, 1 + _dice.Roll(4));
        events.Add(new CombatEvent($"{monster.DisplayName}'s eyestalks swivel and {eyes} eye(s) open!"));
        for (var i = 0; i < eyes && party.Count > 0; i++)
        {
            var target = party[_dice.Roll(party.Count) - 1];
            var eye = _dice.Roll(10);
            ApplyBeholderEye(session, monster, target, eye, events);
            if (!IsAlive(target))
                party.Remove(target);
        }

        return false;
    }

    private void ApplyBeholderEye(CombatSession session, MonsterInstance monster, Character target, int eye, List<CombatEvent> events)
    {
        var names = new[] { "charm person", "charm monster", "sleep", "telekinesis", "flesh to stone", "disintegrate", "fear", "slow", "cause serious wounds", "death ray" };
        var label = names[eye - 1];
        var kind = eye switch { 5 => SaveThrowType.PetrificationPolymorph, 6 or 10 => SaveThrowType.ParalyzationPoisonDeath, _ => SaveThrowType.Spell };
        if (eye == 4)
        {
            var dmg = _dice.Roll(6);
            events.Add(new CombatEvent($"{monster.DisplayName}'s telekinesis eye hurls {target.Name} about for {dmg} damage."));
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - dmg);
            if (target.CurrentHitPoints <= 0)
                KillCharacter(target);
            return;
        }

        if (eye == 9)
        {
            var dmg = _dice.Roll(8) + _dice.Roll(8) + 1;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - dmg);
            events.Add(new CombatEvent($"{target.Name} is struck by {monster.DisplayName}'s cause serious wounds eye for {dmg} damage."));
            if (target.CurrentHitPoints <= 0)
                KillCharacter(target);
            return;
        }

        if (SaveVsSpell(target, 0, kind))
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s {label} eye."));
            return;
        }

        switch (eye)
        {
            case 1:
            case 2:
                target.AddStatus(CharacterStatus.Charmed);
                break;
            case 3:
                if (target.Level <= 4 || _dice.Roll(2) == 1)
                    target.AddStatus(CharacterStatus.Asleep);
                break;
            case 5:
                target.AddStatus(CharacterStatus.Petrified);
                break;
            case 6:
            case 10:
                KillCharacter(target);
                break;
            case 7:
                _frightenedRounds[target.Name] = 6;
                break;
            case 8:
                if (!target.IsMonkImmuneToDiseaseSlowHaste())
                    target.AddStatus(CharacterStatus.Slowed);
                break;
        }

        events.Add(new CombatEvent($"{target.Name} is hit by {monster.DisplayName}'s {label} eye!"));
    }

    private bool TryResolveIronGolemBreath(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Iron Golem Poison Breath"))
            return false;

        if (!_ironGolemBreathRound.TryGetValue(monster, out var last))
            last = session.RoundNumber - 7 + 1;
        if (session.RoundNumber - last < 7 || monster.HasStatus(MonsterStatus.Slowed) && session.RoundNumber % 2 != 0)
            return false;

        _ironGolemBreathRound[monster] = session.RoundNumber;
        events.Add(new CombatEvent($"{monster.DisplayName} breathes a cloud of poisonous gas!"));
        foreach (var target in session.ExposedParty.Where(IsAlive).ToList())
        {
            if (SaveVsSpell(target, 0, SaveThrowType.ParalyzationPoisonDeath))
            {
                events.Add(new CombatEvent($"{target.Name} survives the poison gas."));
                continue;
            }

            KillCharacter(target);
            events.Add(new CombatEvent($"{target.Name} is killed by the poison gas!"));
        }

        return true;
    }

    private bool TryResolveLichFear(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Lich Fear") || !_level8OnceUsed.Add(monster))
            return false;

        foreach (var target in session.ExposedParty.Where(c => IsAlive(c) && c.Level < 5).ToList())
        {
            _frightenedRounds[target.Name] = 6;
            events.Add(new CombatEvent($"{target.Name} flees in panic at the sight of {monster.DisplayName}!"));
        }

        return true;
    }

    private bool TryResolvePrinceGate(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var percent = HasSpecialAbility(monster, "Orcus Gate") ? 80 : HasSpecialAbility(monster, "Demogorgon Gate") ? 85 : 0;
        if (percent == 0 || _nightHagGateAttempted.Contains(monster) || MonsterTemplateLookup == null || _dice.Roll(3) != 1)
            return false;

        _nightHagGateAttempted.Add(monster);
        var roll = _dice.Roll(100);
        if (roll > percent)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in a demon but fails (roll {roll} vs {percent}%)."));
            return true;
        }

        string name;
        if (percent == 80)
            name = _dice.Roll(100) <= 50 ? new[] { "Demon, Type I", "Demon, Type II ", "Demon, Type III", "Demon, Type IV" }[_dice.Roll(4) - 1] : "Demon, Type V";
        else
            name = _dice.Roll(2) == 1 ? new[] { "Demon, Type I", "Demon, Type II ", "Demon, Type III", "Demon, Type IV" }[_dice.Roll(4) - 1] : _dice.Roll(2) == 1 ? "Demon, Type V" : "Demon, Type VI";

        var template = MonsterTemplateLookup(name) ?? MonsterTemplateLookup(name.Trim());
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var summoned = new MonsterInstance(template, 1, $"gate-{monster.GroupId}-{_dice.Roll(10000)}");
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));

        if (percent == 80 && _level8OnceUsed.Add(monster) && MonsterTemplateLookup("Skeleton") is { } undead)
        {
            var count = Math.Min(8, _dice.Roll(8));
            for (var i = 1; i <= count; i++)
                session.Monsters.Add(new MonsterInstance(undead, i, $"orcus-undead-{_dice.Roll(10000)}"));
            events.Add(new CombatEvent($"Orcus calls up {count} skeletons to serve him!"));
        }

        return true;
    }

    private bool TryResolvePrinceLightning(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Orcus Lightning") || _prince12dUsed.Contains(monster) || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        _prince12dUsed.Add(monster);
        events.Add(new CombatEvent($"{monster.DisplayName} hurls a 12-die lightning bolt!"));
        foreach (var target in party)
        {
            var damage = _dice.RollMany(6, 12);
            var saved = SaveVsSpell(target);
            if (saved)
                damage /= 2;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            events.Add(new CombatEvent($"{target.Name} takes {damage} lightning damage{(saved ? " (saved, half)" : string.Empty)}."));
            if (target.CurrentHitPoints <= 0)
                KillCharacter(target);
        }

        return true;
    }

    private readonly HashSet<MonsterInstance> _prince12dUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);

    private bool TryResolveWandOfOrcus(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Wand of Orcus") || _dice.Roll(4) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var target = party[_dice.Roll(party.Count) - 1];
        if (SaveVsSpell(target, -4, SaveThrowType.ParalyzationPoisonDeath))
        {
            events.Add(new CombatEvent($"{monster.DisplayName} touches {target.Name} with the Wand of Orcus, but {target.Name} resists annihilation!"));
            return true;
        }

        KillCharacter(target);
        events.Add(new CombatEvent($"{monster.DisplayName} touches {target.Name} with the Wand of Orcus; {target.Name} is annihilated!"));
        return true;
    }

    private bool TryResolveDemogorgonGaze(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Demogorgon Gaze") || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var mass = _dice.Roll(2) == 1;
        var targets = mass ? party : new List<Character> { party[_dice.Roll(party.Count) - 1] };
        events.Add(new CombatEvent(mass
            ? $"{monster.DisplayName} fixes the gaze of both heads on the party, seeking to hypnotize!"
            : $"{monster.DisplayName}'s right head gazes into {targets[0].Name}'s eyes, seeking to cause insanity!"));
        foreach (var target in targets)
        {
            if (mass && target.Level >= 15 && SaveVsSpell(target) || !mass && SaveVsSpell(target))
            {
                events.Add(new CombatEvent($"{target.Name} avoids the gaze."));
                continue;
            }

            session.SetPartyConfused(target.Name, mass ? 10 : 10 * _dice.Roll(6));
            target.AddStatus(CharacterStatus.Confused);
            events.Add(new CombatEvent($"{target.Name} falls under the demon's {(mass ? "hypnosis" : "insane gaze")}!"));
        }

        return true;
    }

    private bool TryResolveArchDevilGaze(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var fear = HasSpecialAbility(monster, "Arch-devil Fear Gaze");
        var chill = HasSpecialAbility(monster, "Arch-devil Chill Gaze");
        if ((!fear && !chill) || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var target = party[_dice.Roll(party.Count) - 1];
        var useChill = chill && (!fear || _dice.Roll(2) == 1);
        if (SaveVsSpell(target))
        {
            events.Add(new CombatEvent($"{target.Name} withstands {monster.DisplayName}'s gaze."));
            return true;
        }

        if (useChill && !target.IsMonkImmuneToDiseaseSlowHaste())
        {
            target.AddStatus(CharacterStatus.Slowed);
            events.Add(new CombatEvent($"{monster.DisplayName}'s gaze chills {target.Name}, who is slowed!"));
        }
        else
        {
            _frightenedRounds[target.Name] = _dice.Roll(4);
            events.Add(new CombatEvent($"{target.Name} collapses in trembling fear under {monster.DisplayName}'s gaze!"));
        }

        return true;
    }

    private bool TryResolveArchDevilSymbol(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Arch-devil Symbols") || _archDevilSymbolUsed.Contains(monster) || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        _archDevilSymbolUsed.Add(monster);
        var kinds = IsTemplate(monster, "Asmodeus") ? 3 : IsTemplate(monster, "Baalzebul") ? 2 : 1;
        var kind = _dice.Roll(kinds);
        events.Add(new CombatEvent($"{monster.DisplayName} inscribes a symbol of {(kind == 1 ? "pain" : kind == 2 ? "insanity" : "hopelessness")}!"));
        foreach (var target in party)
        {
            if (SaveVsSpell(target))
            {
                events.Add(new CombatEvent($"{target.Name} resists the symbol."));
                continue;
            }

            if (kind == 1 && !target.IsMonkImmuneToDiseaseSlowHaste())
                target.AddStatus(CharacterStatus.Slowed);
            else if (kind == 2)
            {
                session.SetPartyConfused(target.Name, 10);
                target.AddStatus(CharacterStatus.Confused);
            }
            else if (kind == 3)
                _frightenedRounds[target.Name] = 10;
            events.Add(new CombatEvent($"{target.Name} is overcome by the symbol!"));
        }

        return true;
    }

    private readonly HashSet<MonsterInstance> _archDevilSymbolUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);

    private bool TryResolveArchDevilSummon(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Arch-devil Summon") || _nightHagGateAttempted.Contains(monster)
            || MonsterTemplateLookup == null || _dice.Roll(3) != 1)
            return false;

        _nightHagGateAttempted.Add(monster);
        string name;
        int count;
        if (IsTemplate(monster, "Baalzebul"))
            (name, count) = ("Devil, Horned", _dice.Roll(4));
        else if (IsTemplate(monster, "Dispater"))
        {
            var pit = _dice.Roll(100) <= 25;
            if (pit && _dice.Roll(100) > 90)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} tries to summon a pit fiend but fails."));
                return true;
            }

            (name, count) = pit ? ("Devil, Pit Fiend", 1) : ("Devil, Erinyes", _dice.Roll(3));
        }
        else
            (name, count) = _dice.Roll(3) == 1 ? ("Devil, Pit Fiend", 1) : ("Devil, Barbed", 2);

        var template = MonsterTemplateLookup(name);
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} calls for aid, but nothing answers."));
            return true;
        }

        var group = $"gate-{monster.GroupId}-{_dice.Roll(10000)}";
        for (var i = 1; i <= count; i++)
        {
            var summoned = new MonsterInstance(template, i, group);
            session.Monsters.Add(summoned);
            events.Add(new CombatEvent($"{monster.DisplayName} summons {summoned.DisplayName}!"));
        }

        return true;
    }

    private void TryApplyLevel10OnHit(MonsterInstance monster, Character target, string attackName, List<CombatEvent> events)
    {
        if (!IsAlive(target))
            return;

        if (HasSpecialAbility(monster, "Poison Attack (-4)") && !string.Equals(attackName, "Fist", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(attackName, "Claw", StringComparison.OrdinalIgnoreCase))
        {
            if (SaveVsSpell(target, -4, SaveThrowType.ParalyzationPoisonDeath))
                events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s poison."));
            else
            {
                KillCharacter(target);
                events.Add(new CombatEvent($"{target.Name} dies from {monster.DisplayName}'s poison!"));
                return;
            }
        }

        if (HasSpecialAbility(monster, "Tail Level Drain") && string.Equals(attackName, "Tail", StringComparison.OrdinalIgnoreCase))
        {
            var levels = _dice.Roll(4);
            var drained = 0;
            for (var i = 0; i < levels && TryDrainOneLevel(target, out _, out _); i++)
                drained++;
            events.Add(new CombatEvent($"{monster.DisplayName}'s tail drains {drained} level(s) from {target.Name}!"));
        }

        if (HasSpecialAbility(monster, "Tentacle Rot") && string.Equals(attackName, "Tentacle", StringComparison.OrdinalIgnoreCase))
        {
            var loss = Math.Max(1, target.MaxHitPoints / 4);
            target.MaxHitPoints = Math.Max(1, target.MaxHitPoints - loss);
            target.CurrentHitPoints = Math.Min(target.CurrentHitPoints, target.MaxHitPoints);
            events.Add(new CombatEvent($"{target.Name}'s flesh rots at the touch of {monster.DisplayName}'s tentacle (-{loss} maximum hit points)."));
        }

        if (HasSpecialAbility(monster, "Lich Paralyzing Touch") && !target.HasStatus(CharacterStatus.Paralyzed))
        {
            if (SaveVsSpell(target, 0, SaveThrowType.ParalyzationPoisonDeath))
                events.Add(new CombatEvent($"{target.Name} resists the lich's paralyzing touch."));
            else
            {
                target.ApplyParalysis(30);
                events.Add(new CombatEvent($"{target.Name} is paralyzed by the lich's icy touch!"));
            }
        }
    }
}
