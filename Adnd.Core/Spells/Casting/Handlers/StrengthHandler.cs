using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class StrengthHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "strength", StringComparison.OrdinalIgnoreCase);

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

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Strength can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        var existingBonus = session.GetStrengthBuffBonus(target.Name);
        if (existingBonus > 0)
        {
            target.Abilities.Strength = Math.Max(1, target.Abilities.Strength - existingBonus);
            session.ClearStrengthBuff(target.Name);
            target.TemporaryStrengthBonus = 0;
            target.TemporaryStrengthRoundsRemaining = 0;
        }

        var dieSides = GetDieSidesForClass(target.Class);
        var bonus = rng.Next(1, dieSides + 1);

        target.Abilities.Strength += bonus;
        session.SetStrengthBuff(target.Name, bonus, 10);
        target.TemporaryStrengthBonus = bonus;
        target.TemporaryStrengthRoundsRemaining = 10;

        return new SpellCastResult
        {
            Success = true,
            Events =
            {
                $"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}",
                $"{target.Name} gains +{bonus} STR (1d{dieSides}) for 10 rounds."
            }
        };
    }

    private static int GetDieSidesForClass(CharacterClass cls)
    {
        return cls switch
        {
            CharacterClass.Druid or CharacterClass.Cleric => 6,
            CharacterClass.Fighter or CharacterClass.Paladin or CharacterClass.Ranger => 8,
            CharacterClass.MagicUser or CharacterClass.Illusionist => 4,
            CharacterClass.Thief or CharacterClass.Assassin or CharacterClass.Bard => 5,
            CharacterClass.Monk => 4,
            _ => 4
        };
    }
}
