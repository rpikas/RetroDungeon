using Adnd.Core.Combat.Sessions;
using System.Collections;
using System.Runtime.InteropServices;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ColorSprayHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "color_spray", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "color spray", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Color Spray can only be cast in combat.");

        var session = request.CombatSession;

        // Determine target group
        string targetGroupId = "default";
        var firstTarget = request.Targets.FirstOrDefault();
        if (firstTarget?.TargetGroupId != null)
        {
            targetGroupId = firstTarget.TargetGroupId;
        }
        else if (session != null && session.GetDistinctGroupIds().Count() > 1)
        {
            // If multiple groups exist and no target specified, target the first group with alive monsters
            targetGroupId = session.GetDistinctGroupIds()
                .FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0) ?? "default";
        }

        // Get targets from the specified group
        List<MonsterInstance> targets;
        if (session != null)
        {
            targets = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        }
        else
        {
            // Fallback to old behavior if no session
            targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        }
            if (targets.Count == 0)
          //      targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
          return SpellCastResult.Failure("No valid enemy targets for Color Spray.");
    
        var rng = request.Rng ?? Random.Shared;
        var casterLevel = request.Caster.Level;

        //affects 1-6 monsters from group
        var maxAffected = rng.Next(1, 7);
        if (targets.Count > maxAffected)
            targets = targets.OrderBy(_ => rng.Next()).Take(maxAffected).ToList();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"Color Spray affects {targets.Count} creatures" +
            $"");

        foreach (var monster in targets)
        {
            var monsterHd = monster.Template.HitDice;
            var hdDifference = monsterHd - casterLevel;

            if (hdDifference <= 0)
            {
                //Case A: Monster HD <= caster level -> uncounscious, no save
                var rounds = rng.Next(1, 5) + rng.Next(1, 5);// 2d4 rounds
                monster.SetStatus(MonsterStatus.Unconscious, rounds);
                result.Events.Add($"{monster.DisplayName} has {monsterHd} HD, which is less than or equal to caster level " +
                    $"{casterLevel}. {monster.DisplayName} becomes unconscious for {rounds} round(s).");
            }
            else if (hdDifference <= 2)
            {
                //Case B: Monster HD is 1-2 higher than caster level -> save for half duration
                var saveTarget = monster.Template.SavingThrows?.Spell ?? 20;
                var saveRoll = rng.Next(1, 21);

                if (saveRoll >= saveTarget)
                {
                    result.Events.Add($"{monster.DisplayName} resists Color Spray (save {saveRoll} vs {saveTarget}).");
                }
                else
                {
                    var rounds = rng.Next(1, 5);
                    monster.SetStatus(MonsterStatus.Unconscious, rounds);
                    result.Events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}) and becomes unconscious for {rounds} round(s).");
                }
            }
            else
            {
                //Case C: Monster HD is more than 2 higher than caster level -> save for full duration
                var saveTarget = monster.Template.SavingThrows?.Spell ?? 20;
                var saveRoll = rng.Next(1, 21);

                if (saveRoll >= saveTarget)
                {
                    result.Events.Add($"{monster.DisplayName} resists Color Spray (save {saveRoll} vs {saveTarget}).");
                }
                else
                {
                    var rounds = rng.Next(1, 5);
                    monster.SetStatus(MonsterStatus.Stunned, 1);
                    result.Events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}) and becomes stunned for 1 round(s).");
                }
            }
        }
        return result;
    }
}

