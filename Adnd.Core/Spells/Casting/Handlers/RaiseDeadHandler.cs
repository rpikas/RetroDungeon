using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class RaiseDeadHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "raise_dead", StringComparison.OrdinalIgnoreCase);

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

        if (target.HasStatus(CharacterStatus.Lost))
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; the character is lost.");

        var isDead = target.HasStatus(CharacterStatus.Dead) || target.CurrentHitPoints <= 0;
        if (!isDead)
            return SpellCastResult.Failure($"{spell.Name} can only target a dead character.");

        if (target.HasStatus(CharacterStatus.Ashes))
            return SpellCastResult.Failure($"{spell.Name} cannot target ashes.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        var constitution = target.Abilities.Constitution;
        var chance = SystemShockSurvivalChance(constitution);
        var rng = request.Rng ?? Random.Shared;
        var roll = rng.Next(1, 101);

        result.Events.Add($"System Shock roll for {target.Name}: {roll} (needs {chance} or less).");

        if (roll <= chance)
        {
            var beforeHp = target.CurrentHitPoints;
            target.RemoveStatus(CharacterStatus.Dead);
            target.RemoveStatus(CharacterStatus.Ashes);
            target.RemoveStatus(CharacterStatus.Lost);
            target.CurrentHitPoints = 1;
            target.Abilities.Constitution = Math.Max(0, constitution - 1);

            result.HpChanges[target.Name] = Math.Max(0, target.CurrentHitPoints - beforeHp);
            result.Events.Add($"{target.Name} is restored to life with 1 HP.");
            result.Events.Add($"{target.Name} loses 1 Constitution (now {target.Abilities.Constitution}).");
            return result;
        }

        target.RemoveStatus(CharacterStatus.Dead);
        target.AddStatus(CharacterStatus.Ashes);
        target.CurrentHitPoints = 0;
        result.Events.Add($"Raise Dead fails. {target.Name} turns to ashes.");

        return result;
    }

    private static int SystemShockSurvivalChance(int constitution)
    {
        return constitution switch
        {
            <= 1 => 30,
            <= 3 => 35,
            <= 5 => 40,
            <= 7 => 45,
            <= 9 => 50,
            <= 11 => 55,
            <= 13 => 60,
            <= 15 => 65,
            16 => 70,
            17 => 75,
            18 => 80,
            19 => 85,
            20 => 90,
            21 => 95,
            22 => 97,
            23 => 98,
            24 => 99,
            _ => 100
        };
    }
}
