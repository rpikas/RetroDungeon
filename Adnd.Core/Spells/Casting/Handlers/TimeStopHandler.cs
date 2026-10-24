namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class TimeStopHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "time_stop", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (request.Context != SpellUseContext.Combat || session == null)
            return SpellCastResult.Failure("Time Stop can only be cast in combat.");

        var rng = request.Rng ?? Random.Shared;
        var actions = rng.Next(1, 4);
        session.StartTimeStop(request.Caster.Name, actions);

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}! Time stops for everyone but the caster.",
                $"{request.Caster.Name} may act {actions} more time(s) before time resumes."
            }
        };
    }
}
