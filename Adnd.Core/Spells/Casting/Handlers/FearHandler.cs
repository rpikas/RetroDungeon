using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class FearHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "fear_magic_user", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spellId, "fear_illusionist", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Fear requires combat session context.");

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

        var targetMonsters = session.GetAliveMonstersByGroup(targetGroupId).ToList();
        if (targetMonsters.Count == 0)
            return SpellCastResult.Failure("No valid monsters in target group.");

        var rng = request.Rng ?? Random.Shared;
        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        ApplyFear(targetMonsters, ResolveCasterLevel(request, spell), spell.Name, spell.Id, saveVsWand: false, rng, result.Events);

        result.Events.Add($"Fear affected group '{targetGroupId}'.");
        return result;
    }

    // Shared by the Fear spell and the Wand of Fear: failed save sends the monster fleeing in panic.
    internal static void ApplyFear(IEnumerable<MonsterInstance> targets, int rounds, string sourceName, string immunityKey, bool saveVsWand, Random rng, List<string> events)
    {
        foreach (var monster in targets)
        {
            if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(monster, immunityKey))
            {
                events.Add($"{monster.DisplayName} is immune to mind-affecting effects. {sourceName} has no effect.");
                continue;
            }

            var saveTarget = saveVsWand
                ? SpellDamageSaveHelper.GetMonsterWandSaveTarget(monster, 0)
                : SpellDamageSaveHelper.GetMonsterMagicSaveTarget(monster, 0);
            var saveRoll = rng.Next(1, 21);
            var saved = saveTarget > 0 && saveRoll >= saveTarget;

            RuleApplicationInfo.Publish(
                "DMG",
                "79 Savethrow Monster",
                $"{monster.DisplayName} saving throw vs {(saveVsWand ? "wand" : "spell")} ({sourceName})",
                $"Roll d20, need {saveTarget}+ to save. Failed save means creature flees in panic.",
                "1",
                "20",
                saveRoll.ToString(),
                saved
                    ? "Save made. Monster resists fear."
                    : $"Save failed. Monster flees in panic for {rounds} round(s).");

            if (saved)
            {
                events.Add($"{monster.DisplayName} resists fear (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, rng, sourceName))
            {
                events.Add($"{monster.DisplayName} negates fear with magic resistance.");
                continue;
            }

            monster.SetStatus(MonsterStatus.Panicked, rounds);
            events.Add($"{monster.DisplayName} fails save ({saveRoll} vs {saveTarget}) and flees in terror for {rounds} round(s)!");
        }
    }

    private static int ResolveCasterLevel(SpellCastRequest request, Spell spell)
    {
        if (request.EffectiveCasterLevel is int effective && effective > 0)
            return effective;

        var caster = request.Caster;
        var classLevel = spell.SpellClass switch
        {
            SpellClass.MagicUser when caster.Classes.Contains(CharacterClass.MagicUser) => caster.GetClassLevel(CharacterClass.MagicUser),
            SpellClass.Illusionist when caster.Classes.Contains(CharacterClass.Illusionist) => caster.GetClassLevel(CharacterClass.Illusionist),
            _ => caster.Level
        };

        return Math.Max(1, classLevel);
    }
}
