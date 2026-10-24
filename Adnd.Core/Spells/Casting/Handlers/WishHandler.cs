using Adnd.Core.Characters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class WishHandler : ISpellEffectHandler
{
    private const int MaxAbilityScore = 25;
    private const int AgingYears = 3;

    private readonly IWishPrompter? _prompter;

    public WishHandler(IWishPrompter? prompter)
    {
        _prompter = prompter;
    }

    public bool CanHandle(string spellId) => string.Equals(spellId, "wish", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (_prompter == null)
            return SpellCastResult.Failure("Wish cannot be cast here.");

        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        var kind = _prompter.ChooseKind();
        if (kind == null)
            return SpellCastResult.Failure("The wish is left unspoken.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} speaks a {spell.Name}.");

        var outcome = ResolveWish(kind.Value, result, target);
        if (outcome.Success)
        {
            request.Caster.AdvanceAgeByDays(AgingYears * 365);
            outcome.Events.Add($"{request.Caster.Name} ages {AgingYears} years from the strain of the wish (now {request.Caster.Age}).");
            if (request.Caster.DiedOfOldAge)
                outcome.Events.Add($"{request.Caster.Name} has reached the maximum age and dies of old age. Death is final.");
        }

        return outcome;
    }

    private SpellCastResult ResolveWish(WishKind kind, SpellCastResult result, Character target)
    {
        switch (kind)
        {
            case WishKind.HealOrCure:
                return ResolveHeal(result, target);

            case WishKind.Item:
            {
                var item = _prompter.ChooseItem();
                if (item == null)
                    return SpellCastResult.Failure("The wish is left unspoken.");

                target.Inventory.Add(item);
                result.Events.Add($"{item.Name} appears and is added to {target.Name}'s inventory.");
                return result;
            }

            case WishKind.Spell:
            {
                var scroll = _prompter.ChooseSpellScroll();
                if (scroll == null)
                    return SpellCastResult.Failure("The wish is left unspoken.");

                target.Inventory.Add(scroll);
                result.Events.Add($"{scroll.Name} appears and is added to {target.Name}'s inventory.");
                return result;
            }

            default:
                return ResolveImprovement(result, target);
        }
    }

    private static SpellCastResult ResolveHeal(SpellCastResult result, Character target)
    {
        if (target.HasStatus(CharacterStatus.Ashes) || target.HasStatus(CharacterStatus.Lost))
            return SpellCastResult.Failure($"The wish cannot restore {target.Name} in current condition.");

        var beforeHp = target.CurrentHitPoints;
        var wasDead = target.HasStatus(CharacterStatus.Dead) || target.CurrentHitPoints <= 0;
        if (wasDead)
            target.RemoveStatus(CharacterStatus.Dead);

        target.CurrentHitPoints = target.MaxHitPoints;
        target.RemoveStatus(CharacterStatus.Blind);
        target.CureDiseaseAndRestoreConstitution();
        target.RemoveStatus(CharacterStatus.Feeblemind);
        target.RemoveStatus(CharacterStatus.Poisoned);
        target.RemoveStatus(CharacterStatus.Slowed);
        target.RemoveStatus(CharacterStatus.Petrified);
        target.RemoveStatus(CharacterStatus.Paralyzed);

        var healed = Math.Max(0, target.CurrentHitPoints - beforeHp);
        result.Events.Add(wasDead
            ? $"{target.Name} is restored to life with {target.CurrentHitPoints} HP."
            : $"{target.Name} is fully healed for {healed} HP.");
        result.Events.Add($"{target.Name} is cured of blindness, disease (with Constitution restored), feeblemind, poison, slow, paralysis and petrification.");
        result.HpChanges[target.Name] = healed;
        return result;
    }

    private SpellCastResult ResolveImprovement(SpellCastResult result, Character target)
    {
        var ability = _prompter!.ChooseAbility();
        if (string.IsNullOrWhiteSpace(ability))
            return SpellCastResult.Failure("The wish is left unspoken.");

        var scores = target.Abilities;
        int current;
        Action<int> set;
        switch (ability.Trim().ToLowerInvariant())
        {
            case "strength": current = scores.Strength; set = v => scores.Strength = v; break;
            case "intelligence": current = scores.Intelligence; set = v => scores.Intelligence = v; break;
            case "wisdom": current = scores.Wisdom; set = v => scores.Wisdom = v; break;
            case "dexterity": current = scores.Dexterity; set = v => scores.Dexterity = v; break;
            case "constitution": current = scores.Constitution; set = v => scores.Constitution = v; break;
            case "charisma": current = scores.Charisma; set = v => scores.Charisma = v; break;
            default:
                return SpellCastResult.Failure($"Unknown ability: {ability}.");
        }

        if (current >= MaxAbilityScore)
            return SpellCastResult.Failure($"{target.Name}'s {ability} cannot be improved further.");

        set(current + 1);
        result.Events.Add($"{target.Name}'s {ability} permanently increases from {current} to {current + 1}.");
        return result;
    }
}
