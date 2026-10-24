namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ProduceFlameHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "produce_flame", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (request.Context != SpellUseContext.Combat || session == null)
            return SpellCastResult.Failure("Produce Flame can only be cast in combat.");

        var rounds = 2 * Math.Max(1, request.Caster.Level);
        session.ProducedFlameExpiresRound[request.Caster.Name] = session.RoundNumber + rounds - 1;

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}. A harmless flame dances in the palm of their hand for {rounds} round(s); it can serve as a fire source for Pyrotechnics."
            }
        };
    }
}
