using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FearHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "fear_magic_user", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "fear_illusionist", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Fear requires combat session context.");

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

        var rng = request.Rng ?? Random.Shared;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        foreach (var monster in targetMonsters)
        {
            var saveTarget = monster.Template.SavingThrows?.Spell ?? 0;
            var saveRoll = rng.Next(1, 21);
            var saved = saveTarget > 0 && saveRoll >= saveTarget;

            RuleApplicationInfo.Publish(
                "PHB",
                "Fear",
                $"{monster.DisplayName} saving throw vs spell ({spell.Name})",
                $"Roll d20, need {saveTarget}+ to save. Failed save means creature flees combat.",
                "1",
                "20",
                saveRoll.ToString(),
                saved
                    ? "Save made. Monster resists fear."
                    : "Save failed. Monster flees and is removed from combat.");

            if (saved)
            {
                result.Events.Add($"{monster.DisplayName} resists fear (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            monster.CurrentHitPoints = 0;
            result.Events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}), flees in terror, and is gone from the battle!");
        }

        result.Events.Add($"Fear affected group '{targetGroupId}'.");
        return result;
    }
}
