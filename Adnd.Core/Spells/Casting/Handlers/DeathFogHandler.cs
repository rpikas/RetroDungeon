using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class DeathFogHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "death_fog", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Death Fog can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Death Fog requires combat session context.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid monsters for Death Fog.");

        var rounds = Math.Max(1, request.Caster.Level);
        foreach (var monster in targets)
            monster.SetStatus(MonsterStatus.DeathFog, rounds);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"A death fog spreads across all groups for {rounds} round(s).");
        return result;
    }
}
