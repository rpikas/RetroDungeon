using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

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
        var monsterList = monsters?.ToList() ?? new List<MonsterInstance>();

        foreach (var group in monsterList.GroupBy(m => string.IsNullOrWhiteSpace(m.GroupId) ? "default" : m.GroupId))
        {
            var members = group.ToList();
            if (members.Count == 0)
                continue;

            var representative = members[0];
            result.LogLines.Add($"Group {representative.GroupId}: non-individual treasure is rolled once for the whole group ({members.Count} monster(s)).");
            RollLairTreasure(representative, members.Count, result);

            foreach (var monster in members)
                RollIndividualTreasure(monster, result);
        }

        if (result.LogLines.Count > 0)
        {
            RuleApplicationInfo.Publish("Treasure roll details:");
            foreach (var line in result.LogLines)
                RuleApplicationInfo.Publish(line);
        }

        PopulateTotalBucketFromLegacy(result);

        return result;
    }

    private void RollLairTreasure(MonsterInstance monster, int groupCount, TreasureResult result)
    {
        var tokens = ParseTreasureTypes(monster.Template.TreasureType);
        var expectedAverage = GetExpectedAverageGroupSize(monster);
        var lootFactor = expectedAverage > 0d ? groupCount / expectedAverage : 1d;
        var coinAmountScale = lootFactor;
        var itemChanceScale = lootFactor;

        result.LogLines.Add(
            $"{monster.DisplayName}: ExpectedAverageNumberOfMonsters = ({monster.Template.NumberOfAppearancesMin}+{monster.Template.NumberOfAppearancesMax})/2 = {expectedAverage.ToString("0.###", CultureInfo.InvariantCulture)}.");
        result.LogLines.Add(
            $"{monster.DisplayName}: Lootfactor = number of monsters in group / ExpectedAverageNumberOfMonsters = {groupCount}/{expectedAverage.ToString("0.###", CultureInfo.InvariantCulture)} = {lootFactor.ToString("0.###", CultureInfo.InvariantCulture)}.");
        result.LogLines.Add(
            $"{monster.DisplayName}: item find probability scaling factor = Lootfactor = {itemChanceScale.ToString("0.###", CultureInfo.InvariantCulture)}.");
        result.LogLines.Add(
            $"{monster.DisplayName}: coin amount scaling factor = Lootfactor = {coinAmountScale.ToString("0.###", CultureInfo.InvariantCulture)}.");

        if (!monster.IsInLair)
        {
            if (tokens.Count > 0)
                result.LogLines.Add($"{monster.DisplayName}: not in lair, lair treasure skipped.");
            return;
        }

        if (tokens.Count == 0)
        {
            result.LogLines.Add($"{monster.DisplayName}: no lair treasure type.");
            return;
        }

        foreach (var token in tokens)
        {
            var before = CaptureSnapshot(result);
            RollTreasureToken(
                token,
                monster.DisplayName,
                result,
                monster.Template.TreasureChanceOverride,
                "lair",
                amountScaleFactor: 1d,
                coinAmountScale,
                gemJewelryMagicChanceScaleFactor: itemChanceScale,
                gemJewelryValueScaleFactor: 1d,
                gemJewelryCountDivideFactor: lootFactor,
                repeatDivideFactor: lootFactor,
                adjustArtAmountByScale: false);
            ApplyDeltaToBucket(before, result, result.Lair);
        }
    }

    private void RollIndividualTreasure(MonsterInstance monster, TreasureResult result)
    {
        var tokens = ParseTreasureTypes(monster.Template.IndividualTreasure);
        if (tokens.Count == 0)
            return;

        foreach (var token in tokens)
        {
            var logStart = result.LogLines.Count;
            var snapshot = CaptureSnapshot(result);

            RollTreasureToken(
                token,
                monster.DisplayName,
                result,
                null,
                "individual",
                amountScaleFactor: 1d,
                coinAmountScaleFactor: 1d,
                gemJewelryMagicChanceScaleFactor: 1d,
                gemJewelryValueScaleFactor: 1d,
                gemJewelryCountDivideFactor: 1d,
                repeatDivideFactor: 1d,
                adjustArtAmountByScale: false,
                suppressFailedRollLogs: true);

            ApplyDeltaToBucket(snapshot, result, result.NonLair);

            if (!HasFoundTreasure(snapshot, result))
            {
                var toRemove = result.LogLines.Count - logStart;
                if (toRemove > 0)
                    result.LogLines.RemoveRange(logStart, toRemove);
                continue;
            }

            var tokenLines = result.LogLines.Skip(logStart).ToList();
            var toDelete = result.LogLines.Count - logStart;
            if (toDelete > 0)
                result.LogLines.RemoveRange(logStart, toDelete);

            var compactLines = BuildCompactIndividualTreasureLines(monster.DisplayName, snapshot, result, tokenLines);
            foreach (var line in compactLines)
                result.LogLines.Add(line);
        }
    }

    private static IReadOnlyList<string> BuildCompactIndividualTreasureLines(
        string monsterDisplayName,
        TreasureSnapshot before,
        TreasureResult after,
        IReadOnlyList<string> tokenLines)
    {
        var source = ExtractTreasureSource(tokenLines);
        var amountDetails = ExtractCoinAmountDetails(tokenLines);

        var lines = new List<string>();
        AppendCoinLine("CP", after.CopperPieces - before.CopperPieces, amountDetails, monsterDisplayName, source, lines);
        AppendCoinLine("SP", after.SilverPieces - before.SilverPieces, amountDetails, monsterDisplayName, source, lines);
        AppendCoinLine("EP", after.ElectrumPieces - before.ElectrumPieces, amountDetails, monsterDisplayName, source, lines);
        AppendCoinLine("GP", after.GoldPieces - before.GoldPieces, amountDetails, monsterDisplayName, source, lines);
        AppendCoinLine("PP", after.PlatinumPieces - before.PlatinumPieces, amountDetails, monsterDisplayName, source, lines);

        var gemsFound = after.Gems.Count - before.GemsCount;
        var jewelryFound = after.Jewelry.Count - before.JewelryCount;
   //     var artFound = after.Art.Count - before.ArtCount;
        var magicFound = after.MagicPlaceholders.Count - before.MagicPlaceholdersCount;

        if (gemsFound > 0)
            lines.Add($"{monsterDisplayName}: individual {source} {gemsFound} Gem item(s) found.");
        if (jewelryFound > 0)
            lines.Add($"{monsterDisplayName}: individual {source} {jewelryFound} Jewelry item(s) found.");
//        if (artFound > 0)
     //       lines.Add($"{monsterDisplayName}: individual {source} {artFound} Art item(s) found.");
        if (magicFound > 0)
            lines.Add($"{monsterDisplayName}: individual {source} magic treasure found.");

        if (lines.Count == 0)
            lines.Add($"{monsterDisplayName}: individual {source} treasure found.");

        return lines;
    }

    private static void AppendCoinLine(
        string coin,
        int amount,
        IReadOnlyDictionary<string, string> amountDetails,
        string monsterDisplayName,
        string source,
        List<string> output)
    {
        if (amount <= 0)
            return;

        if (amountDetails.TryGetValue(coin, out var detail) && !string.IsNullOrWhiteSpace(detail))
        {
            output.Add($"{monsterDisplayName}: individual {source} Amount {detail} {coin} found.");
            return;
        }

        output.Add($"{monsterDisplayName}: individual {source} {amount} {coin} found.");
    }

    private static string ExtractTreasureSource(IReadOnlyList<string> tokenLines)
    {
        var rollingLine = tokenLines.FirstOrDefault(line => line.Contains(": rolling ", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(rollingLine))
            return "treasure";

        var match = Regex.Match(rollingLine, @":\s*rolling\s+(?<source>.+?)\s*\([^)]+\)\.", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["source"].Value.Trim() : "treasure";
    }

    private static IReadOnlyDictionary<string, string> ExtractCoinAmountDetails(IReadOnlyList<string> tokenLines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tokenLines.Count; i++)
        {
            var chanceMatch = Regex.Match(tokenLines[i], @"\b(?<coin>CP|SP|EP|GP|PP) chance:.*=> success\s*$", RegexOptions.IgnoreCase);
            if (!chanceMatch.Success)
                continue;

            if (i + 1 >= tokenLines.Count)
                continue;

            var amountMatch = Regex.Match(tokenLines[i + 1], @"Amount roll \((?<expr>[^)]+)\) => (?<detail>.+)$", RegexOptions.IgnoreCase);
            if (!amountMatch.Success)
                continue;

            var coin = chanceMatch.Groups["coin"].Value.ToUpperInvariant();
            var detail = amountMatch.Groups["detail"].Value.Trim();
            result[coin] = detail;
        }

        return result;
    }

    private void RollTreasureToken(
        string token,
        string monsterDisplayName,
        TreasureResult result,
        double? overrideChance,
        string scope,
        double amountScaleFactor,
        double coinAmountScaleFactor,
        double gemJewelryMagicChanceScaleFactor,
        double gemJewelryValueScaleFactor,
        double gemJewelryCountDivideFactor,
        double repeatDivideFactor,
        bool adjustArtAmountByScale,
        bool suppressFailedRollLogs = false)
    {
        var (tableCode, repeats) = ParseTreasureToken(token);

        var scaledRepeats = repeats;
        if (repeatDivideFactor > 0d && Math.Abs(repeatDivideFactor - 1d) > 0.0001d && repeats > 1)
            scaledRepeats = Math.Max(1, (int)Math.Round(repeats * repeatDivideFactor, MidpointRounding.AwayFromZero));

        if (string.Equals(tableCode, "WORNEQUIPMENT", StringComparison.OrdinalIgnoreCase))
        {
            result.LogLines.Add($"{monsterDisplayName}: {scope} treasure {token} deferred to worn equipment magic rules.");
            return;
        }

        if (!_tableProvider.TryGetTable(tableCode, out var table))
        {
            result.LogLines.Add($"{monsterDisplayName}: unknown {scope} treasure type '{token}'.");
            return;
        }

        if (overrideChance.HasValue)
        {
            var clamped = Math.Clamp(overrideChance.Value, 0d, 1d);
            var roll = _random.NextDouble();
            if (roll > clamped)
            {
                if (!string.Equals(scope, "lair", StringComparison.OrdinalIgnoreCase))
                    result.LogLines.Add($"{monsterDisplayName}: {scope} treasure type {token} skipped by override chance ({clamped:P0}).");
                return;
            }
        }

        var perRollChanceScale = gemJewelryMagicChanceScaleFactor;
        var perRollCountDivide = gemJewelryCountDivideFactor;
        if (scaledRepeats != repeats)
        {
            perRollChanceScale = 1d;
            perRollCountDivide = 1d;
            result.LogLines.Add($"{monsterDisplayName}: {scope} treasure {tableCode} repeats scaled by Lootfactor {repeatDivideFactor.ToString("0.###", CultureInfo.InvariantCulture)}: x{repeats} => x{scaledRepeats}.");
        }

        for (var i = 1; i <= scaledRepeats; i++)
        {
            if (scaledRepeats > 1)
                result.LogLines.Add($"{monsterDisplayName}: {scope} treasure {tableCode} roll {i}/{scaledRepeats}.");
            RollTable(table, tableCode, monsterDisplayName, result, amountScaleFactor, coinAmountScaleFactor, perRollChanceScale, gemJewelryValueScaleFactor, perRollCountDivide, adjustArtAmountByScale, suppressFailedRollLogs);
        }
    }

    private static (string tableCode, int repeats) ParseTreasureToken(string token)
    {
        var trimmed = token?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return (string.Empty, 1);

        var match = Regex.Match(trimmed, @"^(?<code>[A-Za-z]+)\s*(?:\(\s*x\s*(?<count>\d+)\s*\))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
            return (trimmed.ToUpperInvariant(), 1);

        var tableCode = match.Groups["code"].Value.Trim().ToUpperInvariant();
        var repeats = 1;
        if (match.Groups["count"].Success && int.TryParse(match.Groups["count"].Value, out var parsed))
            repeats = Math.Max(1, parsed);

        return (tableCode, repeats);
    }

    private void RollTable(
        TreasureTable table,
        string tableCode,
        string monsterDisplayName,
        TreasureResult result,
        double amountScaleFactor,
        double coinAmountScaleFactor,
        double gemJewelryMagicChanceScaleFactor,
        double gemJewelryValueScaleFactor,
        double gemJewelryCountDivideFactor,
        bool adjustArtAmountByScale,
        bool suppressFailedRollLogs)
    {
        var source = string.IsNullOrWhiteSpace(table.Name) ? tableCode : table.Name;
        result.LogLines.Add($"{monsterDisplayName}: rolling {source} ({tableCode}).");

        RollCoins("CP", table.Coins.CopperPieces, source, result, v => result.CopperPieces += v, coinAmountScaleFactor, suppressFailedRollLogs);
        RollCoins("SP", table.Coins.SilverPieces, source, result, v => result.SilverPieces += v, coinAmountScaleFactor, suppressFailedRollLogs);
        RollCoins("EP", table.Coins.ElectrumPieces, source, result, v => result.ElectrumPieces += v, coinAmountScaleFactor, suppressFailedRollLogs);
        RollCoins("GP", table.Coins.GoldPieces, source, result, v => result.GoldPieces += v, coinAmountScaleFactor, suppressFailedRollLogs);
        RollCoins("PP", table.Coins.PlatinumPieces, source, result, v => result.PlatinumPieces += v, coinAmountScaleFactor, suppressFailedRollLogs);

        var chanceScale = gemJewelryMagicChanceScaleFactor;
        RollValuables("Gem", table.Gems, source, result.Gems, result.LogLines, amountScaleFactor, chanceScale, gemJewelryValueScaleFactor, gemJewelryCountDivideFactor, suppressFailedRollLogs);
        RollValuables("Jewelry", table.Jewelry, source, result.Jewelry, result.LogLines, amountScaleFactor, chanceScale, gemJewelryValueScaleFactor, gemJewelryCountDivideFactor, suppressFailedRollLogs);
     //   RollValuables("Art", table.Art, source, result.Art, result.LogLines, adjustArtAmountByScale ? amountScaleFactor : 1d, 1d, 1d, 1d, suppressFailedRollLogs);

        foreach (var magicRule in table.MagicRolls)
        {
            if (!RollChance(
                    magicRule.ChancePercent,
                    $"Magic ({magicRule.Table}) chance",
                    result.LogLines,
                    chanceScale,
                    suppressFailedRollLogs))
                continue;

            var count = Math.Max(0, RollAmount(magicRule.AmountExpression, out var amountDetail));
            result.LogLines.Add($"    Amount roll ({magicRule.AmountExpression}) => {amountDetail}");
            count = ScaleAmount(count, amountScaleFactor, $"Magic ({magicRule.Table}) amount", result.LogLines);
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

    private void RollCoins(string label, TreasureRollRule rule, string source, TreasureResult result, Action<int> add, double amountScaleFactor, bool suppressFailedRollLogs)
    {
        if (rule.ChancePercent <= 0)
            return;

        if (!RollChance(rule.ChancePercent, $"{source} {label} chance", result.LogLines, 1d, suppressFailedRollLogs))
            return;

        var amount = Math.Max(0, RollAmount(rule.AmountExpression, out var amountDetail));
        result.LogLines.Add($"    Amount roll ({rule.AmountExpression}) => {amountDetail}");
        if (Math.Abs(amountScaleFactor - 1d) > 0.0001d)
        {
            var scaledAmount = Math.Max(0, (int)Math.Round(amount * Math.Max(0d, amountScaleFactor), MidpointRounding.AwayFromZero));
            result.LogLines.Add($"    {label} before Lootfactor: {amount}");
            result.LogLines.Add($"    {label} after Lootfactor x{amountScaleFactor.ToString("0.###", CultureInfo.InvariantCulture)}: {scaledAmount}");

            amount = scaledAmount;
        }
        if (amount <= 0)
            return;

        add(amount);
        result.LogLines.Add($"  + {label}: {amount}");
    }

    private void RollValuables(
        string category,
        TreasureValuablesRule rule,
        string source,
        List<TreasureValuableResult> target,
        List<string> logs,
        double amountScaleFactor,
        double chanceScaleFactor,
        double valueScaleFactor,
        double countDivideFactor,
        bool suppressFailedRollLogs)
    {
        if (!RollChance(rule.ChancePercent, $"{source} {category} chance", logs, chanceScaleFactor, suppressFailedRollLogs))
            return;

        var count = Math.Max(0, RollAmount(rule.AmountExpression, out var countDetail));
        logs.Add($"    Amount roll ({rule.AmountExpression}) => {countDetail}");
        count = ScaleAmount(count, amountScaleFactor, $"{source} {category} amount", logs);
        if (Math.Abs(countDivideFactor - 1d) > 0.0001d)
        {
            var factor = Math.Max(0d, countDivideFactor);
            var scaledCount = string.Equals(category, "Gem", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0, (int)Math.Round(count * factor, MidpointRounding.AwayFromZero))
                : Math.Max(0, (int)Math.Ceiling(count / Math.Max(0.0001d, factor)));

            if (string.Equals(category, "Gem", StringComparison.OrdinalIgnoreCase))
                logs.Add($"    {category} count after multiplying by Lootfactor {factor.ToString("0.###", CultureInfo.InvariantCulture)}: {count} => {scaledCount}");
            else
                logs.Add($"    {category} count after dividing by Lootfactor {factor.ToString("0.###", CultureInfo.InvariantCulture)} and rounding up: {count} => {scaledCount}");

            count = scaledCount;
        }

        if (count <= 0)
            return;

        for (int i = 0; i < count; i++)
        {
            var value = RollValuableBaseValue(category, rule, logs, i + 1);
            var scaledValue = value;
            if (Math.Abs(valueScaleFactor - 1d) > 0.0001d)
            {
                scaledValue = Math.Max(0, (int)Math.Round(value * Math.Max(0d, valueScaleFactor), MidpointRounding.AwayFromZero));
                logs.Add($"    {category} #{i + 1}: value before Lootfactor {value} gp, after Lootfactor x{valueScaleFactor.ToString("0.###", CultureInfo.InvariantCulture)} => {scaledValue} gp");
            }

            target.Add(new TreasureValuableResult
            {
                Category = category,
                ValueGp = scaledValue,
                SourceTable = source
            });
        }

        var total = target.Where(x => x.SourceTable == source && x.Category == category).TakeLast(count).Sum(x => x.ValueGp);
        logs.Add($"  + {category}: {count} item(s), total {total} gp");
    }

    private int RollValuableBaseValue(string category, TreasureValuablesRule rule, List<string> logs, int itemNumber)
    {
        if (string.Equals(category, "Gem", StringComparison.OrdinalIgnoreCase))
            return RollGemBaseValue(logs, itemNumber);

        if (string.Equals(category, "Jewelry", StringComparison.OrdinalIgnoreCase))
            return RollJewelryBaseValue(logs, itemNumber);

        var min = Math.Min(rule.MinValueGp, rule.MaxValueGp);
        var max = Math.Max(rule.MinValueGp, rule.MaxValueGp);
        var value = max <= 0 ? 0 : _random.Next(min, max + 1);
        logs.Add($"    {category} #{itemNumber}: value roll {value} gp (range {min}-{max})");
        return value;
    }

    private int RollGemBaseValue(List<string> logs, int itemNumber)
    {
        var roll = _random.Next(1, 101);
        var value = roll switch
        {
            <= 25 => 10,
            <= 50 => 50,
            <= 70 => 100,
            <= 90 => 500,
            <= 99 => 1000,
            _ => 5000
        };

        logs.Add($"    Gem #{itemNumber}: d100 roll {roll} => base value {value} gp");
        return value;
    }

    private int RollJewelryBaseValue(List<string> logs, int itemNumber)
    {
        var roll = _random.Next(1, 101);
        var (min, max) = roll switch
        {
            <= 10 => (100, 1000),
            <= 20 => (200, 1200),
            <= 40 => (300, 1800),
            <= 50 => (500, 3000),
            <= 70 => (1000, 6000),
            <= 90 => (2000, 8000),
            _ => (2000, 12000)
        };

        var value = _random.Next(min, max + 1);
        logs.Add($"    Jewelry #{itemNumber}: d100 roll {roll} => base range {min}-{max} gp, value {value} gp");
        return value;
    }

    private bool RollChance(int chancePercent, string context, List<string> logs, double chanceScaleFactor = 1d, bool suppressFailureLog = false)
    {
        var baseProbability = Math.Clamp(chancePercent / 100d, 0d, 1d);
        var probability = baseProbability;
        if (Math.Abs(chanceScaleFactor - 1d) > 0.0001d)
        {
            probability = Math.Clamp(baseProbability * Math.Max(0d, chanceScaleFactor), 0d, 1d);
            logs.Add($"    Chance scaling for {context}: base {baseProbability.ToString("0.######", CultureInfo.InvariantCulture)} x {chanceScaleFactor.ToString("0.###", CultureInfo.InvariantCulture)} => {probability.ToString("0.######", CultureInfo.InvariantCulture)}");
        }

        if (probability <= 0d)
            return false;

        if (probability >= 1d)
        {
            logs.Add($"    Chance roll for {context}: probability 1 => success");
            return true;
        }

        var roll = _random.NextDouble();
        var success = roll <= probability;
        if (success || !suppressFailureLog)
            logs.Add($"    Chance roll for {context}: rolled {roll.ToString("0.######", CultureInfo.InvariantCulture)} on [0,1) vs {probability.ToString("0.######", CultureInfo.InvariantCulture)} => {(success ? "success" : "fail")}");
        return success;
    }

    private static TreasureSnapshot CaptureSnapshot(TreasureResult result)
    {
        return new TreasureSnapshot(
            result.CopperPieces,
            result.SilverPieces,
            result.ElectrumPieces,
            result.GoldPieces,
            result.PlatinumPieces,
            result.Gems.Count,
            result.Jewelry.Count,
        //    result.Art.Count,
            result.MagicPlaceholders.Count);
    }

    private static void ApplyDeltaToBucket(TreasureSnapshot before, TreasureResult after, TreasureBucket bucket)
    {
        var cp = after.CopperPieces - before.CopperPieces;
        var sp = after.SilverPieces - before.SilverPieces;
        var ep = after.ElectrumPieces - before.ElectrumPieces;
        var gp = after.GoldPieces - before.GoldPieces;
        var pp = after.PlatinumPieces - before.PlatinumPieces;

        if (cp > 0) bucket.CopperPieces += cp;
        if (sp > 0) bucket.SilverPieces += sp;
        if (ep > 0) bucket.ElectrumPieces += ep;
        if (gp > 0) bucket.GoldPieces += gp;
        if (pp > 0) bucket.PlatinumPieces += pp;

        for (var i = before.GemsCount; i < after.Gems.Count; i++)
            bucket.Gems.Add(new TreasureValuableResult { Category = after.Gems[i].Category, ValueGp = after.Gems[i].ValueGp, SourceTable = after.Gems[i].SourceTable });

        for (var i = before.JewelryCount; i < after.Jewelry.Count; i++)
            bucket.Jewelry.Add(new TreasureValuableResult { Category = after.Jewelry[i].Category, ValueGp = after.Jewelry[i].ValueGp, SourceTable = after.Jewelry[i].SourceTable });

    //    for (var i = before.ArtCount; i < after.Art.Count; i++)
    //        bucket.Art.Add(new TreasureValuableResult { Category = after.Art[i].Category, ValueGp = after.Art[i].ValueGp, SourceTable = after.Art[i].SourceTable });

        for (var i = before.MagicPlaceholdersCount; i < after.MagicPlaceholders.Count; i++)
            bucket.MagicPlaceholders.Add(new TreasureMagicPlaceholderResult { Table = after.MagicPlaceholders[i].Table, Count = after.MagicPlaceholders[i].Count, SourceTable = after.MagicPlaceholders[i].SourceTable });
    }

    private static void PopulateTotalBucketFromLegacy(TreasureResult result)
    {
        result.Total.CopperPieces = result.CopperPieces;
        result.Total.SilverPieces = result.SilverPieces;
        result.Total.ElectrumPieces = result.ElectrumPieces;
        result.Total.GoldPieces = result.GoldPieces;
        result.Total.PlatinumPieces = result.PlatinumPieces;

        result.Total.Gems = result.Gems
            .Select(g => new TreasureValuableResult { Category = g.Category, ValueGp = g.ValueGp, SourceTable = g.SourceTable })
            .ToList();
        result.Total.Jewelry = result.Jewelry
            .Select(j => new TreasureValuableResult { Category = j.Category, ValueGp = j.ValueGp, SourceTable = j.SourceTable })
            .ToList();
   //     result.Total.Art = result.Art
   //         .Select(a => new TreasureValuableResult { Category = a.Category, ValueGp = a.ValueGp, SourceTable = a.SourceTable })
   //         .ToList();
        result.Total.MagicPlaceholders = result.MagicPlaceholders
            .Select(m => new TreasureMagicPlaceholderResult { Table = m.Table, Count = m.Count, SourceTable = m.SourceTable })
            .ToList();
    }

    private static bool HasFoundTreasure(TreasureSnapshot before, TreasureResult after)
    {
        return after.CopperPieces > before.CopperPieces
               || after.SilverPieces > before.SilverPieces
               || after.ElectrumPieces > before.ElectrumPieces
               || after.GoldPieces > before.GoldPieces
               || after.PlatinumPieces > before.PlatinumPieces
               || after.Gems.Count > before.GemsCount
               || after.Jewelry.Count > before.JewelryCount
        //       || after.Art.Count > before.ArtCount
               || after.MagicPlaceholders.Count > before.MagicPlaceholdersCount;
    }

    private readonly record struct TreasureSnapshot(
        int CopperPieces,
        int SilverPieces,
        int ElectrumPieces,
        int GoldPieces,
        int PlatinumPieces,
        int GemsCount,
        int JewelryCount,
   //     int ArtCount,
        int MagicPlaceholdersCount);

    private static int ScaleAmount(int amount, double factor, string context, List<string> logs)
    {
        if (Math.Abs(factor - 1d) <= 0.0001d)
            return amount;

        var scaled = Math.Max(0, (int)Math.Round(amount * Math.Max(0d, factor), MidpointRounding.AwayFromZero));
        logs.Add($"    Scale {context}: {amount} x {factor.ToString("0.###", CultureInfo.InvariantCulture)} => {scaled}");
        return scaled;
    }

    private static double GetExpectedAverageGroupSize(MonsterInstance monster)
    {
        var min = monster.Template.NumberOfAppearancesMin;
        var max = monster.Template.NumberOfAppearancesMax;
        var avg = (min + max) / 2d;
        return avg <= 0d ? 1d : avg;
    }

    private int RollAmount(string expression, out string detail)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            detail = "empty expression => 0";
            return 0;
        }

        var normalized = expression.Replace(" ", "", StringComparison.Ordinal);
        var parts = normalized.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var basePart = parts[0];
        var multiplier = 1;
        if (parts.Length > 1 && !int.TryParse(parts[1], out multiplier))
            multiplier = 1;

        var baseValue = EvaluateBase(basePart, out var baseDetail);
        var total = baseValue * multiplier;
        detail = multiplier == 1
            ? $"{baseDetail} => {total}"
            : $"{baseDetail}, x{multiplier} => {total}";
        return total;
    }

    private int EvaluateBase(string baseExpression, out string detail)
    {
        if (int.TryParse(baseExpression, out var fixedValue))
        {
            detail = fixedValue.ToString();
            return fixedValue;
        }

        var m = Regex.Match(baseExpression, @"^(?<count>\d+)d(?<sides>\d+)(?<mod>[+-]\d+)?$", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            detail = $"invalid expression '{baseExpression}' => 0";
            return 0;
        }

        var count = int.Parse(m.Groups["count"].Value);
        var sides = int.Parse(m.Groups["sides"].Value);
        var mod = m.Groups["mod"].Success ? int.Parse(m.Groups["mod"].Value) : 0;

        var sum = 0;
        var rolls = new List<int>();
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            var roll = _random.Next(1, Math.Max(2, sides) + 1);
            rolls.Add(roll);
            sum += roll;
        }

        var final = sum + mod;
        var modText = mod == 0 ? string.Empty : mod > 0 ? $"+{mod}" : mod.ToString();
        detail = $"{count}d{sides}{modText} [{string.Join(",", rolls)}] => {final}";
        return final;
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
