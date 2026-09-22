using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class PrayerHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "prayer", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat || request.CombatSession == null)
            return SpellCastResult.Failure("Prayer can only be cast in combat.");

        var casterLevel = Math.Max(1, request.Caster.GetClassLevel(CharacterClass.Cleric));
        request.CombatSession.StartPrayer(request.Caster.Name, casterLevel);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");
        result.Events.Add($"Prayer is active for {casterLevel} round(s): allies gain +1 to attacks/saves and enemies suffer -1 to attacks/saves.");
        return result;
    }
}
