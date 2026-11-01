using Adnd.Core.Characters;
using Adnd.Core.Items;
using Xunit;

namespace Adnd.Tests;

public class EvilOnlyItemTests
{
    private static Item EvilSword() => new()
    {
        Name = "Evil Sword +3",
        Type = ItemType.Weapon,
        Slot = EquipmentSlot.MainHand,
        SpecialAbilities = ["Evil Only"],
    };

    [Theory]
    [InlineData(Alignment.LawfulEvil, true)]
    [InlineData(Alignment.NeutralEvil, true)]
    [InlineData(Alignment.ChaoticEvil, true)]
    [InlineData(Alignment.TrueNeutral, false)]
    [InlineData(Alignment.LawfulGood, false)]
    public void EvilOnlyItem_RequiresEvilAlignment(Alignment alignment, bool allowed)
    {
        var c = new Character { Name = "Test", Alignment = alignment };

        Assert.Equal(allowed, EquipmentManager.MeetsAlignmentRequirement(c, EvilSword()));
    }
}
