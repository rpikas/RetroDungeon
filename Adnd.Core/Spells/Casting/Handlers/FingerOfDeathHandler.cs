using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FingerOfDeathHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "finger_of_death", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Finger of Death can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (firstTarget?.MonsterIndex is int idx)
        {
            target = request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);
        }

        if (target == null && !string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        target ??= request.MonsterTargets.FirstOrDefault(m => m.IsAlive);

        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = rng.Next(1, 21);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} survives (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = 0;
        result.HpChanges[target.DisplayName] = -before;
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and dies instantly. HP {before}->0.");

        return result;
    }
}
