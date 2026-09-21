using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class PhantasmalKillerHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "phantasmal_killer", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Phantasmal Killer can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Phantasmal Killer requires combat session context.");

        var rng = request.Rng ?? Random.Shared;

        string targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g))
            ?? session.GetDistinctGroupIds().FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0)
            ?? "default";

        var candidates = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (candidates.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var target = candidates[rng.Next(candidates.Count)];

        MonsterIntelligenceResolver.RegisterMonsterIntelligence(target.Template.Name, target.Template.Intelligence);
        var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(target.Template.Name);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"Target: {target.DisplayName} (Group {targetGroupId}).");

        if (intelligence <= 1)
        {
            result.Events.Add($"{target.DisplayName} has non-intelligence and is unaffected.");
            return result;
        }

        var roll = rng.Next(1, 7) + rng.Next(1, 7) + rng.Next(1, 7);
        result.Events.Add($"Intelligence check: rolled 3d6 = {roll} vs INT {intelligence}.");

        if (roll > intelligence)
        {
            target.CurrentHitPoints = 0;
            result.Events.Add($"{target.DisplayName} dies from overwhelming fear!");
        }
        else
        {
            result.Events.Add($"{target.DisplayName} is unaffected.");
        }

        return result;
    }
}