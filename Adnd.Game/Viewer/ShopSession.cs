// Boltac's Trading Post, as a tabletop rather than a menu.
//
// A shop has two kinds of thing to choose, and both are on the table. The PEOPLE stand there already, so who is
// shopping is a matter of pointing at them -- which also answers "whose gold is this?", a question the console
// spends a whole screen on. The GOODS are laid out on the stall as tagged cards: Boltac's stock on his side of
// the counter, the shopper's own pack on theirs. Point at a card and it changes hands, and which side it lay on
// is what decides whether that was buying or selling.
//
// So there is no Buy mode, no Sell mode and no paging. An earlier version of this had all three, because the
// goods were a price list in a dialog -- and a price list in a dialog is a menu wearing a tabletop's clothes.
// The cards need no artwork to work as objects: a name and a price on a tag is what a market stall actually
// looks like.
//
// Nothing here decides a trade. <see cref="Shop"/> owns stock, weight and price, because an item duplicated by
// two implementations of "buy" is the kind of bug that is only noticed after it is in a save file.

using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Items;
using Adnd.Data.Characters;
using Adnd.Data.Items;
using Adnd.Data.Party;

namespace Adnd.Game.Viewer;

public sealed class ShopSession
{
    private readonly ItemRepository _items;
    private readonly CharacterRepository _characters;
    private readonly PartyRepository _parties;

    private readonly List<string> _events = new();

    public ShopSession(ItemRepository items, CharacterRepository characters, PartyRepository parties)
    {
        _items = items;
        _characters = characters;
        _parties = parties;
    }

    public Party Party() => _parties.Load();

    /// <summary>The party in marching order, dropping names the roster no longer knows.</summary>
    public List<Character> Members()
    {
        var roster = _characters.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        return Party().Members.Where(roster.ContainsKey).Select(n => roster[n]).ToList();
    }

    /// <summary>
    /// Whose purse is open. Follows the party's own CurrentShopperIndex so the console and the table always
    /// agree about who is at the counter -- switch shoppers on the table and the console menu says so too.
    /// </summary>
    public Character? Shopper()
    {
        var party = Party();
        var members = Members();
        if (members.Count == 0) return null;

        if (party.CurrentShopperIndex >= 0 && party.CurrentShopperIndex < party.Members.Count)
        {
            var name = party.Members[party.CurrentShopperIndex];
            var found = members.FirstOrDefault(c => Same(c.Name, name));
            if (found != null) return found;
        }

        return members[0];
    }

    public List<string> TakeEvents()
    {
        var taken = new List<string>(_events);
        _events.Clear();
        return taken;
    }

    /// <summary>
    /// Everything on the counter: Boltac's shelf on his side, the shopper's own pack on theirs.
    ///
    /// Sent as labels, because whether a thing is affordable, liftable or usable by THIS character is a
    /// question about the rules and the viewer knows nothing about either. Priced from the shopper's point of
    /// view too: the same axe shows its cost on the shelf and half that in your pack, which is Boltac's margin
    /// made visible rather than explained.
    /// </summary>
    public List<object> Wares()
    {
        var wares = new List<object>();
        var shopper = Shopper();
        if (shopper == null) return wares;

        foreach (var it in Shop.Stock(_items))
        {
            var note = it.Cost > shopper.GoldPieces ? "too dear"
                     : !shopper.CanCarry(Shop.CopyOf(it)) ? "too heavy"
                     : !Shop.IsEquipableBy(shopper, it) ? "cannot use"
                     : "";

            wares.Add(new { Id = it.Name, it.Name, Price = it.Cost + " gp", Note = note, Side = ViewerIds.ShopSide });
        }

        for (var i = 0; i < shopper.Inventory.Count; i++)
        {
            var it = shopper.Inventory[i];
            var slot = EquippedIn(shopper, it);

            wares.Add(new
            {
                Id = i.ToString(),
                it.Name,
                Price = Shop.SellPrice(it) + " gp",
                Note = slot.HasValue ? "worn" : "",
                Side = ViewerIds.PackSide,
            });
        }

        return wares;
    }

