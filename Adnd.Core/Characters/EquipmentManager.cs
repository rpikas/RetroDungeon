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

    public static bool Equip(Character c, Item item)
    {
        EnsureEquipmentSlots(c);

        if (item.Slot == null)
            return false;

        if (item.AllowedClasses != null
            && item.AllowedClasses.Count > 0
            && (c.Classes == null || !c.Classes.Any(cls => item.AllowedClasses.Contains(cls))))
        {
            return false;
        }

        var slot = item.Slot.Value;

        if (slot == EquipmentSlot.OffHand
            && item.Type == ItemType.Weapon
            && !IsAllowedOffHandWeapon(item))
        {
            return false;
        }

        var movedItems = new System.Collections.Generic.HashSet<Item>();
        var isHandSlot = slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand;

        if (isHandSlot && item.IsTwoHanded)
        {
            // Two-handed weapon occupies both hands.
            UnequipSlotToInventory(c, EquipmentSlot.MainHand, movedItems);
            UnequipSlotToInventory(c, EquipmentSlot.OffHand, movedItems);
            c.Equipment[EquipmentSlot.MainHand] = item;
            c.Equipment[EquipmentSlot.OffHand] = item;
        }
        else
        {
            if (isHandSlot)
            {
                // If the opposite hand currently participates in a two-handed setup,
                // clear both hands before equipping a one-handed item.
                var otherHand = slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand;
                var otherHandItem = c.Equipment[otherHand];
                if (otherHandItem != null && otherHandItem.IsTwoHanded)
                {
                    UnequipSlotToInventory(c, EquipmentSlot.MainHand, movedItems);
                    UnequipSlotToInventory(c, EquipmentSlot.OffHand, movedItems);
                }
            }

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
            ? mainHandWeapon.Damage
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
            c.ArmorClass += GetEffectiveArmorClassBonusForEquip(equipped);
            c.Inventory.Add(equipped);
        }

        c.Equipment[slot] = null;
    }
}
