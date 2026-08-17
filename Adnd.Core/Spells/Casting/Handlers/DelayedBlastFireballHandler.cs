using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class DelayedBlastFireballHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "delayed_blast_fireball", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Delayed Blast Fireball can only be cast in combat.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy targets for Delayed Blast Fireball.");

        var rng = request.Rng ?? Random.Shared;
        var diceCount = Math.Max(1, request.Caster.Level);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targets)
        {
            var rolledDamage = 0;
            for (int i = 0; i < diceCount; i++)
                rolledDamage += rng.Next(1, 7) + 1; // 1d6 + 1 per die

            var before = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actualDamage = before - monster.CurrentHitPoints;

            result.Events.Add($"{monster.DisplayName} takes {actualDamage} fire damage (rolled {rolledDamage}). HP {before}->{monster.CurrentHitPoints}.");
            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} is consumed by the blast!");
        }

        result.Events.Add($"Delayed Blast Fireball affects all enemy groups ({targets.Count} target(s)).");
        return result;
    }
}
