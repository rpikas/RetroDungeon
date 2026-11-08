using System;
using System.Linq;
using Adnd.Core.Items;

namespace Adnd.Core.Characters;

public static class EquipmentManager
{
    private static bool IsRingOfProtection(Item? it)
        => it != null && string.Equals(it.Name, "Ring of Protection", StringComparison.OrdinalIgnoreCase);

    private static bool IsRingOfInvisibility(Item? it)
        => it != null && string.Equals(it.Name, "Ring of Invisibility", StringComparison.OrdinalIgnoreCase);

    private static int GetEffectiveArmorClassBonusForEquip(Item? item)
    {
        if (item == null)
            return 0;

        // Ring of Protection AC is resolved dynamically from profile rules on Character,
        // not from static item ArmorClassBonus.
        if (IsRingOfProtection(item))
            return 0;

        return item.ArmorClassBonus;
    }

    private static void EnsureEquipmentSlots(Character c)
    {
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (!c.Equipment.ContainsKey(slot))
                c.Equipment[slot] = null;
        }
    }

    private static bool IsAllowedOffHandWeapon(Item item)
    {
        if (item == null || item.Type != ItemType.Weapon)
            return false;

        var name = item.Name ?? string.Empty;
        return name.Contains("Dagger", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Hand Axe", StringComparison.OrdinalIgnoreCase)
               || name.Contains("HandAxe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Items with the "Evil Only" ability can only be equipped by evil-aligned characters.</summary>
    public static bool MeetsAlignmentRequirement(Character c, Item item)
    {
        if (item?.SpecialAbilities == null
            || !item.SpecialAbilities.Any(a => string.Equals(a?.Trim(), "Evil Only", StringComparison.OrdinalIgnoreCase)))
            return true;

        return c.Alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil;
    }

    /// <summary>HP lost per step and per combat round from equipped items with a negative "Regeneration -N" ability (Deadly Ring).</summary>
    public static int GetEquippedLifeDrain(Character c)
    {
        var drain = 0;
        foreach (var item in c.Equipment.Values)
        {
            if (item?.SpecialAbilities == null)
                continue;

            foreach (var ability in item.SpecialAbilities)
            {
                var m = System.Text.RegularExpressions.Regex.Match(ability ?? string.Empty, @"^\s*Regeneration\s*-\s*(\d+)\s*$");
                if (m.Success)
                    drain += int.Parse(m.Groups[1].Value);
            }
        }

        return drain;
    }

    /// <summary>Applies the equipped life drain to <paramref name="c"/>. Returns the HP actually lost.</summary>
    public static int ApplyEquippedLifeDrain(Character c)
    {
        if (c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead) || c.HasStatus(CharacterStatus.Ashes) || c.HasStatus(CharacterStatus.Lost))
            return 0;

        var drain = GetEquippedLifeDrain(c);
        if (drain <= 0)
            return 0;

        var before = c.CurrentHitPoints;
        c.CurrentHitPoints = Math.Max(0, before - drain);
        if (c.CurrentHitPoints <= 0)
            c.AddStatus(CharacterStatus.Dead);
        return before - c.CurrentHitPoints;
    }

    /// <summary>Remove Curse on a character: lifts every curse on the character's equipped items. Returns the items freed.</summary>
    public static List<Item> RemoveCursesFromEquipment(Character c)
    {
        var freed = new List<Item>();
        foreach (var item in c.Equipment.Values.Where(i => i != null && i.IsCursed).Distinct())
        {
            if (RemoveCurse(item!))
                freed.Add(item!);
        }

        return freed;
    }

    /// <summary>Lifts the curse on one item, so it can be taken off again.</summary>
    public static bool RemoveCurse(Item item)
    {
        if (!item.IsCursed)
            return false;

        item.IsCursed = false;
        return true;
    }

    // Wizardry has no ranged weapons, ammo, boots, belts, cloaks or second ring, so those slots do not exist there.
    public static bool IsSlotAvailable(EquipmentSlot slot) =>
        !(slot is EquipmentSlot.Range or EquipmentSlot.Ammo or EquipmentSlot.Feet or EquipmentSlot.Belt
              or EquipmentSlot.Back or EquipmentSlot.Ring2
          && Adnd.Core.Config.GameRulesProvider.Current.Rules == Adnd.Core.Config.Ruleset.Wizardry);

    /// <summary>Wizardry characters follow WizardryItems.json's class letters; everyone else the item's AllowedClasses (empty = anyone).</summary>
    public static bool MeetsClassRequirement(Character c, Item item)
    {
        if (c.Ruleset == Adnd.Core.Config.Ruleset.Wizardry && c.WizardryClass is { } wizardryClass
            && Adnd.Core.Wizardry.WizardryItemClasses.Allows(wizardryClass, item.Name) is { } allowed)
        {
            return allowed;
        }

        if (item.AllowedClasses == null || item.AllowedClasses.Count == 0)
            return true;

        return c.Classes != null && c.Classes.Any(cls => item.AllowedClasses.Contains(cls));
    }

    public static bool Equip(Character c, Item item)
    {
        EnsureEquipmentSlots(c);

        if (item.Slot == null)
            return false;

        if (!IsSlotAvailable(item.Slot.Value))
            return false;

        if (!MeetsAlignmentRequirement(c, item))
            return false;

        if (!MeetsClassRequirement(c, item))
            return false;

        var slot = item.Slot.Value;
        var isHandSlot = slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand;

        // A cursed item stays on until Remove Curse is cast.
        if (c.Equipment[slot]?.IsCursed == true)
            return false;

        if (isHandSlot
            && (c.Equipment[EquipmentSlot.MainHand]?.IsTwoHanded == true
                || c.Equipment[EquipmentSlot.OffHand]?.IsTwoHanded == true
                || (item.IsTwoHanded
                    && (c.Equipment[EquipmentSlot.MainHand] != null
                        || c.Equipment[EquipmentSlot.OffHand] != null))))
        {
            return false;
        }

        if (slot == EquipmentSlot.OffHand
            && item.Type == ItemType.Weapon
            && !IsAllowedOffHandWeapon(item))
        {
            return false;
        }

        var movedItems = new System.Collections.Generic.HashSet<Item>();

        if (isHandSlot && item.IsTwoHanded)
        {
            // Two-handed weapon occupies both hands.
            c.Equipment[EquipmentSlot.MainHand] = item;
            c.Equipment[EquipmentSlot.OffHand] = item;
        }
        else
        {
            UnequipSlotToInventory(c, slot, movedItems);
            c.Equipment[slot] = item;
        }

        c.ArmorClass -= GetEffectiveArmorClassBonusForEquip(item);
        c.Inventory.Remove(item);

        c.RefreshRingProtectionEffects();
        c.RefreshRingWizardryEffects();

        RecalculateDamage(c);
        c.RefreshMoveFromArmorAndClass();
        c.RefreshMonkProgressionStats();

        return true;
    }

    public static bool Unequip(Character c, EquipmentSlot slot)
    {
        EnsureEquipmentSlots(c);

        var equipped = c.Equipment[slot];
        if (equipped == null)
            return false;

        if (equipped.IsCursed)
            return false;

        var movedItems = new System.Collections.Generic.HashSet<Item>();
        if ((slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand) && equipped.IsTwoHanded)
        {
            // Clearing either hand for a two-handed weapon clears both.
            UnequipSlotToInventory(c, EquipmentSlot.MainHand, movedItems);
            UnequipSlotToInventory(c, EquipmentSlot.OffHand, movedItems);
        }
        else
        {
            UnequipSlotToInventory(c, slot, movedItems);
        }

        c.RefreshRingProtectionEffects();
        c.RefreshRingWizardryEffects();

        RecalculateDamage(c);
        c.RefreshMoveFromArmorAndClass();
        c.RefreshMonkProgressionStats();

        return true;
    }

    public static void RecalculateDamageFromEquipment(Character c)
    {
        RecalculateDamage(c);
    }

    private static void RecalculateDamage(Character c)
    {
        EnsureEquipmentSlots(c);

        var mainHandWeapon = c.Equipment[EquipmentSlot.MainHand];
        var offHandWeapon = c.Equipment[EquipmentSlot.OffHand];

        // Legacy safety: if a two-handed weapon is only in off-hand, treat it as primary.
        if (mainHandWeapon == null && offHandWeapon != null && offHandWeapon.IsTwoHanded)
            mainHandWeapon = offHandWeapon;

        var mainDamage = mainHandWeapon != null
                         && mainHandWeapon.Type == ItemType.Weapon
                         && !string.IsNullOrWhiteSpace(mainHandWeapon.Damage)
            ? mainHandWeapon.GetDamageExpression(false)
            : "1d2";

        var sameTwoHandedWeaponInBothHands = mainHandWeapon != null
                                             && offHandWeapon != null
                                             && ReferenceEquals(mainHandWeapon, offHandWeapon)
                                             && mainHandWeapon.IsTwoHanded;

        var hasOffHandWeapon = !sameTwoHandedWeaponInBothHands
                               && offHandWeapon != null
                               && offHandWeapon.Type == ItemType.Weapon
                               && !string.IsNullOrWhiteSpace(offHandWeapon.Damage);

        c.Damage = hasOffHandWeapon
            ? $"{mainDamage}/{offHandWeapon!.Damage}"
            : mainDamage;

        c.RefreshMonkProgressionStats();
    }

    public static int GetTotalArmorClassBonus(Character c)
    {
        int bonus = 0;

        foreach (var kv in c.Equipment)
        {
            if (kv.Value != null)
                bonus += kv.Value.ArmorClassBonus;
        }

        return bonus;
    }

    private static void UnequipSlotToInventory(Character c, EquipmentSlot slot, System.Collections.Generic.HashSet<Item> movedItems)
    {
        var equipped = c.Equipment[slot];
        if (equipped == null)
        {
            c.Equipment[slot] = null;
            return;
        }

        if ((slot == EquipmentSlot.Ring1 || slot == EquipmentSlot.Ring2)
            && IsRingOfInvisibility(equipped))
        {
            c.DeactivateRingInvisibility();
        }

        // For two-handed occupancy mirrored in both hand slots, ensure AC/inventory are adjusted once.
        if (movedItems.Add(equipped))
        {
            if (equipped.IsVersatileWeapon)
                equipped.IsTwoHanded = false;

            c.ArmorClass += GetEffectiveArmorClassBonusForEquip(equipped);
            c.Inventory.Add(equipped);
        }

        c.Equipment[slot] = null;
    }
}
