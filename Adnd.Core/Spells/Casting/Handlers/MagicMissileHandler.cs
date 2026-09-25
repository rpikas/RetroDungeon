namespace Adnd.Core.Spells.Casting.Handlers;
using Adnd.Core.Diagnostics;

public sealed class MagicMissileHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "magic_missile", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "magic_missile_illusionist", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        var selectedGroupId = firstTarget?.TargetGroupId;

        List<Combat.Sessions.MonsterInstance> candidates;
        if (session != null && !string.IsNullOrWhiteSpace(selectedGroupId))
        {
            candidates = session.GetAliveMonstersByGroup(selectedGroupId).ToList();
        }
        else if (session != null && firstTarget?.MonsterIndex is int selectedIndex)
        {
            var selectedMonster = session.Monsters.FirstOrDefault(m => m.Index == selectedIndex && m.IsAlive);
            candidates = selectedMonster != null
                ? session.GetAliveMonstersByGroup(selectedMonster.GroupId).ToList()
                : request.MonsterTargets.Where(m => m.IsAlive).ToList();
        }
        else
        {
            candidates = request.MonsterTargets.Where(m => m.IsAlive).ToList();
        }

        if (candidates.Count == 0)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var missileCount = Math.Min(5, Math.Max(1, ((request.Caster.Level - 1) / 2) + 1));
        var orderedTargets = candidates.OrderBy(_ => rng.Next()).ToList();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription} {missileCount} missile(s).");

        for (int i = 0; i < missileCount; i++)
        {
            var target = orderedTargets.FirstOrDefault(m => m.IsAlive);
            if (target == null)
                break;

            if (i < orderedTargets.Count && orderedTargets[i].IsAlive)
                target = orderedTargets[i];

            var damageRoll = rng.Next(1, 5);
            var damage = damageRoll + 1; // 1d4+1

            var outcome = SpellDamageSaveHelper.ApplyToMonster(target, damage, rng, spell.Name);

            result.Events.Add($"Missile {i + 1}: {SpellDamageSaveHelper.FormatSaveAndDamageLine(target.DisplayName, damage, outcome)}");
            if (TryRevealWillOWispLairTreasureOnLowHp(target))
                result.Events.Add($"{target.DisplayName} falters at {target.CurrentHitPoints} HP, reveals its lair, and gives over its treasure.");

            if (!target.IsAlive)
                result.Events.Add($"{target.DisplayName} is destroyed.");

            if (result.HpChanges.ContainsKey(target.DisplayName))
                result.HpChanges[target.DisplayName] -= outcome.ActualDamage;
            else
                result.HpChanges[target.DisplayName] = -outcome.ActualDamage;
        }

        return result;
    }

    private static bool TryRevealWillOWispLairTreasureOnLowHp(Combat.Sessions.MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var normalized = (target.Template.Name ?? string.Empty)
            .ToLowerInvariant()
            .Replace("'", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        if (!normalized.Contains("willowisp", StringComparison.Ordinal))
            return false;

        if (!target.IsAlive || target.CurrentHitPoints > 5 || target.HasRevealedLairTreasure)
            return false;

        target.HasRevealedLairTreasure = true;
        target.IsInLair = true;
        return true;
    }
}
