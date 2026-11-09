using Adnd.Core.Assassination;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private static bool HasRingOfFireResistance(Character target)
    {
        if (target.Equipment == null)
            return false;

        static bool IsRingOfFireResistance(Item? item)
            => item != null
               && string.Equals(item.Name, "Ring of Fire Resistance", StringComparison.OrdinalIgnoreCase);

        target.Equipment.TryGetValue(EquipmentSlot.Ring1, out var ring1);
        target.Equipment.TryGetValue(EquipmentSlot.Ring2, out var ring2);
        var hasRing = IsRingOfFireResistance(ring1) || IsRingOfFireResistance(ring2);
        if (hasRing)
            return true;

        target.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand);
        return IsSwordPlusThreeFrostBrand(mainHand);
    }

    private static int GetFireResistanceSaveBonus(Character target)
    {
        var saveBonus = Math.Max(target.FireProtectionSaveBonusVsFire, target.PotionFireResistanceSaveBonusVsFire);
        if (HasRingOfFireResistance(target))
            saveBonus = Math.Max(saveBonus, 4);

        return saveBonus;
    }

    private static int ApplyFireProtectionDamageReduction(Character target, int incomingDamage, int damageDiceCount, bool isMagicalFire, bool isNormalFire)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        var hasSpellProtection = target.HasActiveProtectionFromFire;
        var hasPotionProtection = target.HasActivePotionFireResistance;
        var hasRingProtection = HasRingOfFireResistance(target);
        if (!hasSpellProtection && !hasPotionProtection && !hasRingProtection)
            return damage;

        var normalFireImmunity = (hasSpellProtection && target.FireProtectionNormalFireImmunity)
            || (hasPotionProtection && target.PotionFireResistanceNormalFireImmunity)
            || hasRingProtection;
        if (isNormalFire && normalFireImmunity)
            return 0;

        if (isMagicalFire && hasSpellProtection && target.FireProtectionHalfDamageFromMagicalFire)
            damage = (int)Math.Ceiling(damage * 0.5);

        var fireReductionPerDie = 0;
        if (isMagicalFire && hasPotionProtection && target.PotionFireResistanceDamageReductionPerDie > 0)
            fireReductionPerDie = Math.Max(fireReductionPerDie, target.PotionFireResistanceDamageReductionPerDie);
        if (isMagicalFire && hasRingProtection)
            fireReductionPerDie = Math.Max(fireReductionPerDie, 2);

        if (isMagicalFire && fireReductionPerDie > 0 && damageDiceCount > 0)
        {
            var reduction = fireReductionPerDie * damageDiceCount;
            damage = Math.Max(damageDiceCount, damage - reduction);
        }

        if (isMagicalFire && hasSpellProtection && target.FireProtectionAbsorptionRemaining > 0)
        {
            var absorbed = Math.Min(target.FireProtectionAbsorptionRemaining, damage);
            target.FireProtectionAbsorptionRemaining -= absorbed;
            damage -= absorbed;
        }

        return Math.Max(0, damage);
    }

    private static int ApplyColdResistanceDamageReduction(Character target, int incomingDamage, bool isMagicalCold, bool isNormalCold)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        if (!target.HasActiveResistCold)
            return damage;

        // Resist Cold inures normal cold and halves magical cold before saving throw.
        if (isNormalCold)
            return 0;

        if (isMagicalCold)
            damage = Math.Max(1, damage / 2);

        return Math.Max(0, damage);
    }

    private static int ApplyLightningProtectionDamageReduction(Character target, int incomingDamage, bool isMagicalLightning, bool isNormalLightning)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        if (!target.HasActiveProtectionFromLightning)
            return damage;

        if (isNormalLightning && target.LightningProtectionNormalLightningImmunity)
            return 0;

        if (isMagicalLightning && target.LightningProtectionHalfDamageFromMagicalLightning)
            damage = (int)Math.Ceiling(damage * 0.5);

        if (isMagicalLightning && target.LightningProtectionAbsorptionRemaining > 0)
        {
            var absorbed = Math.Min(target.LightningProtectionAbsorptionRemaining, damage);
            target.LightningProtectionAbsorptionRemaining -= absorbed;
            damage -= absorbed;
        }

        return Math.Max(0, damage);
    }

    private static int ApplyUniversalPotionInvulnerabilitySaveBonus(Character target, int saveTarget)
    {
        var adjusted = saveTarget;

        if (target.HasActivePotionInvulnerability && target.PotionInvulnerabilitySaveBonus > 0)
            adjusted = Math.Max(1, adjusted - target.PotionInvulnerabilitySaveBonus);

        var ringProtectionSaveBonus = Math.Max(target.RingProtectionSelfSaveBonus, target.RingProtectionReceivedAuraSaveBonus);
        if (ringProtectionSaveBonus > 0)
            adjusted = Math.Max(1, adjusted - ringProtectionSaveBonus);

        var luckBladeSaveBonus = GetLuckBladeSaveBonus(target);
        if (luckBladeSaveBonus > 0)
            adjusted = Math.Max(1, adjusted - luckBladeSaveBonus);

        if (HasEquippedCloakOfDisplacement(target))
            adjusted = Math.Max(1, adjusted - 2);

        return adjusted;
    }

    private static int GetLuckBladeSaveBonus(Character target)
    {
        if (target?.Equipment == null)
            return 0;

        static bool IsLuckBlade(Item? item)
        {
            if (item == null || item.Type != ItemType.Weapon)
                return false;

            if (!string.IsNullOrWhiteSpace(item.Name)
                && item.Name.Contains("Luck Blade", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return item.SpecialAbilities != null
                   && item.SpecialAbilities.Any(a =>
                       !string.IsNullOrWhiteSpace(a)
                       && a.Contains("Luck Blade", StringComparison.OrdinalIgnoreCase));
        }

        var hasEquippedLuckBlade = (target.Equipment.TryGetValue(EquipmentSlot.MainHand, out var main) && IsLuckBlade(main))
                                   || (target.Equipment.TryGetValue(EquipmentSlot.OffHand, out var off) && IsLuckBlade(off));

        if (hasEquippedLuckBlade)
            return 1;

        return target.Inventory != null && target.Inventory.Any(IsLuckBlade) ? 1 : 0;
    }

    private static void ApplyRingProtectionAuras(CombatSession session)
    {
        if (session?.Party == null)
            return;

        foreach (var member in session.Party)
        {
            member.RefreshRingProtectionEffects();
            member.SetRingProtectionReceivedAuraSaveBonus(0);
        }

        var strongestAura = session.Party
            .Where(m => m.CurrentHitPoints > 0 && !m.HasStatus(CharacterStatus.Dead))
            .Select(m => Math.Max(0, m.RingProtectionAuraSaveBonus))
            .DefaultIfEmpty(0)
            .Max();

        if (strongestAura <= 0)
            return;

        foreach (var member in session.Party)
            member.SetRingProtectionReceivedAuraSaveBonus(strongestAura);
    }

    private static void ApplyRingWizardryEffects(CombatSession session)
    {
        if (session?.Party == null)
            return;

        foreach (var member in session.Party)
            member.RefreshRingWizardryEffects();
    }

    private void ApplyPoisonDamageDuringCombat(CombatSession session, List<CombatEvent> events)
    {
        foreach (var member in session.Party)
        {
            if (member.IsMonkImmuneToPoison())
            {
                if (member.HasStatus(CharacterStatus.Poisoned))
                {
                    member.RemoveStatus(CharacterStatus.Poisoned);
                    events.Add(new CombatEvent($"{member.Name}'s poison is negated by monk immunity."));
                }
                continue;
            }

            if (!IsAlive(member) || !member.HasStatus(CharacterStatus.Poisoned))
                continue;

            var poisonTickRoll = _dice.Roll(100);
            if (poisonTickRoll > 50)
                continue;

            var damage = _dice.Roll(3);
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - damage);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);

            events.Add(new CombatEvent($"Poison harms {member.Name} for {actual} (rolled {damage}). HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} dies from poison!"));
            }
        }
    }

    private static void WakeMonsterIfAsleepAfterDamage(MonsterInstance monster, int actualDamage, List<CombatEvent> events)
    {
        if (actualDamage <= 0)
            return;

        if (!monster.IsAlive)
            return;

        var wokeFromAsleep = monster.HasStatus(MonsterStatus.Asleep)
            && Adnd.Core.Config.GameRulesProvider.Current.Rules != Adnd.Core.Config.Ruleset.Wizardry;
        var wokeFromUnconscious = monster.HasStatus(MonsterStatus.Unconscious);

        if (!wokeFromAsleep && !wokeFromUnconscious)
            return;

        if (wokeFromAsleep)
            monster.SetStatus(MonsterStatus.Asleep, 0);

        if (wokeFromUnconscious)
            monster.SetStatus(MonsterStatus.Unconscious, 0);

        events.Add(new CombatEvent($"{monster.DisplayName} wakes up from taking damage!"));
    }

    private static void WakeCharacterIfAsleepAfterDamage(Character member, int actualDamage, List<CombatEvent> events)
    {
        if (actualDamage <= 0)
            return;

        if (member.CurrentHitPoints <= 0)
            return;

        if (!member.HasStatus(CharacterStatus.Asleep)
            || member.Ruleset == Adnd.Core.Config.Ruleset.Wizardry)
            return;

        member.RemoveStatus(CharacterStatus.Asleep);
        events.Add(new CombatEvent($"{member.Name} wakes up from taking damage!"));
    }

    private static int GetEquippedRegeneration(Character member)
    {
        var hasRegeneration = member.Equipment.Values
            .Where(item => item != null)
            .Any(item => item!.SpecialAbilities.Any(a =>
                string.Equals(a?.Trim(), "Regeneration (1)", StringComparison.OrdinalIgnoreCase)));

        if (hasRegeneration)
            return 1;

        var hasRegenerationFromConsumable = member.Inventory
            .Any(item => ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (1)")
                         || ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (Potion)"));

        return hasRegenerationFromConsumable ? 1 : 0;
    }

    private static bool IsAlive(Character c) => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead);
}
