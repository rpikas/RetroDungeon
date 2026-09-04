using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HealHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "heal", StringComparison.OrdinalIgnoreCase);

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
            return SpellCastResult.Failure($"{spell.Name} cannot heal {target.Name} in current condition.");
        }

        var beforeHp = target.CurrentHitPoints;

        target.CurrentHitPoints = target.MaxHitPoints;
        target.RemoveStatus(CharacterStatus.Blind);
        target.CureDiseaseAndRestoreConstitution();
        target.RemoveStatus(CharacterStatus.Feeblemind);
        target.RemoveStatus(CharacterStatus.Poisoned);
        target.RemoveStatus(CharacterStatus.Slowed);

        var healed = Math.Max(0, target.CurrentHitPoints - beforeHp);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(healed > 0
            ? $"{target.Name} is fully healed for {healed} HP."
            : $"{target.Name} is already at full health.");
        result.Events.Add($"{target.Name} is cured of blindness, disease (with Constitution restored), feeblemind, poison, and slow.");
        result.HpChanges[target.Name] = healed;

        return result;
    }
}
