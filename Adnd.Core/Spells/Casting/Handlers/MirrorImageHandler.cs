namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class MirrorImageHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
    {
        return string.Equals(spellId, "mirror_image", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spellId, "mirror_image_illusionist", StringComparison.OrdinalIgnoreCase);
    }

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Mirror Image can only be cast in combat.");

        var caster = request.Caster;
        var rng = request.Rng ?? Random.Shared;

        var imageCount = rng.Next(1, 5); // 1d4
        var rounds = Math.Max(1, caster.Level * 2);

        request.CombatSession.SetMirrorImage(caster.Name, imageCount, rounds);

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{imageCount} mirror image(s) appear around {caster.Name} for {rounds} rounds."
            }
        };
    }
}
