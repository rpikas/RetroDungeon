using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private sealed class MonsterHold
    {
        public MonsterInstance Monster { get; init; } = null!;
        public string Kind { get; init; } = string.Empty;
        public int RoundsLeft { get; set; }
    }

    private readonly Dictionary<string, MonsterHold> _monsterHolds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ghostFeared = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<MonsterInstance> _level8OnceUsed = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _golemHasteRounds = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);
    private readonly Dictionary<MonsterInstance, int> _pyroBreathCount = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);

    private void ResetLevel8State()
    {
        _monsterHolds.Clear();
        _ghostFeared.Clear();
        _level8OnceUsed.Clear();
        _golemHasteRounds.Clear();
        _pyroBreathCount.Clear();
        _ironGolemBreathRound.Clear();
        _prince12dUsed.Clear();
        _archDevilSymbolUsed.Clear();
    }

    private static bool IsTemplate(MonsterInstance monster, string fragment) =>
        monster.Template.Name?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;

    // Returns true if the party member is held (smothered) and cannot act this round.
    private bool IsHeldByMonster(CombatSession session, Character member, List<CombatEvent> events)
    {
        if (!_monsterHolds.TryGetValue(member.Name, out var hold))
            return false;

        if (!hold.Monster.IsAlive || !IsAlive(member))
        {
            _monsterHolds.Remove(member.Name);
            if (IsAlive(member) && hold.Kind != "Brain")
                events.Add(new CombatEvent($"{member.Name} is freed as {hold.Monster.DisplayName} dies."));
            return false;
        }

        if (hold.Kind == "Brain")
            return false;

        if (hold.Kind == "Constrict")
        {
            events.Add(new CombatEvent($"{member.Name} is held fast by {hold.Monster.DisplayName} and cannot act."));
            return true;
        }

        events.Add(new CombatEvent($"{member.Name} is smothered by {hold.Monster.DisplayName} and cannot act ({hold.RoundsLeft} round(s) until death unless it is killed)."));
        return true;
    }

    private void KillCharacter(Character target)
    {
        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
    }

    private bool TryResolveLevel8Powers(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        TickGolemHaste(monster, events);

        if (TryResolveSmother(session, monster, events))
            return true;
        TickBrainExtraction(session, monster, events);
        ApplyBalorFlameWhip(session, monster, events);
        if (TryResolveDemonGate(session, monster, events))
            return true;
        if (TryResolveGolemHaste(session, monster, events))
            return false;
        if (TryResolveGhostPowers(session, monster, events))
            return true;
        if (TryResolveVampirePowers(session, monster, events))
            return true;
        return TryResolveMindBlast(session, monster, events);
    }

    private void ApplyBalorFlameWhip(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Flame Whip") || _dice.Roll(6) > 4)
            return;

        var targets = session.ExposedParty.Where(IsAlive).ToList();
        if (targets.Count == 0)
            return;

        var target = targets[_dice.Roll(targets.Count) - 1];
        var dice = _dice.Roll(3) + 1;
        var damage = _dice.RollMany(6, dice);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, before - damage);
        events.Add(new CombatEvent($"{monster.DisplayName} lashes {target.Name} with its flaming whip ({dice}d6) for {damage} damage. HP {before}->{target.CurrentHitPoints}."));
        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }
    }

    private bool TryResolveDemonGate(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var (ability, percent) = HasSpecialAbility(monster, "Nalfeshnee Gate") ? ("Nalfeshnee Gate", 60)
            : HasSpecialAbility(monster, "Marilith Gate") ? ("Marilith Gate", 50)
            : HasSpecialAbility(monster, "Balor Gate") ? ("Balor Gate", 70)
            : (string.Empty, 0);
        if (ability.Length == 0 || _nightHagGateAttempted.Contains(monster)
            || MonsterTemplateLookup == null || _dice.Roll(3) != 1)
            return false;

        _nightHagGateAttempted.Add(monster);
        var roll = _dice.Roll(100);
        var success = roll <= percent;
        RuleApplicationInfo.Publish("AD&D", ability, $"{monster.DisplayName} attempts to gate in another demon",
            $"{percent}% chance of success; one attempt per encounter.", "1", "100", roll.ToString(),
            success ? "Gate opens." : "Gate fails.");
        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in another demon but fails (roll {roll} vs {percent}%)."));
            return true;
        }

        var kind = _dice.Roll(100);
        string templateName;
        if (ability == "Nalfeshnee Gate")
            templateName = new[] { "Demon, Type I", "Demon, Type II ", "Demon, Type III", "Demon, Type IV" }[_dice.Roll(4) - 1];
        else if (ability == "Marilith Gate")
            templateName = kind <= 30 ? "Demon, Type I" : kind <= 55 ? "Demon, Type II " : kind <= 70 ? "Demon, Type III"
                : kind <= 85 ? "Demon, Type IV" : kind <= 95 ? "Demon, Type VI" : _dice.Roll(2) == 1 ? "Orcus" : "Demogorgon";
        else
            templateName = kind <= 80 ? "Demon, Type III" : "Demon, Type IV";

        var template = MonsterTemplateLookup(templateName) ?? MonsterTemplateLookup(templateName.Trim());
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var summoned = new MonsterInstance(template, 1, $"gate-{monster.GroupId}-{_dice.Roll(10000)}");
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
        return true;
    }

    private bool TryResolveSmother(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Smother"))
            return false;

        var isTrapper = IsTemplate(monster, "Trapper");
        var held = _monsterHolds.Where(h => ReferenceEquals(h.Value.Monster, monster) && h.Value.Kind == "Smother").ToList();

        foreach (var entry in held)
        {
            var victim = session.ExposedParty.FirstOrDefault(c => string.Equals(c.Name, entry.Key, StringComparison.OrdinalIgnoreCase));
            if (victim == null || !IsAlive(victim))
            {
                _monsterHolds.Remove(entry.Key);
                continue;
            }

            var damage = isTrapper ? Math.Max(1, 4 + victim.ArmorClass) : _dice.Roll(6);
            victim.CurrentHitPoints = Math.Max(0, victim.CurrentHitPoints - damage);
            entry.Value.RoundsLeft--;
            if (victim.CurrentHitPoints <= 0 || entry.Value.RoundsLeft <= 0)
            {
                KillCharacter(victim);
                _monsterHolds.Remove(entry.Key);
                events.Add(new CombatEvent($"{victim.Name} is smothered to death by {monster.DisplayName}!"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} smothers {victim.Name} for {damage} damage. HP {victim.CurrentHitPoints}/{victim.MaxHitPoints}; {entry.Value.RoundsLeft} round(s) until suffocation."));
            }
        }

        if (held.Count > 0 && _monsterHolds.Values.Any(h => ReferenceEquals(h.Monster, monster) && h.Kind == "Smother"))
            return true;

        var candidates = session.ExposedParty.Where(c => IsAlive(c) && !_monsterHolds.ContainsKey(c.Name)).ToList();
        if (candidates.Count == 0 || (isTrapper && _dice.Roll(2) != 1))
            return held.Count > 0;

        var targets = new List<Character>();
        var count = isTrapper ? 1 : Math.Min(candidates.Count, _dice.Roll(3));
        for (var i = 0; i < count; i++)
        {
            var pick = candidates[_dice.Roll(candidates.Count) - 1];
            candidates.Remove(pick);
            targets.Add(pick);
        }

        foreach (var target in targets)
        {
            var rounds = isTrapper ? 6 : _dice.Roll(4) + 1;
            _monsterHolds[target.Name] = new MonsterHold { Monster = monster, Kind = "Smother", RoundsLeft = rounds };
            events.Add(new CombatEvent(isTrapper
                ? $"{monster.DisplayName} closes over {target.Name} and begins to crush and smother (dies in {rounds} rounds unless the trapper is killed)."
                : $"{monster.DisplayName} drops from above onto {target.Name}, smothering (dies in {rounds} rounds unless the lurker is killed)."));
        }

        RuleApplicationInfo.PublishLinked("MM", monster.Template.Name ?? "Smother",
            isTrapper ? "Trapper: 4 + victim AC damage per round; victim cannot act and suffocates in 6 rounds."
                : "Lurker above: 1-6 damage per round; victims cannot act and suffocate in 2-5 rounds.");
        return true;
    }

    private void TryApplyLevel8OnHit(CombatSession session, MonsterInstance monster, Character target, string attackName, List<CombatEvent> events)
    {
        if (!IsAlive(target))
            return;

        if (HasSpecialAbility(monster, "Ghost Touch"))
        {
            var years = 10 * _dice.Roll(4);
            target.AdvanceAgeByDays(years * 365);
            events.Add(new CombatEvent($"{monster.DisplayName}'s touch ages {target.Name} by {years} years!"));
        }

        if (HasSpecialAbility(monster, "Brain Extraction") && !_monsterHolds.ContainsKey(target.Name))
        {
            var rounds = _dice.Roll(4);
            _monsterHolds[target.Name] = new MonsterHold { Monster = monster, Kind = "Brain", RoundsLeft = rounds };
            events.Add(new CombatEvent($"{monster.DisplayName}'s tentacle fastens on {target.Name}'s head; it will draw out the brain in {rounds} round(s) unless the flayer is killed."));
        }

        if (HasSpecialAbility(monster, "Poisonous Bite")
            && string.Equals(attackName, "Bite", StringComparison.OrdinalIgnoreCase))
        {
            if (target.IsMonkImmuneToPoison())
            {
                events.Add(new CombatEvent($"{target.Name} is immune to poison."));
                return;
            }

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s poisonous bite (save {saveRoll} vs {saveTarget})."));
            }
            else
            {
                KillCharacter(target);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) against {monster.DisplayName}'s poison and dies."));
            }
        }
    }

    private void TickBrainExtraction(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        foreach (var entry in _monsterHolds.Where(h => ReferenceEquals(h.Value.Monster, monster) && h.Value.Kind == "Brain").ToList())
        {
            entry.Value.RoundsLeft--;
            if (entry.Value.RoundsLeft > 0)
                continue;

            _monsterHolds.Remove(entry.Key);
            events.Add(new CombatEvent($"{monster.DisplayName} draws out {entry.Key}'s brain, killing them instantly!"));
            var victim = session.ExposedParty.FirstOrDefault(c => string.Equals(c.Name, entry.Key, StringComparison.OrdinalIgnoreCase));
            if (victim != null)
                KillCharacter(victim);
        }
    }

    private bool TryResolveMindBlast(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Mind Blast") || _dice.Roll(3) != 1)
            return false;

        var candidates = session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Paralyzed)).ToList();
        if (candidates.Count == 0)
            return false;

        var count = Math.Min(candidates.Count, _dice.Roll(3));
        events.Add(new CombatEvent($"{monster.DisplayName} unleashes a mind blast!"));
        for (var i = 0; i < count; i++)
        {
            var target = candidates[_dice.Roll(candidates.Count) - 1];
            candidates.Remove(target);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);
            RuleApplicationInfo.Publish("AD&D", "Mind Blast", $"{monster.DisplayName} on {target.Name}",
                "Psionic mind blast (cone 6\" long); save vs. spell or be stunned for 3-12 rounds.", "1", "20", saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Stunned.")}");
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists the mind blast (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.RollMany(4, 3);
            target.ApplyParalysis(rounds);
            events.Add(new CombatEvent($"{target.Name} is stunned by the mind blast for {rounds} rounds (save {saveRoll} vs {saveTarget})."));
        }

        return true;
    }

    private void TickGolemHaste(MonsterInstance monster, List<CombatEvent> events)
    {
        if (!_golemHasteRounds.TryGetValue(monster, out var rounds) || rounds <= 0)
            return;

        _golemHasteRounds[monster] = rounds - 1;
        if (rounds - 1 == 0)
            events.Add(new CombatEvent($"{monster.DisplayName}'s burst of speed ends."));
    }

    private bool TryResolveGolemHaste(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Golem Haste") || _level8OnceUsed.Contains(monster) || session.RoundNumber < 2 || _dice.Roll(3) != 1)
            return false;

        _level8OnceUsed.Add(monster);
        _golemHasteRounds[monster] = 3;
        events.Add(new CombatEvent($"{monster.DisplayName} hastes itself for 3 rounds and attacks twice per round!"));
        RuleApplicationInfo.PublishLinked("MM", "Golem, Clay", "Once per day a clay golem can haste itself for 3 rounds (two attacks per round) after at least one round of combat.");
        return true;
    }

    private bool TryResolveGhostPowers(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var acted = false;
        if (HasSpecialAbility(monster, "Ghost Fear"))
        {
            foreach (var target in session.ExposedParty.Where(c => IsAlive(c) && !_ghostFeared.Contains(c.Name)).ToList())
            {
                _ghostFeared.Add(target.Name);
                if (target.Classes.Contains(CharacterClass.Cleric) && target.Level > 6)
                {
                    events.Add(new CombatEvent($"{target.Name} is unmoved by the sight of {monster.DisplayName} (cleric above level 6)."));
                    continue;
                }

                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                var saveRoll = _dice.Roll(20) + (!target.Classes.Contains(CharacterClass.Cleric) && target.Level > 8 ? 2 : 0);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} withstands the horrible sight of {monster.DisplayName} (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                target.AdvanceAgeByDays(10 * 365);
                var turns = _dice.RollMany(6, 2);
                _frightenedRounds[target.Name] = turns * 10;
                events.Add(new CombatEvent($"{target.Name} ages 10 years at the sight of {monster.DisplayName} and flees for {turns} turns!"));
                acted = true;
            }
        }

        if (HasSpecialAbility(monster, "Magic Jar") && !_level8OnceUsed.Contains(monster) && _dice.Roll(3) == 1)
        {
            var targets = session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Charmed)).ToList();
            if (targets.Count > 0)
            {
                _level8OnceUsed.Add(monster);
                var target = targets[_dice.Roll(targets.Count) - 1];
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s magic jar attempt (save {saveRoll} vs {saveTarget}); the ghost is left semi-materialised."));
                }
                else
                {
                    target.AddStatus(CharacterStatus.Charmed);
                    events.Add(new CombatEvent($"{monster.DisplayName} possesses {target.Name} (magic jar, save {saveRoll} vs {saveTarget}); they attack their allies."));
                }

                acted = true;
            }
        }

        return acted;
    }

    private bool TryResolveVampirePowers(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (HasSpecialAbility(monster, "Summon Creatures") && !_level8OnceUsed.Contains(monster)
            && MonsterTemplateLookup != null && _dice.Roll(4) == 1)
        {
            _level8OnceUsed.Add(monster);
            var wolves = _dice.Roll(2) == 1;
            var template = wolves ? MonsterTemplateLookup("Wolf") : MonsterTemplateLookup("Rat, Giant") ?? MonsterTemplateLookup("Giant Rat");
            if (template != null)
            {
                var count = wolves ? _dice.RollMany(6, 3) : _dice.Roll(10) * 10;
                var summoned = new MonsterInstance(template, Math.Min(count, wolves ? 18 : 12), $"summon-{monster.GroupId}-{_dice.Roll(10000)}");
                session.Monsters.Add(summoned);
                events.Add(new CombatEvent($"{monster.DisplayName} calls {(wolves ? "wolves" : "rats")} from the night; {summoned.DisplayName} arrive!"));
                return true;
            }
        }

        if (!HasSpecialAbility(monster, "Charm Gaze") || _dice.Roll(3) != 1)
            return false;

        var targets = session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Charmed)).ToList();
        if (targets.Count == 0)
            return false;

        var target = targets[_dice.Roll(targets.Count) - 1];
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
        var saveRoll = _dice.Roll(20) - 2;
        RuleApplicationInfo.Publish("AD&D", "Charm Gaze", $"{monster.DisplayName} on {target.Name}",
            "Vampire gaze charms unless save vs. spell is made (at -2).", "1", "20", saveRoll.ToString(),
            $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Charmed.")}");
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s gaze (save {saveRoll} vs {saveTarget})."));
            return true;
        }

        target.AddStatus(CharacterStatus.Charmed);
        events.Add(new CombatEvent($"{target.Name} is charmed by {monster.DisplayName}'s gaze and will attack party allies."));
        return true;
    }
}
