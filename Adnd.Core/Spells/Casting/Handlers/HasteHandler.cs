using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HasteHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "haste", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Haste can only be cast in combat.");

        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected for Haste.");

        if (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.HasStatus(CharacterStatus.Lost))
            return SpellCastResult.Failure("Target cannot be affected by Haste.");

        var casterLevel = request.Caster.Classes.Contains(CharacterClass.MagicUser)
            ? request.Caster.GetClassLevel(CharacterClass.MagicUser)
            : request.Caster.Level;
        var rounds = 3 + Math.Max(1, casterLevel);

        var session = request.CombatSession;
        if (!session.IsHasted(target.Name))
        {
            session.SetHaste(target.Name, rounds, target.Move);
            target.Move *= 2;
        }
        else
        {
            session.SetHaste(target.Name, rounds, session.GetHasteOriginalMove(target.Name));
        }

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{target.Name} is hasted for {rounds} rounds: move is doubled and attacks are doubled."
            }
        };
    }
}
