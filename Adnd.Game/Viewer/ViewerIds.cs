// Wire ids, in one place.
//
// These strings are a contract: the viewer keys its figures on them, so "char:Grond" in a snapshot
// and "char:Grond" in a prompt have to be the same character or the table highlights the wrong
// standee. They were previously spelled out where they were built, which was fine while only the
// snapshot used them, and stops being fine the moment a second thing refers to a figure.
//
// Mirrors WizardryViewer.Protocol's Ids helper on the viewer side; the two must agree.

namespace Adnd.Game.Viewer;

public static class ViewerIds
{
    public static string Character(string name) => "char:" + name;

    public static string Monster(string groupId, int index) => $"mon:{groupId}#{index}";

    public static string Group(string groupId) => "group:" + groupId;

    /// <summary>Boltac's side of the counter.</summary>
    public const string ShopSide = "shop";

    /// <summary>The shopper's own side of the counter.</summary>
    public const string PackSide = "pack";

    /// <summary>
    /// A card on a counter: which side it lies on, and which thing it is.
    ///
    /// The side is part of the id because the same item can be on both at once -- Boltac sells a dagger while
    /// the shopper is carrying one -- and they are two different cards to point at with two different meanings.
    /// The shop side is named by the item, the pack side by its position in the pack, because two identical
    /// daggers in a pack still have to be two separate cards.
    /// </summary>
    public static string Ware(string side, string id) => WarePrefix(side) + id;

    /// <summary>
    /// What every card on one side of the counter starts with, so a reader can tell the sides apart without
    /// respelling the id. <see cref="ViewerCommands"/> keys the two sides differently and this is how it knows
    /// which it is looking at.
    /// </summary>
    public static string WarePrefix(string side) => $"ware:{side}:";
}
