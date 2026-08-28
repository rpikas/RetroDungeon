using System.Text.RegularExpressions;
using System.Text.Json;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;

namespace Adnd.Core.Combat.Resolution;

public sealed class CombatResolver
{
    private readonly IDice _dice;
    private readonly SpellCastingService? _spellCastingService;
    private readonly CharacterSavingThrowService _savingThrowService = new();

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

            if (member.HasStatus(CharacterStatus.Asleep))
            {
                var roundsRemaining = session.GetPartyAsleepRounds(member.Name);
                events.Add(new CombatEvent(roundsRemaining > 0
                    ? $"{member.Name} is asleep and cannot act ({roundsRemaining} round(s) remaining)."
                    : $"{member.Name} is asleep and cannot act."));

                var afterTick = session.TickPartyAsleep(member.Name);
                if (afterTick <= 0)
                {
                    member.RemoveStatus(CharacterStatus.Asleep);
                    events.Add(new CombatEvent($"{member.Name} wakes up."));
                }

                continue;
            }

            if (member.HasStatus(CharacterStatus.Paralyzed))
            {
                events.Add(new CombatEvent($"{member.Name} is paralyzed and cannot act."));
                continue;
            }

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
                    ResolvePartyUseItem(session, member, action, events);
                    break;
                case CombatActionType.DispellUndead:
                    ResolveDispellUndead(session, member, action, events);
                    break;
                case CombatActionType.LayOnHands:
                    ResolveLayOnHands(session, member, action, events);
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
            if (session.RoundNumber == 1 && HasSpecialAbility(monster, "Level 1 Mage spells"))
            {
                ResolveLevel1MageSpells(monster, session, events);
                continue;
            }

            if (HasSpecialAbility(monster, "Level 1 Priest spells") && ResolveLevel1PriestSpell(monster, session, events))
                continue;

            if (TryGetHpDamageBreathDamage(monster, out var breathDamage)
                && ResolveHpDamageBreath(monster, breathDamage, session, events))
                continue;

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

