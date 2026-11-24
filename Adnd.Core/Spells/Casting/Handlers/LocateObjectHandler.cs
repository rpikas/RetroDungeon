namespace Adnd.Core.Spells.Casting.Handlers;

/// <summary>Locate Object (Magic-User 2, Cleric 3): works like Wizardry's Kandi; the game window asks whose belongings to find and supplies the report.</summary>
public sealed class LocateObjectHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "locate_object", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "locate_object_magic_user", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var name = request.Spell?.Name ?? "Locate Object";
        if (request.Context != SpellUseContext.Exploration)
            return SpellCastResult.Failure($"{name} can only be cast outside combat.");

        if (string.IsNullOrWhiteSpace(request.LocateReport))
            return SpellCastResult.Failure($"{name} only works in the dungeon, and needs a character left there.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {name}.");
        result.Events.Add(request.LocateReport);
        return result;
    }
}
