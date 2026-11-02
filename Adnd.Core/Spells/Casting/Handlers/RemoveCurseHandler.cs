using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class RemoveCurseHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "remove_curse", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}.");

        if (request.TargetItem != null)
        {
            result.Events.Add(EquipmentManager.RemoveCurse(request.TargetItem)
                ? $"The curse on {request.TargetItem.Name} is lifted."
                : $"{request.TargetItem.Name} is not cursed.");
            return result;
        }

        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        var freed = EquipmentManager.RemoveCursesFromEquipment(target);
        result.Events.Add(freed.Count == 0
            ? $"{target.Name} has no cursed equipped items."
            : $"The curse is lifted from {target.Name}'s {string.Join(", ", freed.Select(i => i.Name))}.");
        return result;
    }
}
