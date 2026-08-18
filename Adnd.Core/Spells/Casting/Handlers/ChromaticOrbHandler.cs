using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class ChromaticOrbHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "chromatic_orb", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Chromatic Orb can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (!string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        if (target == null)
        {
            target = firstTarget?.MonsterIndex is int idx
                ? request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive)
                : request.MonsterTargets.FirstOrDefault(m => m.IsAlive);
        }

        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var profile = GetOrbProfile(request.Caster.Level);
        var rolled = 0;
        for (int i = 0; i < profile.DiceCount; i++)
            rolled += rng.Next(1, profile.DieSides + 1);

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - rolled);
        var actual = before - target.CurrentHitPoints;

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. Orb color: {profile.Color}.");
        result.Events.Add($"{target.DisplayName} takes {actual} damage (rolled {rolled} from {profile.DiceCount}d{profile.DieSides}). HP {before}->{target.CurrentHitPoints}.");
        if (!target.IsAlive)
            result.Events.Add($"{target.DisplayName} is destroyed.");

        result.HpChanges[target.DisplayName] = -actual;
        return result;
    }

    private static (string Color, int DiceCount, int DieSides) GetOrbProfile(int casterLevel)
    {
        return casterLevel switch
        {
            <= 1 => ("White", 1, 4),
            2 => ("Red", 1, 6),
            3 => ("Orange", 2, 4),
            4 => ("Yellow", 2, 6),
            5 => ("Green", 4, 4),
            _ => ("Blue", 4, 6)
        };
    }
}
