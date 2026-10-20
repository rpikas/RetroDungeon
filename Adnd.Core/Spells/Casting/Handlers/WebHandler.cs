using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class WebHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "web", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Web can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Web requires combat session context.");

        var rng = request.Rng ?? Random.Shared;
        var magicUserLevel = request.Caster.GetClassLevel(CharacterClass.MagicUser);
        var casterLevel = request.EffectiveCasterLevel
                          ?? (magicUserLevel > 0 ? magicUserLevel : request.Caster.Level);
        casterLevel = Math.Max(1, casterLevel);

        var holdRounds = 20 * casterLevel;
        var targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? "default";

        var targets = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targets.Count == 0)
            return SpellCastResult.Failure("No valid targets for Web in the selected group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        var heldCount = 0;
        foreach (var target in targets)
        {
            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
            var saveRoll = rng.Next(1, 21);

            if (saveRoll >= saveTarget)
            {
                result.Events.Add($"{target.DisplayName} avoids the webs (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
            {
                result.Events.Add($"{target.DisplayName} negates web with magic resistance.");
                continue;
            }

            target.SetStatus(MonsterStatus.Webbed, holdRounds);
            session.ResetMonsterWebTrappedRounds(target);
            heldCount++;
            result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is held in webs for {holdRounds} round(s).");
        }

        result.Events.Add($"Web affected group '{targetGroupId}': {heldCount} held, {targets.Count - heldCount} escaped.");
        result.Events.Add("Web suffocation: each full turn trapped adds cumulative +5% chance to suffocate.");
        return result;
    }
}
