using Adnd.Core.Characters;
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
            _ => ResolveAgainstMonsters(request, spell, effect, rng)
        };
    }

    private static SpellCastResult ResolveExpeditionArmorClass(SpellCastRequest request, Spell spell, WizardrySpellEffect effect)
    {
        // Does not stack: a second cast only refreshes the bonus. Cleared when the party leaves the dungeon.
        var members = request.PartyTargets.Append(request.Caster)
            .Concat(request.CombatSession?.Party ?? Enumerable.Empty<Character>())
            .Distinct();
        foreach (var member in members)
            member.TemporaryArmorClassBonusUntilDungeonExit = effect.Dice;

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

        if (effect.Kind == WizardryEffectKind.FullHeal)
        {
            target.CurrentHitPoints = target.MaxHitPoints;
            target.Status &= CharacterStatus.Dead | CharacterStatus.Ashes | CharacterStatus.Lost;
            target.ParalyzedRoundsRemaining = 0;
        }
        else
        {
            var heal = RollDice(effect.Dice, effect.Sides, rng);
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + heal);
        }

        var actual = Math.Max(0, target.CurrentHitPoints - before);
        result.Events.Add($"{target.Name} is healed for {actual} HP.");
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
            ApplyToMonster(monster, spell, effect, rng, result);

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

    private static void ApplyToMonster(MonsterInstance monster, Spell spell, WizardrySpellEffect effect, Random rng, SpellCastResult result)
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
