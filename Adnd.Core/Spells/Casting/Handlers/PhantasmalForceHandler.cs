using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class PhantasmalForceHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
    {
        return string.Equals(spellId, "phantasmal_force", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spellId, "phantasmal_force_magic_user", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spellId, "improved_phantasmal_force", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spellId, "spectral_force", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spellId, "permanent_illusion", StringComparison.OrdinalIgnoreCase);
    }

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Phantasmal Force can only be cast in combat.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Phantasmal Force requires combat session context.");

        var rng = request.Rng ?? Random.Shared;

        var isImproved = string.Equals(request.SpellId, "improved_phantasmal_force", StringComparison.OrdinalIgnoreCase)
                         || spell.TargetingScope == SpellTargetingScope.AllGroups;

        var isSpectralForce = string.Equals(request.SpellId, "spectral_force", StringComparison.OrdinalIgnoreCase);
        var isPermanentIllusion = string.Equals(request.SpellId, "permanent_illusion", StringComparison.OrdinalIgnoreCase);

        var groupIds = isImproved
            ? session.GetDistinctGroupIds().Where(g => session.GetAliveCountByGroup(g) > 0).ToList()
            : ResolveSingleTargetGroup(request, session);

        if (groupIds.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add(isImproved
            ? $"{request.Caster.Name} casts {spell.Name}! A terrifying red dragon illusion breathes on all encounter groups."
            : $"{request.Caster.Name} casts {spell.Name}! A terrifying red dragon illusion breathes on group {groupIds[0]}."
        );

        foreach (var groupId in groupIds)
            ResolvePhantasmalForceAgainstGroup(groupId, spell, session, rng, result, breakOnFirstDisbelief: !isSpectralForce && !isPermanentIllusion, applyPermanentStatus: isPermanentIllusion);

        return result;
    }

    private static List<string> ResolveSingleTargetGroup(SpellCastRequest request, CombatSession session)
    {
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

        return new List<string> { targetGroupId };
    }

    private static void ResolvePhantasmalForceAgainstGroup(string groupId, Spell spell, CombatSession session, Random rng, SpellCastResult result, bool breakOnFirstDisbelief, bool applyPermanentStatus)
    {
        var targets = session.GetAliveMonstersByGroup(groupId).ToList();
        if (targets.Count == 0)
            return;

        if (breakOnFirstDisbelief)
        {
            MonsterInstance? disbeliefMonster = null;
            int disbeliefRoll = 0;
            int disbeliefTarget = 0;

            foreach (var monster in targets)
            {
                var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(monster, 0);
                var saveRoll = rng.Next(1, 21);
                var saved = saveTarget > 0 && saveRoll >= saveTarget;

                RuleApplicationInfo.Publish(
                    "PHB",
                    "Phantasmal Force",
                    $"{monster.DisplayName} save vs spell against illusion",
                    $"Roll d20, need {saveTarget}+ to disbelieve.",
                    "1",
                    "20",
                    saveRoll.ToString(),
                    saved
                        ? "Save made. Monster disbelieves and warns the group; illusion collapses."
                        : "Save failed. Monster believes the dragon breath is real.");

                if (!saved)
                    continue;

                disbeliefMonster = monster;
                disbeliefRoll = saveRoll;
                disbeliefTarget = saveTarget;
                break;
            }

            if (disbeliefMonster != null)
            {
                result.Events.Add($"{disbeliefMonster.DisplayName} (Group {groupId}) makes save ({disbeliefRoll} vs {disbeliefTarget}) and shouts it is fake!");
                result.Events.Add($"The illusion is exposed for group {groupId}. No monsters in that group take damage.");
                return;
            }
        }

        foreach (var monster in targets)
        {
            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(monster, 0);
            var saveRoll = rng.Next(1, 21);
            var saved = saveTarget > 0 && saveRoll >= saveTarget;

            RuleApplicationInfo.Publish(
                "PHB",
                breakOnFirstDisbelief ? "Phantasmal Force" : "Spectral Force",
                $"{monster.DisplayName} save vs spell against illusion",
                breakOnFirstDisbelief
                    ? $"Roll d20, need {saveTarget}+ to disbelieve."
                    : $"Roll d20, need {saveTarget}+ to disbelieve; success does not expose illusion to allies.",
                "1",
                "20",
                saveRoll.ToString(),
                saved
                    ? (breakOnFirstDisbelief
                        ? "Save made. Monster disbelieves and warns the group; illusion collapses."
                        : "Save made. Monster disbelieves, but does not reveal the illusion to others.")
                    : "Save failed. Monster believes the dragon breath is real.");

            if (saved)
            {
                if (!breakOnFirstDisbelief)
                    result.Events.Add($"{monster.DisplayName} (Group {groupId}) disbelieves the illusion (save {saveRoll} vs {saveTarget}) and is unaffected.");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, rng, spell.Name))
                continue;

            var rolledDamage = 0;
            for (int i = 0; i < 16; i++)
                rolledDamage += rng.Next(1, 7);

            var before = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actual = Math.Max(0, before - monster.CurrentHitPoints);

            if (applyPermanentStatus && monster.IsAlive)
                monster.SetStatus(MonsterStatus.PermanentIllusion, int.MaxValue);

            result.Events.Add($"{monster.DisplayName} (Group {groupId}) believes the dragon breath, takes {actual} illusionary fire damage (rolled {rolledDamage}). HP {before}->{monster.CurrentHitPoints}.");
            if (applyPermanentStatus && monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} remains trapped by the permanent illusion for the rest of the battle.");
            if (!monster.IsAlive)
                result.Events.Add($"{monster.DisplayName} dies from terror and shock.");

            if (result.HpChanges.ContainsKey(monster.DisplayName))
                result.HpChanges[monster.DisplayName] -= actual;
            else
                result.HpChanges[monster.DisplayName] = -actual;
        }
    }
}
