using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class WallOfFireHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "wall_of_fire", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Wall of Fire can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Wall of Fire requires combat session context.");

        var casterLevel = Math.Max(1, request.Caster.Level);

        string targetGroupId = "default";
        var firstTarget = request.Targets.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId))
        {
            targetGroupId = firstTarget.TargetGroupId!;
        }
        else if (session.GetDistinctGroupIds().Count() > 1)
        {
            targetGroupId = session.GetDistinctGroupIds()
                .FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0) ?? "default";
        }

        var groupTargets = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (groupTargets.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var rounds = casterLevel; // 1 round per caster level
        foreach (var monster in groupTargets)
            monster.SetStatus(MonsterStatus.WallOfFire, rounds);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"A wall of fire surrounds group {targetGroupId} for {rounds} round(s).");
        return result;
    }
}
