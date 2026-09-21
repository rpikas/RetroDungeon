using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ConfusionDruidHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "confusion_druid", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "confusion", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Confusion can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Confusion requires combat session context.");

        var rng = request.Rng ?? Random.Shared;
        var casterLevel = Math.Max(1, request.Caster.Level);

        string targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g))
            ?? session.GetDistinctGroupIds().FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0)
            ?? "default";

        var groupMonsters = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (groupMonsters.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var highestHitDice = groupMonsters.Max(m => Math.Max(1, m.Template.HitDice));
        var baseCount = rng.Next(1, 5) + rng.Next(1, 5); // 2-8
        var bonusCount = Math.Max(0, casterLevel - highestHitDice);
        var desiredCount = baseCount + bonusCount;

        var intelligentCandidates = new List<MonsterInstance>();
        foreach (var monster in groupMonsters)
        {
            MonsterIntelligenceResolver.RegisterMonsterIntelligence(monster.Template.Name, monster.Template.Intelligence);
            var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(monster.Template.Name);
            if (intelligence <= 1)
                continue;

            intelligentCandidates.Add(monster);
        }

        if (intelligentCandidates.Count == 0)
            return SpellCastResult.Failure("No intelligent creatures in target group can be affected.");

        var affectedCount = Math.Min(intelligentCandidates.Count, Math.Max(0, desiredCount));
        if (affectedCount <= 0)
            return SpellCastResult.Failure("Confusion affects no creatures this cast.");

        var affected = intelligentCandidates
            .OrderBy(_ => rng.Next())
            .Take(affectedCount)
            .ToList();

        var durationRounds = casterLevel; // 1 round / level

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"Target group: {targetGroupId}. Base affected: {baseCount}, bonus: +{bonusCount} (caster level {casterLevel} - highest HD {highestHitDice}), total attempted: {desiredCount}.");

        foreach (var monster in affected)
        {
            monster.SetStatus(MonsterStatus.Confused, durationRounds);
            result.Events.Add($"{monster.DisplayName} is confused for up to {durationRounds} round(s).");
        }

        result.Events.Add($"Confusion affects {affected.Count} creature(s).");
        return result;
    }
}