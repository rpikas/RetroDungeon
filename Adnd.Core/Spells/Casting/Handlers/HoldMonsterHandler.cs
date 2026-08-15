using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HoldMonsterHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "hold_monster", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Hold Monster can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        // Determine target - pick a random humanoid from the specified group
        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault();
        if (firstTarget?.TargetGroupId != null && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId)
                .Where(m => IsUndead(m.InstanceMonsterType))
                .ToList();

            if (groupTargets.Count > 0)
            {
                target = groupTargets[rng.Next(groupTargets.Count)];
            }
        }

        // Fallback to old behavior
        if (target == null)
        {
            var monsterTargets = request.MonsterTargets
                .Where(m => m.IsAlive && !IsUndead(m.InstanceMonsterType))
                .Where(m => m.IsAlive )
                .ToList();

            if (monsterTargets.Count > 0)
            {
                target = monsterTargets[rng.Next(monsterTargets.Count)];
            }
        }

        if (target == null)
            return SpellCastResult.Failure("No valid targets for Hold Monster.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = rng.Next(1, 21);

        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} resists the spell (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        var rounds = rng.Next(2, 7); // 2-6 rounds
        target.SetStatus(MonsterStatus.Paralyzed, rounds);
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is paralyzed for {rounds} round(s)!");

        return result;
    }
    
    private static bool IsUndead(MonsterType monsterType)
    {
       // var humanoids = new[] { MonsterType.Human, MonsterType.Elf, MonsterType.Dwarf, MonsterType.Halfling, MonsterType.Gnome, MonsterType.Orc, MonsterType.Goblin, MonsterType.Hobgoblin, MonsterType.Kobold, MonsterType.Bugbear, MonsterType.Gnoll };
      //  return humanoids.Contains(monsterName);
      return monsterType == MonsterType.Undead;
    }
    
}
