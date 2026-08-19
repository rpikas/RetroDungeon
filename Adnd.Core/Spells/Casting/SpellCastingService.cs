using Adnd.Core.Characters;
using Adnd.Core.Items;

namespace Adnd.Core.Spells.Casting;

public sealed class SpellCastingService
{
    private readonly SpellResolver _resolver;
    private readonly Dictionary<string, Spell> _spellsById;

    public SpellCastingService(SpellResolver resolver, IEnumerable<Spell> spells)
    {
        _resolver = resolver;
        _spellsById = spells.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Spell? FindFirstCastableSpell(Character caster, SpellUseContext context)
    {
        return _spellsById.Values
            .Where(spell => IsContextAllowed(spell, context))
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name)
            .FirstOrDefault(spell => CanCast(caster, spell));
    }

    public Spell? FindSpellByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _spellsById.Values.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public SpellCastResult CastFromItem(SpellCastRequest request)
    {
        if (!_spellsById.TryGetValue(request.SpellId, out var spell))
            return SpellCastResult.Failure($"Unknown spell: {request.SpellId}");

        request.Spell = spell;

        if (!IsContextAllowed(spell, request.Context))
            return SpellCastResult.Failure($"{spell.Name} cannot be cast in this context.");

        NormalizeTargetsForCombat(request, spell);

        var targetValidation = ValidateTargets(spell, request);
        if (!targetValidation.Success)
            return targetValidation;

        return _resolver.Resolve(request);
    }

    public Spell? FindSpellFromItem(Item item)
    {
        if (!ItemSpecialAbilityParser.TryGetCastedSpellName(item, out var spellName))
            spellName = InferSpellNameFromItemName(item);

        if (string.IsNullOrWhiteSpace(spellName))
            return null;

        return FindSpellByName(spellName);
    }

