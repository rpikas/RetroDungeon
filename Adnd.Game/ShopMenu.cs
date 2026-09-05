using System;
using System.Linq;
using Adnd.Data.Items;
using Adnd.Data.Party;
using Adnd.Data.Characters;
using Adnd.Core.Characters;
using Adnd.Core.Items;

namespace Adnd.Game;

public class ShopMenu
{
    private readonly ItemRepository _itemRepo = new("Data/Items");
    private readonly PartyRepository _partyRepo = new("Data/Party");
    private readonly CharacterRepository _charRepo = new("Data/Characters");

    private readonly Adnd.Game.Viewer.TabletopViewerBridge _viewer = new();
    private Adnd.Game.Viewer.ShopSession? _session;

    /// <summary>
    /// The table's view of the shop. Held on the menu rather than made per call, because Show() calls itself to
    /// page the console's list and a fresh session each time would throw away which page the TABLE was on.
    /// </summary>
    private Adnd.Game.Viewer.ShopSession Session =>
        _session ??= new Adnd.Game.Viewer.ShopSession(_itemRepo, _charRepo, _partyRepo);

    /// <summary>
    /// What is on the shelves right now, for the hub to draw while the party is merely standing outside.
    ///
    /// The same list the shop itself publishes, from the same session, so the goods do not change the moment
    /// somebody walks in -- which is exactly the kind of difference two separate lists would eventually grow.
    /// </summary>
    public System.Collections.Generic.List<object> DisplayWares() => Session.Wares();

    private void PublishShop()
    {
        if (!_viewer.Enabled)
            return;

        _viewer.PublishTown("Shop", Session.Members(), Session.Prompt(), Session.Wares());
    }

    /// <summary>Says what just happened on both surfaces, and waits to be acknowledged on either.</summary>
    private void Report()
    {
        var events = Session.TakeEvents();
        if (events.Count == 0)
            return;

        foreach (var line in events)
            Console.WriteLine(line);

        var text = string.Join(Environment.NewLine, events);
        // The counter stays laid out while the message is up: what just changed hands is the thing worth seeing.
        _viewer.PublishTown("Shop", Session.Members(), Adnd.Game.Viewer.ViewerMessage.Prompt(text), Session.Wares());

        while (true)
        {
            var (_, command) = AwaitChoice();
            if (command == null || command == "continue" || command == "back")
                return;
        }
    }

    /// <summary>A key at the console or a click on the table, whichever comes first.</summary>
    private (ConsoleKey Key, string? Command) AwaitChoice()
    {
        while (true)
        {
            if (Console.KeyAvailable) return (Console.ReadKey(true).Key, null);

            var command = _viewer.TryTakeCommand();
            if (command != null) return (default, command);

            System.Threading.Thread.Sleep(60);
        }
    }

    private Character? GetCurrentShopper(Adnd.Data.Party.Party party)
    {
        if (party == null) return null;
        if (party.Members == null || party.Members.Count == 0) return null;

        // Load roster into dictionary for fast lookup by name
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, c => c);

        if (party.CurrentShopperIndex >= 0 && party.CurrentShopperIndex < party.Members.Count)
        {
            var name = party.Members[party.CurrentShopperIndex];
            if (roster.TryGetValue(name, out var c))
                return c;
        }

        // Fallback to first available party member resolved from roster
        foreach (var name in party.Members)
            if (roster.TryGetValue(name, out var c))
                return c;

