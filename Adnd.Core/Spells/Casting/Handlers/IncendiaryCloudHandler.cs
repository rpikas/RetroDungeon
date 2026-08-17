using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class IncendiaryCloudHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "incendiary_cloud", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Incendiary Cloud can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Incendiary Cloud requires combat session context.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid monsters for Incendiary Cloud.");

        var rounds = Math.Max(1, request.Caster.Level);
        foreach (var monster in targets)
            monster.SetStatus(MonsterStatus.IncendiaryCloud, rounds);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"An incendiary cloud spreads across all groups for {rounds} round(s).");
        return result;
    }
}
