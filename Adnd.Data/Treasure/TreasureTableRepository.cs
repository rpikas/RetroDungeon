using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Adnd.Core.Treasure;

namespace Adnd.Data.Treasure;

public sealed class TreasureTableRepository : ITreasureTableProvider
{
    private readonly string _folder;
    private Dictionary<string, TreasureTable>? _cache;

    public TreasureTableRepository(string folder = "Data/Treasure")
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    public Dictionary<string, TreasureTable> LoadAll()
    {
        EnsureLoaded();
        return new Dictionary<string, TreasureTable>(_cache!, StringComparer.OrdinalIgnoreCase);
    }

    public TreasureTable? LoadByCode(string treasureType)
    {
        if (string.IsNullOrWhiteSpace(treasureType))
            return null;

        EnsureLoaded();
        _cache!.TryGetValue(NormalizeCode(treasureType), out var table);
        return table;
    }

    public bool TryGetTable(string treasureType, out TreasureTable table)
    {
        table = LoadByCode(treasureType)!;
        return table != null;
    }

    // Backward-compatible alias.
    public IReadOnlyDictionary<string, TreasureTable> LoadAllByCode() => LoadAll();

    private void EnsureLoaded()
    {
        if (_cache != null)
            return;

        _cache = new Dictionary<string, TreasureTable>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(_folder, "Treasure_*.json"))
        {
            var code = NormalizeCode(Path.GetFileNameWithoutExtension(file)
                .Replace("Treasure_", string.Empty, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(code))
                continue;

            var json = File.ReadAllText(file);
            var model = JsonSerializer.Deserialize<TreasureJsonModel>(json);
            if (model == null)
                continue;

            _cache[code] = Convert(model);
        }
    }

    private static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    private static TreasureTable Convert(TreasureJsonModel model)
    {
        return new TreasureTable
        {
            Name = model.Name,
            Coins = new TreasureCoinsTable
            {
                CopperPieces = ToRoll(model.Coins.CopperPieces),
                SilverPieces = ToRoll(model.Coins.SilverPieces),
                ElectrumPieces = ToRoll(model.Coins.ElectrumPieces),
                GoldPieces = ToRoll(model.Coins.GoldPieces),
                PlatinumPieces = ToRoll(model.Coins.PlatinumPieces)
            },
            Gems = ToValuables(model.Gems),
            Jewelry = ToValuables(model.Jewelry),
       //     Art = ToValuables(model.Art),
            MagicRolls = model.Magic.Rolls.Select(r => new TreasureMagicRule
            {
                Table = r.Table,
                AmountExpression = r.Amount,
                ChancePercent = r.Chance
            }).ToList()
        };
    }

    private static TreasureRollRule ToRoll(TreasureRollJson json)
    {
        return new TreasureRollRule
        {
            AmountExpression = json.Amount,
            ChancePercent = json.Chance
        };
    }

    private static TreasureValuablesRule ToValuables(TreasureValuablesJson json)
    {
        return new TreasureValuablesRule
        {
            AmountExpression = json.Amount,
            ChancePercent = json.Chance,
            MinValueGp = json.MinValueGp,
            MaxValueGp = json.MaxValueGp
        };
    }
}
