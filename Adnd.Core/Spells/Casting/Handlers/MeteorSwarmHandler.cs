using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class MeteorSwarmHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "meteor_swarm", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Meteor Swarm can only be cast in combat.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy targets for Meteor Swarm.");

        var rng = request.Rng ?? Random.Shared;

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targets)
        {
            var rolledDamage = 0;
            for (int meteor = 0; meteor < 4; meteor++)
            {
                for (int die = 0; die < 10; die++)
                    rolledDamage += rng.Next(1, 7);
            }

            var before = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actualDamage = before - monster.CurrentHitPoints;

            result.Events.Add($"{monster.DisplayName} takes {actualDamage} fire damage (rolled {rolledDamage}). HP {before}->{monster.CurrentHitPoints}.");
            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} is obliterated by meteor impacts!");
        }

        result.Events.Add($"Meteor Swarm affects all enemy groups ({targets.Count} target(s)).");
        return result;
    }
}
