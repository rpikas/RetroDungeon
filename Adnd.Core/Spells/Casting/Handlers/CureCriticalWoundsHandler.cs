using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CureCriticalWoundsHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "cure_critical_wounds", StringComparison.OrdinalIgnoreCase);

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

        var rng = request.Rng ?? Random.Shared;
        var before = target.CurrentHitPoints;
        var heal = rng.Next(1, 9) + rng.Next(1, 9) + rng.Next(1, 9) + 3;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + heal);
        var actual = Math.Max(0, target.CurrentHitPoints - before);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(actual > 0
            ? $"{target.Name} is healed for {actual} HP."
            : $"{target.Name} is already at full health.");
        result.HpChanges[target.Name] = actual;
        return result;
    }
}