    /// <summary>
    /// What can be pointed at: every ware on the counter, and every other purse in the party.
    ///
    /// No Buy or Sell mode, and no pages. A ware IS the option -- the card on the stall is what gets clicked,
    /// and which side of the counter it lies on is what decides whether that is buying or selling. Only what
    /// the game will actually accept is offered: something too dear or too heavy stays on the shelf as an
    /// object, but it is not a thing you can click, because a refusal after the click is worse on a table than
    /// in a console, where at least there is somewhere to print why.
    /// </summary>
    public ViewerPrompt Prompt()
    {
        var shopper = Shopper();
        if (shopper == null)
            return new ViewerPrompt("shop", "Boltac has nobody to serve.", null,
                new[] { new ViewerPromptOption("back", "Leave the shop") });

        var options = new List<ViewerPromptOption>();

        foreach (var it in Shop.Stock(_items))
        {
            if (it.Cost > shopper.GoldPieces) continue;
            if (!shopper.CanCarry(Shop.CopyOf(it))) continue;

            options.Add(new ViewerPromptOption("take:" + it.Name, $"Buy {it.Name} ({it.Cost} gp)",
                                              Target: ViewerIds.Ware(ViewerIds.ShopSide, it.Name)));
        }

        for (var i = 0; i < shopper.Inventory.Count; i++)
        {
            var it = shopper.Inventory[i];
            options.Add(new ViewerPromptOption($"give:{i}", $"Sell {it.Name} ({Shop.SellPrice(it)} gp)",
                                              Target: ViewerIds.Ware(ViewerIds.PackSide, i.ToString())));
        }

        foreach (var c in Members().Where(c => !Same(c.Name, shopper.Name)))
            options.Add(new ViewerPromptOption("shop:" + c.Name, $"{c.Name} shops ({c.GoldPieces} gp)",
                                              Target: ViewerIds.Character(c.Name)));

        options.Add(new ViewerPromptOption("pool", $"Pool the party's gold to {shopper.Name}"));
        options.Add(new ViewerPromptOption("back", "Leave the shop"));

        // Not anchored to the shopper, deliberately. A bubble belongs over a figure being ASKED something, and
        // it is drawn beside them -- which at Boltac's, the rightmost pad on the board, put it half off the
        // screen. This question is about the counter rather than about a person, the person is named in the
        // text, and the two buttons left over are small enough to sit on the board.
        return new ViewerPrompt("shop",
            $"{shopper.Name} is at the counter with {shopper.GoldPieces} gp. Point at what changes hands.",
            null, options);
    }

    /// <summary>Applies one command, returning true when the table should be redrawn.</summary>
    public bool Apply(string command)
    {
        var shopper = Shopper();
        if (shopper == null) return false;

        switch (command)
        {
            case "pool":
                return Pool(shopper);
        }

        if (command.StartsWith("shop:", StringComparison.Ordinal))
            return MakeShopper(command.Substring(5));

        if (command.StartsWith("take:", StringComparison.Ordinal))
        {
            var name = command.Substring(5);
            var shelf = Shop.Stock(_items).FirstOrDefault(i => Same(i.Name, name));
            if (shelf == null) return false;

            _events.Add(Shop.Buy(shopper, shelf, _items, _characters, _parties, Party()));
            return true;
        }

        if (command.StartsWith("give:", StringComparison.Ordinal))
        {
            if (!int.TryParse(command.Substring(5), out var index)) return false;
            if (index < 0 || index >= shopper.Inventory.Count) return false;

            var item = shopper.Inventory[index];
            _events.Add(Shop.Sell(shopper, item, EquippedIn(shopper, item), _items, _characters, _parties, Party()));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Hands the purse to somebody else, by moving the party's own shopper index rather than keeping a second
    /// idea of who is shopping. One truth, so the console cannot disagree.
    /// </summary>
    private bool MakeShopper(string name)
    {
        var party = Party();
        var at = party.Members.FindIndex(m => Same(m, name));
        if (at < 0) return false;

        party.CurrentShopperIndex = at;
        _parties.Save(party);
        return true;
    }

    /// <summary>Everyone else's gold into the shopper's purse, which is what makes a big purchase possible.</summary>
    private bool Pool(Character shopper)
    {
        var moved = 0;

        foreach (var c in Members().Where(c => !Same(c.Name, shopper.Name) && c.GoldPieces > 0))
        {
            moved += c.GoldPieces;
            shopper.GoldPieces += c.GoldPieces;
            c.GoldPieces = 0;
            _characters.Save(c);
        }

        if (moved == 0)
        {
            _events.Add("Nobody else has any gold.");
            return true;
        }

        _characters.Save(shopper);
        _events.Add($"{shopper.Name} takes the party's {moved} gp, and now carries {shopper.GoldPieces}.");
        return true;
    }

    /// <summary>Which slot an inventory item is worn in, or null when it is just being carried.</summary>
    private static EquipmentSlot? EquippedIn(Character c, Item item)
    {
        foreach (var pair in c.Equipment)
            if (ReferenceEquals(pair.Value, item))
                return pair.Key;

        return null;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
