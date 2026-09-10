using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class SlowHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "slow", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Slow can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Slow requires combat session context.");

        var casterLevel = request.Caster.Classes.Contains(Adnd.Core.Characters.CharacterClass.MagicUser)
            ? request.Caster.GetClassLevel(Adnd.Core.Characters.CharacterClass.MagicUser)
            : request.Caster.Level;
        casterLevel = Math.Max(1, casterLevel);

        var rounds = 3 + casterLevel;

        string targetGroupId = "default";
        var firstTarget = request.Targets.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId))
        {
            targetGroupId = firstTarget.TargetGroupId!;
        }
        else if (session.GetDistinctGroupIds().Count() > 1)
        {
            targetGroupId = session.GetDistinctGroupIds()
                .FirstOrDefault(g => session.GetAliveCountByGroup(g) > 0) ?? "default";
        }

        var candidates = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (candidates.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var rng = request.Rng ?? Random.Shared;
        var targetCount = Math.Min(casterLevel, candidates.Count);
        var targets = candidates.OrderBy(_ => rng.Next()).Take(targetCount).ToList();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");
        result.Events.Add($"Slow can affect up to {casterLevel} creature(s). {targets.Count} target(s) selected in group '{targetGroupId}'.");

        var affected = 0;

        foreach (var monster in targets)
        {
            var saveTarget = monster.Template.SavingThrows?.Spell ?? 20;
            var saveRoll = rng.Next(1, 21);
            if (saveRoll >= saveTarget)
            {
                result.Events.Add($"{monster.DisplayName} resists Slow (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, rng, spell.Name))
            {
                result.Events.Add($"{monster.DisplayName} negates Slow with magic resistance.");
                continue;
            }

            monster.SetStatus(MonsterStatus.Slowed, rounds);
            affected++;
            result.Events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is slowed for {rounds} round(s).");
        }

        result.Events.Add($"Slow affected {affected} target(s). Slowed enemies move at half speed and attack at half rate.");
        return result;
    }
}
