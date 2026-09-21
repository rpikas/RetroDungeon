using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Adnd.Core.Spells.Casting.Handlers;

public static class MonsterIntelligenceResolver
{
    private static readonly ConcurrentDictionary<string, int> _intelligenceByMonsterName = new(StringComparer.OrdinalIgnoreCase);

    public static int GetIntelligenceForMonster(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return 10;

        return _intelligenceByMonsterName.TryGetValue(NormalizeName(monsterName), out var value)
            ? value
            : 10;
    }

    public static void RegisterMonsterIntelligence(string monsterName, string? intelligenceCategory)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return;

        _intelligenceByMonsterName[NormalizeName(monsterName)] = ParseIntelligence(intelligenceCategory);
    }

    private static int ParseIntelligence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 10;

        var text = value.Trim();
        var lower = text.ToLowerInvariant();

        if (lower.StartsWith("non")) return 1;
        if (lower.StartsWith("animal")) return 3;
        if (lower.StartsWith("semi")) return 5;
        if (lower.StartsWith("average")) return 9;
        if (lower.StartsWith("very")) return 12;
        if (lower.StartsWith("high")) return 14;
        if (lower.StartsWith("exceptional")) return 16;
        if (lower.StartsWith("genius")) return 18;
        if (lower.StartsWith("supra")) return 20;
        if (lower.StartsWith("godlike")) return 25;

        // Common MM wording not in the supplied table.
        if (lower.StartsWith("low")) return 7;

        // Numeric or range fallback, e.g. "11-12".
        var numbers = Regex.Matches(lower, @"\d+")
            .Select(m => int.TryParse(m.Value, out var n) ? n : -1)
            .Where(n => n >= 0)
            .ToList();

        if (numbers.Count == 1)
            return numbers[0];

        if (numbers.Count >= 2)
            return (int)Math.Ceiling((numbers[0] + numbers[1]) / 2.0);

        return 10;
    }

    private static string NormalizeName(string name) => name.Trim();
}