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

    [ThreadStatic]
    internal static int? CurrentSpellLevel;

    internal static bool IsImmuneToSpellsBelowLevel8(MonsterInstance monster)
    {
        return CurrentSpellLevel is < 8
               && monster.Template.SpecialDefenses.Any(d =>
                   string.Equals(d?.Name?.Trim(), "Immune to Spells Below 8th Level", StringComparison.OrdinalIgnoreCase));
    }

    internal static MagicResistanceCheck CheckMagicResistance(MonsterInstance monster, Random rng, string spellName)
    {
        if (IsImmuneToSpellsBelowLevel8(monster))
        {
            RuleApplicationInfo.Publish(
                "AD&D",
                "Spell Immunity",
                $"{monster.DisplayName} vs {spellName}",
                "Rakshasas are not affected by spells below the 8th level.",
                "-", "-", "-",
                "Spell has no effect.");
            return new MagicResistanceCheck(true, 100, 0, true);
        }

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

        if ((isMagicSave || isPoisonSave)
            && HasColdFireLightningImmunityAndSevenHdSaves(monster))
        {
            adjusted = Math.Min(adjusted, 12);
        }

        return adjusted;
    }

    internal static bool HasMindAffectingImmunity(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Mind-affecting spells Immunity", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Mind-affecting spells Immunity", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "Immune to charm, sleep, and all mind-affecting spells", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Immune to charm, sleep, and all mind-affecting spells", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("mind-affecting", StringComparison.Ordinal)
                       && merged.Contains("immun", StringComparison.Ordinal));
        });
    }

    internal static bool IsMindAffectingSpell(string? spellIdOrName)
    {
        if (string.IsNullOrWhiteSpace(spellIdOrName))
            return false;

        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("charm", StringComparison.Ordinal)
               || key.Contains("sleep", StringComparison.Ordinal)
               || key.Contains("hold", StringComparison.Ordinal)
               || key.Contains("fear", StringComparison.Ordinal)
               || key.Contains("confusion", StringComparison.Ordinal)
               || key.Contains("chaos", StringComparison.Ordinal)
               || key.Contains("feeblemind", StringComparison.Ordinal)
               || key.Contains("phantasmal", StringComparison.Ordinal)
               || key.Contains("color_spray", StringComparison.Ordinal)
               || key.Contains("color spray", StringComparison.Ordinal)
               || key.Contains("paralyzation", StringComparison.Ordinal)
               || key.Contains("power_word_stun", StringComparison.Ordinal)
               || key.Contains("power word stun", StringComparison.Ordinal);
    }

    internal static bool IsNegatedByMindAffectingImmunity(MonsterInstance monster, string? spellIdOrName)
    {
        return (HasSleepAndCharmImmunity(monster) && IsSleepOrCharmSpell(spellIdOrName))
               || (HasCharmSleepHoldSpellImmunity(monster) && IsCharmSleepHoldSpell(spellIdOrName))
               || (HasNightHagSpellImmunities(monster) && IsNightHagBlockedSpell(spellIdOrName))
               || (HasMindAffectingImmunity(monster) && IsMindAffectingSpell(spellIdOrName))
               || (HasUndeadTraitsCharmSleepHoldImmunity(monster) && IsCharmSleepHoldSpell(spellIdOrName))
               || (HasImmunityToSomeAttacks(monster) && IsSomeAttackImmunitySpell(spellIdOrName));
    }

    private static bool HasCharmSleepHoldSpellImmunity(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var merged = $"{d.Name} {d.Description}".ToLowerInvariant();
            return merged.Contains("immune", StringComparison.Ordinal)
                   && merged.Contains("charm", StringComparison.Ordinal)
                   && merged.Contains("sleep", StringComparison.Ordinal)
                   && merged.Contains("hold", StringComparison.Ordinal);
        });
    }

    private static bool HasNightHagSpellImmunities(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var merged = $"{d.Name} {d.Description}".ToLowerInvariant();
            return merged.Contains("invulnerable to sleep, charm, fear, fire, and cold", StringComparison.Ordinal);
        });
    }

    private static bool HasElectricityImmunity(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var merged = $"{d.Name} {d.Description}".ToLowerInvariant();
            return (merged.Contains("electric", StringComparison.Ordinal) || merged.Contains("lightning", StringComparison.Ordinal))
                   && (merged.Contains("immun", StringComparison.Ordinal) || merged.Contains("impervious", StringComparison.Ordinal))
                   && !merged.Contains("fire", StringComparison.Ordinal);
        });
    }

    private static bool HasSleepAndCharmImmunity(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Immune to Sleep & Charm", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Immune to Sleep & Charm", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("immune", StringComparison.Ordinal)
                       && merged.Contains("sleep", StringComparison.Ordinal)
                       && merged.Contains("charm", StringComparison.Ordinal));
        });
    }

    private static bool HasUndeadTraitsCharmSleepHoldImmunity(MonsterInstance monster)
    {
        bool Matches(string? name, string? description)
        {
            var n = name?.Trim() ?? string.Empty;
            var d = description?.Trim() ?? string.Empty;
            var merged = $"{n} {d}".ToLowerInvariant();

            if (string.Equals(n, "Undead Traits", StringComparison.OrdinalIgnoreCase)
                && merged.Contains("immune", StringComparison.Ordinal)
                && merged.Contains("charm", StringComparison.Ordinal)
                && merged.Contains("sleep", StringComparison.Ordinal)
                && merged.Contains("hold", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        return monster.Template.SpecialAbilities.Any(a => Matches(a.Name, a.Description))
               || monster.Template.SpecialDefenses.Any(d => Matches(d.Name, d.Description));
    }

    private static bool IsCharmSleepHoldSpell(string? spellIdOrName)
    {
        if (string.IsNullOrWhiteSpace(spellIdOrName))
            return false;

        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("charm", StringComparison.Ordinal)
               || key.Contains("sleep", StringComparison.Ordinal)
               || key.Contains("hold", StringComparison.Ordinal);
    }

    private static bool IsNightHagBlockedSpell(string? spellIdOrName)
    {
        if (string.IsNullOrWhiteSpace(spellIdOrName))
            return false;

        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("sleep", StringComparison.Ordinal)
               || key.Contains("charm", StringComparison.Ordinal)
               || key.Contains("fear", StringComparison.Ordinal)
               || IsFireOrHeatSpell(key)
               || IsColdSpell(key);
    }

    private static bool IsSleepOrCharmSpell(string? spellIdOrName)
    {
        if (string.IsNullOrWhiteSpace(spellIdOrName))
            return false;

        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("charm", StringComparison.Ordinal)
               || key.Contains("sleep", StringComparison.Ordinal);
    }

    internal static bool HasImmunityToSomeAttacks(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Immunity to Some Attacks", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Immunity to Some Attacks", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "Immunity to cold", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Immunity to cold", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "Immunity to fire and heat", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Immunity to fire and heat", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("immune to electricity", StringComparison.Ordinal)
                       && merged.Contains("fear", StringComparison.Ordinal)
                       && merged.Contains("hold", StringComparison.Ordinal)
                       && merged.Contains("paralyzation", StringComparison.Ordinal)
                       && merged.Contains("polymorph", StringComparison.Ordinal)
                       && merged.Contains("sleep", StringComparison.Ordinal));
        });
    }

    private static bool IsSomeAttackImmunitySpell(string? spellIdOrName)
    {
        if (string.IsNullOrWhiteSpace(spellIdOrName))
            return false;

        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("fear", StringComparison.Ordinal)
               || key.Contains("hold", StringComparison.Ordinal)
               || key.Contains("paralyzation", StringComparison.Ordinal)
               || key.Contains("sleep", StringComparison.Ordinal)
               || key.Contains("polymorph", StringComparison.Ordinal);
    }

    private static bool IsElectricitySpell(string spellIdOrName)
    {
        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("lightning", StringComparison.Ordinal)
               || key.Contains("electric", StringComparison.Ordinal);
    }

    private static bool IsColdSpell(string spellIdOrName)
    {
        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("cold", StringComparison.Ordinal)
               || key.Contains("ice", StringComparison.Ordinal);
    }

    private static bool HasSpecialDefense(MonsterInstance monster, string defenseName)
    {
        return monster.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), defenseName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Description?.Trim(), defenseName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasColdImmunity(MonsterInstance monster)
    {
        if (HasSpecialDefense(monster, "Immunity to cold")
            || HasSpecialDefense(monster, "Cold Immunity"))
        {
            return true;
        }

        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();
            return merged.Contains("cold", StringComparison.Ordinal)
                   && (merged.Contains("immune", StringComparison.Ordinal)
                       || merged.Contains("immunity", StringComparison.Ordinal)
                       || merged.Contains("impervious", StringComparison.Ordinal)
                       || merged.Contains("not harmed by cold", StringComparison.Ordinal)
                       || merged.Contains("not harmed by cold-based attacks", StringComparison.Ordinal));
        });
    }

    private static bool HasColdFireLightningImmunityAndSevenHdSaves(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            var hasElements = (merged.Contains("cold", StringComparison.Ordinal)
                               || merged.Contains("ice", StringComparison.Ordinal))
                              && (merged.Contains("fire", StringComparison.Ordinal)
                                  || merged.Contains("heat", StringComparison.Ordinal))
                              && (merged.Contains("lightning", StringComparison.Ordinal)
                                  || merged.Contains("electric", StringComparison.Ordinal));

            var hasSevenHdSaves = merged.Contains("save", StringComparison.Ordinal)
                                  && merged.Contains("7 hd", StringComparison.Ordinal);

            return hasElements && hasSevenHdSaves;
        });
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

    internal static bool IsMummy(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        var name = monster.Template.Name?.Trim() ?? string.Empty;
        return string.Equals(name, "Mummy", StringComparison.OrdinalIgnoreCase)
               || name.Contains("mummy", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasPoisonOrParalysisImmunity(MonsterInstance monster)
    {
        if (IsMummy(monster))
            return true;

        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();
            return (merged.Contains("poison", StringComparison.Ordinal) && merged.Contains("immune", StringComparison.Ordinal))
                   || (merged.Contains("paraly", StringComparison.Ordinal) && merged.Contains("immune", StringComparison.Ordinal))
                   || merged.Contains("poison or paralysis do not harm", StringComparison.Ordinal);
        });
    }

    internal static Outcome ApplyToMonster(MonsterInstance monster, int rolledDamage, Random rng, string spellName, int fireDamageDiceCount = 0, int coldDamageDiceCount = 0)
    {
        if (IsAffectedOnlyByMagicalCold(monster))
        {
            if (IsColdSpell(spellName))
            {
                // A cold wand or white dragon breath kills brown mold outright.
                var hpBefore = monster.CurrentHitPoints;
                monster.CurrentHitPoints = 0;
                return new Outcome(0, 0, false, hpBefore, hpBefore, 0, hpBefore, false, null, null);
            }

            return new Outcome(
                SaveTarget: 0,
                SaveRoll: 0,
                Saved: true,
                AppliedDamage: 0,
                BeforeHp: monster.CurrentHitPoints,
                AfterHp: monster.CurrentHitPoints,
                ActualDamage: 0,
                MagicResisted: false,
                MagicResistanceRoll: null,
                MagicResistancePercent: null);
        }

        if (IsAffectedOnlyByFireBasedAttacks(monster) && !IsFireOrHeatSpell(spellName))
        {
            return new Outcome(
                SaveTarget: 0,
                SaveRoll: 0,
                Saved: true,
                AppliedDamage: 0,
                BeforeHp: monster.CurrentHitPoints,
                AfterHp: monster.CurrentHitPoints,
                ActualDamage: 0,
                MagicResisted: false,
                MagicResistanceRoll: null,
                MagicResistancePercent: null);
        }

        if ((HasImmunityToSomeAttacks(monster) || HasColdFireLightningImmunityAndSevenHdSaves(monster) || HasElectricityImmunity(monster))
            && IsElectricitySpell(spellName))
        {
            return new Outcome(
                SaveTarget: 0,
                SaveRoll: 0,
                Saved: true,
                AppliedDamage: 0,
                BeforeHp: monster.CurrentHitPoints,
                AfterHp: monster.CurrentHitPoints,
                ActualDamage: 0,
                MagicResisted: false,
                MagicResistanceRoll: null,
                MagicResistancePercent: null);
        }

        var adjustedRolledDamage = rolledDamage;
        if ((HasColdImmunity(monster) || HasColdFireLightningImmunityAndSevenHdSaves(monster) || HasNightHagSpellImmunities(monster))
            && IsColdSpell(spellName))
            adjustedRolledDamage = 0;

        if (adjustedRolledDamage > 0
            && coldDamageDiceCount > 0
            && IsColdSpell(spellName)
            && monster.Template.SpecialDefenses.Any(d =>
                string.Equals(d?.Name?.Trim(), "Cold Vulnerability", StringComparison.OrdinalIgnoreCase)))
        {
            adjustedRolledDamage += coldDamageDiceCount;
        }

        if ((HasSpecialDefense(monster, "Immunity to fire and heat")
             || HasNightHagSpellImmunities(monster)
             || HasColdFireLightningImmunityAndSevenHdSaves(monster))
            && IsFireOrHeatSpell(spellName))
            adjustedRolledDamage = 0;

        if (adjustedRolledDamage > 0
            && IsMummy(monster)
            && IsFireOrHeatSpell(spellName)
            && fireDamageDiceCount > 0)
        {
            adjustedRolledDamage += fireDamageDiceCount;
        }

        if (HasSpecialDefense(monster, "Blink"))
            adjustedRolledDamage = Math.Max(0, adjustedRolledDamage / 2);

        if (HasImmunityToSomeAttacks(monster) && IsColdSpell(spellName))
        {
            adjustedRolledDamage = Math.Max(0, rolledDamage / 2);
            monster.SetStatus(MonsterStatus.Slowed, 2);
        }

        var saveTarget = GetMonsterMagicSaveTarget(monster, 0);
        var saveRoll = rng.Next(1, 21);
        var saved = saveTarget > 0 && saveRoll >= saveTarget;
        var appliedDamage = saved ? adjustedRolledDamage / 2 : adjustedRolledDamage;
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
            "79 Savethrow Monster",
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

    private static bool IsFireOrHeatSpell(string spellIdOrName)
    {
        var key = spellIdOrName.Trim().ToLowerInvariant();
        return key.Contains("fire", StringComparison.Ordinal)
               || key.Contains("flame", StringComparison.Ordinal)
               || key.Contains("heat", StringComparison.Ordinal)
               || key.Contains("incendiary", StringComparison.Ordinal)
               || key.Contains("burn", StringComparison.Ordinal);
    }

    internal static bool IsAffectedOnlyByMagicalCold(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
            string.Equals(d?.Name?.Trim(), "Affected Only by Magical Cold", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAffectedOnlyByFireBasedAttacks(MonsterInstance monster)
    {
        return monster.Template.SpecialDefenses.Any(d =>
        {
            var name = d?.Name?.Trim() ?? string.Empty;
            var description = d?.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}";
            return string.Equals(name, "Affected only by fire based attacks", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Affected only by fire based attacks", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("affected only by fire", StringComparison.OrdinalIgnoreCase)
                       && merged.Contains("attack", StringComparison.OrdinalIgnoreCase));
        });
    }
}
