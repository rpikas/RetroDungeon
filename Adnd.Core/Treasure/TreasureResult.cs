using System.Collections.Generic;
using System.Linq;

namespace Adnd.Core.Treasure;

public sealed class TreasureResult
{
    public TreasureBucket Total { get; set; } = new();
    public TreasureBucket Lair { get; set; } = new();
    public TreasureBucket NonLair { get; set; } = new();

    public int CopperPieces { get; set; }
    public int SilverPieces { get; set; }
    public int ElectrumPieces { get; set; }
    public int GoldPieces { get; set; }
    public int PlatinumPieces { get; set; }

    public List<TreasureValuableResult> Gems { get; set; } = new();
    public List<TreasureValuableResult> Jewelry { get; set; } = new();
    public List<TreasureValuableResult> Art { get; set; } = new();
    public List<TreasureMagicPlaceholderResult> MagicPlaceholders { get; set; } = new();

    public List<string> LogLines { get; set; } = new();

    public int TotalGemValueGp => Gems.Sum(g => g.ValueGp);
    public int TotalJewelryValueGp => Jewelry.Sum(j => j.ValueGp);
    public int TotalArtValueGp => Art.Sum(a => a.ValueGp);

    public void SyncLegacyTotalsFromBuckets()
    {
        CopperPieces = Total.CopperPieces;
        SilverPieces = Total.SilverPieces;
        ElectrumPieces = Total.ElectrumPieces;
        GoldPieces = Total.GoldPieces;
        PlatinumPieces = Total.PlatinumPieces;

        Gems = Total.Gems.ToList();
        Jewelry = Total.Jewelry.ToList();
        Art = Total.Art.ToList();
        MagicPlaceholders = Total.MagicPlaceholders.ToList();
    }
}

public sealed class TreasureBucket
{
    public int CopperPieces { get; set; }
    public int SilverPieces { get; set; }
    public int ElectrumPieces { get; set; }
    public int GoldPieces { get; set; }
    public int PlatinumPieces { get; set; }

    public List<TreasureValuableResult> Gems { get; set; } = new();
    public List<TreasureValuableResult> Jewelry { get; set; } = new();
    public List<TreasureValuableResult> Art { get; set; } = new();
    public List<TreasureMagicPlaceholderResult> MagicPlaceholders { get; set; } = new();

    public int TotalGemValueGp => Gems.Sum(g => g.ValueGp);
    public int TotalJewelryValueGp => Jewelry.Sum(j => j.ValueGp);
    public int TotalArtValueGp => Art.Sum(a => a.ValueGp);
}

public sealed class TreasureValuableResult
{
    public string Category { get; set; } = string.Empty;
    public int ValueGp { get; set; }
    public string SourceTable { get; set; } = string.Empty;
}

public sealed class TreasureMagicPlaceholderResult
{
    public string Table { get; set; } = string.Empty;
    public int Count { get; set; }
    public string SourceTable { get; set; } = string.Empty;
    public string ResolvedName { get; set; } = string.Empty;
    public int ExperienceValue { get; set; }
}
