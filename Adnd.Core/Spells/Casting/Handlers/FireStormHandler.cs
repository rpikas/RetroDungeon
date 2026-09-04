using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FireStormHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "fire_storm", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Fire Storm can only be cast in combat.");

        var targets = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy targets for Fire Storm.");

        var rng = request.Rng ?? Random.Shared;
        var casterLevel = Math.Max(1, request.Caster.Level);
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        foreach (var monster in targets)
        {
            var rolledDamage = rng.Next(1, 9) + rng.Next(1, 9) + casterLevel; // 2d8 + caster level
            var outcome = SpellDamageSaveHelper.ApplyToMonster(monster, rolledDamage, rng, spell.Name);

            result.Events.Add(
                $"{monster.DisplayName} save vs spell rolled {outcome.SaveRoll} vs {outcome.SaveTarget} => {(outcome.Saved ? "SUCCESS" : "FAIL")}. " +
                $"Damage {rolledDamage}{(outcome.Saved ? $" halved to {outcome.AppliedDamage}" : string.Empty)}. HP {outcome.BeforeHp}->{outcome.AfterHp}.");

            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} is consumed by divine fire!");
        }

        result.Events.Add($"Fire Storm affects all enemy groups ({targets.Count} target(s)).");
        return result;
    }
}
