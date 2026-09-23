using Adnd.Core.Characters;
using Adnd.Core.Items;
using Adnd.Data.Characters;
using Adnd.Data.Party;
using System;
using System.Linq;

namespace Adnd.Game;

public static class EquipmentHelper
{
    public static bool PromptAndEquipItem(Character c, CharacterRepository charRepo)
    {
        if (c.Inventory.Count == 0)
        {
            return false;
        }

        Console.WriteLine("\nInventory:");
        for (int i = 0; i < c.Inventory.Count; i++)
            Console.WriteLine($"{i + 1}. {c.Inventory[i].Name}");

        Console.Write("\nChoose # (or Enter to cancel): ");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > c.Inventory.Count)
            return false;

        var item = c.Inventory[idx - 1];

        if (item.AllowedClasses != null
            && item.AllowedClasses.Count > 0
            && (c.Classes == null || !c.Classes.Any(cls => item.AllowedClasses.Contains(cls))))
        {
            var required = string.Join(", ", item.AllowedClasses.Select(cls => cls.ToDisplayString()));
            Console.WriteLine($"\n{c.Name} cannot equip {item.Name}. Allowed classes: {required}.");
            Console.ReadKey(true);
            return false;
        }

        EquipmentSlot? targetSlot = null;

        // Determine target slot based on item type and properties
        if (item.Type == ItemType.Weapon)
        {
            if (!string.IsNullOrWhiteSpace(item.FireRate) || item.RequiresAmmo || !string.IsNullOrWhiteSpace(item.AmmoType))
            {
                targetSlot = EquipmentSlot.Range;
            }
            else
            {
                // Weapons: prompt user to choose MainHand or OffHand
                Console.WriteLine("\nEquip to:");
                Console.WriteLine("1. Main Hand");
                Console.WriteLine("2. Off Hand");
                Console.Write("Choose slot: ");
                var slotChoice = InputHelper.ReadNumber(1, 2);
                if (slotChoice.HasValue)
                {
                    targetSlot = slotChoice.Value == 1 ? EquipmentSlot.MainHand : EquipmentSlot.OffHand;
                }
                else
                {
                    return false;
                }
            }
        }
        else if (item.Type == ItemType.Misc && item.Quantity > 0 && !string.IsNullOrWhiteSpace(item.AmmoType))
        {
            targetSlot = EquipmentSlot.Ammo;
        }
        else if (item.Type == ItemType.Shield)
        {
            // Shields automatically go to OffHand
            targetSlot = EquipmentSlot.OffHand;
        }
        else if (item.Slot.HasValue)
        {
            // Other items use their predefined slot
            targetSlot = item.Slot.Value;
        }

        if (targetSlot != null)
        {
            var slot = targetSlot.Value;

            // Temporarily set the item's slot for EquipmentManager
            var originalSlot = item.Slot;
            item.Slot = slot;

            var ok = EquipmentManager.Equip(c, item);

            // Restore original slot (for weapons it was null)
            item.Slot = originalSlot;

            if (ok)
            {
                // Save character equipment/inventory changes
                charRepo.Save(c);
                return true;
            }

            Console.WriteLine($"\n{c.Name} cannot equip {item.Name}.");
            Console.ReadKey(true);
        }

        return false;
    }
}
