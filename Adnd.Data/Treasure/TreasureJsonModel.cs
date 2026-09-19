namespace Adnd.Data.Treasure;

public class TreasureJsonModel
{
    public string Name { get; set; } = "";
    public TreasureCoinsJson Coins { get; set; } = new();
    public TreasureValuablesJson Gems { get; set; } = new();
    public TreasureValuablesJson Jewelry { get; set; } = new();
//    public TreasureValuablesJson Art { get; set; } = new();

    // Placeholder for future DMG-style magic item generation.
    public TreasureMagicJson Magic { get; set; } = new();
}

public class TreasureCoinsJson
{
    public TreasureRollJson CopperPieces { get; set; } = new();
    public TreasureRollJson SilverPieces { get; set; } = new();
    public TreasureRollJson ElectrumPieces { get; set; } = new();
    public TreasureRollJson GoldPieces { get; set; } = new();
    public TreasureRollJson PlatinumPieces { get; set; } = new();
}

public class TreasureValuablesJson
{
    // Dice expression for count, e.g. "2d6", "1d4+1", "0".
    public string Amount { get; set; } = "0";

    // Percent chance (0-100) to include this valuables category.
    public int Chance { get; set; } = 0;

    // Per-item value range in gp.
    public int MinValueGp { get; set; } = 0;
    public int MaxValueGp { get; set; } = 0;
}

public class TreasureMagicJson
{
    public List<TreasureMagicRollJson> Rolls { get; set; } = new();
}

public class TreasureMagicRollJson
{
    // E.g. "Potion", "Scroll", "Weapon", "Armor", or future table keys.
    public string Table { get; set; } = "";

    // Dice/count expression, e.g. "1", "1d2".
    public string Amount { get; set; } = "1";

    // Percent chance (0-100).
    public int Chance { get; set; } = 0;
}

public class TreasureRollJson
{
    // Dice expression for quantity, e.g. "5d10*100", "0".
    public string Amount { get; set; } = "0";

    // Percent chance (0-100) for this coin type.
    public int Chance { get; set; } = 0;
}