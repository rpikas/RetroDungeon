using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class UnholyWordHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "unholy_word", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Unholy Word can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Unholy Word requires combat session context.");

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

        var fiends = groupTargets
            .Where(m => m.InstanceMonsterType is MonsterType.Devil or MonsterType.Demon)
            .ToList();

        if (fiends.Count == 0)
            return SpellCastResult.Failure("Unholy Word only affects Devils or Demons in the targeted group.");

        var rng = request.Rng ?? Random.Shared;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in fiends)
        {
            var hd = monster.Template.HitDice;
            if (hd <= 3)
            {
                monster.CurrentHitPoints = 0;
                result.Events.Add($"{monster.DisplayName} ({monster.InstanceMonsterType}, {hd} HD) is destroyed by unholy power!");
                continue;
            }

            if (hd <= 7)
            {
                var rounds = rng.Next(10, 41); // 10-40
                monster.SetStatus(MonsterStatus.Paralyzed, rounds);
                result.Events.Add($"{monster.DisplayName} ({hd} HD) is paralyzed for {rounds} round(s).");
                continue;
            }

            if (hd <= 11)
            {
                var rounds = rng.Next(2, 9); // 2-8
                monster.SetStatus(MonsterStatus.Stunned, rounds);
                result.Events.Add($"{monster.DisplayName} ({hd} HD) is stunned for {rounds} round(s).");
                continue;
            }

            monster.AdjustThac0(-2);
            result.Events.Add($"{monster.DisplayName} ({hd} HD) gets -2 THAC0.");
        }

        if (fiends.Count != groupTargets.Count)
            result.Events.Add("Non-devil/non-demon monsters in the targeted group are unaffected.");

        result.Events.Add($"Unholy Word affected group '{targetGroupId}'.");
        return result;
    }
}
