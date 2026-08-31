// What Boltac sells, what he pays, and what happens when a deal goes through.
//
// Lifted out of ShopMenu for the reason the temple's rules were: the console and the tabletop both trade here,
// and stock is the kind of thing two implementations get wrong in a way nobody notices until an item has been
// duplicated. Buying is not just "gold down, item up" -- it is a stock decrement that can fail, a carry-weight
// check that can refuse, and a COPY of the shop's item rather than the shop's own instance. Selling puts the
// item back on the shelf, which is why it goes through here too.

using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Items;
using Adnd.Data.Characters;
using Adnd.Data.Items;
using Adnd.Data.Party;

namespace Adnd.Game;

public static class Shop
{
    /// <summary>
    /// What is on the shelves: buyable, and not sold out. The 52 cap is the console's, kept so both surfaces
    /// see the same shop rather than the table quietly stocking more than Boltac admits to.
    /// </summary>
    public static List<Item> Stock(ItemRepository items) =>
        items.LoadAll()
            .Where(i => i.IsShopBuyable || (i.StockQuantity.HasValue && i.StockQuantity.Value > 0))
            .Where(i => !i.StockQuantity.HasValue || i.StockQuantity.Value > 0)
            .Take(52)
            .ToList();

    /// <summary>Boltac buys at half. He is a merchant.</summary>
    public static int SellPrice(Item item) => item.Cost / 2;

    public static bool InStock(Item item) => !item.StockQuantity.HasValue || item.StockQuantity.Value > 0;

    /// <summary>Stock as the shop shows it: a number, or a dash for something he never runs out of.</summary>
    public static string StockDisplay(Item item) =>
        item.StockQuantity.HasValue ? item.StockQuantity.Value.ToString() : "-";

    /// <summary>
    /// Whether this character could actually wear or wield it. A hint rather than a rule: anyone may BUY
    /// anything, which is how you outfit the fighter with the mage's gold.
    /// </summary>
    public static bool IsEquipableBy(Character character, Item item)
    {
        var canGoInSlot = item.Type == ItemType.Weapon
            || item.Type == ItemType.Shield
            || item.Slot.HasValue;

        if (!canGoInSlot)
            return false;

        if (item.AllowedClasses == null || item.AllowedClasses.Count == 0)
            return true;

        return character.Classes != null && character.Classes.Any(cls => item.AllowedClasses.Contains(cls));
    }

    /// <summary>A copy of a shop item, so the shelf's own instance never ends up in somebody's pack.</summary>
    public static Item CopyOf(Item it) => new Item
    {
        Name = it.Name,
        Type = it.Type,
        Slot = it.Slot,
        Cost = it.Cost,
        Weight = it.Weight,
        ToHitBonus = it.ToHitBonus,
        IsShopBuyable = it.IsShopBuyable,
        StockQuantity = it.StockQuantity,
        ArmorClassBonus = it.ArmorClassBonus,
        Damage = it.Damage,
        DamageVsLarge = it.DamageVsLarge,
        AllowedClasses = new List<CharacterClass>(it.AllowedClasses),
        SpecialAbilities = new List<string>(it.SpecialAbilities)
    };

    /// <summary>
    /// Sells <paramref name="shelfItem"/> to <paramref name="buyer"/>, or says why not.
    ///
    /// The stock decrement comes BEFORE the gold, deliberately: it is the step that can fail on someone else's
    /// account -- two shoppers, one last suit of plate -- and failing after taking the money would be worse
    /// than failing before.
    /// </summary>
    public static string Buy(Character buyer, Item shelfItem, ItemRepository items,
                             CharacterRepository characters, PartyRepository parties, Party party)
    {
        if (!InStock(shelfItem))
            return $"{shelfItem.Name} is out of stock.";

        if (buyer.GoldPieces < shelfItem.Cost)
            return $"{buyer.Name} cannot afford {shelfItem.Name} ({shelfItem.Cost} gp).";

        var purchased = CopyOf(shelfItem);

        if (!buyer.CanCarry(purchased))
            return $"{buyer.Name} cannot carry more weight ({buyer.CurrentCarryWeight}/{buyer.MaxCarryWeight}).";

        if (!items.TryAdjustStock(shelfItem.Name, -1))
            return $"Boltac cannot find another {shelfItem.Name}.";

        buyer.GoldPieces -= shelfItem.Cost;
        buyer.TryReceiveItem(purchased);
        characters.Save(buyer);
        parties.Save(party);

        return $"{buyer.Name} buys {shelfItem.Name} for {shelfItem.Cost} gp, and has {buyer.GoldPieces} left.";
    }

    /// <summary>
    /// Buys an item back off a character, unequipping it first when it is being worn.
    ///
    /// The console asks twice before selling something equipped, which is right for a numbered list where 3 and
    /// 4 are next to each other. From the table the item was named on the button that was pressed, so the click
    /// IS the confirmation -- but the label has to say it is equipped, and it does.
    /// </summary>
    public static string Sell(Character seller, Item item, EquipmentSlot? equippedIn, ItemRepository items,
                             CharacterRepository characters, PartyRepository parties, Party party)
    {
        var price = SellPrice(item);

        if (equippedIn.HasValue)
        {
            var equipped = seller.Equipment[equippedIn.Value];
            if (equipped == null)
                return $"{item.Name} is not in {equippedIn.Value} after all.";

            EquipmentManager.Unequip(seller, equippedIn.Value);
            seller.Inventory.Remove(equipped);
        }
        else if (!seller.Inventory.Remove(item))
        {
            return $"{seller.Name} no longer has {item.Name}.";
        }

        seller.GoldPieces += price;
        items.TryAdjustStock(item.Name, +1);
        characters.Save(seller);
        parties.Save(party);

        return $"{seller.Name} sells {item.Name} for {price} gp, and has {seller.GoldPieces} now.";
    }
}
