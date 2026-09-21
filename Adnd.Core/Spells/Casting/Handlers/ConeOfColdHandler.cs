using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ConeOfColdHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "cone_of_cold", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Cone of Cold can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Cone of Cold requires combat session context.");

        var rng = request.Rng ?? Random.Shared;
        var casterLevel = Math.Max(1, request.Caster.Level);

        string targetGroupId = "default";
        var firstTarget = request.Targets.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId))
        {
            targetGroupId = firstTarget!.TargetGroupId!;
        }
        else if (session.GetDistinctGroupIds().Count() > 1)
        {
            targetGroupId = session.GetDistinctGroupIds()
                .FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0) ?? "default";
        }

        var targetMonsters = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targetMonsters.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var damagePerTarget = (rng.Next(1, 5) + 1) * casterLevel;

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"Damage roll: (1d4+1) x {casterLevel} = {damagePerTarget}.");

        foreach (var monster in targetMonsters)
        {
            var outcome = SpellDamageSaveHelper.ApplyToMonster(monster, damagePerTarget, rng, spell.Name);
            result.Events.Add(SpellDamageSaveHelper.FormatSaveAndDamageLine(monster.DisplayName, damagePerTarget, outcome));

            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} is frozen to death!");
        }

        result.Events.Add($"Cone of Cold affected {targetMonsters.Count} monster(s) in {targetGroupId}.");
        return result;
    }
}