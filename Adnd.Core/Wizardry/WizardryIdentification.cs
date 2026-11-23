using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Items;

namespace Adnd.Core.Wizardry;

public enum IdentifyOutcome
{
    NotBishop,
    AlreadyIdentified,
    Identified,
    Failed,
    EquippedCursed
}

/// <summary>Unidentified Wizardry items and Bishop identification. See docs\Wizardry-Handbook.md.</summary>
public static class WizardryIdentification
{
    /// <summary>The generic name an unidentified item shows, or null when the item has no generic name.</summary>
    public static string? UnknownNameFor(Item item)
    {
        var name = item.Name;
        bool Has(string s) => name.Contains(s, StringComparison.OrdinalIgnoreCase);

        return item.Type switch
        {
            ItemType.Weapon when Has("Sword") || Has("Slayer") || Has("Masher") || Has("Blade") => "Sword",
            ItemType.Weapon when Has("Dagger") => "Dagger",
            ItemType.Weapon when Has("Shuriken") => "Stars",
            ItemType.Weapon when Has("Mace") || Has("Flail") || Has("Staff") => "Staff",
            ItemType.Weapon => "Weapon",
            ItemType.Shield => "Shield",
            ItemType.Armor when Has("Helm") => "Helm",
            ItemType.Armor when Has("Glove") || Has("Gauntlet") => "Gloves",
            ItemType.Armor when Has("Robe") || Has("Garb") => "Clothing",
            ItemType.Armor => "Armour",
            ItemType.Potion => "Potion",
            ItemType.Scroll => "Scroll",
            _ when Has("Ring") => "Ring",
            _ when Has("Amulet") => "Amulet",
            _ => null
        };
    }

    /// <summary>Marks a found Wizardry item as unidentified when it has a generic name.</summary>
    public static void MakeUnidentified(Item item)
    {
        var unknown = UnknownNameFor(item);
        if (unknown == null)
            return;

        item.UnknownName = unknown;
        item.IsIdentified = false;
    }

    public static bool IsBishop(Character c) =>
        c.Ruleset == Ruleset.Wizardry && c.WizardryClass == WizardryClass.Bishop;

    public static bool IsSamurai(Character c) =>
        c.Ruleset == Ruleset.Wizardry && c.WizardryClass == WizardryClass.Samurai;

    public static bool IsLord(Character c) =>
        c.Ruleset == Ruleset.Wizardry && c.WizardryClass == WizardryClass.Lord;

    public static bool IsNinja(Character c) =>
        c.Ruleset == Ruleset.Wizardry && c.WizardryClass == WizardryClass.Ninja;

    /// <summary>A Wizardry Lord dispells undead from level 4, as a Priest three levels lower.</summary>
    public static int LordDispellLevel(int lordLevel) => Math.Max(0, lordLevel - 3);

    /// <summary>A Wizardry Bishop dispells undead from level 4 (when priest spellcasting starts), as a Priest three levels lower.</summary>
    public static int BishopDispellLevel(int bishopLevel) => Math.Max(0, bishopLevel - 3);

    /// <summary>The Priest level a Wizardry Lord or Bishop turns undead at, or null for other characters.</summary>
    public static int? HybridDispellLevel(Character c)
        => IsLord(c) ? LordDispellLevel(c.Level) : IsBishop(c) ? BishopDispellLevel(c.Level) : null;

    public static int IdentifyChancePercent(int bishopLevel) => Math.Clamp(bishopLevel * 5 + 10, 0, 100);

    public static int CursedEquipChancePercent(int bishopLevel) => Math.Clamp(35 - bishopLevel * 3, 0, 100);

    /// <summary>Gold a temple/shop charges to identify an item: half its value.</summary>
    public static int ShopIdentifyCost(Item item) => item.Cost / 2;

    /// <summary>A shop lifts the curse on an equipped item for half the item's value.</summary>
    public static int ShopUncurseCost(Item item) => item.Cost / 2;

    /// <summary>
    /// A Bishop examines an item. First the curse risk: a cursed item is accidentally equipped.
    /// Otherwise the identify chance decides; failure leaves the item unknown and the Bishop may try again.
    /// </summary>
    public static IdentifyOutcome TryBishopIdentify(Character bishop, Item item, Random random)
    {
        if (!IsBishop(bishop))
            return IdentifyOutcome.NotBishop;
        if (item.IsIdentified)
            return IdentifyOutcome.AlreadyIdentified;

        var level = bishop.Level;
        if (item.IsCursed && random.Next(100) < CursedEquipChancePercent(level))
        {
            item.IsIdentified = true;
            bishop.Inventory.Remove(item);
            if (!EquipmentManager.Equip(bishop, item))
                bishop.Inventory.Add(item);
            return IdentifyOutcome.EquippedCursed;
        }

        if (random.Next(100) < IdentifyChancePercent(level))
        {
            item.IsIdentified = true;
            return IdentifyOutcome.Identified;
        }

        return IdentifyOutcome.Failed;
    }
}
