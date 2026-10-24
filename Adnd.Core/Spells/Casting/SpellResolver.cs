namespace Adnd.Core.Spells.Casting;

using Adnd.Core.Combat.Sessions;

public sealed class SpellResolver
{
    private readonly List<ISpellEffectHandler> _handlers;

    public SpellResolver(IEnumerable<ISpellEffectHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var handler = _handlers.FirstOrDefault(h => h.CanHandle(request.SpellId));
        if (handler == null)
            return SpellCastResult.Failure($"Spell effect not implemented: {request.SpellId}");

        var previousLevel = Handlers.SpellDamageSaveHelper.CurrentSpellLevel;
        Handlers.SpellDamageSaveHelper.CurrentSpellLevel = request.Spell?.Level;

        var session = request.CombatSession;
        var spellText = $"{request.Spell?.Name} {request.SpellId}";
        var burnsRegeneration = session != null
            && (spellText.Contains("fire", StringComparison.OrdinalIgnoreCase)
                || spellText.Contains("flame", StringComparison.OrdinalIgnoreCase)
                || spellText.Contains("acid", StringComparison.OrdinalIgnoreCase)
                || spellText.Contains("meteor", StringComparison.OrdinalIgnoreCase));
        var hpBefore = burnsRegeneration
            ? session!.Monsters.ToDictionary(m => m, m => m.CurrentHitPoints, ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>)
            : null;

        try
        {
            return handler.Resolve(request);
        }
        finally
        {
            Handlers.SpellDamageSaveHelper.CurrentSpellLevel = previousLevel;

            // Fire and acid damage to regenerating monsters cannot be regenerated.
            if (hpBefore != null)
            {
                foreach (var (monster, before) in hpBefore)
                {
                    var lost = before - monster.CurrentHitPoints;
                    if (lost > 0 && monster.Template.SpecialDefenses.Concat(monster.Template.SpecialAbilities.Select(a => a))
                            .Any(d => d?.Name != null && d.Name.TrimStart().StartsWith("Regeneration", StringComparison.OrdinalIgnoreCase)))
                        session!.AddMonsterWoundingUnhealableDamage(monster, lost);
                }
            }
        }
    }
}
