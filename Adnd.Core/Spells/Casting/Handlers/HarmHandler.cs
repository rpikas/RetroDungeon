using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HarmHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "harm", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Harm can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;

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

        var remainingHp = rng.Next(1, 5);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.CurrentHitPoints, remainingHp);
        var actualDamage = Math.Max(0, before - target.CurrentHitPoints);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"{target.DisplayName} is reduced to {target.CurrentHitPoints} HP. HP {before}->{target.CurrentHitPoints}.");
        result.HpChanges[target.DisplayName] = -actualDamage;
        return result;
    }
}