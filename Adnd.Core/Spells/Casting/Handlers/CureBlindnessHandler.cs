using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CureBlindnessHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "cure_blindness", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "cause_blindness", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var isCause = string.Equals(spell.Id, "cause_blindness", StringComparison.OrdinalIgnoreCase);
        return isCause
            ? ResolveCauseBlindness(request, spell)
            : ResolveCureBlindness(request, spell);
    }

    private static SpellCastResult ResolveCureBlindness(SpellCastRequest request, Spell spell)
    {
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

        var wasBlind = target.HasStatus(CharacterStatus.Blind);
        target.RemoveStatus(CharacterStatus.Blind);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add(wasBlind
            ? $"{target.Name}'s sight is restored."
            : $"{target.Name} is not blind.");
        return result;
    }

    private static SpellCastResult ResolveCauseBlindness(SpellCastRequest request, Spell spell)
    {
        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;
        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);

        if (firstTarget?.MonsterIndex is int idx)
            target = request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);

        if (target == null && !string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        target ??= request.MonsterTargets.FirstOrDefault(m => m.IsAlive);
        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
        var saveRoll = rng.Next(1, 21);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} resists cause blindness (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
        {
            result.Events.Add($"{target.DisplayName} negates cause blindness with magic resistance.");
            return result;
        }

        target.SetStatus(MonsterStatus.Blinded, int.MaxValue);
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is blinded.");
        return result;
    }
}
