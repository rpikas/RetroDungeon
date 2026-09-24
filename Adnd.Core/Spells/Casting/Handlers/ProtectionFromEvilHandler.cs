using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ProtectionFromEvilHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "protection_from_evil", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "protection_from_evil_magic_user", StringComparison.OrdinalIgnoreCase);

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

        var casterLevel = ResolveCasterLevel(request, spell);
        var rounds = spell.SpellClass == SpellClass.Cleric
            ? 3 * casterLevel
            : 2 * casterLevel;

        target.SetProtectionFromEvil(rounds);

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{target.Name} gains +2 AC and +2 saves vs evil sources for {rounds} rounds."
            }
        };
    }

    private static int ResolveCasterLevel(SpellCastRequest request, Spell spell)
    {
        var caster = request.Caster;
        var classLevel = spell.SpellClass switch
        {
            SpellClass.Cleric when caster.Classes.Contains(CharacterClass.Cleric) => caster.GetClassLevel(CharacterClass.Cleric),
            SpellClass.MagicUser when caster.Classes.Contains(CharacterClass.MagicUser) => caster.GetClassLevel(CharacterClass.MagicUser),
            _ => caster.Level
        };

        return Math.Max(1, classLevel);
    }
}
