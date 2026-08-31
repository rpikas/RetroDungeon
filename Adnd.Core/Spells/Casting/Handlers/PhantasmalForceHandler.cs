using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class PhantasmalForceHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "phantasmal_force", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Phantasmal Force can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Phantasmal Force requires combat session context.");

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
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}! A terrifying red dragon illusion breathes on group {targetGroupId}.");

        MonsterInstance? disbeliefMonster = null;
        int disbeliefRoll = 0;
        int disbeliefTarget = 0;

        foreach (var monster in targets)
        {
            var saveTarget = monster.Template.SavingThrows?.Spell ?? 0;
            var saveRoll = rng.Next(1, 21);
            var saved = saveTarget > 0 && saveRoll >= saveTarget;

            RuleApplicationInfo.Publish(
                "PHB",
                "Phantasmal Force",
                $"{monster.DisplayName} save vs spell against illusion",
                $"Roll d20, need {saveTarget}+ to disbelieve.",
                "1",
                "20",
                saveRoll.ToString(),
                saved
                    ? "Save made. Monster disbelieves and warns the group; illusion collapses."
                    : "Save failed. Monster believes the dragon breath is real.");

            if (!saved)
                continue;

            disbeliefMonster = monster;
            disbeliefRoll = saveRoll;
            disbeliefTarget = saveTarget;
            break;
        }

        if (disbeliefMonster != null)
        {
            result.Events.Add($"{disbeliefMonster.DisplayName} makes save ({disbeliefRoll} vs {disbeliefTarget}) and shouts it is fake!");
            result.Events.Add("The illusion is exposed. No monsters in the group take damage.");
            return result;
        }

        foreach (var monster in targets)
        {
            var rolledDamage = 0;
            for (int i = 0; i < 16; i++)
                rolledDamage += rng.Next(1, 7);

            var before = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actual = Math.Max(0, before - monster.CurrentHitPoints);

            result.Events.Add($"{monster.DisplayName} believes the dragon breath, takes {actual} illusionary fire damage (rolled {rolledDamage}). HP {before}->{monster.CurrentHitPoints}.");
            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} dies from terror and shock.");

            if (result.HpChanges.ContainsKey(monster.DisplayName))
                result.HpChanges[monster.DisplayName] -= actual;
            else
                result.HpChanges[monster.DisplayName] = -actual;
        }

        return result;
    }
}
