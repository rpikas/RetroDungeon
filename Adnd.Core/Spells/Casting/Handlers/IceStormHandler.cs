using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class IceStormHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "ice_storm", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Ice Storm can only be cast in combat.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy targets for Ice Storm.");

        var rng = request.Rng ?? Random.Shared;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targets)
        {
            var damage = 0;
            for (int i = 0; i < 3; i++)
                damage += rng.Next(1, 11);

            var before = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - damage);
            var actualDamage = before - monster.CurrentHitPoints;

            result.Events.Add($"{monster.DisplayName} takes {actualDamage} cold damage (rolled {damage}). HP {before}->{monster.CurrentHitPoints}.");

            if (monster.CurrentHitPoints <= 0)
                result.Events.Add($"{monster.DisplayName} is frozen solid!");
        }

        result.Events.Add($"Ice Storm affects all enemy groups ({targets.Count} target(s)).");
        return result;
    }
}
