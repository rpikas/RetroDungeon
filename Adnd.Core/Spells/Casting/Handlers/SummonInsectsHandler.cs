using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class SummonInsectsHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "summon_insects", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Summon Insects can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Summon Insects requires combat session context.");

        var targets = session.AliveMonsters.ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid enemy targets for Summon Insects.");

        var rounds = Math.Max(1, request.Caster.Level);
        foreach (var monster in targets)
            monster.SetStatus(MonsterStatus.SummonInsects, rounds);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"Insects swarm all enemy groups for {rounds} round(s), dealing 2 damage each round (no save).");
        return result;
    }
}
