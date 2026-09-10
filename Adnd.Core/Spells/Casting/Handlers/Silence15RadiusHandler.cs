using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class Silence15RadiusHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "silence_15_radius", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Silence 15' Radius can only be cast in combat.");

        var targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "default";

        var targets = request.CombatSession.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid targets for Silence 15' Radius in the selected group.");

        var casterLevel = Math.Max(1, request.Caster.GetClassLevel(CharacterClass.Cleric));
        var rounds = casterLevel * 2;

        foreach (var target in targets)
            target.SetStatus(MonsterStatus.Silenced, rounds);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");
        result.Events.Add($"Group {targetGroupId} is silenced for {rounds} round(s) and cannot cast spells.");
        return result;
    }
}
