using System;
using System.Collections.Generic;

namespace Adnd.Core.Treasure;

public sealed record WizardryItemChance(int Percent, int FirstItem, int LastItem);

public sealed record WizardryLevelTreasure(int Dice, int Sides, int Multiplier, int ExtraDieSides, IReadOnlyList<WizardryItemChance> ChestItems);

public sealed record WizardryTreasureRoll(int Gold, List<int> ItemNumbers, string Log);

// Wizardry treasure: depends only on the monster's dungeon level and whether the group is in its lair.
public static class WizardryTreasureTable
{
    public const string SourceTable = "WizardryTreasure";

    // Gold when not in lair: dice, sides, multiplier, optional extra die sides (0 = none).
    private static readonly (int Dice, int Sides, int Mult, int Extra)[] NotInLairGold =
    {
        (2, 5, 10, 0),
        (4, 5, 10, 0),
        (6, 5, 10, 0),
        (8, 5, 10, 0),
        (12, 5, 10, 0),
        (10, 10, 10, 0),
        (10, 10, 10, 2),
        (10, 10, 10, 4),
        (10, 10, 10, 8),
        (2, 5, 100, 0)
    };

    private static readonly WizardryLevelTreasure[] InLair =
    {
        new(2, 5, 100, 0, new[] { new WizardryItemChance(10, 3, 17) }),
        new(4, 5, 10, 0, new[] { new WizardryItemChance(20, 3, 17), new WizardryItemChance(10, 19, 33) }),
        new(6, 5, 10, 0, new[] { new WizardryItemChance(30, 3, 17), new WizardryItemChance(15, 19, 33) }),
        new(8, 5, 10, 0, new[] { new WizardryItemChance(40, 3, 17), new WizardryItemChance(20, 19, 33) }),
        new(10, 5, 10, 0, new[] { new WizardryItemChance(50, 3, 17), new WizardryItemChance(30, 19, 33), new WizardryItemChance(10, 35, 52) }),
        new(12, 5, 10, 0, new[] { new WizardryItemChance(100, 3, 17), new WizardryItemChance(50, 19, 33), new WizardryItemChance(20, 35, 52) }),
        new(10, 10, 10, 0, new[] { new WizardryItemChance(75, 19, 33), new WizardryItemChance(25, 35, 52), new WizardryItemChance(10, 54, 80) }),
        new(10, 10, 10, 2, new[] { new WizardryItemChance(100, 19, 33), new WizardryItemChance(50, 35, 52), new WizardryItemChance(15, 54, 80) }),
        new(10, 10, 10, 4, new[] { new WizardryItemChance(70, 35, 52), new WizardryItemChance(25, 54, 80), new WizardryItemChance(5, 81, 93) }),
        new(10, 10, 10, 8, new[] { new WizardryItemChance(100, 35, 52), new WizardryItemChance(50, 54, 80), new WizardryItemChance(10, 81, 93) })
    };

