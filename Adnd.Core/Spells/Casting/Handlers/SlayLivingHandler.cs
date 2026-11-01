using System.Text.RegularExpressions;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class SlayLivingHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "slay_living", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Slay Living can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (firstTarget?.MonsterIndex is int idx)
            target = request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);

        if (target == null && !string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        target ??= request.MonsterTargets.FirstOrDefault(m => m.IsAlive);

        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
        var saveRoll = rng.Next(1, 21);
        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} survives (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        var deathResistance = GetDeathResistancePercent(target);
        if (deathResistance > 0)
        {
            var resistRoll = rng.Next(1, 101);
            if (resistRoll <= deathResistance)
            {
                result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) but resists death ({resistRoll} on 1d100 vs {deathResistance}%).");
                return result;
            }
        }

        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
        {
            result.Events.Add($"{target.DisplayName} negates Slay Living with magic resistance.");
            return result;
        }

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = 0;
        result.HpChanges[target.DisplayName] = -before;
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and dies instantly. HP {before}->0.");

        return result;
    }

    // Reads the percentage from a "Death Resistance" ability, e.g. "Death resistant (30%)."
    private static int GetDeathResistancePercent(MonsterInstance monster)
    {
        foreach (var ability in monster.Template.SpecialAbilities)
        {
            if (!string.Equals(ability.Name?.Trim(), "Death Resistance", StringComparison.OrdinalIgnoreCase))
                continue;

            var m = Regex.Match(ability.Description ?? string.Empty, @"(\d+)\s*%");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var percent))
                return Math.Clamp(percent, 0, 100);
        }

        return 0;
    }
}