        return null;
    }

    private System.Collections.Generic.List<Character> ResolvePartyCharacters(Adnd.Data.Party.Party party)
    {
        var list = new System.Collections.Generic.List<Character>();
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, c => c);
        foreach (var name in party.Members)
            if (roster.TryGetValue(name, out var c))
                list.Add(c);
        return list;
    }

    public void Show(int startIndex, int numberOfItems=15,bool selectShopper=true)// default to 15 items shown in shop, used when entering shop
    {
        // If entering shop and there are at least two party members,
        // always ask which member should enter the shop.
        var initialParty = _partyRepo.Load();
        if ((initialParty.Members.Count >= 2) && (startIndex == 0) && selectShopper)
            SelectShopper();

        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== SHOP ===\n");


            var party = _partyRepo.Load();
            var shopper = GetCurrentShopper(party);
            // One page can hold at most 26 rows, because a row is named by a single letter A-Z and
            // every letter has to mean exactly one visible item. The old limit of 48 needed a
            // second, lower-case range to label the overflow, which made 'a' and 'A' different
            // items on the same screen. Paging reaches the rest.
            if (numberOfItems > 26) numberOfItems = 26;

            if (shopper != null)
                Console.WriteLine($"Shopper: {shopper.Name} - GP: {shopper.GoldPieces}\n");
            else
                Console.WriteLine("No party members.\n");

            var items = Shop.Stock(_itemRepo);

            Console.WriteLine("Items available for purchase:\n");

            for (int i = startIndex; ((i < items.Count) && (i < startIndex + numberOfItems)); i++)
          //      for (int i = 0; (i < items.Count && i < numberOfItems); i++)
                {
                    var it = items[i];
                    var notEquipableTag = shopper != null && !IsEquipableBy(shopper, it) ? " - (Not Equipable)" : string.Empty;
                    var stockText = GetStockDisplay(it);
                    var label = GetShopLabel(i - startIndex);
                    Console.WriteLine($"{label}. {it.Name}{notEquipableTag} - Cost: {it.Cost} gp - Stock: {stockText}");
            }

            Console.WriteLine("\nB)uy Items");
            Console.WriteLine("S)ell Items");
            Console.WriteLine("E)quip/unequip Items");
            Console.WriteLine("P)ool Gold");
            Console.WriteLine("C)hoose a Different Shopper");
            if (startIndex + numberOfItems < items.Count) Console.WriteLine("N)ext items in shop");
            if (startIndex > 0) Console.WriteLine("G)o to previous items in shop");
            Console.WriteLine("I)nitial items in shop");
            Console.WriteLine("F)ilter items in shop");
            Console.WriteLine("L<-eave");

            if (_viewer.Enabled)
                Console.WriteLine("(or trade from the table)");

            PublishShop();

            // Both surfaces live at once, as in the maze, a fight and the temple. The console keeps its own way
            // out, so a viewer that has been closed cannot trap the party at the counter.
            var (key, command) = AwaitChoice();

            if (command != null)
            {
                if (command == "back") break;

                if (Session.Apply(command))
                {
                    PublishShop();
                    Report();
                }

                continue;
            }

            if (key == ConsoleKey.B) BuyItems(startIndex, numberOfItems);
            else if (key == ConsoleKey.S) SellItems();
            else if (key == ConsoleKey.E) EquipItems();
            else if (key == ConsoleKey.P) PoolGoldToCurrentShopper();
            else if (key == ConsoleKey.C) SelectShopper();
            else if (key == ConsoleKey.N) Show(startIndex+numberOfItems, numberOfItems, false);
            else if ((key == ConsoleKey.G) && (startIndex > 0)) Show(startIndex - numberOfItems, numberOfItems, false);
            else if (key == ConsoleKey.I) Show(0, numberOfItems, false);
        //    else if (key == ConsoleKey.F) FilterItemsa();
            else if (key == ConsoleKey.L || key == ConsoleKey.Enter) break;

        }
    }

    private void PoolGoldToCurrentShopper()
    {
        var party = _partyRepo.Load();
        var shopper = GetCurrentShopper(party) ?? ResolvePartyCharacters(party).FirstOrDefault();

        if (shopper == null)
        {
            Console.WriteLine("No party members.");
            Console.ReadKey(true);
            return;
        }

        var members = ResolvePartyCharacters(party);
        if (members.Count == 0)
        {
            Console.WriteLine("No resolved party members.");
            Console.ReadKey(true);
            return;
        }

        int pooled = 0;
        foreach (var member in members)
        {
            if (string.Equals(member.Name, shopper.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            pooled += member.GoldPieces;
            member.GoldPieces = 0;
            _charRepo.Save(member);
        }

        shopper.GoldPieces += pooled;
        _charRepo.Save(shopper);

        Console.WriteLine($"\nPooled {pooled} gp to {shopper.Name}. New total: {shopper.GoldPieces} gp");
        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }


    private void BuyItems(int startIndex, int numberOfItems)
    {
        var items = Shop.Stock(_itemRepo);
        var party = _partyRepo.Load();

        if (party.Members.Count == 0)
        {
            Console.WriteLine("No party members.");
            Console.ReadKey(true);
            return;
        }

        var buyer = GetCurrentShopper(party) ?? ResolvePartyCharacters(party).First();

        Console.Clear();
        Console.WriteLine($"=== BUY ITEMS ===\nShopper: {buyer.Name} - Gold: {buyer.GoldPieces} gp\n");

        //   for (int i = 0; i < items.Count; i++)
        for (int i = startIndex; ((i < items.Count) && (i < startIndex + numberOfItems)); i++)

        {
            var it = items[i];
            var notEquipableTag = !IsEquipableBy(buyer, it) ? " (Not Equipable)" : string.Empty;
            var stockText = GetStockDisplay(it);
            var label = GetShopLabel(i - startIndex);
            Console.WriteLine($"{label}. {it.Name}{notEquipableTag} ({it.Cost} gp) - Stock: {stockText}");
        }

        Console.Write("\nChoose letter: ");
        var sel = ReadShopLetterIndex(startIndex, numberOfItems, items.Count);
        if (sel.HasValue)
        {
            var it = items[sel.Value];
            Console.WriteLine($"Gold before: {buyer.GoldPieces} gp");

            if (it.StockQuantity.HasValue && it.StockQuantity.Value <= 0)
            {
                Console.WriteLine("This item is out of stock.");
            }
            else if (buyer.GoldPieces < it.Cost)
            {
                Console.WriteLine("Not enough gold to buy this item.");
            }
            else
            {
                var purchased = new Item
                {
                    Name = it.Name,
                    Type = it.Type,
                    Slot = it.Slot,
                    Cost = it.Cost,
                    Weight = it.Weight,
                    ToHitBonus = it.ToHitBonus,
                    Status = it.Status,
                    IsShopBuyable = it.IsShopBuyable,
                    StockQuantity = it.StockQuantity,
                    ArmorClassBonus = it.ArmorClassBonus,
                    Damage = it.Damage,
                    DamageVsLarge = it.DamageVsLarge,
                    AllowedClasses = new System.Collections.Generic.List<CharacterClass>(it.AllowedClasses),
                    SpecialAbilities = new System.Collections.Generic.List<string>(it.SpecialAbilities)
                };

                if (!buyer.CanCarry(purchased))
                {
                    Console.WriteLine($"{buyer.Name} cannot carry more weight ({buyer.CurrentCarryWeight}/{buyer.MaxCarryWeight}).");
                    Console.ReadKey(true);
                    return;
                }

                if (!_itemRepo.TryAdjustStock(it.Name, -1))
                {
                    Console.WriteLine("Unable to update stock for this item.");
                    Console.ReadKey(true);
                    return;
                }

                buyer.GoldPieces -= it.Cost;
                buyer.TryReceiveItem(purchased);
                Console.WriteLine($"Bought {it.Name} for {it.Cost} gp.");
                Console.WriteLine($"Gold after: {buyer.GoldPieces} gp");
                _charRepo.Save(buyer);
                _partyRepo.Save(party);
            }
        }
        else Console.WriteLine("Invalid.");

        Console.ReadKey(true);
    }

    /// <summary>
    /// Letter shown beside an item, counted from the top of the page rather than the top of the
    /// whole catalogue, so the first row on screen is always A. Only one page is ever visible, so
    /// there is nothing for a second lettering range to reach.
    /// </summary>
    private static string GetShopLabel(int indexOnPage)
    {
        if (indexOnPage >= 0 && indexOnPage < 26)
            return ((char)('A' + indexOnPage)).ToString();

        return "?";
    }

    /// <summary>
    /// Read a letter and turn it into an index into the full item list.
    ///
    /// Case-insensitive on purpose. The labels are printed as capitals, and typing the letter you
    /// can see without holding Shift used to mean something else entirely: 'a' was item 27, not
    /// item 1. On a short list that produced "Invalid" for every lowercase key, and on a longer one
    /// it silently bought an item from a page you were not looking at.
    ///
    /// The letter is relative to the page on display, so it selects the row beside it whatever page
    /// you are on.
    /// </summary>
    private static int? ReadShopLetterIndex(int startIndex, int pageSize, int count)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter)
            return null;

        var ch = char.ToUpperInvariant(key.KeyChar);
        if (ch < 'A' || ch > 'Z')
            return null;

        var indexOnPage = ch - 'A';
        if (indexOnPage >= pageSize)
            return null;

        var idx = startIndex + indexOnPage;
        if (idx < 0 || idx >= count)
            return null;

        return idx;
    }

    private void SelectShopper()
    {
        var party = _partyRepo.Load();
        if (party.Members.Count == 0)
        {
            Console.WriteLine("No party members.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("Choose who will enter the shop:\n");

        // Build list of resolved party members (filter out not-found characters)
        var resolvedMembers = new System.Collections.Generic.List<Character>();
        foreach (var name in party.Members)
        {
            var ch = _charRepo.GetAll().FirstOrDefault(c => c.Name == name);
            if (ch != null)
                resolvedMembers.Add(ch);
        }

        if (resolvedMembers.Count == 0)
        {
            Console.WriteLine("No valid party members found.");
            Console.ReadKey(true);
            return;
        }

        for (int i = 0; i < resolvedMembers.Count; i++)
        {
            var member = resolvedMembers[i];
            var cls = member.Classes != null && member.Classes.Count > 0
                ? string.Join("/", member.Classes.Select(cc => cc.ToDisplayString()))
                : member.Class.ToDisplayString();

            Console.WriteLine($"{i + 1}. {member.Name} ({cls}) - GP: {member.GoldPieces}");
        }

        Console.Write("Choose #: ");
        var sel = InputHelper.ReadNumber(1, resolvedMembers.Count);
        if (sel.HasValue)
        {
            // Find the index of the selected character in the original party.Members list
            var selectedChar = resolvedMembers[sel.Value - 1];
            party.CurrentShopperIndex = party.Members.IndexOf(selectedChar.Name);
            _partyRepo.Save(party);
            // No confirmation message; return directly to shop
        }
        else
        {
            Console.WriteLine("Invalid.");
            Console.ReadKey(true);
        }
    }

    private void SellItems()
    {
        var party = _partyRepo.Load();
        var seller = GetCurrentShopper(party) ?? ResolvePartyCharacters(party).FirstOrDefault();

        if (seller == null)
        {
            Console.WriteLine("No party members.");
            Console.ReadKey(true);
            return;
        }

        if (seller.Inventory.Count == 0)
        {
            Console.WriteLine("Inventory empty.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine($"=== SELL ITEMS ===\nShopper: {seller.Name} - Gold: {seller.GoldPieces} gp\n");

        var entries = new System.Collections.Generic.List<(Item item, bool equipped, EquipmentSlot? slot)>();

        foreach (var kv in seller.Equipment)
        {
            if (kv.Value != null)
                entries.Add((kv.Value, true, kv.Key));
        }

        foreach (var it in seller.Inventory)
        {
            entries.Add((it, false, null));
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No items to sell.");
            Console.ReadKey(true);
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var price = e.item.Cost / 2;
            if (e.equipped)
                Console.WriteLine($"{i + 1}. [EQUIPPED:{e.slot}] {e.item.Name} (sell {price} gp)");
            else
                Console.WriteLine($"{i + 1}. {e.item.Name} (sell {price} gp)");
        }

        Console.Write("\nChoose #: ");
        if (int.TryParse(Console.ReadLine(), out int idx) && idx >= 1 && idx <= entries.Count)
        {
            var entry = entries[idx - 1];
            var it = entry.item;
            Console.WriteLine($"Gold before: {seller.GoldPieces} gp");
            var sellPrice = it.Cost / 2;

            if (entry.equipped && entry.slot.HasValue)
            {
                Console.WriteLine($"{it.Name} is currently equipped in {entry.slot}. Unequip to inventory and then sell? (Y/N)");
                var key = Console.ReadKey(true).Key;
                if (key != ConsoleKey.Y)
                {
                    Console.WriteLine("Sale cancelled.");
                    Console.ReadKey(true);
                    return;
                }

                var slot = entry.slot.Value;
                var equippedItem = seller.Equipment[slot];
                if (equippedItem == null)
                {
                    Console.WriteLine("Unexpected: item not found in equipment.");
                    Console.ReadKey(true);
                    return;
                }

                EquipmentManager.Unequip(seller, slot);

                Console.WriteLine($"Unequipped {equippedItem.Name} to inventory. Sell for {sellPrice} gp? (Y/N)");
                var key2 = Console.ReadKey(true).Key;
                if (key2 != ConsoleKey.Y)
                {
                    Console.WriteLine("Sale cancelled. Item remains in inventory.");
                    _charRepo.Save(seller);
                    _partyRepo.Save(party);
                    Console.ReadKey(true);
                    return;
                }

                seller.Inventory.Remove(equippedItem);
            }
            else
            {
                seller.Inventory.Remove(it);
            }

            seller.GoldPieces += sellPrice;
            _itemRepo.TryAdjustStock(it.Name, +1);
            Console.WriteLine($"Sold {it.Name} for {sellPrice} gp.");
            Console.WriteLine($"Gold after: {seller.GoldPieces} gp");
            _charRepo.Save(seller);
            _partyRepo.Save(party);
        }
        else Console.WriteLine("Invalid.");

        Console.ReadKey(true);
    }

    private void EquipItems()
    {
        var party = _partyRepo.Load();
        var c = GetCurrentShopper(party) ?? ResolvePartyCharacters(party).FirstOrDefault();

        if (c == null)
        {
            Console.WriteLine("No party members.");
            Console.ReadKey(true);
            return;
        }

        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== EQUIP ITEMS ===");
            Console.WriteLine($"Shopper: {c.Name} - GP: {c.GoldPieces} gp\n");

            Console.WriteLine("Currently equipped:");
            foreach (var kv in c.Equipment)
            {
                var slot = kv.Key;
                var it = kv.Value;
                if (it == null)
                    Console.WriteLine($" - {slot}: (empty)");
                else
                    Console.WriteLine($" - {slot}: {it.Name}");
            }

            Console.WriteLine("\nE)quip from inventory");
            Console.WriteLine("U)nequip item");
                Console.WriteLine("L<-eave");

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.E)
            {
                if (c.Inventory.Count == 0)
                {
                    Console.WriteLine("\nInventory empty.");
                    Console.ReadKey(true);
                    continue;
                }

                EquipmentHelper.PromptAndEquipItem(c, _charRepo);
            }
            else if (key == ConsoleKey.U)
            {
                var equipped = c.Equipment
                    .Where(kv => kv.Value != null)
                    .ToList();

                if (equipped.Count == 0)
                {
                    Console.WriteLine("\nNo equipped items to unequip.");
                    Console.ReadKey(true);
                    continue;
                }

                Console.WriteLine("\nUnequip from:");
                for (int i = 0; i < equipped.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {equipped[i].Key} ({equipped[i].Value!.Name})");
                }

                Console.Write("Choose #: ");
                var sel = InputHelper.ReadNumber(1, equipped.Count);
                if (!sel.HasValue)
                    continue;

                var slot = equipped[sel.Value - 1].Key;
                if (EquipmentManager.Unequip(c, slot))
                    _charRepo.Save(c);
            }
            else if (key == ConsoleKey.L || key == ConsoleKey.Enter)
            {
                break;
            }
        }
    }

    /// <summary>See <see cref="Shop.IsEquipableBy"/> -- kept as a local name for the call sites here.</summary>
    private static bool IsEquipableBy(Character character, Item item) => Shop.IsEquipableBy(character, item);

    private static string GetStockDisplay(Item item) => Shop.StockDisplay(item);
}
