using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class WallOfStoneHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
    {
        return string.Equals(spellId, "wall_of_stone", StringComparison.OrdinalIgnoreCase);
    }

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (request.Context != SpellUseContext.Combat || session == null)
            return SpellCastResult.Failure("Wall of Stone can only be cast in combat.");

        var rng = request.Rng ?? Random.Shared;
        var monsters = session.Monsters.Where(m => m.IsAlive).ToList();
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. A massive wall of stone rises between the party and its foes!");

        var ordinary = 0;
        foreach (var monster in monsters)
        {
            if (WallOfStoneRules.IsBurrower(monster))
            {
                var rounds = rng.Next(1, 3);
                monster.SetStatus(MonsterStatus.WalledOff, rounds);
                result.Events.Add($"{monster.DisplayName} can burrow through rock and is only delayed for {rounds} round(s).");
            }
            else if (WallOfStoneRules.IsBreacher(monster))
            {
                monster.SetStatus(MonsterStatus.WalledOff, 999);
                result.Events.Add($"{monster.DisplayName} is stopped for now, but has the magic to breach the wall.");
            }
            else
            {
                monster.SetStatus(MonsterStatus.WalledOff, 999);
                ordinary++;
            }
        }

        if (ordinary == monsters.Count && monsters.Count > 0)
        {
            session.Outcome = CombatOutcome.Escaped;
            result.Events.Add("The enemies cannot pass the wall. The battle ends with no experience or treasure.");
        }
        else if (ordinary > 0)
        {
            result.Events.Add($"{ordinary} enemy(ies) are blocked behind the wall; the rest of the battle continues.");
        }

        return result;
    }
}
