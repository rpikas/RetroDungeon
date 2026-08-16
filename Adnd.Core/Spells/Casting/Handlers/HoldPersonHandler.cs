using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HoldPersonHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "hold_person", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Hold Person can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        string? targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        var hasExplicitGroupTarget = !string.IsNullOrWhiteSpace(targetGroupId);

        var humanoidCandidates = session != null
            ? session.GetAliveMonstersByGroup(targetGroupId ?? "default")
                .Where(m => IsHumanoid(m.InstanceMonsterType))
                .ToList()
            : new List<MonsterInstance>();

        // If a specific group was selected, do not fall back to other groups.
        if (humanoidCandidates.Count == 0 && hasExplicitGroupTarget)
            return SpellCastResult.Failure("No valid humanoid targets in the selected group for Hold Person.");

        if (humanoidCandidates.Count == 0)
        {
            humanoidCandidates = request.MonsterTargets
                .Where(m => m.IsAlive && IsHumanoid(m.InstanceMonsterType))
                .ToList();
        }

        if (humanoidCandidates.Count == 0)
            return SpellCastResult.Failure("No valid humanoid targets for Hold Person.");

        var targets = humanoidCandidates
            .OrderBy(_ => rng.Next())
            .Take(3)
            .ToList();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");
        result.Events.Add($"Hold Person targets up to 3 foes in one group ({targets.Count} selected).");

        var heldCount = 0;

        foreach (var target in targets)
        {
            var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
            var saveRoll = rng.Next(1, 21);

            if (saveRoll >= saveTarget)
            {
                result.Events.Add($"{target.DisplayName} resists the spell (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            var rounds = rng.Next(4, 4 + request.Caster.Level); // original adnd rules 4 rounds + 1/level
            target.SetStatus(MonsterStatus.Paralyzed, rounds);
            heldCount++;
            result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is paralyzed for {rounds} round(s)!");
        }

        result.Events.Add($"Hold Person held {heldCount} target(s).");

        return result;
    }

    private static bool IsHumanoid(MonsterType monsterType)
    {
     //   var humanoids = new[] { "human", "elf", "dwarf", "halfling", "gnome", "orc", "goblin", "hobgoblin", "kobold", "bugbear", "gnoll" };
       // var nameLower = monsterName.ToLowerInvariant();
       //return humanoids.Any(h => nameLower.Contains(h));
       return monsterType == MonsterType.Humanoid;
    }


}
