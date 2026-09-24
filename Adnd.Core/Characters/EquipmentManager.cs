using System;
using System.Linq;
using Adnd.Core.Items;

namespace Adnd.Core.Characters;

public static class EquipmentManager
{
    private static bool IsRingOfProtection(Item? it)
        => it != null && string.Equals(it.Name, "Ring of Protection", StringComparison.OrdinalIgnoreCase);

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

        static bool IsRingOfInvisibility(Item? it)
            => it != null && string.Equals(it.Name, "Ring of Invisibility", StringComparison.OrdinalIgnoreCase);

        // Remove old item if slot is occupied
        if (c.Equipment[slot] != null)
        {
            if ((slot == EquipmentSlot.Ring1 || slot == EquipmentSlot.Ring2)
                && IsRingOfInvisibility(c.Equipment[slot]))
            {
                c.DeactivateRingInvisibility();
            }

            // subtract the armor class bonus of the currently equipped item
            c.ArmorClass += GetEffectiveArmorClassBonusForEquip(c.Equipment[slot]);
            c.Inventory.Add(c.Equipment[slot]);
        }

        // equip the new item and apply its armor class bonus
        c.Equipment[slot] = item;
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

        if (c.Equipment[slot] == null)
            return false;

        if ((slot == EquipmentSlot.Ring1 || slot == EquipmentSlot.Ring2)
            && string.Equals(c.Equipment[slot]?.Name, "Ring of Invisibility", StringComparison.OrdinalIgnoreCase))
        {
            c.DeactivateRingInvisibility();
        }

        // remove item's armor class bonus when unequipping
        c.ArmorClass += GetEffectiveArmorClassBonusForEquip(c.Equipment[slot]);
        c.Inventory.Add(c.Equipment[slot]);
        c.Equipment[slot] = null;

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

        var mainDamage = mainHandWeapon != null
                         && mainHandWeapon.Type == ItemType.Weapon
                         && !string.IsNullOrWhiteSpace(mainHandWeapon.Damage)
            ? mainHandWeapon.Damage
            : "1d2";

        var hasOffHandWeapon = offHandWeapon != null
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
}
