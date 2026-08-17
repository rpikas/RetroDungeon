using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class MelfsAcidArrowHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "melfs_acid_arrow", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Melf's Acid Arrow can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (firstTarget?.TargetGroupId != null && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        if (target == null)
        {
            target = firstTarget?.MonsterIndex is int idx
                ? request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive)
                : request.MonsterTargets.FirstOrDefault(m => m.IsAlive);
        }

        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var rounds = rng.Next(1, 4);
        target.SetStatus(MonsterStatus.AcidArrow, rounds);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"{target.DisplayName} is hit by acid for {rounds} round(s).");
        return result;
    }
}
