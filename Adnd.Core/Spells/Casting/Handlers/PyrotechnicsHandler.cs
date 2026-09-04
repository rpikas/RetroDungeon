using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class PyrotechnicsHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "pyrotechnics", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Pyrotechnics can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Pyrotechnics requires combat session context.");

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

        var rounds = Math.Max(1, request.Caster.Level);
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}! Blinding sparks engulf group '{targetGroupId}'.");

        foreach (var monster in targets)
        {
            var existingRounds = monster.GetStatusRounds(MonsterStatus.Blinded);
            if (existingRounds > 0)
            {
                var keptRounds = Math.Max(existingRounds, rounds);
                monster.SetStatus(MonsterStatus.Blinded, keptRounds);
                result.Events.Add($"{monster.DisplayName} is already blinded; blindness duration is now {keptRounds} round(s).");
                continue;
            }

            monster.SetStatus(MonsterStatus.Blinded, rounds);
            result.Events.Add($"{monster.DisplayName} is blinded for {rounds} round(s) (no save). THAC0 suffers -4 while attacking non-invisible targets.");
        }

        return result;
    }
}
