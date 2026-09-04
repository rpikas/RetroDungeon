using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class WallOfThornsHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "wall_of_thorns", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Wall of Thorns can only be cast in combat.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy targets for Wall of Thorns.");

        var rng = request.Rng ?? Random.Shared;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targets)
        {
            var rolledDamage = 8 + monster.ArmorClass;
            if (rolledDamage < 1)
                rolledDamage = 1;

            var outcome = SpellDamageSaveHelper.ApplyToMonster(monster, rolledDamage, rng, spell.Name);
            result.Events.Add(
                $"{monster.DisplayName} AC {monster.ArmorClass}: damage {rolledDamage}. Save vs spell {outcome.SaveRoll} vs {outcome.SaveTarget} => {(outcome.Saved ? "SUCCESS" : "FAIL")}. " +
                $"Applied {outcome.AppliedDamage}. HP {outcome.BeforeHp}->{outcome.AfterHp}.");

            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} is torn apart by the wall of thorns!");
        }

        result.Events.Add($"Wall of Thorns affects all enemy groups ({targets.Count} target(s)).");
        return result;
    }
}
