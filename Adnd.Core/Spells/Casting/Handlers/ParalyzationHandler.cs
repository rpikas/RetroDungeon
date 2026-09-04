using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ParalyzationHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "paralyzation", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Paralyzation can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Paralyzation requires combat session context.");

        var rng = request.Rng ?? Random.Shared;
        var casterLevel = Math.Max(1, request.Caster.Level);
        var hdLimit = casterLevel * 2;

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

        var candidates = session.GetAliveMonstersByGroup(targetGroupId)
            .OrderBy(m => m.Template.HitDice)
            .ThenBy(_ => rng.Next())
            .ToList();

        if (candidates.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var selected = new List<MonsterInstance>();
        var hdBudget = hdLimit;

        foreach (var monster in candidates)
        {
            var hd = Math.Max(1, monster.Template.HitDice);
            if (hd > hdBudget)
                continue;

            selected.Add(monster);
            hdBudget -= hd;
            if (hdBudget <= 0)
                break;
        }

        if (selected.Count == 0)
            return SpellCastResult.Failure($"No monsters can be affected: each target exceeds HD budget ({hdLimit}).");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name} on group '{targetGroupId}'!");
        result.Events.Add($"Paralyzation can affect up to {hdLimit} total HD; selected {selected.Count} target(s).");

        var affectedCount = 0;
        const int paralyzedRounds = 4;

        foreach (var target in selected)
        {
            var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
            var saveRoll = rng.Next(1, 21);

            if (saveRoll >= saveTarget)
            {
                result.Events.Add($"{target.DisplayName} resists paralyzation (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            target.SetStatus(MonsterStatus.Paralyzed, paralyzedRounds);
            affectedCount++;
            result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is paralyzed for {paralyzedRounds} round(s)!");
        }

        result.Events.Add($"Paralyzation paralyzed {affectedCount} target(s).");
        return result;
    }
}
