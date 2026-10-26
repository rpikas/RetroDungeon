using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

/// <summary>
/// Light: the party sees one step further for 6 turns + 1 turn per caster level. The handler only works out
/// how long it lasts; the caller stores it on the party (<see cref="SpellCastResult.LightSteps"/>).
/// </summary>
public sealed class LightHandler : ISpellEffectHandler
{
    /// <summary>How many steps in the dungeon make up one turn.</summary>
    public const int StepsPerTurn = 10;

    public bool CanHandle(string spellId)
        => string.Equals(spellId, "light", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "light_illusionist", StringComparison.OrdinalIgnoreCase)
           || IsContinual(spellId);

    private static bool IsContinual(string spellId)
        => string.Equals(spellId, "continual_light", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "continual_light_illusionist", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Exploration)
            return SpellCastResult.Failure($"{spell.Name} can only be cast outside combat.");

        var caster = request.Caster;
        if (IsContinual(spell.Id))
        {
            var permanent = new SpellCastResult { Success = true, ContinualLight = true };
            permanent.Events.Add($"{caster.Name} casts {spell.Name}. The party sees two steps further until the light is dispelled or darkness swallows it.");
            return permanent;
        }
        var classLevel = spell.SpellClass switch
        {
            SpellClass.Cleric when caster.Classes.Contains(CharacterClass.Cleric) => caster.GetClassLevel(CharacterClass.Cleric),
            SpellClass.Illusionist when caster.Classes.Contains(CharacterClass.Illusionist) => caster.GetClassLevel(CharacterClass.Illusionist),
            _ => caster.Level
        };

        var casterLevel = Math.Max(1, request.EffectiveCasterLevel ?? classLevel);
        var turns = 6 + casterLevel;
        var steps = turns * StepsPerTurn;

        var result = new SpellCastResult { Success = true, LightSteps = steps };
        result.Events.Add($"{caster.Name} casts {spell.Name}. The party sees one step further for {turns} turns ({steps} steps).");
        return result;
    }
}
