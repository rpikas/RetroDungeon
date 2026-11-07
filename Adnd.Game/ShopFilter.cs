using Adnd.Core.Items;

namespace Adnd.Game;

public enum ShopFilter
{
    None,
    ArmorAndShields,
    Weapons,
    Other
}

public static class ShopFilterExtensions
{
    public static bool Matches(this ShopFilter filter, Item item) => filter switch
    {
        ShopFilter.ArmorAndShields => item.Type is ItemType.Armor or ItemType.Shield,
        ShopFilter.Weapons => item.Type == ItemType.Weapon,
        ShopFilter.Other => item.Type is not (ItemType.Armor or ItemType.Shield or ItemType.Weapon),
        _ => true
    };

    public static string Label(this ShopFilter filter) => filter switch
    {
        ShopFilter.ArmorAndShields => "Armor and shields",
        ShopFilter.Weapons => "Weapons",
        ShopFilter.Other => "Other",
        _ => "No filter"
    };
}
