using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class NeutralizePoisonHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "neutralize_poison", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "neutralize_poison_druid", StringComparison.OrdinalIgnoreCase);

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

        var wasPoisoned = target.HasStatus(CharacterStatus.Poisoned);
        target.RemoveStatus(CharacterStatus.Poisoned);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(wasPoisoned
            ? $"{target.Name} is no longer poisoned."
            : $"{target.Name} is not poisoned.");

        return result;
    }
}
