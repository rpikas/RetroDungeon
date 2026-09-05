namespace Adnd.Data.Items;

public class ItemJsonModel
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Type { get; set; } = "";
    public string Slot { get; set; } = "";
    public int Cost { get; set; }
    public int Weight { get; set; } = 0;
    public int ToHitBonus { get; set; } = 0;
    public bool IsShopBuyable { get; set; } = true;
    public int? StockQuantity { get; set; }
    public int ArmorClassBonus { get; set; }
    public string Damage { get; set; } = "";
    public string DamageVsLarge { get; set; } = "";
    public string DamageType { get; set; } = "";
    public int SpeedFactor { get; set; } = 0;
    public string WeaponLength { get; set; } = "";
    public bool IsTwoHanded { get; set; } = false;
    public string Range { get; set; } = "";
    public int MagicBonus { get; set; } = 0;
    public List<string> SpecialAbilities { get; set; } = new();
    public bool IsCursed { get; set; } = false;
    public List<string> AllowedClasses { get; set; } = new();
    public string Rarity { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public string Version { get; set; } = "";
}

public class ItemCategoryJsonModel
{
    public string Category { get; set; } = "";
    public List<ItemJsonModel> Items { get; set; } = new();
}
