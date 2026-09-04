using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class SnareHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "snare", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Snare can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Snare requires combat session context.");

        var rng = request.Rng ?? Random.Shared;

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

        var targets = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        const int snareRounds = 5;
        foreach (var monster in targets)
        {
            var damage = rng.Next(1, 7);
            var beforeHp = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - damage);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            monster.SetStatus(MonsterStatus.Snared, snareRounds);

            result.Events.Add($"{monster.DisplayName} is snared and takes {actualDamage} damage (1d6). HP {beforeHp}->{monster.CurrentHitPoints}.");
            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} collapses in the snare.");
        }

        result.Events.Add($"Snare affected group '{targetGroupId}' with no save." );
        return result;
    }
}
