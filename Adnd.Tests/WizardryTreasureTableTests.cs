using System;
using Adnd.Core.Treasure;
using Xunit;

namespace Adnd.Tests;

public class WizardryTreasureTableTests
{
    [Fact]
    public void ItemList_HasAll93Entries()
    {
        Assert.Equal(93, WizardryTreasureTable.ItemCount);
        for (var i = 1; i <= 93; i++)
            Assert.NotEmpty(WizardryTreasureTable.GetItemNameCandidates(i));
    }

    [Fact]
    public void NotInLair_IsGoldOnlyWithinLevelRange()
    {
        var random = new Random(1);
        for (var i = 0; i < 200; i++)
        {
            var level1 = WizardryTreasureTable.Roll(1, false, random);
            Assert.Empty(level1.ItemNumbers);
            Assert.InRange(level1.Gold, 20, 100);

            var level9 = WizardryTreasureTable.Roll(9, false, random);
            Assert.InRange(level9.Gold, 100, 100 * 8 * 10);
        }
    }

    [Fact]
    public void InLair_Level6_AlwaysHasItemFromFirstRange()
    {
        var random = new Random(2);
        for (var i = 0; i < 100; i++)
        {
            var roll = WizardryTreasureTable.Roll(6, true, random);
            Assert.Contains(roll.ItemNumbers, n => n >= 3 && n <= 17);
            Assert.InRange(roll.Gold, 120, 600);
        }
    }
}
