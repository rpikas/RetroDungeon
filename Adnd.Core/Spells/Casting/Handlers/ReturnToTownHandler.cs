namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ReturnToTownHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "word_of_recall", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "teleport", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Exploration)
            return SpellCastResult.Failure($"{spell.Name} can only be cast outside combat.");

        var result = new SpellCastResult { Success = true, ReturnToTown = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. The party is whisked away to town!");
        return result;
    }
}