    private static string InferSpellNameFromItemName(Item item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name))
            return string.Empty;

        if (string.Equals(item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase))
            return "Cure Light Wounds";

        if (string.Equals(item.Name, "Potion of Extra Healing", StringComparison.OrdinalIgnoreCase))
            return "Cure Serious Wounds";

        if (string.Equals(item.Name, "Potion of Cure Poison", StringComparison.OrdinalIgnoreCase))
            return "Neutralize Poison";

        if (item.Name.StartsWith("Potion of ", StringComparison.OrdinalIgnoreCase))
            return item.Name[10..].Trim();

        if (item.Name.StartsWith("Scroll of ", StringComparison.OrdinalIgnoreCase))
            return item.Name[10..].Trim();

        return string.Empty;
    }

    public SpellCastResult Cast(SpellCastRequest request)
    {
        if (!_spellsById.TryGetValue(request.SpellId, out var spell))
            return SpellCastResult.Failure($"Unknown spell: {request.SpellId}");

        request.Spell = spell;

        if (!IsContextAllowed(spell, request.Context))
            return SpellCastResult.Failure($"{spell.Name} cannot be cast in this context.");

        NormalizeTargetsForCombat(request, spell);

        var state = request.Caster.Spellcasting.FirstOrDefault(s => s.SpellClass == spell.SpellClass);
        if (state == null)
            return SpellCastResult.Failure($"{request.Caster.Name} cannot cast {spell.Name}.");

        var levelIndex = spell.Level - 1;
        if (levelIndex < 0 || levelIndex >= state.SlotsPerDay.Count || state.SlotsPerDay[levelIndex] <= 0)
            return SpellCastResult.Failure("No spell slots for that spell level.");

        while (state.SlotsUsed.Count <= levelIndex)
            state.SlotsUsed.Add(0);

        if (state.SlotsUsed[levelIndex] >= state.SlotsPerDay[levelIndex])
            return SpellCastResult.Failure("No remaining spell slots for that spell level.");

        var isDivine = state.SpellClass is SpellClass.Cleric or SpellClass.Druid;

        if (!isDivine && !state.KnownSpellIds.Contains(spell.Id, StringComparer.OrdinalIgnoreCase))
            return SpellCastResult.Failure($"{request.Caster.Name} does not know {spell.Name}.");

        var targetValidation = ValidateTargets(spell, request);
        if (!targetValidation.Success)
            return targetValidation;

        var result = _resolver.Resolve(request);
        if (!result.Success)
            return result;

        state.SlotsUsed[levelIndex] += 1;

        result.SlotConsumed = true;
        return result;
    }

    private static bool CanCast(Character caster, Spell spell)
    {
        var state = caster.Spellcasting.FirstOrDefault(s => s.SpellClass == spell.SpellClass);
        if (state == null)
            return false;

        var levelIndex = spell.Level - 1;
        if (levelIndex < 0 || levelIndex >= state.SlotsPerDay.Count)
            return false;

        if (state.SlotsPerDay[levelIndex] <= 0)
            return false;

        var used = levelIndex < state.SlotsUsed.Count ? state.SlotsUsed[levelIndex] : 0;
        if (used >= state.SlotsPerDay[levelIndex])
            return false;

        var isDivine = state.SpellClass is SpellClass.Cleric or SpellClass.Druid;
        if (isDivine)
            return true;

        return state.KnownSpellIds.Contains(spell.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsContextAllowed(Spell spell, SpellUseContext context)
    {
        return spell.CastContext switch
        {
            SpellCastContext.Both => true,
            SpellCastContext.Combat => context == SpellUseContext.Combat,
            SpellCastContext.Exploration => context == SpellUseContext.Exploration,
            _ => false
        };
    }

    private static SpellCastResult ValidateTargets(Spell spell, SpellCastRequest request)
    {
        if (spell.Targeting == SpellTargeting.Single && request.Targets.Count != 1)
            return SpellCastResult.Failure("This spell requires exactly one target.");

        if (spell.Targeting == SpellTargeting.Multiple && request.Targets.Count == 0)
            return SpellCastResult.Failure("This spell requires at least one target.");

        foreach (var t in request.Targets)
        {
            switch (spell.RangeType)
            {
                case SpellRangeType.Self:
                    if (t.Type != SpellCastTargetType.Ally || !string.Equals(t.CharacterName, request.Caster.Name, StringComparison.OrdinalIgnoreCase))
                        return SpellCastResult.Failure("This spell can only target the caster.");
                    break;

                case SpellRangeType.Ally:
                    if (t.Type != SpellCastTargetType.Ally)
                        return SpellCastResult.Failure("This spell must target allies.");
                    if (!request.PartyTargets.Any(p => string.Equals(p.Name, t.CharacterName, StringComparison.OrdinalIgnoreCase)))
                        return SpellCastResult.Failure("Invalid ally target.");
                    break;

                case SpellRangeType.Enemy:
                    if (t.Type != SpellCastTargetType.Enemy)
                        return SpellCastResult.Failure("This spell must target enemies.");
                    if (!IsValidEnemyTarget(t, request))
                        return SpellCastResult.Failure("Invalid enemy target.");
                    break;
            }
        }

        return new SpellCastResult { Success = true };
    }

    private static void NormalizeTargetsForCombat(SpellCastRequest request, Spell spell)
    {
        if (request.Context != SpellUseContext.Combat)
            return;

        if (spell.RangeType != SpellRangeType.Enemy)
            return;

        if (spell.Targeting == SpellTargeting.Multiple)
        {
            var alive = request.MonsterTargets
                .Where(m => m.IsAlive)
                .ToList();

            if (alive.Count == 0)
                return;

            var hasAnyValidEnemy = request.Targets.Any(t => t.Type == SpellCastTargetType.Enemy
                                                             && IsValidEnemyTarget(t, request));

            var hasValidGroupTarget = request.Targets.Any(t => t.Type == SpellCastTargetType.Enemy
                                                                && !string.IsNullOrWhiteSpace(t.TargetGroupId)
                                                                && alive.Any(m => string.Equals(m.GroupId, t.TargetGroupId, StringComparison.OrdinalIgnoreCase)));

            // Respect explicit group targeting for single-group AoE spells (e.g. Sleep/Fireball in multi-group encounters).
            if (hasValidGroupTarget)
                return;

            if (!hasAnyValidEnemy)
                request.Targets.RemoveAll(t => t.Type == SpellCastTargetType.Enemy);

            foreach (var monster in alive)
            {
                if (!request.Targets.Any(t => t.Type == SpellCastTargetType.Enemy && t.MonsterIndex == monster.Index))
                    request.Targets.Add(SpellCastTarget.Enemy(monster.Index));
            }

            return;
        }

        if (spell.Targeting != SpellTargeting.Single)
            return;

        var hasValidEnemy = request.Targets.Any(t => t.Type == SpellCastTargetType.Enemy
                                                     && IsValidEnemyTarget(t, request));

        if (hasValidEnemy)
            return;

        var fallback = request.MonsterTargets.FirstOrDefault(m => m.IsAlive);
        if (fallback == null)
            return;

        request.Targets.RemoveAll(t => t.Type == SpellCastTargetType.Enemy);
        request.Targets.Add(SpellCastTarget.Enemy(fallback.Index));
    }

    private static bool IsValidEnemyTarget(SpellCastTarget target, SpellCastRequest request)
    {
        if (target.MonsterIndex is int idx)
            return request.MonsterTargets.Any(m => m.Index == idx && m.IsAlive);

        if (!string.IsNullOrWhiteSpace(target.TargetGroupId))
            return request.MonsterTargets.Any(m => m.IsAlive && string.Equals(m.GroupId, target.TargetGroupId, StringComparison.OrdinalIgnoreCase));

        return false;
    }
}
