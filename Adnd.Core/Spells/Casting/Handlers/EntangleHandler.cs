using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class EntangleHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "entangle", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Entangle can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        var targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "default";

        var targets = session != null
            ? session.GetAliveMonstersByGroup(targetGroupId).ToList()
            : request.MonsterTargets.Where(m => m.IsAlive).ToList();

        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid targets for Entangle in the selected group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        var entangledCount = 0;
        const int rounds = 5;

        foreach (var target in targets)
        {
            var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
            var saveRoll = rng.Next(1, 21);

            if (saveRoll >= saveTarget)
            {
                result.Events.Add($"{target.DisplayName} resists entangle (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            target.SetStatus(MonsterStatus.Entangled, rounds);
            entangledCount++;
            result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is entangled for {rounds} round(s)!");
        }

        result.Events.Add($"Entangle affected {entangledCount} target(s).");
        return result;
    }
}
