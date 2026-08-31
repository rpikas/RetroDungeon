using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FaerieFireHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "faerie_fire", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Faerie Fire can only be cast in combat.");

        var session = request.CombatSession;
        var targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "default";

        var targets = session != null
            ? session.GetAliveMonstersByGroup(targetGroupId).ToList()
            : request.MonsterTargets.Where(m => m.IsAlive).ToList();

        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid targets for Faerie Fire in the selected group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        var affected = 0;
        foreach (var target in targets)
        {
            if (target.HasStatus(MonsterStatus.FaerieFire))
                continue;

            var acPenalty = IsLarge(target.Template.Size) ? 3 : 2;
            target.AdjustArmorClass(acPenalty);
            target.SetStatus(MonsterStatus.FaerieFire, int.MaxValue);
            affected++;
            result.Events.Add($"{target.DisplayName} is outlined by faerie fire: AC worsens by {acPenalty}.");
        }

        result.Events.Add($"Faerie Fire affected {affected} target(s).");
        return result;
    }

    private static bool IsLarge(MonsterSize size)
        => size == MonsterSize.Large;
}
