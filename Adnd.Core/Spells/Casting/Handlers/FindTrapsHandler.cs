namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FindTrapsHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "find_traps", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Exploration)
            return SpellCastResult.Failure("Find Traps can only be cast outside combat.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}.");
        result.Events.Add("Magical insight reveals hidden traps nearby.");
        return result;
    }
}
