using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class StoneToFleshHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "stone_to_flesh", StringComparison.OrdinalIgnoreCase);

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

        if (target.HasStatus(CharacterStatus.Dead)
            || target.HasStatus(CharacterStatus.Ashes)
            || target.HasStatus(CharacterStatus.Lost)
            || target.CurrentHitPoints <= 0)
        {
            return SpellCastResult.Failure($"{spell.Name} cannot affect {target.Name} in current condition.");
        }

        var wasPetrified = target.HasStatus(CharacterStatus.Petrified);
        if (wasPetrified)
            target.RemoveStatus(CharacterStatus.Petrified);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(wasPetrified
            ? $"{target.Name} is restored from petrification."
            : $"{target.Name} is not petrified.");

        return result;
    }
}
