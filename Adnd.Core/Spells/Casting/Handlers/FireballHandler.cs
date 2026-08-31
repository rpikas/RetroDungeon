using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FireballHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "fireball", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var caster = request.Caster;
        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Fireball requires combat session context.");

        // Determine damage: 1d6 per caster level, max 10d6
        var diceCount = Math.Min(10, caster.Level);
        var rng = request.Rng ?? Random.Shared;

        // Determine target group from request targets or default
        string targetGroupId = "default";
        var firstTarget = request.Targets.FirstOrDefault();
        if (firstTarget?.TargetGroupId != null)
        {
            targetGroupId = firstTarget.TargetGroupId;
        }
        else if (session.GetDistinctGroupIds().Count() > 1)
        {
            // If multiple groups exist and no target specified, target the first group with alive monsters
            targetGroupId = session.GetDistinctGroupIds()
                .FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0) ?? "default";
        }

        var targetMonsters = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targetMonsters.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{caster.Name} casts {spell.Name}!");

        if (targetMonsters.Count == 1)
        {
            result.Events.Add($"A massive fireball explodes, engulfing {targetMonsters.First().DisplayName}!");
        }
        else
        {
            result.Events.Add($"A massive fireball explodes among the {targetMonsters.First().Name} group!");
        }

        // Apply damage to each monster in the group
        foreach (var monster in targetMonsters)
        {
            int totalDamage = 0;
            for (int i = 0; i < diceCount; i++)
            {
                totalDamage += rng.Next(1, 7); // 1d6
            }

            var outcome = SpellDamageSaveHelper.ApplyToMonster(monster, totalDamage, rng, spell.Name);

            if (outcome.ActualDamage > 0)
            {
                result.Events.Add(
                    $"{monster.DisplayName} save vs spell: rolled {outcome.SaveRoll} vs {outcome.SaveTarget} => {(outcome.Saved ? "SUCCESS" : "FAIL")}. " +
                    $"Damage {totalDamage}{(outcome.Saved ? $" halved to {outcome.AppliedDamage}" : string.Empty)}. HP {outcome.BeforeHp}->{outcome.AfterHp}.");
                if (monster.CurrentHitPoints <= 0)
                {
                    result.Events.Add($"{monster.DisplayName} is incinerated!");
                }
            }
        }

        result.Events.Add($"The fireball affected {targetMonsters.Count} monsters!");
        return result;
    }
}
