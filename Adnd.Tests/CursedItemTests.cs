using System.Collections.Generic;
using Adnd.Core.Characters;
using Adnd.Core.Items;
using Xunit;

namespace Adnd.Tests;

public class CursedItemTests
{
    private static (Character, Item) WearDeadlyRing()
    {
        var c = new Character { Name = "Bubba", CurrentHitPoints = 10, MaxHitPoints = 10 };
        var ring = new Item
        {
            Name = "Deadly Ring",
            Type = ItemType.MagicItem,
            Slot = EquipmentSlot.Ring1,
            IsCursed = true,
            SpecialAbilities = new List<string> { "Regeneration -3", "Cursed" }
        };
        c.Inventory.Add(ring);
        Assert.True(EquipmentManager.Equip(c, ring));
        return (c, ring);
    }

    [Fact]
    public void DeadlyRing_DrainsThreeHp_AndStaysOnUntilCurseRemoved()
    {
        var (c, ring) = WearDeadlyRing();

        Assert.Equal(3, EquipmentManager.ApplyEquippedLifeDrain(c));
        Assert.Equal(7, c.CurrentHitPoints);
        Assert.False(EquipmentManager.Unequip(c, EquipmentSlot.Ring1));

        Assert.Single(EquipmentManager.RemoveCursesFromEquipment(c));
        Assert.True(EquipmentManager.Unequip(c, EquipmentSlot.Ring1));
        Assert.Contains(ring, c.Inventory);
    }

    [Fact]
    public void RemoveCurse_OnItem_OnlyAffectsThatItem()
    {
        var (c, ring) = WearDeadlyRing();
        var other = new Item { Name = "Cursed Helm", IsCursed = true };

        Assert.True(EquipmentManager.RemoveCurse(other));
        Assert.True(ring.IsCursed);
    }
}
