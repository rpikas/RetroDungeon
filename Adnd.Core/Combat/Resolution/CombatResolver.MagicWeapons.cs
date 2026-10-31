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
    private static string ResolveWeaponDamageExpression(Character member, Item? mainHand, MonsterInstance target)
    {
        if (mainHand != null && mainHand.Type == ItemType.Weapon)
        {
            if (mainHand.IsVersatileWeapon)
                return mainHand.GetDamageExpression(target.Template.Size == MonsterSize.Large);

            if (target.Template.Size == MonsterSize.Large && !string.IsNullOrWhiteSpace(mainHand.DamageVsLarge))
                return mainHand.DamageVsLarge;

            if (!string.IsNullOrWhiteSpace(mainHand.Damage))
                return mainHand.Damage;
        }

        return string.IsNullOrWhiteSpace(member.Damage) ? "1d2" : member.Damage;
    }

    private int GetDragonSlayerDamageMultiplier(Item? weapon, MonsterInstance target)
    {
        if (!IsSwordPlusTwoDragonSlayer(weapon))
            return 1;

        if (!IsTargetEligibleForDragonSlayerBonus(target))
            return 1;

        if (weapon == null)
            return 1;

        var chosenDragonType = GetOrAssignDragonSlayerType(weapon);
        return IsTargetSpecificDragonType(target, chosenDragonType) ? 3 : 1;
    }

    private int GetSwordOfDancingCurrentBonus(Item? weapon, CombatSession session)
    {
        if (!IsSwordOfDancing(weapon) || session == null)
            return 0;

        var phase = ((Math.Max(1, session.RoundNumber) - 1) % 4) + 1;
        return phase;
    }

    private static bool IsSwordOfDancing(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Dancing", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dancing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dancing", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusFourDefender(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Sword +4, Defender", StringComparison.OrdinalIgnoreCase)
               || (weapon.SpecialAbilities != null && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Defender", StringComparison.OrdinalIgnoreCase)));
    }

    private static int GetDefenderAttackBonusAllocation(Item weapon)
    {
        if (weapon.SpecialAbilities == null)
            return 4;

        var profile = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("DefenderAttackBonus:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(profile)
            && int.TryParse(profile[(profile.IndexOf(':') + 1)..].Trim(), out var parsed))
        {
            return Math.Clamp(parsed, 0, 4);
        }

        return 4;
    }

    private static int GetDefenderArmorClassBonus(Character target, Adnd.Core.Monsters.MonsterAttack attack)
    {
        if (target?.Equipment == null)
            return 0;

        if (!IsMonsterAttackFromHandheldWeapon(attack))
            return 0;

        if (!target.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand)
            || !IsSwordPlusFourDefender(mainHand))
        {
            return 0;
        }

        var attackBonusAllocation = GetDefenderAttackBonusAllocation(mainHand!);
        return Math.Max(0, 4 - attackBonusAllocation);
    }

    private static bool ShouldApplyCloakOfDisplacementAutoMiss(CombatSession session, Character target)
    {
        if (session == null || target == null)
            return false;

        if (!HasEquippedCloakOfDisplacement(target))
            return false;

        return session.CloakOfDisplacementFirstMissUsed.Add(target.Name);
    }

    private static bool HasEquippedCloakOfDisplacement(Character target)
    {
        if (target?.Equipment == null)
            return false;

        if (!target.Equipment.TryGetValue(EquipmentSlot.Back, out var back)
            || back == null)
        {
            return false;
        }

        if (string.Equals(back.Name, "Cloak of Displacement", StringComparison.OrdinalIgnoreCase))
            return true;

        return ItemSpecialAbilityParser.HasSpecialAbility(back, "Displacement");
    }

    private static bool IsMonsterAttackFromHandheldWeapon(Adnd.Core.Monsters.MonsterAttack attack)
    {
        var name = (attack?.Name ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("hurled") || name.Contains("thrown") || name.Contains("missile"))
            return false;

        return name.Contains("sword")
               || name.Contains("dagger")
               || name.Contains("mace")
               || name.Contains("spear")
               || name.Contains("axe")
               || name.Contains("club")
               || name.Contains("hammer")
               || name.Contains("staff")
               || name.Contains("flail")
               || name.Contains("halberd")
               || name.Contains("morning star")
               || name.Contains("trident");
    }

    private static bool IsSwordPlusFiveHolyAvenger(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Sword +5, Holy Avenger", StringComparison.OrdinalIgnoreCase)
               || (weapon.SpecialAbilities != null && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Holy Avenger", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsTargetChaoticEvil(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        return string.Equals(target.Template.Alignment?.Trim(), "Chaotic Evil", StringComparison.OrdinalIgnoreCase)
               || string.Equals(target.Template.Alignment?.Trim(), "CE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaladinHolyAvengerAuraActive(Character member, Item? mainHand)
    {
        return member != null
               && member.IsPaladin()
               && IsSwordPlusFiveHolyAvenger(mainHand)
               && member.CurrentHitPoints > 0
               && !member.HasStatus(CharacterStatus.Dead);
    }

    private static bool IsWithinHolyAvengerAura(Character paladin, Character target)
    {
        // Current combat model has no tactical map distance; treat active allies as within 5' aura.
        return paladin != null
               && target != null
               && target.CurrentHitPoints > 0
               && !target.HasStatus(CharacterStatus.Dead);
    }

    private bool TrySuppressMonsterSpellByHolyAvengerAura(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (session?.Party == null || monster?.Template == null)
            return false;

        foreach (var member in session.Party)
        {
            if (member?.Equipment == null)
                continue;

            member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand);
            if (!IsPaladinHolyAvengerAuraActive(member, mainHand))
                continue;

            var chance = 50;
            var roll = _rng.Next(1, 101);
            if (roll <= chance)
            {
                events.Add(new CombatEvent($"{member.Name}'s Holy Avenger aura disrupts {monster.DisplayName}'s spellcasting (MR {chance}%, roll {roll})."));
                return true;
            }
        }

        return false;
    }

    private void ApplyHolyAvengerPartyAuras(CombatSession session)
    {
        // Character currently has no persisted magic-resistance stat.
        // Holy Avenger party aura is handled dynamically via spell suppression checks.
    }

    private bool TryExtinguishFireWithFrostBrand(Character member, CombatSession session, List<CombatEvent> events)
    {
        if (member?.Equipment == null)
            return false;

        if (!member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand)
            || !IsSwordPlusThreeFrostBrand(mainHand))
        {
            return false;
        }

        var roll = _dice.Roll(100);
        if (roll > 50)
            return false;

        session.SetPartyVeryHotFireExposure(member.Name, 0, 0);
        events.Add(new CombatEvent($"{member.Name} thrusts Frost Brand into the flames and extinguishes nearby fire (50% chance)."));
        return true;
    }

    private string GetOrAssignDragonSlayerType(Item weapon)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var existing = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("DragonSlayerType:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing))
            return existing[(existing.IndexOf(':') + 1)..].Trim();

        var rolledType = _dice.Roll(10) switch
        {
            1 => "Black",
            2 => "Blue",
            3 => "Brass",
            4 => "Bronze",
            5 => "Copper",
            6 => "Gold",
            7 => "Green",
            8 => "Red",
            9 => "Silver",
            _ => "White"
        };

        weapon.SpecialAbilities.Add($"DragonSlayerType:{rolledType}");
        return rolledType;
    }

    private static bool IsTargetSpecificDragonType(MonsterInstance target, string dragonType)
    {
        if (target?.Template == null || string.IsNullOrWhiteSpace(dragonType))
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        var type = dragonType.ToLowerInvariant();

        return name.Contains("dragon") && name.Contains(type);
    }

    private static bool IsHalfDamageFromSharpWeapons(MonsterInstance target, Item? mainHand)
    {
        if (mainHand == null || mainHand.Type != ItemType.Weapon)
            return false;

        var damageType = mainHand.DamageType?.Trim();
        if (string.IsNullOrWhiteSpace(damageType))
            return false;

        var isSharp = damageType.Equals("Piercing", StringComparison.OrdinalIgnoreCase)
            || damageType.Equals("Slashing", StringComparison.OrdinalIgnoreCase);

        if (!isSharp)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "Half Damage from Sharp Weapons", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSlashingAttack(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        return string.Equals(weapon.DamageType?.Trim(), "Slashing", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSwordPlusOnePlusTwoVsMagicUsingAndEnchantedCreatures(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Replace('&', ' ').ToLowerInvariant();
        if (normalized.Contains("sword +1, +2 vs. magic-using", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +2 vs magic-using", StringComparison.Ordinal)
            || normalized.Contains("sword +1, +2 vs. magic using", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+2 vs. magic-using and enchanted creatures", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOnePlusThreeVsLycanthropesAndShapeChangers(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Replace('&', ' ').ToLowerInvariant();
        if (normalized.Contains("sword +1, +3 vs. lycanthropes", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +3 vs lycanthropes", StringComparison.Ordinal)
            || normalized.Contains("sword +1, +3 vs. lycanthropes shape changers", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+3 vs. lycanthropes and shape changers", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOnePlusThreeVsRegeneratingCreatures(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +1, +3 vs. regenerating creatures", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +3 vs regenerating creatures", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+3 vs. regenerating creatures", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOnePlusFourVsReptiles(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +1, +4 vs. reptiles", StringComparison.Ordinal)
            || normalized.Contains("sword +1 +4 vs reptiles", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("+4 vs. reptiles", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusOneFlameTongue(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Flame Tongue", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Flame Tongue", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusTwoGiantSlayer(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +2, giant slayer", StringComparison.Ordinal)
            || normalized.Contains("sword +2 giant slayer", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Giant Slayer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordPlusTwoDragonSlayer(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +2, dragon slayer", StringComparison.Ordinal)
            || normalized.Contains("sword +2 dragon slayer", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dragon Slayer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDwarvenThrower(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Dwarven Thrower", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dwarven Thrower", StringComparison.OrdinalIgnoreCase));
    }

    private void TryApplyMaceOfDisruptionEvilBacklash(CombatSession session, Character member, List<CombatEvent> events)
    {
        if (session.MaceOfDisruptionBacklashApplied.Contains(member.Name))
            return;

        if (!IsCharacterEvil(member.Alignment))
            return;

        var touching = member.Equipment.Values.Any(IsMaceOfDisruption)
                       || member.Inventory.Any(IsMaceOfDisruption);
        if (!touching)
            return;

        session.MaceOfDisruptionBacklashApplied.Add(member.Name);

        const int damage = 100;
        var before = member.CurrentHitPoints;
        member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - damage);
        var actual = before - member.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(member, actual, events);

        events.Add(new CombatEvent($"{member.Name} is blasted by Mace of Disruption's holy backlash for {actual} damage."));
        if (member.CurrentHitPoints <= 0)
        {
            member.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{member.Name} is slain by Mace of Disruption backlash."));
        }
    }

    private bool TryResolveMaceOfDisruptionEffect(Character wielder, Item? weapon, MonsterInstance target, bool useRanged, List<CombatEvent> events)
    {
        if (wielder == null || target?.Template == null || weapon == null)
            return false;

        if (useRanged || !IsMaceOfDisruption(weapon) || !IsMaceOfDisruptionAffectedTarget(target))
            return false;

        var saveChance = GetMaceOfDisruptionSaveChancePercent(target);
        var roll = _dice.Roll(100);
        if (roll > saveChance)
        {
            target.CurrentHitPoints = 0;
            events.Add(new CombatEvent($"Mace of Disruption disrupts {target.DisplayName} utterly ({roll} vs save {saveChance}%)."));
            return true;
        }

        events.Add(new CombatEvent($"{target.DisplayName} resists disruption ({roll} vs save {saveChance}%)."));
        return false;
    }

    private static bool IsMaceOfDisruption(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Mace of Disruption", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Mace of Disruption", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetMaceOfDisruptionDamageMultiplier(Item? weapon, MonsterInstance target)
    {
        return IsMaceOfDisruption(weapon) && IsMaceOfDisruptionAffectedTarget(target)
            ? 2
            : 1;
    }

    private static bool IsMaceOfDisruptionAffectedTarget(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Undead)
            return true;

        if (target.InstanceMonsterType is MonsterType.Demon or MonsterType.Devil)
            return true;

        return IsTargetEvilAlignment(target.Template.Alignment);
    }

    private static int GetMaceOfDisruptionSaveChancePercent(MonsterInstance target)
    {
        if (target?.Template == null)
            return 95;

        var n = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("skeleton")
            || n.Contains("zombie")
            || n.Contains("ghoul")
            || n.Contains("shadow")
            || n.Contains("wight")
            || n.Contains("wraith"))
            return 0;
        if (n.Contains("mummy")) return 20;
        if (n.Contains("spectre") || n.Contains("specter")) return 35;
        if (n.Contains("vampire")) return 50;
        if (n.Contains("ghost")) return 65;
        if (n.Contains("lich")) return 60;
        return 95;
    }

    private static bool IsCharacterEvil(Alignment alignment)
    {
        return alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil;
    }

    private static bool IsTargetEvilAlignment(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return false;

        var text = alignment.Trim();
        return text.Equals("Lawful Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Neutral Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Chaotic Evil", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHammerOfThunderbolts(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Hammer of Thunderbolts", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Hammer of Thunderbolts", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsJavelinOfLightning(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Javelin of Lightning", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Javelin of Lightning", StringComparison.OrdinalIgnoreCase));
    }

    private void ResolveJavelinOfLightningStrike(Character thrower, Item weapon, MonsterInstance impactTarget, CombatSession session, List<CombatEvent> events)
    {
        if (!IsJavelinOfLightning(weapon) || impactTarget?.Template == null)
            return;

        var baseName = Regex.Replace(weapon.Name ?? "Javelin of Lightning", @"\s*\(\d+\)\s*$", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Javelin of Lightning";

        var primaryBefore = impactTarget.CurrentHitPoints;
        impactTarget.CurrentHitPoints = Math.Max(0, impactTarget.CurrentHitPoints - 20);
        var primaryLightningDamage = primaryBefore - impactTarget.CurrentHitPoints;
        if (primaryLightningDamage > 0)
        {
            events.Add(new CombatEvent($"{thrower.Name}'s Javelin of Lightning discharges for 20 electrical damage on {impactTarget.DisplayName}. HP {primaryBefore}->{impactTarget.CurrentHitPoints}."));
            WakeMonsterIfAsleepAfterDamage(impactTarget, primaryLightningDamage, events);
            MonsterSplitResolver.TryResolveSplit(
                session,
                impactTarget,
                primaryLightningDamage,
                JellySplitTrigger.Lightning,
                message => events.Add(new CombatEvent(message)));
        }

        var backStrokeTargets = session.GetAliveMonstersByGroup(impactTarget.GroupId)
            .Where(m => !ReferenceEquals(m, impactTarget))
            .ToList();

        foreach (var secondary in backStrokeTargets)
        {
            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(secondary, 20);
            var saveRoll = _dice.Roll(20);
            var boltDamage = saveRoll >= saveTarget ? 10 : 20;

            var beforeSecondary = secondary.CurrentHitPoints;
            secondary.CurrentHitPoints = Math.Max(0, secondary.CurrentHitPoints - boltDamage);
            var actualSecondary = beforeSecondary - secondary.CurrentHitPoints;

            if (actualSecondary <= 0)
                continue;

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"Backstroke lightning grazes {secondary.DisplayName}: save {saveRoll} vs {saveTarget}, takes 10 electrical damage. HP {beforeSecondary}->{secondary.CurrentHitPoints}."
                : $"Backstroke lightning rakes {secondary.DisplayName}: save {saveRoll} vs {saveTarget}, takes 20 electrical damage. HP {beforeSecondary}->{secondary.CurrentHitPoints}."));

            WakeMonsterIfAsleepAfterDamage(secondary, actualSecondary, events);
            var splitFromLightning = MonsterSplitResolver.TryResolveSplit(
                session,
                secondary,
                actualSecondary,
                JellySplitTrigger.Lightning,
                message => events.Add(new CombatEvent(message)));
            if (!splitFromLightning && secondary.CurrentHitPoints <= 0)
                events.Add(new CombatEvent($"{secondary.DisplayName} is destroyed by the lightning backstroke."));
        }

        weapon.Quantity = Math.Max(0, weapon.Quantity - 1);
        if (weapon.Quantity <= 0)
        {
            if (thrower.Equipment.TryGetValue(EquipmentSlot.Range, out var equippedRange)
                && ReferenceEquals(equippedRange, weapon))
            {
                thrower.Equipment[EquipmentSlot.Range] = null;
            }

            thrower.Inventory.RemoveAll(item => ReferenceEquals(item, weapon) || (IsJavelinOfLightning(item) && item.Quantity <= 0));
            events.Add(new CombatEvent($"{thrower.Name}'s Javelin of Lightning is consumed in the discharge."));
        }
        else
        {
            weapon.Name = $"{baseName} ({weapon.Quantity})";
            events.Add(new CombatEvent($"{thrower.Name}'s Javelin of Lightning crackles out. {weapon.Quantity} remaining."));
        }
    }

    private static bool MeetsHammerOfThunderboltsBaselineStrength(Character wielder)
    {
        if (wielder == null)
            return false;

        if (wielder.Abilities.Strength > 18)
            return true;

        return wielder.Abilities.Strength == 18
               && (wielder.ExceptionalStrengthPercentile ?? 0) >= 1;
    }

    private static bool IsGauntletsOfOgrePower(Item? item)
    {
        var name = item?.Name?.Trim() ?? string.Empty;
        return name.Contains("Gauntlets of Ogre Power", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGirdleOfGiantStrength(Item? item)
    {
        var name = item?.Name?.Trim() ?? string.Empty;
        return name.Contains("Girdle of Giant Strength", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHammerOfThunderboltsTrueName(Item? weapon)
    {
        if (weapon?.SpecialAbilities == null)
            return false;

        return weapon.SpecialAbilities.Any(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.Contains("TrueNameKnown", StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanWieldHammerOfThunderboltsAsPlusFive(Character wielder, Item? weapon)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return false;

        if (!HasHammerOfThunderboltsTrueName(weapon))
            return false;

        var hasGauntlets = wielder.Equipment.TryGetValue(EquipmentSlot.Hands, out var hands)
                           && IsGauntletsOfOgrePower(hands);
        var hasGirdle = wielder.Equipment.TryGetValue(EquipmentSlot.Belt, out var belt)
                        && IsGirdleOfGiantStrength(belt);

        return hasGauntlets && hasGirdle;
    }

    private static int GetHammerOfThunderboltsToHitBonus(Character wielder, Item? weapon)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return 0;

        if (CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon))
            return 5;

        return MeetsHammerOfThunderboltsBaselineStrength(wielder) ? 3 : 0;
    }

    private static int GetHammerOfThunderboltsDamageBonus(Character wielder, Item? weapon, bool useRanged)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return 0;

        if (!useRanged && !MeetsHammerOfThunderboltsBaselineStrength(wielder) && !CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon))
            return 0;

        if (CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon))
            return 5;

        return MeetsHammerOfThunderboltsBaselineStrength(wielder) ? 3 : 0;
    }

    private static int GetHammerOfThunderboltsDamageDiceMultiplier(Character wielder, Item? weapon)
    {
        if (!IsHammerOfThunderbolts(weapon) || wielder == null)
            return 1;

        return MeetsHammerOfThunderboltsBaselineStrength(wielder) || CanWieldHammerOfThunderboltsAsPlusFive(wielder, weapon)
            ? 2
            : 1;
    }

    private static bool IsTargetGiantOrKinForHammerOfThunderbolts(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        return name.Contains("giant")
               || name.Contains("ogre")
               || name.Contains("ogre mage")
               || name.Contains("troll")
               || name.Contains("ettin");
    }

    private static bool CanUseDwarvenThrowerFullPower(Character wielder, Item? weapon)
    {
        if (!IsDwarvenThrower(weapon) || wielder == null)
            return false;

        return wielder.Race == Race.Dwarf
               && (wielder.Classes.Contains(CharacterClass.Fighter)
                   || wielder.Class == CharacterClass.Fighter);
    }

    private static int GetDwarvenThrowerDamageMultiplier(Character wielder, Item? weapon, MonsterInstance target, bool useRanged)
    {
        if (!useRanged || !CanUseDwarvenThrowerFullPower(wielder, weapon) || target?.Template == null)
            return 1;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("giant")
            || name.Contains("ogre")
            || name.Contains("ogre mage")
            || name.Contains("troll")
            || name.Contains("ettin"))
        {
            return 3;
        }

        return 2;
    }

    private static bool IsSwordPlusTwoNineLivesStealer(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("sword +2, nine lives stealer", StringComparison.Ordinal)
            || normalized.Contains("sword +2 nine lives stealer", StringComparison.Ordinal))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Nine Lives Stealer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordOfWounding(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Wounding", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Wounding", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordOfLifeStealing(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Life Stealing", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Life Stealing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Life Stealing", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordOfSharpness(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Sword of Sharpness", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Sharpness", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSwordVorpalWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Vorpal", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Vorpal", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveSwordOfSharpnessSeverEffect(Item? weapon, MonsterInstance target, int naturalRoll, List<CombatEvent> events)
    {
        if (!IsSwordOfSharpness(weapon) || target?.Template == null)
            return false;

        // AD&D note: modified sever score considers only sword bonus (+1 for Sword of Sharpness).
        var severScore = naturalRoll + 1;
        var needed = GetSwordOfSharpnessSeverScoreNeeded(target);
        if (severScore < needed)
            return false;

        var locationRoll = Random.Shared.Next(1, 7);
        var location = locationRoll switch
        {
            1 => "arm",
            2 => "leg",
            3 => "tail",
            4 => "tentacle",
            5 => "neck",
            _ => "other extremity"
        };

        if (string.Equals(location, "neck", StringComparison.OrdinalIgnoreCase))
        {
            target.CurrentHitPoints = 0;
            events.Add(new CombatEvent($"Sword of Sharpness severs {target.DisplayName}'s neck! {target.DisplayName} is slain instantly."));
            return true;
        }

        var before = target.CurrentHitPoints;
        var extra = Math.Max(4, Math.Max(1, target.MaxHitPoints / 4));
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - extra);
        var applied = before - target.CurrentHitPoints;
        events.Add(new CombatEvent($"Sword of Sharpness severs {target.DisplayName}'s {location} (score {severScore} vs {needed}), causing {applied} additional damage."));
        return true;
    }

    private static bool TryResolveSwordVorpalSeverEffect(Item? weapon, MonsterInstance target, int naturalRoll, List<CombatEvent> events)
    {
        if (!IsSwordVorpalWeapon(weapon) || target?.Template == null)
            return false;

        // AD&D note: modified sever score considers only sword bonus (+3 for Vorpal).
        var severScore = naturalRoll + 3;
        var needed = GetSwordVorpalSeverScoreNeeded(target);
        if (severScore < needed)
            return false;

        if (!CanTargetBeDecapitated(target))
        {
            events.Add(new CombatEvent($"Vorpal strike lands on {target.DisplayName} (score {severScore} vs {needed}), but decapitation is impossible due to form."));
            return true;
        }

        if (SurvivesDecapitation(target))
        {
            var before = target.CurrentHitPoints;
            var extra = Math.Max(6, Math.Max(1, target.MaxHitPoints / 3));
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - extra);
            var applied = before - target.CurrentHitPoints;
            events.Add(new CombatEvent($"Vorpal strike severs {target.DisplayName}'s head, but it survives; extra damage {applied}."));
            return true;
        }

        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"Vorpal strike severs {target.DisplayName}'s head! {target.DisplayName} is slain instantly."));
        return true;
    }

    private static int GetSwordOfSharpnessSeverScoreNeeded(MonsterInstance target)
    {
        if (target?.Template == null)
            return 99;

        if (IsSolidMetalOrStoneTarget(target))
            return 21;

        return target.Template.Size == MonsterSize.Large ? 20 : 19;
    }

    private static int GetSwordVorpalSeverScoreNeeded(MonsterInstance target)
    {
        if (target?.Template == null)
            return 99;

        if (IsSolidMetalOrStoneTarget(target))
            return 22;

        return target.Template.Size == MonsterSize.Large ? 21 : 20;
    }

    private static bool IsSolidMetalOrStoneTarget(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("stone")
               || composite.Contains("rock")
               || composite.Contains("metal")
               || composite.Contains("iron")
               || composite.Contains("steel")
               || composite.Contains("golem");
    }

    private static bool CanTargetBeDecapitated(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        if (composite.Contains("ooze")
            || composite.Contains("slime")
            || composite.Contains("jelly")
            || composite.Contains("amorph")
            || composite.Contains("formless")
            || composite.Contains("gaseous")
            || composite.Contains("swarm")
            || composite.Contains("elemental"))
        {
            return false;
        }

        if (!composite.Contains("head")
            && !composite.Contains("neck")
            && (target.InstanceMonsterType == MonsterType.Ooze || target.InstanceMonsterType == MonsterType.Elemental))
        {
            return false;
        }

        return true;
    }

    private static bool SurvivesDecapitation(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("doppleganger")
               || composite.Contains("elemental")
               || composite.Contains("golem");
    }

    private static bool IsBluntWeapon(Item weapon)
    {
        if (!string.IsNullOrWhiteSpace(weapon.DamageType))
            return weapon.DamageType.Contains("blunt", StringComparison.OrdinalIgnoreCase);

        var name = weapon.Name.ToLowerInvariant();
        return new[] { "mace", "staff", "club", "hammer", "flail", "morning star", "bo stick", "jo stick" }
            .Any(k => name.Contains(k, StringComparison.Ordinal));
    }

    private static bool CanWeaponHarmTargetByMagicRequirement(Item? weapon, MonsterInstance target)
    {
        if (HasSpecialDefense(target, "Immune to Weapons"))
            return false;

        if (SpellDamageSaveHelper.IsAffectedOnlyByMagicalCold(target))
            return false;

        if (HasSpecialDefense(target, "Rakshasa Weapon Defense"))
            return GetEffectiveWeaponBonusForHitRequirement(weapon) >= 1;

        if (HasSpecialDefense(target, "Blunt Weapon Immunity")
            && (weapon == null || IsBluntWeapon(weapon))
            && GetEffectiveWeaponBonusForHitRequirement(weapon) < 1)
        {
            return false;
        }

        if (HasSpecialDefense(target, "Weapon Immunity")
            && target.Template.SpecialDefenses.Any(d => $"{d.Name} {d.Description}".Contains("silver, iron", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsSilverWeapon(weapon) || IsIronWeapon(weapon))
                return true;

            return GetEffectiveWeaponBonusForHitRequirement(weapon) >= 3;
        }

        if (HasSpecialDefense(target, "Iron or Magic Weapons Required"))
            return IsIronWeapon(weapon) || GetEffectiveWeaponBonusForHitRequirement(weapon) >= 1;

        if (HasMagicOrColdIronWeaponsRequirement(target))
        {
            if (IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(target, weapon))
                return true;

            if (IsColdIronWeapon(weapon))
                return true;

            var effectiveBonusForColdIronOrMagicRequirement = GetEffectiveWeaponBonusForHitRequirement(weapon);
            return effectiveBonusForColdIronOrMagicRequirement >= 1;
        }

        if (HasImmunityToNormalWeapons(target))
        {
            if (IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(target, weapon))
                return true;

            if (IsSilverWeapon(weapon))
                return true;

            var effectiveBonusForNormalWeaponImmunity = GetEffectiveWeaponBonusForHitRequirement(weapon);
            return effectiveBonusForNormalWeaponImmunity >= 1;
        }

        var requiredBonus = GetRequiredWeaponBonusToHit(target);
        if (requiredBonus <= 0)
            return true;

        var effectiveWeaponBonus = Math.Max(GetEffectiveWeaponBonusForHitRequirement(weapon),
            Math.Max(0, weapon?.ToHitBonus ?? 0) + GetSituationalSwordBonusAgainstTarget(weapon, target));
        return effectiveWeaponBonus >= requiredBonus;
    }

    private static bool HasMagicOrColdIronWeaponsRequirement(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Magic or Cold Iron Weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Magic or Cold Iron Weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Only harmed by magic or cold iron weapons.", StringComparison.OrdinalIgnoreCase)
                   || (merged.Contains("only harmed by magic", StringComparison.Ordinal)
                       && merged.Contains("cold iron", StringComparison.Ordinal));
        });
    }

    private static bool CanMonsterHarmLycanthropeForm(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        if (monster.Template.Name?.Contains("silver", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        var text = string.Join(" ",
            new[]
            {
                monster.Template.Name,
                monster.Template.TypeName,
                string.Join(" ", monster.Template.SpecialAbilities.Select(a => $"{a.Name} {a.Description}")),
                string.Join(" ", monster.Template.SpecialAttacks.Select(a => $"{a.Name} {a.Description}")),
                string.Join(" ", monster.Template.SpecialDefenses.Select(a => $"{a.Name} {a.Description}"))
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return text.Contains("magic weapon", StringComparison.OrdinalIgnoreCase)
               || text.Contains("+1", StringComparison.OrdinalIgnoreCase)
               || text.Contains("+2", StringComparison.OrdinalIgnoreCase)
               || text.Contains("+3", StringComparison.OrdinalIgnoreCase)
               || text.Contains("enchanted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasImmunityToNormalWeapons(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var name = d.Name?.Trim() ?? string.Empty;
            var description = d.Description?.Trim() ?? string.Empty;
            var merged = $"{name} {description}".ToLowerInvariant();

            return string.Equals(name, "Hit only by magic weapons", StringComparison.OrdinalIgnoreCase)
                   || merged.Contains("silver or magic weapons required", StringComparison.Ordinal)
                   || merged.Contains("silver or magical weapons", StringComparison.Ordinal)
                   || string.Equals(description, "Hit only by magic weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "Hit Only by Silver or Magic Weapons", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(description, "Hit Only by Silver or Magic Weapons", StringComparison.OrdinalIgnoreCase)
                   || merged.Contains("hit only by magic weapon", StringComparison.Ordinal)
                   || merged.Contains("hit only by silver or magic weapon", StringComparison.Ordinal)
                   || merged.Contains("only be harmed by magic weapon", StringComparison.Ordinal)
                   || (merged.Contains("can only be hit by silver", StringComparison.Ordinal)
                       && merged.Contains("+1", StringComparison.Ordinal));
        });
    }

    private static bool HasMagicWeaponsOnlyHalfDamageDefense(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
        {
            var merged = $"{d.Name} {d.Description}".ToLowerInvariant();
            return merged.Contains("magic weapons only", StringComparison.Ordinal)
                   && merged.Contains("half damage", StringComparison.Ordinal)
                   && merged.Contains("magical weapon", StringComparison.Ordinal);
        });
    }

    private bool TryResolveMummyImprovisedWeaponDamage(CombatSession session, Character attacker, MonsterInstance target, Item? weapon, List<CombatEvent> events)
    {
        if (!IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(target, weapon))
            return false;

        var before = target.CurrentHitPoints;
        var damage = 0;

        if (IsHolyWaterWeapon(weapon))
        {
            damage = _dice.Roll(4) + _dice.Roll(4);
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            events.Add(new CombatEvent($"{attacker.Name} splashes holy water on {target.DisplayName} for {damage} damage (2d4). HP {before}->{target.CurrentHitPoints}."));
        }
        else if (IsBurningOilWeapon(weapon))
        {
            var firstRoundDamage = _dice.Roll(8);
            var secondRoundDamage = firstRoundDamage * 2;
            damage = firstRoundDamage;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - firstRoundDamage);
            session.SetMummyBurningOilSecondRoundDamage(target, secondRoundDamage);
            events.Add(new CombatEvent($"{attacker.Name} splashes burning oil on {target.DisplayName} for {firstRoundDamage} damage this round and {secondRoundDamage} damage next round. HP {before}->{target.CurrentHitPoints}."));
        }
        else if (IsTorchWeapon(weapon))
        {
            damage = _dice.Roll(3);
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            events.Add(new CombatEvent($"{attacker.Name} strikes {target.DisplayName} with a torch for {damage} damage (1d3). HP {before}->{target.CurrentHitPoints}."));
        }

        var actual = Math.Max(0, before - target.CurrentHitPoints);
        WakeMonsterIfAsleepAfterDamage(target, actual, events);

        if (target.CurrentHitPoints <= 0)
            events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));

        return true;
    }

    private static bool TryApplyMummyBurningOilSecondRoundDamage(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var pendingDamage = session.ConsumeMummyBurningOilSecondRoundDamage(monster);
        if (pendingDamage <= 0)
            return false;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - pendingDamage);
        var actual = Math.Max(0, before - monster.CurrentHitPoints);

        if (actual > 0)
            WakeMonsterIfAsleepAfterDamage(monster, actual, events);

        events.Add(new CombatEvent($"Burning oil continues to burn {monster.DisplayName} for {actual} damage in the second round. HP {before}->{monster.CurrentHitPoints}."));
        if (!monster.IsAlive)
            events.Add(new CombatEvent($"{monster.DisplayName} is destroyed by burning oil."));

        return !monster.IsAlive;
    }

    private static bool IsMummyAndVulnerableToImprovisedAntiUndeadWeapon(MonsterInstance target, Item? weapon)
    {
        if (SpellDamageSaveHelper.IsWraith(target))
            return IsHolyWaterWeapon(weapon);

        return SpellDamageSaveHelper.IsMummy(target)
               && (IsTorchWeapon(weapon) || IsBurningOilWeapon(weapon) || IsHolyWaterWeapon(weapon));
    }

    private static bool IsTorchWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        return name.Contains("torch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBurningOilWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        return name.Contains("burning oil", StringComparison.OrdinalIgnoreCase)
               || name.Contains("flask of oil", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHolyWaterWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        return name.Contains("holy water", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSpecialDefense(MonsterInstance target, string defenseName)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d?.Name?.Trim(), defenseName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasOnlyAffectedByFireBasedAttacksDefense(MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
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

    private static bool IsFireBasedWeaponAttack(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        return text.Contains("fire", StringComparison.OrdinalIgnoreCase)
               || text.Contains("flame", StringComparison.OrdinalIgnoreCase)
               || text.Contains("burn", StringComparison.OrdinalIgnoreCase)
               || text.Contains("torch", StringComparison.OrdinalIgnoreCase)
               || text.Contains("oil", StringComparison.OrdinalIgnoreCase)
               || text.Contains("incendiary", StringComparison.OrdinalIgnoreCase);
    }
}
