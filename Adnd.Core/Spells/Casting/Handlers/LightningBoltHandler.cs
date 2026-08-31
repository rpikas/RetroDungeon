using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class LightningBoltHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "lightning_bolt", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Lightning Bolt can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Lightning Bolt requires combat session context.");

        var rng = request.Rng ?? Random.Shared;
        var diceCount = Math.Max(1, request.Caster.Level); // 1d6 per caster level

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

        var targetMonsters = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targetMonsters.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targetMonsters)
        {
            var rolledDamage = 0;
            for (int i = 0; i < diceCount; i++)
                rolledDamage += rng.Next(1, 7);

            var outcome = SpellDamageSaveHelper.ApplyToMonster(monster, rolledDamage, rng, spell.Name);
            result.Events.Add(
                $"{monster.DisplayName} save vs spell rolled {outcome.SaveRoll} vs {outcome.SaveTarget} => {(outcome.Saved ? "SUCCESS" : "FAIL")}. " +
                $"Damage {rolledDamage}{(outcome.Saved ? $" halved to {outcome.AppliedDamage}" : string.Empty)}. HP {outcome.BeforeHp}->{outcome.AfterHp}.");
            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} is electrocuted!");
        }

        result.Events.Add($"Lightning Bolt affected {targetMonsters.Count} monster(s).");
        return result;
    }
}
