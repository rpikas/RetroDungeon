using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CureDiseaseHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "cure_disease", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "cure_disease_druid", StringComparison.OrdinalIgnoreCase);

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
            || target.HasStatus(CharacterStatus.Petrified)
            || target.CurrentHitPoints <= 0)
        {
            var canTreatMummyRotCorpse = (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.CurrentHitPoints <= 0)
                                         && target.MummyRotRaiseWindowRoundsRemaining > 0
                                         && target.MummyRotRaiseRequiresCureDisease;
            var canTreatPoisonSporesCorpse = (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.CurrentHitPoints <= 0)
                                             && target.PoisonSporesRaiseWindowDaysRemaining > 0
                                             && target.PoisonSporesRaiseRequiresCureDisease;
            if (!canTreatMummyRotCorpse && !canTreatPoisonSporesCorpse)
                return SpellCastResult.Failure($"{spell.Name} cannot affect {target.Name} in current condition.");
        }

        var wasDiseased = target.HasStatus(CharacterStatus.Diseased);
        var clericLevel = request.EffectiveCasterLevel
                          ?? request.Caster.GetClassLevel(CharacterClass.Cleric);
        var curedLycanthropy = target.TryCureLycanthropyFromCleric(clericLevel);
        target.CureDiseaseAndRestoreConstitution();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        if (request.CombatSession?.TryCureGreenSlime(target.Name) == true)
            result.Events.Add($"The green slime on {target.Name} withers and dies.");
        result.Events.Add(wasDiseased
            ? $"{target.Name} is no longer diseased and Constitution is restored."
            : $"{target.Name} is not diseased.");

        if (target.MummyRotRaiseWindowRoundsRemaining > 0 && target.MummyRotRaiseRequiresCureDisease)
        {
            target.MummyRotRaiseRequiresCureDisease = false;
            result.Events.Add($"{target.Name}'s mummy rot is treated. Raise Dead must still be used before the 6-turn window expires.");
        }

        if (target.PoisonSporesRaiseWindowDaysRemaining > 0 && target.PoisonSporesRaiseRequiresCureDisease)
        {
            target.PoisonSporesRaiseRequiresCureDisease = false;
            result.Events.Add($"{target.Name}'s poison spores condition is treated. Resurrection/Raise Dead must still be used within 24 hours.");
        }

        if (curedLycanthropy)
            result.Events.Add($"{target.Name}'s lycanthropy infection is cured before becoming permanent (cleric level {clericLevel}).");

        return result;
    }
}
