using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Treasure;

public sealed class TreasureService
{
    private readonly ITreasureTableProvider _tableProvider;
    private readonly Random _random;

    public TreasureService(ITreasureTableProvider tableProvider, Random? random = null)
    {
        _tableProvider = tableProvider;
        _random = random ?? Random.Shared;
    }

    public TreasureResult RollTreasureForEncounter(IEnumerable<MonsterInstance> monsters)
    {
        var result = new TreasureResult();

        foreach (var monster in monsters)
        {
            if (!monster.IsInLair)
            {
                result.LogLines.Add($"{monster.DisplayName}: not in lair, no treasure.");
                continue;
            }

            var tokens = ParseTreasureTypes(monster.Template.TreasureType);
            if (tokens.Count == 0)
            {
                result.LogLines.Add($"{monster.DisplayName}: no treasure type.");
                continue;
            }

            foreach (var token in tokens)
            {
                if (!_tableProvider.TryGetTable(token, out var table))
                {
                    result.LogLines.Add($"{monster.DisplayName}: unknown treasure type '{token}'.");
                    continue;
                }

                var overrideChance = monster.Template.TreasureChanceOverride;
                if (overrideChance.HasValue)
                {
                    var clamped = Math.Clamp(overrideChance.Value, 0d, 1d);
                    var roll = _random.NextDouble();
                    if (roll > clamped)
                    {
                        result.LogLines.Add($"{monster.DisplayName}: treasure type {token} skipped by override chance ({clamped:P0}).");
                        continue;
                    }
                }

                RollTable(table, token, monster.DisplayName, result);
            }
        }

        return result;
    }

    private void RollTable(TreasureTable table, string tableCode, string monsterDisplayName, TreasureResult result)
    {
        var source = string.IsNullOrWhiteSpace(table.Name) ? tableCode : table.Name;
        result.LogLines.Add($"{monsterDisplayName}: rolling {source} ({tableCode}).");

        RollCoins("CP", table.Coins.CopperPieces, source, result, v => result.CopperPieces += v);
        RollCoins("SP", table.Coins.SilverPieces, source, result, v => result.SilverPieces += v);
        RollCoins("EP", table.Coins.ElectrumPieces, source, result, v => result.ElectrumPieces += v);
        RollCoins("GP", table.Coins.GoldPieces, source, result, v => result.GoldPieces += v);
        RollCoins("PP", table.Coins.PlatinumPieces, source, result, v => result.PlatinumPieces += v);

        RollValuables("Gem", table.Gems, source, result.Gems, result.LogLines);
        RollValuables("Jewelry", table.Jewelry, source, result.Jewelry, result.LogLines);
        RollValuables("Art", table.Art, source, result.Art, result.LogLines);

        foreach (var magicRule in table.MagicRolls)
        {
            if (!RollChance(magicRule.ChancePercent))
                continue;

            var count = Math.Max(0, RollAmount(magicRule.AmountExpression));
            if (count <= 0)
                continue;

            result.MagicPlaceholders.Add(new TreasureMagicPlaceholderResult
            {
                Table = magicRule.Table,
                Count = count,
                SourceTable = source
            });

            result.LogLines.Add($"  + Magic placeholder: {magicRule.Table} x{count}");
        }
    }

    private void RollCoins(string label, TreasureRollRule rule, string source, TreasureResult result, Action<int> add)
    {
        if (!RollChance(rule.ChancePercent))
            return;

        var amount = Math.Max(0, RollAmount(rule.AmountExpression));
        if (amount <= 0)
            return;

        add(amount);
        result.LogLines.Add($"  + {label}: {amount}");
    }

    private void RollValuables(string category, TreasureValuablesRule rule, string source, List<TreasureValuableResult> target, List<string> logs)
    {
        if (!RollChance(rule.ChancePercent))
            return;

        var count = Math.Max(0, RollAmount(rule.AmountExpression));
        if (count <= 0)
            return;

        var min = Math.Min(rule.MinValueGp, rule.MaxValueGp);
        var max = Math.Max(rule.MinValueGp, rule.MaxValueGp);

        for (int i = 0; i < count; i++)
        {
            var value = max <= 0 ? 0 : _random.Next(min, max + 1);
            target.Add(new TreasureValuableResult
            {
                Category = category,
                ValueGp = value,
                SourceTable = source
            });
        }

        var total = target.Where(x => x.SourceTable == source && x.Category == category).TakeLast(count).Sum(x => x.ValueGp);
        logs.Add($"  + {category}: {count} item(s), total {total} gp");
    }

    private bool RollChance(int chancePercent)
    {
        var chance = Math.Clamp(chancePercent, 0, 100);
        if (chance <= 0)
            return false;
        if (chance >= 100)
            return true;

        return _random.Next(1, 101) <= chance;
    }

    private int RollAmount(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return 0;

        var normalized = expression.Replace(" ", "", StringComparison.Ordinal);
        var parts = normalized.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var basePart = parts[0];
        var multiplier = 1;
        if (parts.Length > 1 && !int.TryParse(parts[1], out multiplier))
            multiplier = 1;

        var baseValue = EvaluateBase(basePart);
        return baseValue * multiplier;
    }

    private int EvaluateBase(string baseExpression)
    {
        if (int.TryParse(baseExpression, out var fixedValue))
            return fixedValue;

        var m = Regex.Match(baseExpression, @"^(?<count>\d+)d(?<sides>\d+)(?<mod>[+-]\d+)?$", RegexOptions.IgnoreCase);
        if (!m.Success)
            return 0;

        var count = int.Parse(m.Groups["count"].Value);
        var sides = int.Parse(m.Groups["sides"].Value);
        var mod = m.Groups["mod"].Success ? int.Parse(m.Groups["mod"].Value) : 0;

        var sum = 0;
        for (var i = 0; i < Math.Max(1, count); i++)
            sum += _random.Next(1, Math.Max(2, sides) + 1);

        return sum + mod;
    }

    private static List<string> ParseTreasureTypes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        return raw
            .Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && !string.Equals(t, "None", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