            if (monster.HasStatus(MonsterStatus.Entangled))
            {
                var remaining = monster.TickStatus(MonsterStatus.Entangled);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is entangled ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free of entangle."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.TurnedUndead))
            {
                var remaining = monster.TickStatus(MonsterStatus.TurnedUndead);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is turned and cannot attack ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} is no longer turned."));

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
            if (monster.HasStatus(MonsterStatus.Unconscious))
            {
                var remaining = monster.TickStatus(MonsterStatus.Unconscious);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is unconscious ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} regains consciousness."));

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
                        else
                        {
                            if (HasSpecialAbility(monster, "Poison"))
                            {
                                var poisonRoll = _dice.Roll(100);
                                if (poisonRoll <= 70)
                                {
                                    var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath);
                                    var saveRoll = _dice.Roll(20);
                                    if (saveRoll >= saveTarget)
                                    {
                                        events.Add(new CombatEvent($"{target.Name} resists poison (save {saveRoll} vs {saveTarget})."));
                                    }
                                    else if (!target.HasStatus(CharacterStatus.Poisoned))
                                    {
                                        target.AddStatus(CharacterStatus.Poisoned);
                                        events.Add(new CombatEvent($"{target.Name} is poisoned by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})"));
                                    }
                                }
                            }

                            if (HasAnySpecialAbility(monster, "Paralyze", "Paralyzation", "Paralysis"))
                            {
                                var paralyzeRoll = _dice.Roll(100);
                                if (paralyzeRoll <= 60)
                                {
                                    var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath);
                                    var saveRoll = _dice.Roll(20);

                                    if (saveRoll >= saveTarget)
                                    {
                                        events.Add(new CombatEvent($"{target.Name} resists paralysis (save {saveRoll} vs {saveTarget})."));
                                    }
                                    else if (!target.HasStatus(CharacterStatus.Paralyzed))
                                    {
                                        target.AddStatus(CharacterStatus.Paralyzed);
                                        events.Add(new CombatEvent($"{target.Name} is paralyzed by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})"));
                                    }
                                    else
                                    {
                                        events.Add(new CombatEvent($"{target.Name} resists further paralysis from {monster.DisplayName}."));
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        events.Add(new CombatEvent($"{monster.DisplayName} misses {target.Name}."));
                    }
                }
            }
        }

        ApplyPoisonDamageDuringCombat(session, events);

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

    private void ResolvePartyUseItem(CombatSession session, Character user, CombatAction action, List<CombatEvent> events)
    {
        if (_spellCastingService == null)
        {
            events.Add(new CombatEvent($"{user.Name} cannot use magical items right now."));
            return;
        }

        if (!action.ItemInventoryIndex.HasValue || action.ItemInventoryIndex.Value < 0 || action.ItemInventoryIndex.Value >= user.Inventory.Count)
        {
            events.Add(new CombatEvent($"{user.Name} has no valid item selected."));
            return;
        }

        var item = user.Inventory[action.ItemInventoryIndex.Value];
        var spellId = action.SpellId;

        if (string.IsNullOrWhiteSpace(spellId))
        {
            var spell = _spellCastingService.FindSpellFromItem(item);
            spellId = spell?.Id;
        }

        if (string.IsNullOrWhiteSpace(spellId))
        {
            events.Add(new CombatEvent($"{item.Name} has no usable spell effect."));
            return;
        }

        var targets = action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();
        var result = _spellCastingService.CastFromItem(new SpellCastRequest
        {
            Caster = user,
            SpellId = spellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party,
            MonsterTargets = session.Monsters,
            Targets = targets,
            RoundNumber = session.RoundNumber,
            CombatSession = session
        });

        if (!result.Success)
        {
            events.Add(new CombatEvent($"{user.Name} fails to use {item.Name}: {result.Error}"));
            return;
        }

        if (item.Type is ItemType.Potion or ItemType.Scroll)
            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

        events.Add(new CombatEvent($"{user.Name} uses {item.Name}."));
        foreach (var message in result.Events)
            events.Add(new CombatEvent(message));
    }

    private static void ResolveLayOnHands(CombatSession session, Character paladin, CombatAction action, List<CombatEvent> events)
    {
        if (!paladin.IsPaladin())
        {
            events.Add(new CombatEvent($"{paladin.Name} cannot use Lay on Hands."));
            return;
        }

        if (paladin.LayOnHandsUsedToday)
        {
            events.Add(new CombatEvent($"{paladin.Name} has already used Lay on Hands today."));
            return;
        }

        var targetName = action.Target?.CharacterName;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            events.Add(new CombatEvent($"{paladin.Name} has no Lay on Hands target."));
            return;
        }

        var target = session.Party.FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            events.Add(new CombatEvent($"{paladin.Name}'s Lay on Hands target is no longer present."));
            return;
        }

        if (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.HasStatus(CharacterStatus.Lost))
        {
            events.Add(new CombatEvent($"{paladin.Name} cannot heal {target.Name} with Lay on Hands."));
            return;
        }

        var healAmount = Math.Max(0, paladin.GetPaladinLevel()) * 2;
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healAmount);
        var healed = target.CurrentHitPoints - before;

        paladin.LayOnHandsUsedToday = true;

        events.Add(new CombatEvent(healed > 0
            ? $"{paladin.Name} lays on hands and heals {target.Name} for {healed} hit point(s)."
            : $"{paladin.Name} lays on hands on {target.Name}, but no healing is needed."));
    }

    private void ResolveDispellUndead(CombatSession session, Character actor, CombatAction action, List<CombatEvent> events)
    {
        var effectiveClericLevel = GetEffectiveTurnUndeadLevel(actor);
        if (effectiveClericLevel < 1)
        {
            events.Add(new CombatEvent($"{actor.Name} cannot dispell undead."));
            return;
        }

        var targetMonsters = string.IsNullOrWhiteSpace(action.TargetGroupId)
            ? session.AliveMonsters.ToList()
            : session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();

        var undead = targetMonsters.Where(m => m.InstanceMonsterType == Adnd.Core.Monsters.MonsterType.Undead).ToList();
        if (undead.Count == 0)
        {
            events.Add(new CombatEvent($"{actor.Name} presents a holy symbol, but no undead are affected."));
            return;
        }

        events.Add(new CombatEvent($"{actor.Name} uses Dispell Undead!"));

        var table = LoadTurnUndeadTable();
        var row = table.FirstOrDefault(r => effectiveClericLevel >= r.MinLevel && effectiveClericLevel <= r.MaxLevel)
                  ?? table.OrderByDescending(r => r.MaxLevel).First();

        foreach (var monster in undead)
        {
            var key = ResolveTurnUndeadKey(monster.Name);
            if (!row.Results.TryGetValue(key, out var token) || string.IsNullOrWhiteSpace(token))
                token = row.Results.TryGetValue("Skeleton", out var fallback) ? fallback : "-";

            token = token.Trim();

            if (token == "-")
            {
                events.Add(new CombatEvent($"{monster.DisplayName} resists the turning attempt."));
                continue;
            }

            if (string.Equals(token, "D", StringComparison.OrdinalIgnoreCase))
            {
                monster.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{monster.DisplayName} is disintegrated by holy power!"));
                continue;
            }

            if (string.Equals(token, "T", StringComparison.OrdinalIgnoreCase))
            {
                var rounds = _dice.Roll(10) + 2;
                monster.SetStatus(MonsterStatus.TurnedUndead, rounds);
                events.Add(new CombatEvent($"{monster.DisplayName} is turned for {rounds} round(s)!"));
                continue;
            }

            if (!int.TryParse(token, out var required))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is unaffected."));
                continue;
            }

            var roll = _dice.Roll(20);
            if (roll >= required)
            {
                var rounds = _dice.Roll(10) + 2;
                monster.SetStatus(MonsterStatus.TurnedUndead, rounds);
                events.Add(new CombatEvent($"{monster.DisplayName} is turned ({roll} vs {required}) for {rounds} round(s)!"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} resists turning ({roll} vs {required})."));
            }
        }
    }

    private static int GetEffectiveTurnUndeadLevel(Character actor)
    {
        var clericLevel = actor.Classes.Contains(CharacterClass.Cleric)
            ? actor.GetClassLevel(CharacterClass.Cleric)
            : 0;

        var paladinLevel = actor.Classes.Contains(CharacterClass.Paladin)
            ? Math.Max(0, actor.GetClassLevel(CharacterClass.Paladin) - 2)
            : 0;

        if (!actor.Classes.Contains(CharacterClass.Cleric) && paladinLevel < 1)
            return 0;

        if (actor.Classes.Contains(CharacterClass.Paladin) && actor.GetClassLevel(CharacterClass.Paladin) < 3)
            paladinLevel = 0;

        return Math.Max(clericLevel, paladinLevel);
    }

    private static string ResolveTurnUndeadKey(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return "Skeleton";

        var n = monsterName.Trim().ToLowerInvariant();
        if (n.Contains("skeleton")) return "Skeleton";
        if (n.Contains("zombie")) return "Zombie";
        if (n.Contains("ghoul")) return "Ghoul";
        if (n.Contains("shadow")) return "Shadow";
        if (n.Contains("wight")) return "Wight";
        if (n.Contains("wraith")) return "Wraith";
        if (n.Contains("mummy")) return "Mummy";
        if (n.Contains("spectre") || n.Contains("specter")) return "Spectre";
        if (n.Contains("vampire")) return "Vampire";
        if (n.Contains("ghost")) return "Ghost";
        if (n.Contains("lich")) return "Lich";
        return "Skeleton";
    }

    private static List<TurnUndeadRow> LoadTurnUndeadTable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Characters", "Progression", "ClericTurnUndead.json");
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Adnd.Core", "Characters", "Progression", "ClericTurnUndead.json"));

        if (!File.Exists(path))
            return new List<TurnUndeadRow>
            {
                new() { MinLevel = 1, MaxLevel = 2, Results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Skeleton"] = "10" } }
            };

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("TurnUndeadTable", out var tableEl) || tableEl.ValueKind != JsonValueKind.Array)
            return new List<TurnUndeadRow>();

        var rows = new List<TurnUndeadRow>();
        foreach (var rowEl in tableEl.EnumerateArray())
        {
            var range = rowEl.GetProperty("LevelRange").GetString() ?? "1-1";
            var parts = range.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var min = 1;
            var max = 1;
            if (parts.Length >= 1) int.TryParse(parts[0], out min);
            if (parts.Length >= 2) int.TryParse(parts[1], out max);
            else max = min;

            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rowEl.TryGetProperty("Results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in resultsEl.EnumerateObject())
                    results[p.Name] = p.Value.GetString() ?? "-";
            }

            rows.Add(new TurnUndeadRow { MinLevel = min, MaxLevel = max, Results = results });
        }

        return rows;
    }

    private sealed class TurnUndeadRow
    {
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public Dictionary<string, string> Results { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private List<CombatEvent> FinalizeRound(CombatSession session, List<CombatEvent> events)
    {
        if (session.Outcome == CombatOutcome.InProgress)
            session.RoundNumber++;

        return events;
    }

    private int GetAttacksThisRound(float attacksPerRound, int roundNumber)
    {
        // 1 attack per round
        if (attacksPerRound <= 1f)
            return 1;

        // 2 attacks per round
        if (attacksPerRound >= 2f)
            return 2;

        // 1.5 attacks per round (3/2)
        // Odd rounds: 1 attack
        // Even rounds: 2 attacks
        if (Math.Abs(attacksPerRound - 1.5f) < 0.01f)
            return (roundNumber % 2 == 0) ? 2 : 1;

        // fallback
        return 1;
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
     



        int attacks = GetAttacksThisRound(member.NumberOfAttacks, session.RoundNumber);

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

                var weaponName = mainHand != null ? mainHand.Name : "bare hands";

                events.Add(new CombatEvent(
                    $"{member.Name} hits {target.DisplayName} with {weaponName} for {damage} damage. HP {before}->{target.CurrentHitPoints}."));

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

    private void ResolveLevel1MageSpells(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var damage = _dice.Roll(4) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Magic Missile! {target.Name} takes {actual} damage (rolled {damage})."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }

            return;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Sleep!"));
        foreach (var target in aliveParty)
        {
            var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell);
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists Sleep (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(3) + 1; // 2-4 rounds
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and falls asleep for {rounds} round(s)."));
        }
    }

    private bool ResolveLevel1PriestSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (session.Level1PriestSpellCastsUsed >= 3)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
            return false; // Attack normally.

        var damagedAllies = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, monster) && m.CurrentHitPoints < m.MaxHitPoints)
            .ToList();

        if (damagedAllies.Count == 0)
            return false;

        var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
        var healRoll = _dice.Roll(8);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
        var actual = target.CurrentHitPoints - before;

        session.Level1PriestSpellCastsUsed += 1;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
        return true;
    }

    private bool ResolveHpDamageBreath(MonsterInstance monster, int maxDamage, CombatSession session, List<CombatEvent> events)
    {
        if (maxDamage <= 0)
            return false;

        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var rolledDamage = _dice.Roll(maxDamage);
        events.Add(new CombatEvent($"{monster.DisplayName} uses {maxDamage} HP damage breath (rolled {rolledDamage})!"));

        foreach (var target in aliveParty)
        {
            var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon);
            var saveRoll = _dice.Roll(20);
            var damage = saveRoll >= saveTarget ? rolledDamage / 2 : rolledDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds breath save ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails breath save ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private static bool TryGetHpDamageBreathDamage(MonsterInstance monster, out int damage)
    {
        foreach (var ability in monster.Template.SpecialAbilities)
        {
            var name = ability.Name?.Trim() ?? string.Empty;
            var m = Regex.Match(name, @"^(?<damage>\d+)\s*hp damage breath$", RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;

            if (int.TryParse(m.Groups["damage"].Value, out damage) && damage > 0)
                return true;
        }

        damage = 0;
        return false;
    }

    private static bool HasSpecialAbility(MonsterInstance monster, string abilityName)
    {
        return monster.Template.SpecialAbilities.Any(a =>
            string.Equals(a.Name, abilityName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAnySpecialAbility(MonsterInstance monster, params string[] abilityNames)
    {
        return monster.Template.SpecialAbilities.Any(a =>
            abilityNames.Any(n => string.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase)));
    }

    private void ApplyPoisonDamageDuringCombat(CombatSession session, List<CombatEvent> events)
    {
        foreach (var member in session.Party)
        {
            if (!IsAlive(member) || !member.HasStatus(CharacterStatus.Poisoned))
                continue;

            var poisonTickRoll = _dice.Roll(100);
            if (poisonTickRoll > 50)
                continue;

            var damage = _dice.Roll(3);
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - damage);
            var actual = before - member.CurrentHitPoints;

            events.Add(new CombatEvent($"Poison harms {member.Name} for {actual} (rolled {damage}). HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} dies from poison!"));
            }
        }
    }

    private static bool IsAlive(Character c) => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead);
}
