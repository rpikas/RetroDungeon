using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CloudkillHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "cloudkill", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "makanito", StringComparison.OrdinalIgnoreCase);

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

        var rng = request.Rng ?? Random.Shared;
        var killedCount = 0;
        foreach (var monster in targetMonsters)
        {
            var label = $"{monster.DisplayName} (HD {FormatHitDice(monster)})";
            var template = monster.Template;

            if (SpellDamageSaveHelper.HasPoisonOrParalysisImmunity(monster))
            {
                result.Events.Add($"{label} is immune to poison and is unaffected.");
                continue;
            }

            int? savePenalty;
            if (template.HitDice < 4 || (template.HitDice == 4 && template.ExtraHitPoints <= 0))
                savePenalty = null;
            else if (template.HitDice == 4 || template.HitDice == 5)
                savePenalty = 4;
            else if (template.HitDice == 6)
                savePenalty = 0;
            else
            {
                result.Events.Add($"{label} is too hardy and survives the {spell.Name} cloud.");
                continue;
            }

            if (savePenalty is int penalty)
            {
                var saveTarget = SpellDamageSaveHelper.GetMonsterPoisonSaveTarget(monster, 20);
                var saveRoll = rng.Next(1, 21);
                if (saveRoll - penalty >= saveTarget)
                {
                    result.Events.Add($"{label} saves vs. poison ({saveRoll}{(penalty > 0 ? $" -{penalty}" : "")} vs {saveTarget}) and survives.");
                    continue;
                }

                result.Events.Add($"{label} fails its save vs. poison ({saveRoll}{(penalty > 0 ? $" -{penalty}" : "")} vs {saveTarget}) and dies.");
            }
            else
            {
                result.Events.Add($"{label} dies instantly in the poisonous cloud.");
            }

            monster.CurrentHitPoints = 0;
            killedCount++;
        }

        result.Events.Add($"{spell.Name} affected group {targetGroupId}: {killedCount} slain.");
        return result;
    }

    private static string FormatHitDice(MonsterInstance monster)
        => monster.Template.ExtraHitPoints > 0
            ? $"{monster.Template.HitDice}+{monster.Template.ExtraHitPoints}"
            : monster.Template.HitDice.ToString();
}
