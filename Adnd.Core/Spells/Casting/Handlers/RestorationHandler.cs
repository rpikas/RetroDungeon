using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class RestorationHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "restoration", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        var clericLevel = Math.Max(1, request.Caster.GetClassLevel(CharacterClass.Cleric));
        if (!RestorationRules.CanBenefit(target, clericLevel))
        {
            var reason = RestorationRules.Apply(target, request.Caster, clericLevel);
            return SpellCastResult.Failure(string.Join(" ", reason));
        }

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name} on {target.Name}.");
        result.Events.AddRange(RestorationRules.Apply(target, request.Caster, clericLevel));
        return result;
    }
}
