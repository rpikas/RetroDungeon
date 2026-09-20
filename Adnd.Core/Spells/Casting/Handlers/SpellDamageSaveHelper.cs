using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

internal static class SpellDamageSaveHelper
{
    internal readonly record struct Outcome(
        int SaveTarget,
        int SaveRoll,
        bool Saved,
        int AppliedDamage,
        int BeforeHp,
        int AfterHp,
        int ActualDamage,
        bool MagicResisted,
        int? MagicResistanceRoll,
        int? MagicResistancePercent);

    internal readonly record struct MagicResistanceCheck(bool HasResistance, int ChancePercent, int Roll, bool Resisted);

    internal static MagicResistanceCheck CheckMagicResistance(MonsterInstance monster, Random rng, string spellName)
    {
        var chancePercent = monster.Template.MagicResistancePercent;
        if (!chancePercent.HasValue || chancePercent.Value <= 0)
            return new MagicResistanceCheck(false, 0, 0, false);

        var clampedChance = Math.Clamp(chancePercent.Value, 0, 100);
        var roll = rng.Next(1, 101);
        var resisted = roll <= clampedChance;

        RuleApplicationInfo.Publish(
            "DMG",
            "Magic Resistance",
            $"{monster.DisplayName} magic resistance vs {spellName}",
            "When applicable, roll 1d100. If roll is <= magic resistance %, spell effect is totally avoided.",
            "1",
            "100",
            roll.ToString(),
            resisted
                ? $"Magic resistance succeeds ({clampedChance}%). Spell effect avoided."
                : $"Magic resistance fails ({clampedChance}%). Spell proceeds.");

        return new MagicResistanceCheck(true, clampedChance, roll, resisted);
    }

    internal static bool IsNegatedByMagicResistance(MonsterInstance monster, Random rng, string spellName)
    {
        return CheckMagicResistance(monster, rng, spellName).Resisted;
    }

    internal static int GetMonsterMagicSaveTarget(MonsterInstance monster, int fallbackTarget = 20)
    {
        var baseTarget = monster.Template.SavingThrows?.Spell ?? fallbackTarget;
        return AdjustMonsterSaveTargetForSpecialDefenses(monster, baseTarget, isMagicSave: true, isPoisonSave: false);
    }

    internal static int GetMonsterPoisonSaveTarget(MonsterInstance monster, int fallbackTarget = 20)
    {
        var baseTarget = monster.Template.SavingThrows?.ParalyzationPoisonDeath ?? fallbackTarget;
        return AdjustMonsterSaveTargetForSpecialDefenses(monster, baseTarget, isMagicSave: false, isPoisonSave: true);
    }

    private static int AdjustMonsterSaveTargetForSpecialDefenses(MonsterInstance monster, int baseTarget, bool isMagicSave, bool isPoisonSave)
    {
        var adjusted = baseTarget;

        if ((isMagicSave || isPoisonSave)
            && HasSpecialDefense(monster, "Magic & Poison Saves as 4 levels higher"))
        {
            adjusted = Math.Max(1, adjusted - 4);
        }

        return adjusted;
    }

    private static bool HasSpecialDefense(MonsterInstance monster, string defenseName)
    {
        return monster.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), defenseName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Description?.Trim(), defenseName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string FormatSaveAndDamageLine(string targetDisplayName, int rolledDamage, Outcome outcome, string? rolledDamageText = null)
    {
        if (outcome.MagicResisted)
        {
            var mrRoll = outcome.MagicResistanceRoll ?? 0;
            var mrChance = outcome.MagicResistancePercent ?? 0;
            return $"{targetDisplayName} save vs spell rolled {outcome.SaveRoll} vs {outcome.SaveTarget} => {(outcome.Saved ? "SUCCESS" : "FAIL")}. " +
                   $"Magic resistance roll 1d100={mrRoll} vs {mrChance}% => NEGATED. No damage. HP {outcome.BeforeHp}->{outcome.AfterHp}.";
        }

        var damageText = rolledDamageText ?? rolledDamage.ToString();
        return $"{targetDisplayName} save vs spell rolled {outcome.SaveRoll} vs {outcome.SaveTarget} => {(outcome.Saved ? "SUCCESS" : "FAIL")}. " +
               $"Damage {damageText}{(outcome.Saved ? $" halved to {outcome.AppliedDamage}" : string.Empty)}. HP {outcome.BeforeHp}->{outcome.AfterHp}.";
    }

    internal static bool IsUndeadImmuneToSpell(MonsterInstance monster, string? spellIdOrName)
    {
        if (monster.InstanceMonsterType != MonsterType.Undead && monster.Template.Type != MonsterType.Undead)
            return false;

        if (string.IsNullOrWhiteSpace(spellIdOrName))
            return false;

        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("sleep", StringComparison.Ordinal)
               || key.Contains("charm", StringComparison.Ordinal)
               || key.Contains("hold", StringComparison.Ordinal)
               || key.Contains("cold", StringComparison.Ordinal)
               || key.Contains("ice", StringComparison.Ordinal);
    }

    internal static Outcome ApplyToMonster(MonsterInstance monster, int rolledDamage, Random rng, string spellName)
    {
        var saveTarget = GetMonsterMagicSaveTarget(monster, 0);
        var saveRoll = rng.Next(1, 21);
        var saved = saveTarget > 0 && saveRoll >= saveTarget;
        var appliedDamage = saved ? rolledDamage / 2 : rolledDamage;
        var resistance = CheckMagicResistance(monster, rng, spellName);
        if (resistance.Resisted)
            appliedDamage = 0;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - appliedDamage);
        var actual = Math.Max(0, before - monster.CurrentHitPoints);

        if (actual > 0)
        {
            if (monster.HasStatus(MonsterStatus.Asleep))
                monster.SetStatus(MonsterStatus.Asleep, 0);

            if (monster.HasStatus(MonsterStatus.Unconscious))
                monster.SetStatus(MonsterStatus.Unconscious, 0);
        }

        RuleApplicationInfo.Publish(
            "DMG",
            "Saving Throws",
            $"{monster.DisplayName} saving throw vs spell ({spellName})",
            $"Roll d20, need {saveTarget}+ to save. Success means half damage.",
            "1",
            "20",
            saveRoll.ToString(),
            resistance.Resisted
                ? $"Save {(saved ? "made" : "failed")}, but magic resistance negated the spell."
                : saved
                ? $"Save made. Damage halved: {rolledDamage} -> {appliedDamage}."
                : $"Save failed. Full damage: {appliedDamage}.");

        return new Outcome(
            saveTarget,
            saveRoll,
            saved,
            appliedDamage,
            before,
            monster.CurrentHitPoints,
            actual,
            resistance.Resisted,
            resistance.HasResistance ? resistance.Roll : null,
            resistance.HasResistance ? resistance.ChancePercent : null);
    }
}
