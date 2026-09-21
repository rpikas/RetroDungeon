using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class MazeHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
    {
        var id = spellId?.Trim() ?? string.Empty;

        return string.Equals(id, "maze_magic_user", StringComparison.OrdinalIgnoreCase)
               || string.Equals(id, "maze_illusionist", StringComparison.OrdinalIgnoreCase)
               || string.Equals(id, "maze_illustionist", StringComparison.OrdinalIgnoreCase)
               || string.Equals(id, "maze", StringComparison.OrdinalIgnoreCase);
    }

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Maze can only be cast in combat.");

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

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        if (target.Template.Name.Contains("minotaur", StringComparison.OrdinalIgnoreCase))
        {
            result.Events.Add($"{target.DisplayName} is a Minotaur and is unaffected by Maze.");
            return result;
        }

        MonsterIntelligenceResolver.RegisterMonsterIntelligence(target.Template.Name, target.Template.Intelligence);
        var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(target.Template.Name);
        var rounds = RollMazeDurationRounds(intelligence, rng);

        target.SetStatus(MonsterStatus.Mazed, rounds);

        var unitText = intelligence <= 5 ? "turn(s)" : "round(s)";
        var displayDuration = intelligence <= 5 ? rounds / 10 : rounds;
        result.Events.Add($"{target.DisplayName} (INT {intelligence}) is trapped in the maze for {displayDuration} {unitText}.");
        return result;
    }

    private static int RollMazeDurationRounds(int intelligence, Random rng)
    {
        if (intelligence < 3)
        {
            var turns = rng.Next(2, 9); // 2-8 turns
            return turns * 10;
        }

        if (intelligence <= 5)
        {
            var turns = rng.Next(1, 5); // 1-4 turns
            return turns * 10;
        }

        if (intelligence <= 8)
            return rng.Next(5, 21); // 5-20 rounds

        if (intelligence <= 11)
            return rng.Next(4, 17); // 4-16 rounds

        if (intelligence <= 14)
            return rng.Next(3, 13); // 3-12 rounds

        if (intelligence <= 17)
            return rng.Next(2, 9); // 2-8 rounds

        return rng.Next(1, 5); // 1-4 rounds
    }
}