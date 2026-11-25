using System.Globalization;

namespace Adnd.Core.Monsters;

// d100 outdoor encounter table: one column of roll ranges per terrain, each row a creature type.
public sealed class OutdoorEncounterTable
{
    private readonly Dictionary<string, List<(int Low, int High, string Creature)>> _byTerrain =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Terrains => _byTerrain.Keys;

    public static OutdoorEncounterTable FromRows(IReadOnlyList<string> terrains, IEnumerable<(string Creature, IReadOnlyList<string> Cells)> rows)
    {
        var table = new OutdoorEncounterTable();
        foreach (var terrain in terrains)
            table._byTerrain[terrain] = new();

        foreach (var (creature, cells) in rows)
        {
            if (string.IsNullOrWhiteSpace(creature))
                continue;
            for (var i = 0; i < terrains.Count && i < cells.Count; i++)
            {
                if (TryParseRange(cells[i], out var low, out var high))
                    table._byTerrain[terrains[i]].Add((low, high, creature.Trim()));
            }
        }

        return table;
    }

    // Accepts "5", "5.0", "02-03" and "00" (= 100); anything else (blank, dash) is no entry.
    public static bool TryParseRange(string? text, out int low, out int high)
    {
        low = high = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || !TryParseRoll(parts[0], out low))
            return false;
        if (parts.Length == 1)
        {
            high = low;
            return true;
        }

        if (!TryParseRoll(parts[1], out high))
            return false;
        if (high < low)
            (low, high) = (high, low);
        return true;
    }

    private static bool TryParseRoll(string text, out int value)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            value = 0;
            return false;
        }

        value = (int)Math.Round(d);
        if (value == 0)
            value = 100;
        return value is >= 1 and <= 100;
    }

    public bool HasTerrain(string terrain) => _byTerrain.ContainsKey(terrain);

    public string? Lookup(string terrain, int roll)
    {
        if (!_byTerrain.TryGetValue(terrain, out var entries))
            return null;
        foreach (var e in entries)
            if (roll >= e.Low && roll <= e.High)
                return e.Creature;
        return null;
    }
}
