namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class BlinkHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
    {
        return string.Equals(spellId, "blink", StringComparison.OrdinalIgnoreCase);
    }

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Blink can only be cast in combat.");

        var caster = request.Caster;
        var rounds = Math.Max(1, caster.Level);
        request.CombatSession.SetBlink(caster.Name, rounds);

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{caster.Name} begins to blink in and out of existence for {rounds} round(s)."
            }
        };
    }
}
