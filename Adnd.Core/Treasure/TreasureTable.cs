using System.Collections.Generic;

namespace Adnd.Core.Treasure;

public sealed class TreasureTable
{
    public string Name { get; set; } = string.Empty;
    public TreasureCoinsTable Coins { get; set; } = new();
    public TreasureValuablesRule Gems { get; set; } = new();
    public TreasureValuablesRule Jewelry { get; set; } = new();
   // public TreasureValuablesRule Art { get; set; } = new();
    public List<TreasureMagicRule> MagicRolls { get; set; } = new();
}

public sealed class TreasureCoinsTable
{
    public TreasureRollRule CopperPieces { get; set; } = new();
    public TreasureRollRule SilverPieces { get; set; } = new();
    public TreasureRollRule ElectrumPieces { get; set; } = new();
    public TreasureRollRule GoldPieces { get; set; } = new();
    public TreasureRollRule PlatinumPieces { get; set; } = new();
}

public sealed class TreasureRollRule
{
    public string AmountExpression { get; set; } = "0";
    public int ChancePercent { get; set; }
}

public sealed class TreasureValuablesRule
{
    public string AmountExpression { get; set; } = "0";
    public int ChancePercent { get; set; }
    public int MinValueGp { get; set; }
    public int MaxValueGp { get; set; }
}

public sealed class TreasureMagicRule
{
    public string Table { get; set; } = string.Empty;
    public string AmountExpression { get; set; } = "1";
    public int ChancePercent { get; set; }
}
