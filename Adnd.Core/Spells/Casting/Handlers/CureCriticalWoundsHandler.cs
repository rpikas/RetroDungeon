using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CureCriticalWoundsHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "cure_critical_wounds", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "cure_critical_wounds_druid", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "cause_critical_wounds", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "cause_critical_wounds_druid", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var isCause = spell.Id.StartsWith("cause_", StringComparison.OrdinalIgnoreCase);
        if (isCause)
            return ResolveCauseCriticalWounds(request, spell);

        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        if (target.HasStatus(CharacterStatus.Dead)
            || target.HasStatus(CharacterStatus.Ashes)
            || target.HasStatus(CharacterStatus.Lost)
            || target.CurrentHitPoints <= 0)
        {
            return SpellCastResult.Failure($"{spell.Name} cannot heal {target.Name} in current condition.");
        }

        var rng = request.Rng ?? Random.Shared;
        var before = target.CurrentHitPoints;
        var isDruidVersion = string.Equals(spell.Id, "cure_critical_wounds_druid", StringComparison.OrdinalIgnoreCase);
        // Cleric: 3d8+3, Druid: 3d6+3
        var heal = isDruidVersion
            ? rng.Next(1, 7) + rng.Next(1, 7) + rng.Next(1, 7) + 3
            : rng.Next(1, 9) + rng.Next(1, 9) + rng.Next(1, 9) + 3;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + heal);
        var actual = Math.Max(0, target.CurrentHitPoints - before);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(actual > 0
            ? $"{target.Name} is healed for {actual} HP."
            : $"{target.Name} is already at full health.");
        result.HpChanges[target.Name] = actual;
        return result;
    }

    private static SpellCastResult ResolveCauseCriticalWounds(SpellCastRequest request, Spell spell)
    {
        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        Combat.Sessions.MonsterInstance? target = null;
        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);

        if (firstTarget?.MonsterIndex is int idx)
            target = request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);

        if (target == null && !string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        target ??= request.MonsterTargets.FirstOrDefault(m => m.IsAlive);
        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var isDruidVersion = string.Equals(spell.Id, "cause_critical_wounds_druid", StringComparison.OrdinalIgnoreCase);
        var rolled = isDruidVersion
            ? rng.Next(1, 7) + rng.Next(1, 7) + rng.Next(1, 7) + 3
            : rng.Next(1, 9) + rng.Next(1, 9) + rng.Next(1, 9) + 3;

        var outcome = SpellDamageSaveHelper.ApplyToMonster(target, rolled, rng, spell.Name);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(SpellDamageSaveHelper.FormatSaveAndDamageLine(target.DisplayName, rolled, outcome));
        if (!target.IsAlive)
            result.Events.Add($"{target.DisplayName} is destroyed.");

        result.HpChanges[target.DisplayName] = -outcome.ActualDamage;
        return result;
    }
}
