using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CloudkillHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "cloudkill", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Cloudkill can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Cloudkill requires combat session context.");

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

        var killedCount = 0;
        foreach (var monster in targetMonsters)
        {
            if (monster.Template.HitDice <= 3)
            {
                monster.CurrentHitPoints = 0;
                killedCount++;
                result.Events.Add($"{monster.DisplayName} (HD {monster.Template.HitDice}) dies instantly in the poisonous cloud.");
            }
            else
            {
                result.Events.Add($"{monster.DisplayName} (HD {monster.Template.HitDice}) survives Cloudkill.");
            }
        }

        result.Events.Add($"Cloudkill affected group {targetGroupId}: {killedCount} slain instantly.");
        return result;
    }
}
