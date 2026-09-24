using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ProtectionFromLightningHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "protection_from_lightning", StringComparison.OrdinalIgnoreCase);

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

        var casterLevel = Math.Max(1, request.Caster.Level);
        var rounds = 10 * casterLevel;

        if (string.Equals(target.Name, request.Caster.Name, StringComparison.OrdinalIgnoreCase))
        {
            var absorption = 12 * casterLevel;
            target.SetProtectionFromLightningSelf(rounds, absorption);

            return new SpellCastResult
            {
                Success = true,
                Events =
                {
                    $"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                    $"{target.Name} gains immunity to normal lightning and magical lightning absorption pool {absorption} HP for {rounds} rounds."
                }
            };
        }

        target.SetProtectionFromLightningOther(rounds);

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{target.Name} gains normal-lightning immunity, +4 saves vs lightning, and 50% damage from magical lightning for {rounds} rounds."
            }
        };
    }
}
