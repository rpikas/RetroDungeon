using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class DisintegrateHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "disintegrate", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Disintegrate can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (firstTarget?.MonsterIndex is int idx)
        {
            target = request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);
        }

        if (target == null && !string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        target ??= request.MonsterTargets.FirstOrDefault(m => m.IsAlive);

        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = 0;
        var actual = before;

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"{target.DisplayName} is disintegrated instantly. HP {before}->0.");
        result.HpChanges[target.DisplayName] = -actual;
        return result;
    }
}
