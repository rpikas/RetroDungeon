using System.Text.RegularExpressions;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Dices;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;

namespace Adnd.Core.Combat.Resolution;

public sealed class CombatResolver
{
    private readonly IDice _dice;
    private readonly SpellCastingService? _spellCastingService;

    public CombatResolver(IDice? dice = null, SpellCastingService? spellCastingService = null)
    {
        _dice = dice ?? new SystemDice();
        _spellCastingService = spellCastingService;
    }

    public List<CombatEvent> ResolveRound(CombatSession session, IReadOnlyDictionary<string, CombatActionType> partyActions)
    {
        var converted = new Dictionary<string, CombatAction>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type) in partyActions)
        {
            if (type is not (CombatActionType.Spell or CombatActionType.CastSpell))
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            var caster = session.Party.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (caster == null || _spellCastingService == null)
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            var spell = _spellCastingService.FindFirstCastableSpell(caster, SpellUseContext.Combat);
            if (spell == null)
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            SpellCastTarget? target = null;
            if (spell.RangeType == SpellRangeType.Enemy)
            {
                var enemy = session.AliveMonsters.FirstOrDefault();
                if (enemy != null)
                    target = SpellCastTarget.Enemy(enemy.Index);
            }
            else if (spell.RangeType == SpellRangeType.Self)
            {
                target = SpellCastTarget.Ally(caster);
            }
            else
            {
                var ally = session.AliveParty.OrderBy(a => a.CurrentHitPoints).FirstOrDefault() ?? caster;
                target = SpellCastTarget.Ally(ally);
            }

            converted[name] = new CombatAction
            {
                Type = CombatActionType.CastSpell,
                SpellId = spell.Id,
                Target = target
            };
        }

        return ResolveRound(session, converted);
    }

    public List<CombatEvent> ResolveRound(CombatSession session, IReadOnlyDictionary<string, CombatAction> partyActions)
    {
        var events = new List<CombatEvent>
        {
            new($"-- Round {session.RoundNumber} --")
        };

        if (session.Outcome != CombatOutcome.InProgress)
            return events;

        var parrying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool partyAttemptedRun = false;

        foreach (var member in session.Party)
        {
            if (!IsAlive(member))
                continue;

            if (!partyActions.TryGetValue(member.Name, out var action))
                action = CombatAction.OfType(CombatActionType.Parry);

            switch (action.Type)
            {
                case CombatActionType.Fight:
                    ResolvePartyAttack(session, member, action, events);
                    break;
                case CombatActionType.Parry:
                    parrying.Add(member.Name);
                    events.Add(new CombatEvent($"{member.Name} parries."));
                    break;
                case CombatActionType.UseItem:
                    events.Add(new CombatEvent($"{member.Name} uses an item (not yet implemented)."));
                    break;
                case CombatActionType.Spell:
                case CombatActionType.CastSpell:
                    ResolvePartySpell(session, member, action, events);
                    break;
                case CombatActionType.Run:
                    partyAttemptedRun = true;
                    events.Add(new CombatEvent($"{member.Name} tries to run!"));
                    break;
            }

            if (!session.AliveMonsters.Any())
            {
                session.Outcome = CombatOutcome.Victory;
                events.Add(new CombatEvent("All monsters are defeated!"));
                return FinalizeRound(session, events);
            }
        }

        if (partyAttemptedRun)
        {
            var runRoll = _dice.Roll(100);
            if (runRoll <= 50)
            {
                session.Outcome = CombatOutcome.Escaped;
                events.Add(new CombatEvent("The party escapes!"));
                return FinalizeRound(session, events);
            }

            events.Add(new CombatEvent("The party fails to escape!"));
        }

        foreach (var monster in session.AliveMonsters.ToList())
        {
            if (monster.HasStatus(MonsterStatus.IncendiaryCloud))
            {
                var rolledDamage = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                var remaining = monster.TickStatus(MonsterStatus.IncendiaryCloud);

                events.Add(new CombatEvent($"Incendiary cloud burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains inside the incendiary cloud ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The incendiary cloud around {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is consumed by flames."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.DeathFog))
            {
                var rolledDamage = _dice.Roll(10);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                var remaining = monster.TickStatus(MonsterStatus.DeathFog);

                events.Add(new CombatEvent($"Death fog engulfs {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains in the death fog ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The death fog around {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} dies in the death fog."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.WallOfFire))
            {
                var rolledDamage = _dice.Roll(4) + _dice.Roll(4);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                var remaining = monster.TickStatus(MonsterStatus.WallOfFire);

                events.Add(new CombatEvent($"Flames burn {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains inside the wall of fire ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The wall of fire around {monster.DisplayName} fades."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is burned to ashes."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.AcidArrow))
            {
                var rolledDamage = _dice.Roll(4) + _dice.Roll(4);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                var remaining = monster.TickStatus(MonsterStatus.AcidArrow);

                events.Add(new CombatEvent($"Acid burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is still corroding ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The acid on {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is destroyed."));
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.Asleep))
            {
                var remaining = monster.TickStatus(MonsterStatus.Asleep);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is asleep ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} wakes up."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Paralyzed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Paralyzed);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is held ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Panicked))
            {
                var remaining = monster.TickStatus(MonsterStatus.Panicked);
                var fleeRoll = _dice.Roll(100);
                if (fleeRoll <= 50)
                {
                    monster.CurrentHitPoints = 0;
                    events.Add(new CombatEvent($"{monster.DisplayName} flees in panic!"));
                    continue;
                }

                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} panics and cannot act ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} regains its nerve."));

                continue;
            }

            var attacks = monster.Template.Attacks.Count > 0 ? monster.Template.Attacks : new List<Adnd.Core.Monsters.MonsterAttack> { new() { NumberOfAttacks = 1, Damage = "1d4", Name = "Claw" } };

            foreach (var attack in attacks)
            {
                int attackCount = Math.Max(1, attack.NumberOfAttacks);
                for (int i = 0; i < attackCount; i++)
                {
                    var target = SelectMonsterTarget(session);
                    if (target is null)
                    {
                        session.Outcome = CombatOutcome.Defeat;
                        events.Add(new CombatEvent("The party is defeated."));
                        return FinalizeRound(session, events);
                    }

                    var blessedAcAdjustment = session.IsBlessed(target.Name) ? -1 : 0;
                    var targetAc = target.ArmorClass + (parrying.Contains(target.Name) ? 2 : 0);
                    var thac0 = GetMonsterThac0(monster);
                    int needed = thac0 - targetAc;
                    int roll = _dice.Roll(20);

                    if (roll >= needed)
                    {
                        int damage = RollDamage(attack.Damage);
                        target.CurrentHitPoints -= damage;
                        events.Add(new CombatEvent($"{monster.DisplayName} hits {target.Name} for {damage}."));

                        if (target.CurrentHitPoints <= 0)
                        {
                            target.CurrentHitPoints = 0;
                            target.AddStatus(CharacterStatus.Dead);
                            events.Add(new CombatEvent($"{target.Name} is slain!"));
                        }
                    }
                    else
                    {
                        events.Add(new CombatEvent($"{monster.DisplayName} misses {target.Name}."));
                    }
                }
            }
        }

        if (!session.AliveParty.Any())
        {
            session.Outcome = CombatOutcome.Defeat;
            events.Add(new CombatEvent("The party is defeated."));
        }
        else if (!session.AliveMonsters.Any())
        {
            session.Outcome = CombatOutcome.Victory;
            events.Add(new CombatEvent("All monsters are defeated!"));
        }

        return FinalizeRound(session, events);
    }

    private void ResolvePartySpell(CombatSession session, Character caster, CombatAction action, List<CombatEvent> events)
    {
        if (_spellCastingService == null)
        {
            events.Add(new CombatEvent($"{caster.Name} cannot cast spells right now."));
            return;
        }

        if (string.IsNullOrWhiteSpace(action.SpellId))
        {
            events.Add(new CombatEvent($"{caster.Name} has no spell selected."));
            return;
        }

        var targets = action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();

        var result = _spellCastingService.Cast(new SpellCastRequest
        {
            Caster = caster,
            SpellId = action.SpellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party,
            MonsterTargets = session.Monsters,
            Targets = targets,
            RoundNumber = session.RoundNumber,
            CombatSession = session
        });

        if (!result.Success)
        {
            events.Add(new CombatEvent($"{caster.Name} fails to cast: {result.Error}"));
            return;
        }

        foreach (var message in result.Events)
            events.Add(new CombatEvent(message));
    }

    private List<CombatEvent> FinalizeRound(CombatSession session, List<CombatEvent> events)
    {
        if (session.Outcome == CombatOutcome.InProgress)
            session.RoundNumber++;

        return events;
    }

    private void ResolvePartyAttack(CombatSession session, Character member, CombatAction action, List<CombatEvent> events)
    {
        // Determine target: the named monster first, then a spread, then the group, then whoever is first.
        Combat.Sessions.MonsterInstance? target = null;

        // A monster the player picked out. Falls through when it is already dead -- initiative means an
        // earlier attacker may have finished it, and the swing should land somewhere rather than be lost.
        if (!string.IsNullOrEmpty(action.TargetMonsterId))
        {
            var named = session.FindMonster(action.TargetMonsterId);
            if (named != null && named.IsAlive) target = named;
        }

        // Spread: take the next monster along, within the chosen group if one was named. The cursor is on the
        // session, so consecutive attackers asking to spread walk along the line instead of stacking up.
        if (target is null && action.SpreadTargets)
        {
            var spreadable = (string.IsNullOrEmpty(action.TargetGroupId)
                    ? session.AliveMonsters
                    : session.GetAliveMonstersByGroup(action.TargetGroupId))
                .ToList();

            if (spreadable.Count > 0)
            {
                var at = ((session.SpreadCursor % spreadable.Count) + spreadable.Count) % spreadable.Count;
                target = spreadable[at];
                session.SpreadCursor = at + 1;
            }
        }

        if (target is null && !string.IsNullOrEmpty(action.TargetGroupId))
        {
            // Attack a monster from the specified group
            var groupMonsters = session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();
            if (groupMonsters.Count > 0)
            {
                target = groupMonsters.First();
            }
        }

        if (target is null)
        {
            // Default to first alive monster (backward compatibility)
            target = session.AliveMonsters.FirstOrDefault();
        }

        if (target is null)
            return;

        int attacks = Math.Max(1, member.NumberOfAttacks);
        for (int i = 0; i < attacks; i++)
        {
            var thac0Modifier = session.IsBlessed(member.Name) ? 1 : 0;
            if (member.Equipment.TryGetValue(Adnd.Core.Items.EquipmentSlot.MainHand, out var mainHand)
                && mainHand != null)
            {
                thac0Modifier += Math.Max(0, mainHand.ToHitBonus);
            }

            int needed = (member.Thac0 - thac0Modifier) - target.ArmorClass;
            int roll = _dice.Roll(20);
            if (roll >= needed)
            {
                int damage = RollDamage(string.IsNullOrWhiteSpace(member.Damage) ? "1d2" : member.Damage);
                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
                var actualDamage = before - target.CurrentHitPoints;
                events.Add(new CombatEvent($"{member.Name} hits {target.DisplayName} for {actualDamage} (rolled {damage}). HP {before}->{target.CurrentHitPoints}."));

                if (target.CurrentHitPoints <= 0)
                {
                    events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));
                    break;
                }
            }
            else
            {
                events.Add(new CombatEvent($"{member.Name} misses {target.DisplayName}."));
            }
        }
    }

    private int GetMonsterThac0(MonsterInstance monster)
    {
        return Math.Max(10, 20 - Math.Max(0, monster.Template.HitDice - 1));
    }

    private Character? SelectMonsterTarget(CombatSession session)
    {
        var frontline = session.Party.Take(3).Where(IsAlive).ToList();
        if (frontline.Count == 0)
            frontline = session.AliveParty.ToList();

        if (frontline.Count == 0)
            return null;

        var index = _dice.Roll(frontline.Count) - 1;
        return frontline[index];
    }

    private int RollDamage(string damageExpression)
    {
        var normalized = damageExpression.Trim().ToLowerInvariant();

        if (normalized.Contains('/'))
            normalized = normalized.Split('/')[0].Trim();

        var m = Regex.Match(normalized, @"^(?<count>\d+)d(?<sides>\d+)(?<mod>[+-]\d+)?$");
        if (!m.Success)
            return 1;

        int count = int.Parse(m.Groups["count"].Value);
        int sides = int.Parse(m.Groups["sides"].Value);
        int mod = m.Groups["mod"].Success ? int.Parse(m.Groups["mod"].Value) : 0;

        int value = _dice.RollMany(sides, Math.Max(1, count)) + mod;
        return Math.Max(1, value);
    }

    private static bool IsAlive(Character c) => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead);
}
