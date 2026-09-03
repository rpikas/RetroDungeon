using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class BarkskinHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "barkskin", StringComparison.OrdinalIgnoreCase);

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
            return SpellCastResult.Failure($"{spell.Name} cannot protect {target.Name} in current condition.");
        }

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Barkskin can only be cast in combat.");

        var acBonus = GetArmorClassImprovement(request.Caster.Level);
        var rounds = 4 + Math.Max(1, request.Caster.Level);

        var existingBonus = request.CombatSession.GetBarkskinBonus(target.Name);
        if (existingBonus > 0)
        {
            target.ArmorClass += existingBonus;
            request.CombatSession.ClearBarkskin(target.Name);
        }

        target.ArmorClass -= acBonus;
        request.CombatSession.SetBarkskin(target.Name, acBonus, rounds);

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{target.Name} gains {acBonus} AC improvement from Barkskin for {rounds} rounds."
            }
        };
    }

    private static int GetArmorClassImprovement(int druidLevel)
    {
        return 1;
    }
}
