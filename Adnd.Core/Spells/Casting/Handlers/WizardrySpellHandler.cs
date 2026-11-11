using Adnd.Core.Characters;
using Adnd.Core.Combat.Resolution;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

/// <summary>Resolves the Wizardry Mage and Priest spells that have a table-driven effect.</summary>
public sealed class WizardrySpellHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => WizardrySpells.IsWizardrySpellId(spellId) && WizardrySpells.GetEffect(spellId).Kind != WizardryEffectKind.None;

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var effect = WizardrySpells.GetEffect(spell.Id);
        var rng = request.Rng ?? Random.Shared;

        return effect.Kind switch
        {
            WizardryEffectKind.Heal or WizardryEffectKind.FullHeal => ResolveHeal(request, spell, effect, rng),
            WizardryEffectKind.CureParalysis => ResolveCureParalysis(request, spell),
            WizardryEffectKind.CurePoison => ResolveCurePoison(request, spell),
            WizardryEffectKind.PartyArmorClass => ResolvePartyArmorClass(request, spell, effect),
            WizardryEffectKind.ExpeditionArmorClass => ResolveExpeditionArmorClass(request, spell, effect),
            WizardryEffectKind.CasterArmorClass => ResolveCasterArmorClass(request, spell, effect),
            WizardryEffectKind.ShowPosition => ResolveShowPosition(request, spell),
            WizardryEffectKind.Raise => ResolveRaise(request, spell, rng),
            WizardryEffectKind.RaiseFull => ResolveRaise(request, spell, rng, full: true),
            WizardryEffectKind.LocateDead => ResolveLocateDead(request, spell),
            WizardryEffectKind.Recall => ResolveRecall(request, spell),
            WizardryEffectKind.Teleport => ResolveTeleport(request, spell),
            WizardryEffectKind.Intervention => ResolveIntervention(request, spell, effect.Dice > 0, rng),
            WizardryEffectKind.LightShort or WizardryEffectKind.LightLong => ResolveLight(request, spell, effect.Kind == WizardryEffectKind.LightLong),
            WizardryEffectKind.TrapDetect => new SpellCastResult { Success = true, Events = { $"{request.Caster.Name} casts {spell.Name}." } },
            _ => ResolveAgainstMonsters(request, spell, effect, rng)
        };
    }

    private static SpellCastResult ResolveCasterArmorClass(SpellCastRequest request, Spell spell, WizardrySpellEffect effect)
    {
        if (request.CombatSession == null)
            return SpellCastResult.Failure($"{spell.Name} can only be cast in combat.");

        var bonuses = request.CombatSession.WizardryCasterArmorClassBonus;
        bonuses[request.Caster.Name] = bonuses.GetValueOrDefault(request.Caster.Name) + effect.Dice;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {request.Caster.Name}'s AC improves by {effect.Dice} for this battle.");
        return result;
    }

    private enum Boon { Augment, Cure, Heal, Protect, RaiseDead, Silence, Banish }

    private static SpellCastResult ResolveIntervention(SpellCastRequest request, Spell spell, bool greater, Random rng)
    {
        var caster = request.Caster;
        if (caster.Level < 13)
            return SpellCastResult.Failure($"{spell.Name} can only be cast by a character of level 13 or higher.");

        var session = request.CombatSession;
        var inCombat = request.Context == SpellUseContext.Combat && session != null;
        var party = request.PartyTargets.Append(caster).Distinct().ToList();
        var dead = party.Where(c => c.HasStatus(CharacterStatus.Dead)).ToList();

        var boons = new List<Boon> { Boon.Cure, Boon.Heal };
        if (inCombat)
            boons.AddRange(new[] { Boon.Augment, Boon.Silence, Boon.Banish });
        if (greater)
        {
            if (inCombat)
                boons.Add(Boon.Protect);
            if (dead.Count > 0)
                boons.Add(Boon.RaiseDead);
        }

        if (!CombatResolver.TryDrainOneLevel(caster, out var from, out var to))
            return SpellCastResult.Failure($"{caster.Name} cannot lose a level, so the gods do not answer.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{caster.Name} calls on the gods with {spell.Name} and loses a level ({from}->{to}).");

        var boon = boons[rng.Next(boons.Count)];
        var alive = party.Where(c => !c.HasStatus(CharacterStatus.Dead) && !c.HasStatus(CharacterStatus.Ashes) && !c.HasStatus(CharacterStatus.Lost) && c.CurrentHitPoints > 0).ToList();
        switch (boon)
        {
            case Boon.Augment:
                session!.WizardryAugmentedMagic = true;
                result.Events.Add("The gods answer: the party's spells are augmented and will hit harder this battle.");
                break;
            case Boon.Cure:
                foreach (var c in alive)
                {
                    c.Status &= CharacterStatus.Dead | CharacterStatus.Ashes | CharacterStatus.Lost;
                    c.ParalyzedRoundsRemaining = 0;
                }
                result.Events.Add("The gods answer: every party member is cured of all conditions.");
                break;
            case Boon.Heal:
                foreach (var c in alive)
                {
                    var before = c.CurrentHitPoints;
                    c.CurrentHitPoints = c.MaxHitPoints;
                    c.Status &= CharacterStatus.Dead | CharacterStatus.Ashes | CharacterStatus.Lost;
                    c.ParalyzedRoundsRemaining = 0;
                    result.HpChanges[c.Name] = Math.Max(0, c.CurrentHitPoints - before);
                }
                result.Events.Add("The gods answer: Madi heals the whole party and clears all conditions.");
                break;
            case Boon.Protect:
                session!.WizardryPartyArmorClassBonus += 20;
                result.Events.Add("The gods answer: the party's AC improves by 20 for this battle.");
                break;
            case Boon.RaiseDead:
            {
                var di = WizardrySpells.All.First(s => s.Id == WizardrySpells.ToId("Di"));
                result.Events.Add("The gods answer: the fallen are called back with Di.");
                foreach (var c in dead)
                {
                    var sub = ResolveRaise(new SpellCastRequest
                    {
                        Caster = caster,
                        Spell = di,
                        SpellId = di.Id,
                        Context = request.Context,
                        Targets = new List<SpellCastTarget> { SpellCastTarget.Ally(c) },
                        PartyTargets = party
                    }, di, rng);
                    result.Events.AddRange(sub.Events.Skip(1));
                    foreach (var kv in sub.HpChanges)
                        result.HpChanges[kv.Key] = kv.Value;
                }
                break;
            }
            case Boon.Silence:
                foreach (var m in session!.Monsters.Where(m => m.IsAlive))
                    m.SetStatus(MonsterStatus.Silenced, 99);
                result.Events.Add("The gods answer: all monsters are silenced.");
                break;
            case Boon.Banish:
                foreach (var m in session!.Monsters.Where(m => m.IsAlive))
                {
                    m.HasFled = true;
                    m.CurrentHitPoints = 0;
                }
                result.Events.Add("The gods answer: all monsters are teleported away from the battle.");
                break;
        }

        return result;
    }

    private static SpellCastResult ResolveTeleport(SpellCastRequest request, Spell spell)
    {
        if (request.Context != SpellUseContext.Exploration || request.MalorTeleport == null)
            return SpellCastResult.Failure($"{spell.Name} can only be cast in the dungeon, outside combat.");

        var (teleported, message) = request.MalorTeleport();
        if (!teleported)
            return SpellCastResult.Failure(message);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} chants {spell.Name}. {message}");
        return result;
    }

    private static SpellCastResult ResolveRecall(SpellCastRequest request, Spell spell)
    {
        if (request.Context != SpellUseContext.Exploration)
            return SpellCastResult.Failure($"{spell.Name} can only be cast outside combat.");

        var result = new SpellCastResult { Success = true, ReturnToTown = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. The party is torn back to town, leaving its equipment and most of its gold behind!");

        foreach (var member in request.PartyTargets.Append(request.Caster).Distinct())
        {
            member.Equipment.Clear();
            member.Inventory.Clear();
            member.CopperPieces -= member.CopperPieces * 9 / 10;
            member.SilverPieces -= member.SilverPieces * 9 / 10;
            member.ElectrumPieces -= member.ElectrumPieces * 9 / 10;
            member.GoldPieces -= member.GoldPieces * 9 / 10;
            member.PlatinumPieces -= member.PlatinumPieces * 9 / 10;
        }
        return result;
    }

    private static SpellCastResult ResolveLocateDead(SpellCastRequest request, Spell spell)
    {
        var position = WizardryDungeonPosition.Provider?.Invoke();
        if (string.IsNullOrWhiteSpace(position))
            return SpellCastResult.Failure($"{spell.Name} only works inside the dungeon.");

        var fallen = request.PartyTargets.Append(request.Caster)
            .Distinct()
            .Where(c => c.HasStatus(CharacterStatus.Dead) || c.HasStatus(CharacterStatus.Ashes) || c.HasStatus(CharacterStatus.Lost))
            .ToList();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}.");
        if (fallen.Count == 0)
        {
            result.Events.Add("No party member is dead or missing.");
            return result;
        }

        foreach (var c in fallen)
        {
            var state = c.HasStatus(CharacterStatus.Lost) ? "is lost" : c.HasStatus(CharacterStatus.Ashes) ? "lies as ashes" : "is dead";
            result.Events.Add($"{c.Name} {state}, carried with the party. {position}");
        }
        return result;
    }

    private static SpellCastResult ResolveRaise(SpellCastRequest request, Spell spell, Random rng, bool full = false)
    {
        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? null
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        if (target.DiedOfOldAge)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; death due to old age is final.");
        if (target.HasStatus(CharacterStatus.Lost))
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; the character is lost.");
        if (target.HasStatus(CharacterStatus.Ashes) && !full)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; only a dead character can be raised.");
        if (!target.HasStatus(CharacterStatus.Dead) && !target.HasStatus(CharacterStatus.Ashes) && target.CurrentHitPoints > 0)
            return SpellCastResult.Failure($"{spell.Name} can only target a dead character.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        var constitution = target.Abilities.Constitution;
        var chance = RaiseDeadHandler.SystemShockSurvivalChance(constitution);
        var saveTarget = RaiseDeadHandler.ChanceToD20SaveTarget(chance);
        var roll = rng.Next(1, 21);
        result.Events.Add($"Saving throw (d20) for {target.Name}: CON {constitution} gives {chance}% survival (needs {saveTarget} or less). Rolled {roll}.");

        target.RemoveStatus(CharacterStatus.Dead);
        target.RemoveStatus(CharacterStatus.Ashes);
        if (roll <= saveTarget)
        {
            target.CurrentHitPoints = full ? Math.Max(1, target.MaxHitPoints) : 1;
            target.Abilities.Constitution = Math.Max(0, constitution - 1);
            target.ClearMummyRotDeathCurse();
            target.ClearPoisonSporesDeathCurse();
            result.HpChanges[target.Name] = target.CurrentHitPoints;
            result.Events.Add(full
                ? $"{target.Name} is resurrected with full HP ({target.CurrentHitPoints}/{target.MaxHitPoints}) and loses 1 Constitution (now {target.Abilities.Constitution})."
                : $"{target.Name} is restored to life with 1 HP and loses 1 Constitution (now {target.Abilities.Constitution}).");
            return result;
        }

        target.CurrentHitPoints = 0;
        if (full)
        {
            target.AddStatus(CharacterStatus.Lost);
            result.Events.Add($"{spell.Name} fails. {target.Name} is lost forever.");
        }
        else
        {
            target.AddStatus(CharacterStatus.Ashes);
            result.Events.Add($"{spell.Name} fails. {target.Name} turns to ashes.");
        }
        return result;
    }

    private static SpellCastResult ResolveLight(SpellCastRequest request, Spell spell, bool lasting)
    {
        if (request.Context != SpellUseContext.Exploration)
            return SpellCastResult.Failure($"{spell.Name} can only be cast outside combat.");

        var caster = request.Caster;
        if (lasting)
        {
            var permanent = new SpellCastResult { Success = true, ContinualLight = true };
            permanent.Events.Add($"{caster.Name} casts {spell.Name}. The party sees two steps further until it leaves the dungeon or enters darkness.");
            return permanent;
        }

        var turns = 6 + Math.Max(1, request.EffectiveCasterLevel ?? caster.Level);
        var steps = turns * LightHandler.StepsPerTurn;
        var result = new SpellCastResult { Success = true, LightSteps = steps };
        result.Events.Add($"{caster.Name} casts {spell.Name}. The party sees one step further for {turns} turns ({steps} steps).");
        return result;
    }

    private static SpellCastResult ResolveShowPosition(SpellCastRequest request, Spell spell)
    {
        var position = WizardryDungeonPosition.Provider?.Invoke();
        if (string.IsNullOrWhiteSpace(position))
            return SpellCastResult.Failure($"{spell.Name} only works inside the dungeon.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {position}");
        return result;
    }

    private static SpellCastResult ResolveExpeditionArmorClass(SpellCastRequest request, Spell spell, WizardrySpellEffect effect)
    {
        // Does not stack: a second cast only refreshes the bonus. Cleared when the party leaves the dungeon.
        var members = request.PartyTargets.Append(request.Caster)
            .Concat(request.CombatSession?.Party ?? Enumerable.Empty<Character>())
            .Distinct();
        foreach (var member in members)
            member.SetTemporaryArmorClassBonusUntilDungeonExit(effect.Dice);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. The party's AC improves by {effect.Dice} until it leaves the dungeon.");
        return result;
    }

    private static SpellCastResult ResolvePartyArmorClass(SpellCastRequest request, Spell spell, WizardrySpellEffect effect)
    {
        if (request.CombatSession == null)
            return SpellCastResult.Failure($"{spell.Name} can only be cast in combat.");

        // Buffs from several casts add up; the party AC is lowered for the rest of the battle.
        request.CombatSession.WizardryPartyArmorClassBonus += effect.Dice;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. The party's AC improves by {effect.Dice} for this battle.");
        return result;
    }

    private static SpellCastResult ResolveCurePoison(SpellCastRequest request, Spell spell)
    {
        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        if (target.HasStatus(CharacterStatus.Poisoned))
        {
            target.RemoveStatus(CharacterStatus.Poisoned);
            result.Events.Add($"{target.Name} is no longer poisoned.");
        }
        else
        {
            result.Events.Add($"{target.Name} was not poisoned.");
        }

        return result;
    }

    private static SpellCastResult ResolveCureParalysis(SpellCastRequest request, Spell spell)
    {
        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        if (target.HasStatus(CharacterStatus.Paralyzed))
        {
            target.ClearParalysis();
            result.Events.Add($"{target.Name} is no longer paralyzed.");
        }
        else
        {
            result.Events.Add($"{target.Name} was not paralyzed.");
        }

        return result;
    }

    private static SpellCastResult ResolveHeal(SpellCastRequest request, Spell spell, WizardrySpellEffect effect, Random rng)
    {
        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        if (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.HasStatus(CharacterStatus.Lost)
            || target.CurrentHitPoints <= 0)
            return SpellCastResult.Failure($"{spell.Name} cannot heal {target.Name} in current condition.");

        var before = target.CurrentHitPoints;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        var rolled = 0;
        var augmented = false;
        if (effect.Kind == WizardryEffectKind.FullHeal)
        {
            target.CurrentHitPoints = target.MaxHitPoints;
            target.Status &= CharacterStatus.Dead | CharacterStatus.Ashes | CharacterStatus.Lost;
            target.ParalyzedRoundsRemaining = 0;
        }
        else
        {
            rolled = RollDice(effect.Dice, effect.Sides, rng);
            var heal = rolled;
            augmented = request.CombatSession?.WizardryAugmentedMagic == true;
            if (augmented)
                heal = (int)Math.Ceiling(heal * 1.5);
            rolled = heal;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + heal);
        }

        var actual = Math.Max(0, target.CurrentHitPoints - before);
        result.Events.Add(effect.Kind == WizardryEffectKind.FullHeal
            ? $"{target.Name} is fully healed for {actual} HP."
            : $"{target.Name} is healed for {actual} HP (rolled {rolled} on {effect.Dice}d{effect.Sides}{(augmented ? ", augmented" : "")}).");
        result.HpChanges[target.Name] = actual;
        return result;
    }

    private static SpellCastResult ResolveAgainstMonsters(SpellCastRequest request, Spell spell, WizardrySpellEffect effect, Random rng)
    {
        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure($"{spell.Name} can only be cast in combat.");

        var targets = SelectTargets(request, spell, effect, rng);
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targets)
            ApplyToMonster(monster, spell, effect, rng, result, request.CombatSession?.WizardryAugmentedMagic == true ? 1.5 : 1.0);

        return result;
    }

    private static List<MonsterInstance> SelectTargets(SpellCastRequest request, Spell spell, WizardrySpellEffect effect, Random rng)
    {
        var session = request.CombatSession;
        var alive = session != null
            ? session.Monsters.Where(m => m.IsAlive).ToList()
            : request.MonsterTargets.Where(m => m.IsAlive).ToList();

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        var picked = firstTarget?.MonsterIndex is int idx ? alive.FirstOrDefault(m => m.Index == idx) : null;
        var groupId = firstTarget?.TargetGroupId ?? picked?.GroupId;

        if (spell.TargetingScope == SpellTargetingScope.AllGroups)
            return alive;

        var inGroup = string.IsNullOrWhiteSpace(groupId) ? alive : alive.Where(m => m.GroupId == groupId).ToList();
        if (inGroup.Count == 0)
            inGroup = alive.Where(m => m.GroupId == alive[0].GroupId).ToList();
        if (inGroup.Count == 0)
            return inGroup;

        if (effect.Group)
            return inGroup;

        return new List<MonsterInstance> { picked ?? inGroup[rng.Next(inGroup.Count)] };
    }

    private static void ApplyToMonster(MonsterInstance monster, Spell spell, WizardrySpellEffect effect, Random rng, SpellCastResult result, double damageMultiplier = 1.0)
    {
        if (effect.UndeadOnly && monster.InstanceMonsterType != MonsterType.Undead && monster.Template.Type != MonsterType.Undead)
        {
            result.Events.Add($"{monster.DisplayName} is not undead; {spell.Name} has no effect.");
            return;
        }

        switch (effect.Kind)
        {
            case WizardryEffectKind.Damage:
            {
                var rolled = RollDice(effect.Dice, effect.Sides, rng);
                if (monster.HasStatus(MonsterStatus.Asleep) || monster.HasStatus(MonsterStatus.Paralyzed))
                    rolled *= 2;
                rolled = (int)Math.Ceiling(rolled * damageMultiplier);

                var outcome = SpellDamageSaveHelper.ApplyToMonster(monster, rolled, rng, spell.Name);
                result.Events.Add(SpellDamageSaveHelper.FormatSaveAndDamageLine(monster.DisplayName, rolled, outcome));
                if (!monster.IsAlive)
                    result.Events.Add($"{monster.DisplayName} is destroyed.");
                AddHpChange(result, monster, -outcome.ActualDamage);
                break;
            }
            case WizardryEffectKind.KillWeak:
                if (monster.CurrentHitPoints < WizardryRulesLimits.MakanitoHitPointLimit)
                    Kill(monster, spell, result);
                else
                    result.Events.Add($"{monster.DisplayName} has too many hit points to be suffocated.");
                break;
            case WizardryEffectKind.KillSave:
                ResolveBadi(monster, spell, rng, result);
                break;
            case WizardryEffectKind.Kill:
                Kill(monster, spell, result);
                break;
            case WizardryEffectKind.ReduceToD8:
            {
                var before = monster.CurrentHitPoints;
                var newHp = Math.Min(before, rng.Next(1, 9));
                monster.CurrentHitPoints = newHp;
                result.Events.Add($"{monster.DisplayName} is reduced to {newHp} HP.");
                AddHpChange(result, monster, newHp - before);
                break;
            }
            case WizardryEffectKind.Sleep:
                ApplyStatus(monster, spell, MonsterStatus.Asleep, rng.Next(2, 6), "falls asleep", rng, result);
                break;
            case WizardryEffectKind.Paralyze:
                ApplyStatus(monster, spell, MonsterStatus.Paralyzed, rng.Next(2, 6), "is paralyzed", rng, result);
                break;
            case WizardryEffectKind.Silence:
                ApplyStatus(monster, spell, MonsterStatus.Silenced, rng.Next(2, 6), "is silenced", rng, result);
                break;
            case WizardryEffectKind.GroupArmorClass:
                monster.AdjustArmorClass(effect.Dice);
                result.Events.Add($"{monster.DisplayName} is enveloped in darkness; its AC worsens by {effect.Dice}.");
                break;
            case WizardryEffectKind.Fear:
                ApplyStatus(monster, spell, MonsterStatus.Feared, rng.Next(2, 6), "is terrified", rng, result);
                break;
        }
    }

    private static void ApplyStatus(MonsterInstance monster, Spell spell, MonsterStatus status, int rounds, string text, Random rng, SpellCastResult result)
    {
        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(monster, spell.Id))
        {
            result.Events.Add($"{monster.DisplayName} is immune to {spell.Name}.");
            return;
        }

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(monster, 20);
        var saveRoll = rng.Next(1, 21);
        if (saveRoll >= saveTarget || SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, rng, spell.Name))
        {
            result.Events.Add($"{monster.DisplayName} resists {spell.Name} (save {saveRoll} vs {saveTarget}).");
            return;
        }

        monster.SetStatus(status, rounds);
        result.Events.Add($"{monster.DisplayName} {text} for {rounds} round(s).");
    }

    private static void ResolveBadi(MonsterInstance monster, Spell spell, Random rng, SpellCastResult result)
    {
        var isUndead = monster.Template.Type == MonsterType.Undead
                       || monster.Template.SpecialAbilities.Any(a => string.Equals(a.Name?.Trim(), "Undead", StringComparison.OrdinalIgnoreCase));
        if (isUndead)
        {
            result.Events.Add($"{monster.DisplayName} is undead and is unaffected by {spell.Name}.");
            return;
        }

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(monster, 20);
        var saveRoll = rng.Next(1, 21);
        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{monster.DisplayName} survives (save {saveRoll} vs {saveTarget}).");
            return;
        }

        var deathResistance = SpellDamageSaveHelper.GetDeathResistancePercent(monster);
        if (deathResistance > 0)
        {
            var resistRoll = rng.Next(1, 101);
            if (resistRoll <= deathResistance)
            {
                result.Events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}) but resists death ({resistRoll} on 1d100 vs {deathResistance}%).");
                return;
            }
        }

        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, rng, spell.Name))
        {
            result.Events.Add($"{monster.DisplayName} negates {spell.Name} with magic resistance.");
            return;
        }

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = 0;
        result.Events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}) and dies instantly. HP {before}->0.");
        AddHpChange(result, monster, -before);
    }

    private static void Kill(MonsterInstance monster, Spell spell, SpellCastResult result)
    {
        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = 0;
        result.Events.Add($"{monster.DisplayName} is destroyed by {spell.Name}.");
        AddHpChange(result, monster, -before);
    }

    private static void AddHpChange(SpellCastResult result, MonsterInstance monster, int delta)
    {
        result.HpChanges[monster.DisplayName] = result.HpChanges.TryGetValue(monster.DisplayName, out var existing) ? existing + delta : delta;
    }

    private static int RollDice(int dice, int sides, Random rng)
    {
        var total = 0;
        for (var i = 0; i < dice; i++)
            total += rng.Next(1, sides + 1);
        return total;
    }
}

internal static class WizardryRulesLimits
{
    public const int MakanitoHitPointLimit = 35;
}
