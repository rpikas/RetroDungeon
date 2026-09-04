using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FeeblemindHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "feeblemind", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Feeblemind can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Feeblemind requires combat session context.");

        var rng = request.Rng ?? Random.Shared;

        string? targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        var candidates = !string.IsNullOrWhiteSpace(targetGroupId)
            ? session.GetAliveMonstersByGroup(targetGroupId!).ToList()
            : session.AliveMonsters.ToList();

        if (candidates.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var target = candidates[rng.Next(candidates.Count)];
        var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = rng.Next(1, 21);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} resists feeblemind (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        target.SetStatus(MonsterStatus.Feebleminded, int.MaxValue);
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is permanently feebleminded.");
        return result;
    }
}