    // Index 0 = item number 1. Each entry lists item names to try in order.
    private static readonly string[][] ItemNames =
    {
        new[] { "Long Sword" },
        new[] { "Short Sword" },
        new[] { "Anointed Mace" },
        new[] { "Anointed Flail" },
        new[] { "Staff" },
        new[] { "Dagger" },
        new[] { "Small Shield" },
        new[] { "Large Shield" },
        new[] { "Robes" },
        new[] { "Leather Armor" },
        new[] { "Chain Mail" },
        new[] { "Breast Plate" },
        new[] { "Plate Mail" },
        new[] { "Helmet", "Helm" },
        new[] { "Potion of Dios" },
        new[] { "Potion of Latumofis" },
        new[] { "Long Sword +1" },
        new[] { "Short Sword +1" },
        new[] { "Mace +1" },
        new[] { "Staff of Mogref" },
        new[] { "Scroll of Katino" },
        new[] { "Leather +1" },
        new[] { "Chain Mail +1" },
        new[] { "Plate Mail +1" },
        new[] { "Shield +1" },
        new[] { "Breast Plate +1" },
        new[] { "Scroll of Badios" },
        new[] { "Scroll of Halito" },
        new[] { "Long Sword -1" },
        new[] { "Short Sword -1" },
        new[] { "Mace -1" },
        new[] { "Staff +2" },
        new[] { "Dragon Slayer" },
        new[] { "Helm +1" },
        new[] { "Leather -1" },
        new[] { "Chain -1" },
        new[] { "Breast Plate -1" },
        new[] { "Shield -1" },
        new[] { "Jeweled Amulet" },
        new[] { "Scroll of Badios" },
        new[] { "Potion of Sopic" },
        new[] { "Long Sword +2" },
        new[] { "Short Sword +2" },
        new[] { "Mace +2" },
        new[] { "Scroll of Lomilwa" },
        new[] { "Scroll of Dilto" },
        new[] { "Gloves of Copper" },
        new[] { "Leather +2" },
        new[] { "Chain +2" },
        new[] { "Plate Mail +2" },
        new[] { "Shield +2" },
        new[] { "Helm +2 (E)" },
        new[] { "Potion of Dial" },
        new[] { "Ring of Shielding" },
        new[] { "Were Slayer" },
        new[] { "Mage Masher" },
        new[] { "Mace of Poison" },
        new[] { "Staff of Montino" },
        new[] { "Vorpal Blade +1" },
        new[] { "Ring of Rigidity" },
        new[] { "Rod of Flame" },
        new[] { "Evil Chain +2" },
        new[] { "Neutral Plate +2" },
        new[] { "Evil Shield +3" },
        new[] { "Ring of Suffocation" },
        new[] { "Helm of Malor" },
        new[] { "Scroll of Badial" },
        new[] { "Short Sword -2" },
        new[] { "Dagger +2" },
        new[] { "Mace -2" },
        new[] { "Staff -2" },
        new[] { "Dagger of Speed" },
        new[] { "Cursed Robe -2" },
        new[] { "Leather -2" },
        new[] { "Chain -2" },
        new[] { "Breast Plate -2" },
        new[] { "Shield -2" },
        new[] { "Cursed Helmet -2" },
        new[] { "Breast Plate +2" },
        new[] { "Silver Gloves" },
        new[] { "Evil Sword +3" },
        new[] { "Evil S-Sword +3" },
        new[] { "Thieves Dagger" },
        new[] { "Breast Plate +3" },
        new[] { "Lords Garb +3" },
        new[] { "Muramasa Blade" },
        new[] { "Shuriken" },
        new[] { "Chain Protection vs. Fire +2" },
        new[] { "Evil Plate +3" },
        new[] { "Shield +3" },
        new[] { "Ring of Healing" },
        new[] { "Ring Pro Undead" },
        new[] { "Deadly Ring" }
    };

    public static int ItemCount => ItemNames.Length;

    public static IReadOnlyList<string> GetItemNameCandidates(int itemNumber)
        => itemNumber >= 1 && itemNumber <= ItemNames.Length ? ItemNames[itemNumber - 1] : Array.Empty<string>();

    public static WizardryTreasureRoll Roll(int dungeonLevel, bool inLair, Random random)
    {
        var index = Math.Clamp(dungeonLevel, 1, 10) - 1;
        var items = new List<int>();

        if (!inLair)
        {
            var (dice, sides, mult, extra) = NotInLairGold[index];
            var gold = RollGold(dice, sides, mult, extra, random);
            return new WizardryTreasureRoll(gold, items, $"Wizardry level {index + 1}, not in lair: {Describe(dice, sides, mult, extra)} = {gold} gp.");
        }

        var table = InLair[index];
        var chestGold = RollGold(table.Dice, table.Sides, table.Multiplier, table.ExtraDieSides, random);
        foreach (var chance in table.ChestItems)
        {
            if (random.Next(1, 101) > chance.Percent)
                continue;

            items.Add(random.Next(chance.FirstItem, chance.LastItem + 1));
        }

        return new WizardryTreasureRoll(chestGold, items,
            $"Wizardry level {index + 1}, in lair chest: {Describe(table.Dice, table.Sides, table.Multiplier, table.ExtraDieSides)} = {chestGold} gp, {items.Count} item(s).");
    }

    private static int RollGold(int dice, int sides, int mult, int extra, Random random)
    {
        var total = 0;
        for (var i = 0; i < dice; i++)
            total += random.Next(1, sides + 1);

        var factor = extra > 0 ? random.Next(1, extra + 1) : 1;
        return total * factor * mult;
    }

    private static string Describe(int dice, int sides, int mult, int extra)
        => extra > 0 ? $"[{dice}d{sides}]*[1d{extra}]*{mult}" : $"[{dice}d{sides}]*{mult}";
}
