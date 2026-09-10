using System;
using System.Linq;
using Adnd.Core.Items;

namespace Adnd.Core.Characters;

public static class EquipmentManager
{
    public static bool Equip(Character c, Item item)
    {
        if (item.Slot == null)
            return false;

        if (item.AllowedClasses != null
            && item.AllowedClasses.Count > 0
            && (c.Classes == null || !c.Classes.Any(cls => item.AllowedClasses.Contains(cls))))
        {
            return false;
        }

        var slot = item.Slot.Value;

        // Remove old item if slot is occupied
        if (c.Equipment[slot] != null)
        {
            // subtract the armor class bonus of the currently equipped item
            c.ArmorClass += c.Equipment[slot].ArmorClassBonus;
            c.Inventory.Add(c.Equipment[slot]);
        }

        // equip the new item and apply its armor class bonus
        c.Equipment[slot] = item;
        c.ArmorClass -= item.ArmorClassBonus;
        c.Inventory.Remove(item);

        RecalculateDamage(c);
        c.RefreshMoveFromArmorAndClass();
        c.RefreshMonkProgressionStats();

        return true;
    }

    public static bool Unequip(Character c, EquipmentSlot slot)
    {
        if (c.Equipment[slot] == null)
            return false;

        // remove item's armor class bonus when unequipping
        c.ArmorClass += c.Equipment[slot].ArmorClassBonus;
        c.Inventory.Add(c.Equipment[slot]);
        c.Equipment[slot] = null;

        RecalculateDamage(c);
        c.RefreshMoveFromArmorAndClass();
        c.RefreshMonkProgressionStats();

        return true;
    }

    private static void RecalculateDamage(Character c)
    {
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
