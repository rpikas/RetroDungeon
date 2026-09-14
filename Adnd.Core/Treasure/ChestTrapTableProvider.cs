using System.Text.Json;

namespace Adnd.Core.Treasure;

public sealed class ChestTrapTableProvider
{
    private readonly List<ChestTrapLevelBand> _bands;

    public ChestTrapTableProvider(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            _bands = new List<ChestTrapLevelBand>();
            return;
        }

        var json = File.ReadAllText(jsonPath);
        var model = JsonSerializer.Deserialize<ChestTrapTableModel>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        _bands = model?.LevelBands ?? new List<ChestTrapLevelBand>();
    }

    public ChestTrapType RollTrap(int dungeonLevel, Random random, out string rollInfo)
    {
        var level = Math.Max(1, dungeonLevel);
        var band = _bands.FirstOrDefault(b => level >= b.MinLevel && level <= b.MaxLevel)
                   ?? _bands.OrderByDescending(b => b.MaxLevel).FirstOrDefault();

        if (band == null || band.Weights == null || band.Weights.Count == 0)
        {
            rollInfo = "Chest trap table missing; defaulting to No trap.";
            return ChestTrapType.None;
        }

        var entries = band.Weights
            .Where(kv => kv.Value > 0)
            .Select(kv => new WeightedTrap(ParseTrapType(kv.Key), kv.Value))
            .ToList();

        if (entries.Count == 0)
        {
            rollInfo = "Chest trap weights invalid; defaulting to No trap.";
            return ChestTrapType.None;
        }

        var total = entries.Sum(e => e.Weight);
        var roll = random.Next(1, total + 1);
        var cursor = 0;

        foreach (var entry in entries)
        {
            cursor += entry.Weight;
            if (roll <= cursor)
            {
                rollInfo = $"Chest trap roll at dungeon level {level}: d{total}={roll} => {entry.Type}.";
                return entry.Type;
            }
        }

        rollInfo = "Chest trap roll fallback => No trap.";
        return ChestTrapType.None;
    }

    private static ChestTrapType ParseTrapType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ChestTrapType.None;

        var key = raw.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return key switch
        {
            "notrap" or "none" => ChestTrapType.None,
            "poisonneedle" or "poinsonneedle" => ChestTrapType.PoisonNeedle,
            "explodingbox" or "explosdingbox" => ChestTrapType.ExplodingBox,
            "gasbomb" => ChestTrapType.GasBomb,
            "crossbowbolt" => ChestTrapType.CrossbowBolt,
            "alarm" => ChestTrapType.Alarm,
            "mageblaster" => ChestTrapType.MageBlaster,
            "priestblaster" => ChestTrapType.PriestBlaster,
            _ => ChestTrapType.None
        };
    }

    public sealed class ChestTrapTableModel
    {
        public List<ChestTrapLevelBand> LevelBands { get; set; } = new();
    }

    public sealed class ChestTrapLevelBand
    {
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public Dictionary<string, int> Weights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct WeightedTrap(ChestTrapType Type, int Weight);
}
