namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ChantHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "chant", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Chant can only be cast in combat.");

        request.CombatSession.StartChant(request.Caster.Name);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"{request.Caster.Name} begins chanting; allies gain +1 and enemies suffer -1 while the chant continues.");
        return result;
    }
}