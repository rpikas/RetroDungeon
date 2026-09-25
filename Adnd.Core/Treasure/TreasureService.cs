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
        var linkedRuleMessages = new List<(string page, string message)>();

        foreach (var group in monsterList.GroupBy(m => string.IsNullOrWhiteSpace(m.GroupId) ? "default" : m.GroupId))
        {
            var members = group.ToList();
            if (members.Count == 0)
                continue;

            var representative = members[0];
            result.LogLines.Add($"Group {representative.GroupId}: non-individual treasure is rolled once for the whole group ({members.Count} monster(s)).");
            RollLairTreasure(representative, members.Count, result, linkedRuleMessages);

            foreach (var monster in members)
                RollIndividualTreasure(monster, result, linkedRuleMessages);
        }

        if (result.LogLines.Count > 0)
        {
            RuleApplicationInfo.Publish("Treasure roll details:");
            foreach (var line in result.LogLines)
                RuleApplicationInfo.Publish(line);
        }

        foreach (var linked in linkedRuleMessages)
            RuleApplicationInfo.PublishLinked("Treasure", linked.page, linked.message);

        PopulateTotalBucketFromLegacy(result);

        return result;
    }

    private void RollLairTreasure(MonsterInstance monster, int groupCount, TreasureResult result, List<(string page, string message)> linkedRuleMessages)
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
                monster.Template.DungeonLevel,
                result,
                linkedRuleMessages,
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

    private void RollIndividualTreasure(MonsterInstance monster, TreasureResult result, List<(string page, string message)> linkedRuleMessages)
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
                monster.Template.DungeonLevel,
                result,
                linkedRuleMessages,
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

        foreach (var magicLine in tokenLines.Where(IsMagicRollDetailLine))
            lines.Add(magicLine);

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

    private static bool IsMagicRollDetailLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return line.Contains("Magic item #", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Potions table (A)", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Rings table (C)", StringComparison.OrdinalIgnoreCase)
               || line.Contains("not defined yet, skipped", StringComparison.OrdinalIgnoreCase)
               || line.Contains("table key", StringComparison.OrdinalIgnoreCase);
    }

    private void RollTreasureToken(
        string token,
        string monsterDisplayName,
        int encounterLevel,
        TreasureResult result,
        List<(string page, string message)> linkedRuleMessages,
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
            RollWornEquipmentTreasure(
                monsterDisplayName,
                encounterLevel,
                result,
                linkedRuleMessages,
                scope,
                gemJewelryMagicChanceScaleFactor,
                suppressFailedRollLogs);
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

            RollMagicItems(magicRule.Table, count, result.LogLines);

            result.MagicPlaceholders.Add(new TreasureMagicPlaceholderResult
            {
                Table = magicRule.Table,
                Count = count,
                SourceTable = source
            });

            result.LogLines.Add($"  + Magic placeholder: {magicRule.Table} x{count}");
        }
    }

    private void RollMagicItems(string requestedTable, int count, List<string> logs)
    {
        for (var i = 1; i <= count; i++)
        {
            var normalized = (requestedTable ?? string.Empty).Trim();
            if (normalized.Equals("Any", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Any 3 plus 1 scroll", StringComparison.OrdinalIgnoreCase)
                || normalized.Length == 0)
            {
                RollFromAnyMagicItemTypeTable(i, logs);
                continue;
            }

            if (normalized.Equals("Potion", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Potions", StringComparison.OrdinalIgnoreCase))
            {
                var potionRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Potions (A) from table key '{requestedTable}'.");
                logs.Add($"      Potions table (A) d100 {potionRoll:00} => {ResolvePotionResult(potionRoll)} (placeholder).");
                continue;
            }

            if (normalized.Equals("Scroll", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Scrolls", StringComparison.OrdinalIgnoreCase))
            {
                var scrollRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Scrolls (B) from table key '{requestedTable}'.");
                logs.Add($"      Scrolls table (B) d100 {scrollRoll:00} => {ResolveScrollResult(scrollRoll)} (placeholder).");
                continue;
            }

            if (normalized.Equals("Rod", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Rods", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Staff", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Staves", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Wand", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Wands", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Rods/Staves/Wands", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Rods, Staves & Wands", StringComparison.OrdinalIgnoreCase))
            {
                var dRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Rods, Staves & Wands (D) from table key '{requestedTable}'.");
                logs.Add($"      Rods, Staves & Wands table (D) d100 {dRoll:00} => {ResolveRodStaffWandResult(dRoll)}.");
                continue;
            }

            if (normalized.Equals("E1", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("E.1", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Magic (E.1)", StringComparison.OrdinalIgnoreCase))
            {
                var e1Roll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Miscellaneous Magic (E.1) from table key '{requestedTable}'.");
                logs.Add($"      Miscellaneous Magic table (E.1) d100 {e1Roll:00} => {ResolveMiscMagicE1Result(e1Roll)}.");
                continue;
            }

            if (normalized.Equals("E2", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("E.2", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Magic (E.2)", StringComparison.OrdinalIgnoreCase))
            {
                var e2Roll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Miscellaneous Magic (E.2) from table key '{requestedTable}'.");
                logs.Add($"      Miscellaneous Magic table (E.2) d100 {e2Roll:00} => {ResolveMiscMagicE2Result(e2Roll)}.");
                continue;
            }

            if (normalized.Equals("E3", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("E.3", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Magic (E.3)", StringComparison.OrdinalIgnoreCase))
            {
                var e3Roll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Miscellaneous Magic (E.3) from table key '{requestedTable}'.");
                logs.Add($"      Miscellaneous Magic table (E.3) d100 {e3Roll:00} => {ResolveMiscMagicE3Result(e3Roll)}.");
                continue;
            }

            if (normalized.Equals("E4", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("E.4", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Magic (E.4)", StringComparison.OrdinalIgnoreCase))
            {
                var e4Roll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Miscellaneous Magic (E.4) from table key '{requestedTable}'.");
                logs.Add($"      Miscellaneous Magic table (E.4) d100 {e4Roll:00} => {ResolveMiscMagicE4Result(e4Roll)}.");
                continue;
            }

            if (normalized.Equals("E5", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("E.5", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Magic (E.5)", StringComparison.OrdinalIgnoreCase))
            {
                var e5Roll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Miscellaneous Magic (E.5) from table key '{requestedTable}'.");
                logs.Add($"      Miscellaneous Magic table (E.5) d100 {e5Roll:00} => {ResolveMiscMagicE5Result(e5Roll)}.");
                continue;
            }

            if (normalized.Equals("Armor", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Armour", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Shield", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Shields", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Armor & Shields", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Armour & Shields", StringComparison.OrdinalIgnoreCase))
            {
                var fRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Armor & Shields (F) from table key '{requestedTable}'.");
                logs.Add($"      Armor & Shields table (F) d100 {fRoll:00} => {ResolveArmorShieldResult(fRoll)}.");
                continue;
            }

            if (normalized.Equals("Sword", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Swords", StringComparison.OrdinalIgnoreCase))
            {
                var gRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Swords (G) from table key '{requestedTable}'.");
                logs.Add($"      Swords table (G) d100 {gRoll:00} => {ResolveSwordResult(gRoll)}.");
                continue;
            }

            if (normalized.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Weapons", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Weapon", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Miscellaneous Weapons", StringComparison.OrdinalIgnoreCase))
            {
                var hRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Miscellaneous Weapons (H) from table key '{requestedTable}'.");
                logs.Add($"      Miscellaneous Weapons table (H) d100 {hRoll:00} => {ResolveMiscWeaponResult(hRoll)}.");
                continue;
            }

            if (normalized.Equals("Ring", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Rings", StringComparison.OrdinalIgnoreCase))
            {
                var ringRoll = _random.Next(1, 101);
                logs.Add($"    Magic item #{i}: forced Rings (C) from table key '{requestedTable}'.");
                logs.Add($"      Rings table (C) d100 {ringRoll:00} => {ResolveRingResult(ringRoll)} (placeholder).");
                continue;
            }

            logs.Add($"    Magic item #{i}: table key '{requestedTable}' not mapped to a defined subtable yet, skipped.");
        }
    }

    private void RollFromAnyMagicItemTypeTable(int itemNumber, List<string> logs)
    {
        var typeRoll = _random.Next(1, 101);

        if (typeRoll <= 20)
        {
            var potionRoll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Potions (A).");
            logs.Add($"      Potions table (A) d100 {potionRoll:00} => {ResolvePotionResult(potionRoll)} (placeholder).");
            return;
        }

        if (typeRoll <= 35)
        {
            var scrollRoll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Scrolls (B).");
            logs.Add($"      Scrolls table (B) d100 {scrollRoll:00} => {ResolveScrollResult(scrollRoll)} (placeholder).");
            return;
        }

        if (typeRoll <= 40)
        {
            var ringRoll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Rings (C).");
            logs.Add($"      Rings table (C) d100 {ringRoll:00} => {ResolveRingResult(ringRoll)} (placeholder).");
            return;
        }

        if (typeRoll <= 45)
        {
            var dRoll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Rods, Staves & Wands (D).");
            logs.Add($"      Rods, Staves & Wands table (D) d100 {dRoll:00} => {ResolveRodStaffWandResult(dRoll)}.");
            return;
        }

        if (typeRoll <= 48)
        {
            var e1Roll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Miscellaneous Magic (E.1).");
            logs.Add($"      Miscellaneous Magic table (E.1) d100 {e1Roll:00} => {ResolveMiscMagicE1Result(e1Roll)}.");
            return;
        }

        if (typeRoll <= 51)
        {
            var e2Roll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Miscellaneous Magic (E.2).");
            logs.Add($"      Miscellaneous Magic table (E.2) d100 {e2Roll:00} => {ResolveMiscMagicE2Result(e2Roll)}.");
            return;
        }

        if (typeRoll <= 54)
        {
            var e3Roll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Miscellaneous Magic (E.3).");
            logs.Add($"      Miscellaneous Magic table (E.3) d100 {e3Roll:00} => {ResolveMiscMagicE3Result(e3Roll)}.");
            return;
        }

        if (typeRoll <= 57)
        {
            var e4Roll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Miscellaneous Magic (E.4).");
            logs.Add($"      Miscellaneous Magic table (E.4) d100 {e4Roll:00} => {ResolveMiscMagicE4Result(e4Roll)}.");
            return;
        }

        if (typeRoll <= 60)
        {
            var e5Roll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Miscellaneous Magic (E.5).");
            logs.Add($"      Miscellaneous Magic table (E.5) d100 {e5Roll:00} => {ResolveMiscMagicE5Result(e5Roll)}.");
            return;
        }

        if (typeRoll <= 75)
        {
            var fRoll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Armor & Shields (F).");
            logs.Add($"      Armor & Shields table (F) d100 {fRoll:00} => {ResolveArmorShieldResult(fRoll)}.");
            return;
        }

        if (typeRoll <= 86)
        {
            var gRoll = _random.Next(1, 101);
            logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Swords (G).");
            logs.Add($"      Swords table (G) d100 {gRoll:00} => {ResolveSwordResult(gRoll)}.");
            return;
        }

        var hTypeRoll = _random.Next(1, 101);
        logs.Add($"    Magic item #{itemNumber}: d100 {typeRoll:00} => Miscellaneous Weapons (H).");
        logs.Add($"      Miscellaneous Weapons table (H) d100 {hTypeRoll:00} => {ResolveMiscWeaponResult(hTypeRoll)}.");
    }

    private static string ResolvePotionResult(int roll)
    {
        return roll switch
        {
            <= 3 => "Animal Control",
            <= 6 => "Clairaudience",
            <= 9 => "Clairvoyance",
            <= 12 => "Climbing",
            <= 15 => "Delusion",
            <= 18 => "Diminution",
            <= 20 => "Dragon Control",
            <= 23 => "ESP",
            <= 26 => "Extra-Healing",
            <= 29 => "Fire Resistance",
            <= 32 => "Flying",
            <= 34 => "Gaseous Form",
            <= 36 => "Giant Control",
            <= 39 => "Giant Strength",
            <= 41 => "Growth",
            <= 47 => "Healing",
            <= 49 => "Heroism",
            <= 51 => "Human Control",
            <= 54 => "Invisibility",
            <= 57 => "Invulnerability",
            <= 60 => "Levitation",
            <= 63 => "Longevity",
            <= 66 => "Oil of Etherealness",
            <= 69 => "Oil of Slipperiness",
            <= 72 => "Philter of Love",
            <= 75 => "Philter of Persuasiveness",
            <= 78 => "Plant Control",
            <= 81 => "Polymorph (self)",
            <= 84 => "Poison",
            <= 87 => "Speed",
            <= 90 => "Super-Heroism",
            <= 93 => "Sweet Water",
            <= 96 => "Treasure Finding",
            <= 97 => "Undead Control",
            _ => "Water Breathing"
        };
    }

    private static string ResolveScrollResult(int roll)
    {
        return roll switch
        {
            <= 10 => "1 spell (levels 1-4)",
            <= 16 => "1 spell (levels 1-6)",
            <= 19 => "1 spell (levels 2-9, d8+1; or 2-7*, d6+1)",
            <= 24 => "2 spells (levels 1-4)",
            <= 27 => "2 spells (levels 1-8; or 1-6*)",
            <= 32 => "3 spells (levels 1-4)",
            <= 35 => "3 spells (levels 2-9; or 2-7*)",
            <= 39 => "4 spells (levels 1-6)",
            <= 42 => "4 spells (levels 1-8; or 1-6*)",
            <= 46 => "5 spells (levels 1-6)",
            <= 49 => "5 spells (levels 1-8; or 1-6*)",
            <= 52 => "6 spells (levels 1-6)",
            <= 54 => "6 spells (levels 3-8, d6+2; or 3-6*, d4+2)",
            <= 57 => "7 spells (levels 1-8)",
            <= 59 => "7 spells (levels 2-9)",
            <= 60 => "7 spells (levels 4-9, d6+3; or 4-7*, d4+3)",
            <= 62 => "Protection — Demons",
            <= 64 => "Protection — Devils",
            <= 70 => "Protection — Elementals",
            <= 76 => "Protection — Lycanthropes",
            <= 82 => "Protection — Magic",
            <= 87 => "Protection — Petrification",
            <= 92 => "Protection — Possession",
            <= 97 => "Protection — Undead",
            _ => "Curse"
        };
    }

    private static string ResolveRingResult(int roll)
    {
        return roll switch
        {
            <= 6 => "Contrariness",
            <= 12 => "Delusion",
            <= 14 => "Djinn Summoning",
            <= 15 => "Elemental Command",
            <= 21 => "Feather Falling",
            <= 27 => "Fire Resistance",
            <= 30 => "Free Action",
            <= 33 => "Human Influence",
            <= 40 => "Invisibility",
            <= 43 => "Mammal Control",
            <= 44 => "Multiple Wishes",
            <= 60 => "Protection",
            <= 61 => "Regeneration",
            <= 63 => "Shooting Stars",
            <= 65 => "Spell Storing",
            <= 69 => "Spell Turning",
            <= 75 => "Swimming",
            <= 77 => "Telekinesis",
            <= 79 => "Three Wishes",
            <= 85 => "Warmth",
            <= 90 => "Water Walking",
            <= 98 => "Weakness",
            <= 99 => "Wizardry",
            _ => "X-Ray Vision"
        };
    }

    private static string ResolveRodStaffWandResult(int roll)
    {
        return roll switch
        {
            <= 3 => "Rod of Absorption (gp 40000, xp 7500)",
            <= 4 => "Rod of Beguiling (gp 30000, xp 5000)",
            <= 14 => "Rod of Cancellation (gp 15000, xp 10000)",
            <= 16 => "Rod of Lordly Might (gp 20000, xp 6000)",
            <= 17 => "Rod of Resurrection (gp 35000, xp 10000)",
            <= 18 => "Rod of Rulership (gp 35000, xp 8000)",
            <= 19 => "Rod of Smiting (gp 15000, xp 4000)",
            <= 20 => "Staff of Command (gp 25000, xp 5000)",
            <= 22 => "Staff of Curing (gp 25000, xp 6000)",
            <= 23 => "Staff of the Magi (gp 75000, xp 15000)",
            <= 24 => "Staff of Power (gp 60000, xp 12000)",
            <= 27 => "Staff of the Serpent (gp 35000, xp 7000)",
            <= 31 => "Staff of Striking (gp 15000, xp 6000)",
            <= 33 => "Staff of Withering (gp 35000, xp 8000)",
            <= 34 => "Wand of Conjuration (gp 35000, xp 7000)",
            <= 38 => "Wand of Enemy Detection (gp 10000, xp 2000)",
            <= 41 => "Wand of Fear (gp 15000, xp 3000)",
            <= 44 => "Wand of Fire (gp 25000, xp 4500)",
            <= 47 => "Wand of Frost (gp 50000, xp 6000)",
            <= 52 => "Wand of Illumination (gp 10000, xp 2000)",
            <= 56 => "Wand of Illusion (gp 20000, xp 3000)",
            <= 59 => "Wand of Lightning (gp 30000, xp 4000)",
            <= 68 => "Wand of Magic Detection (gp 25000, xp 2500)",
            <= 73 => "Wand of Metal & Mineral Detection (gp 7500, xp 1500)",
            <= 78 => "Wand of Magic Missiles (gp 35000, xp 4000)",
            <= 86 => "Wand of Negation (gp 15000, xp 3500)",
            <= 89 => "Wand of Paralysis (gp 25000, xp 3500)",
            <= 92 => "Wand of Polymorphing (gp 25000, xp 3500)",
            <= 94 => "Wand of Secret Door & Trap Location (gp 40000, xp 5000)",
            _ => "Wand of Wonder (gp 10000, xp 6000)"
        };
    }

    private static string ResolveMiscMagicE1Result(int roll)
    {
        return roll switch
        {
            <= 2 => "Alchemy Jug (gp 12000, xp 3000)",
            <= 4 => "Amulet of Inescapable Location (gp 1000, xp ---)",
            <= 5 => "Amulet of Life Protection (gp 20000, xp 5000)",
            <= 7 => "Amulet of the Planes (gp 30000, xp 6000)",
            <= 11 => "Amulet of Proof Against Detection and Location (gp 15000, xp 4000)",
            <= 13 => "Apparatus of Kwalish (gp 35000, xp 8000)",
            <= 16 => "Arrow of Direction (gp 17500, xp 2500)",
            <= 17 => "Artifact or Relic (special table)",
            <= 20 => "Bag of Beans (gp 5000, xp 1000)",
            <= 21 => "Bag of Devouring (gp 1500, xp ---)",
            <= 26 => "Bag of Holding (gp 25000, xp 5000)",
            <= 27 => "Bag of Transmuting (gp 500, xp ---)",
            <= 29 => "Bag of Tricks (gp 15000, xp 2500)",
            <= 31 => "Beaker of Plentyful Potions (gp 12500, xp 1500)",
            <= 32 => "Boat, Folding (gp 25000, xp 10000)",
            <= 33 => "Book of Exalted Deeds (gp 40000, xp 8000)",
            <= 34 => "Book of Infinite Spells (gp 50000, xp 9000)",
            <= 35 => "Book of Vile Darkness (gp 40000, xp 8000)",
            <= 36 => "Boats of Dancing (gp 5000, xp ---)",
            <= 42 => "Boots of Elvenkind (gp 5000, xp 1000)",
            <= 47 => "Boots of Levitation (gp 15000, xp 2000)",
            <= 51 => "Boots of Speed (gp 20000, xp 2500)",
            <= 55 => "Boots of Striding and Springing (gp 20000, xp 2500)",
            <= 58 => "Bowl Commanding Water Elementals (gp 25000, xp 4000)",
            <= 59 => "Bowl of Watery Death (gp 1000, xp ---)",
            <= 79 => "Bracers of Defense (gp 3000*, xp 500*)",
            <= 81 => "Bracers of Defenselessness (gp 2000, xp ---)",
            <= 84 => "Brazier Commanding Fire Elementals (gp 25000, xp 4000)",
            <= 85 => "Brazier of Sleep Smoke (gp 1000, xp ---)",
            <= 92 => "Brooch of Shielding (gp 10000, xp 1000)",
            <= 93 => "Broom of Animated Attack (gp 3000, xp ---)",
            <= 98 => "Broom of Flying (gp 10000, xp 2000)",
            _ => "Bucknard's Everfull Purse (gp 15000/25000/40000, xp 1500/2500/4000)"
        };
    }

    private static string ResolveMiscMagicE2Result(int roll)
    {
        return roll switch
        {
            <= 6 => "Candle of Invocation (gp 5000, xp 1000)",
            <= 8 => "Carpet of Flying (gp 25000, xp 7500)",
            <= 10 => "Censer Controlling Air Elementals (gp 25000, xp 4000)",
            <= 11 => "Censer of Summoning Hostile Air Elementals (gp 1000, xp ---)",
            <= 13 => "Chime of Opening (gp 20000, xp 3500)",
            <= 14 => "Chime of Hunger (gp ---, xp ---)",
            <= 18 => "Cloak of Displacement (gp 17500, xp 3000)",
            <= 27 => "Cloak of Elvenkind (gp 6000, xp 1000)",
            <= 30 => "Cloak of Mana Ray (gp 12500, xp 2000)",
            <= 32 => "Cloak of Poisonousness (gp 2500, xp ---)",
            <= 55 => "Cloak of Protection (gp 10000*, xp 1000*)",
            <= 60 => "Crystal Ball (gp 5000**, xp 1000**)",
            <= 61 => "Crystal Hypnosis Ball (gp 3000, xp ---)",
            <= 63 => "Cube of Force (xp 3, gp 20)",
            <= 65 => "Cube of Frost Resistance (xp 2, gp 14)",
            <= 67 => "Cubic Gate (xp 5, gp 17,5)",
            <= 69 => "Daern's Instant Fortress (xp 7, gp 27,5)",
            <= 72 => "Decanter of Endless Water (xp 1, gp 10)",
            <= 76 => "Deck of Many Things (xp —, gp 10)",
            <= 77 => "Drums of Deafening (xp —, gp —)",
            <= 79 => "Drums of Panic (xp 6,5, gp 35)",
            <= 85 => "Dust of Appearance (xp 1, gp —)",
            <= 91 => "Dust of Disappearance (xp 2, gp 8)",
            <= 92 => "Dust of Sneezing and Choking (xp —, gp —)",
            <= 93 => "Efreeti Bottle (xp 9, gp 45)",
            <= 94 => "Eversmoking Bottle (xp 500, gp 2,5)",
            <= 95 => "Eyes of Charming (xp 4, gp 24)",
            <= 97 => "Eyes of the Eagle (xp 3,5, gp 18)",
            <= 99 => "Eyes of Minute Seeing (xp 2, gp 12,5)",
            _ => "Eyes of Petrification (xp —, gp —)"
        };
    }

    private static string ResolveMiscMagicE3Result(int roll)
    {
        return roll switch
        {
            <= 15 => "Figurine of Wondrous Power (gp 1000*, xp 100*)",
            <= 16 => "Flask of Curses (gp 1000, xp ---)",
            <= 18 => "Gauntlets of Dexterity (gp 10000, xp 1000)",
            <= 20 => "Gauntlets of Fumbling (gp 1000, xp ---)",
            <= 22 => "Gauntlets of Ogre Power (gp 15000, xp 1000)",
            <= 25 => "Gauntlets of Swimming and Climbing (gp 10000, xp 1000)",
            <= 26 => "Gem of Brightness (gp 17500, xp 2000)",
            <= 27 => "Gem of Seeing (gp 25000, xp 2000)",
            <= 28 => "Girdle of Femininity/Masculinity (gp 1000, xp ---)",
            <= 29 => "Girdle of Giant Strength (gp 2500, xp 200)",
            <= 30 => "Helm of Brilliance (gp 60000, xp 2500)",
            <= 35 => "Helm of Comprehending Languages & Reading Magic (gp 12500, xp 1000)",
            <= 37 => "Helm of Opposite Alignment (gp 1000, xp ---)",
            <= 39 => "Helm of Telepathy (gp 35000, xp 3000)",
            <= 40 => "Helm of Teleportation (gp 30000, xp 2500)",
            <= 45 => "Helm of Underwater Action (gp 10000, xp 1000)",
            <= 46 => "Horn of Blasting (gp 55000, xp 5000)",
            <= 48 => "Horn of Bubbles (gp ---, xp ---)",
            <= 49 => "Horn of Collapsing (gp 25000, xp 1500)",
            <= 53 => "Horn of the Tritons (gp 17500, xp 2000)",
            <= 60 => "Horn of Valhalla (gp 15000**, xp 1000**)",
            <= 63 => "Horseshoes of Speed (gp 10000, xp 2000)",
            <= 65 => "Horseshoes of a Zephyr (gp 7500, xp 1500)",
            <= 70 => "Incense of Meditation (gp 7500, xp 500)",
            <= 71 => "Incense of Obsession (gp 500, xp ---)",
            <= 72 => "Ioun Stones (gp 5000***, xp 300***)",
            <= 78 => "Instrument of the Bards (gp 5000****, xp 1000****)",
            <= 80 => "Iron Flask (gp ---, xp ---)",
            <= 85 => "Javelin of Lightning (gp 3000, xp 250)",
            <= 90 => "Javelin of Piercing (gp 3000, xp 250)",
            <= 91 => "Jewel of Attacks (gp 1000, xp ---)",
            <= 92 => "Jewel of Flawlessness (gp 1000/facet, xp ---)",
            _ => "Keoghtom's Ointment (gp 10000, xp 500)"
        };
    }

    private static string ResolveMiscMagicE4Result(int roll)
    {
        return roll switch
        {
            <= 1 => "Libram of Gainful Conjuration (gp 40000, xp 8000)",
            <= 2 => "Libram of Ineffable Damnation (gp 40000, xp 8000)",
            <= 3 => "Libram of Silver Magic (gp 40000, xp 8000)",
            <= 4 => "Lyre of Building (gp 30000, xp 5000)",
            <= 5 => "Manual of Bodily Health (gp 50000, xp 5000)",
            <= 6 => "Manual of Gainful Exercise (gp 50000, xp 5000)",
            <= 7 => "Manual of Golems (gp 30000, xp 3000)",
            <= 8 => "Manual of Puissant Skill at Arms (gp 40000, xp 8000)",
            <= 9 => "Manual of Quickness of Action (gp 50000, xp 5000)",
            <= 10 => "Manual of Stealthy Pilfering (gp 40000, xp 8000)",
            <= 11 => "Mattock of the Titans (gp 7000, xp 3500)",
            <= 12 => "Maul of the Titans (gp 12000, xp 4000)",
            <= 15 => "Medallion of ESP (gp 10000/30000, xp 1000/3000)",
            <= 17 => "Medallion of Thought Projection (gp 1000, xp ---)",
            <= 18 => "Mirror of Life Trapping (gp 25000, xp 2500)",
            <= 19 => "Mirror of Mental Prowess (gp 50000, xp 5000)",
            <= 20 => "Mirror of Opposition (gp 2000, xp ---)",
            <= 23 => "Necklace of Adaptation (gp 10000, xp 1000)",
            <= 27 => "Necklace of Missiles (gp 200*, xp 50*)",
            <= 33 => "Necklace of Prayer Beads (gp 3000**, xp 500**)",
            <= 35 => "Necklace of Strangulation (gp 1000, xp ---)",
            <= 38 => "Net of Entrapment (gp 7500, xp 1000)",
            <= 42 => "Net of Snaring (gp 6000, xp 1000)",
            <= 44 => "Nolzur's Marvelous Pigments (gp 3000***, xp 500***)",
            <= 46 => "Pearl of Power (gp 2000****, xp 200****)",
            <= 48 => "Pearl of Wisdom (gp 5000, xp 500)",
            <= 50 => "Periapt of Foul Rotting (gp 1000, xp ---)",
            <= 53 => "Periapt of Health (gp 10000, xp 1000)",
            <= 60 => "Periapt of Proof Against Poison (gp 12500, xp 1500)",
            <= 64 => "Periapt of Wound Closure (gp 10000, xp 1000)",
            <= 70 => "Phylactery of Faithfulness (gp 7500, xp 1000)",
            <= 74 => "Phylactery of Long Years (gp 25000, xp 3000)",
            <= 76 => "Phylactery of Monstrous Attention (gp 2000, xp ---)",
            <= 84 => "Pipes of the Sewers (gp 8500, xp 1750)",
            <= 85 => "Portable Hole (gp 50000, xp 5000)",
            _ => "Quaals Feather Token (gp 2000/7000, xp 500/1000)"
        };
    }

    private static string ResolveMiscMagicE5Result(int roll)
    {
        return roll switch
        {
            <= 1 => "Robe of the Archmagi (xp 6, gp 65)",
            <= 8 => "Robe of Blending (xp 3,5, gp 35)",
            <= 9 => "Robe of Eyes (xp 4,5, gp 45)",
            <= 10 => "Robe of Powerlessness (xp —, gp 1)",
            <= 11 => "Robe of Scintillating Colors (xp 2,75, gp 27,5)",
            <= 19 => "Robe of Useful Items (xp 1,5, gp 15)",
            <= 25 => "Rope of Climbing (xp 1, gp 10)",
            <= 27 => "Rope of Constriction (xp 1,5, gp 12)",
            <= 31 => "Rope of Entanglement (xp 1,25, gp 12,5)",
            <= 32 => "Rug of Smothering (xp —, gp 1)",
            <= 33 => "Rug of Welcome (xp 6,5, gp 45)",
            <= 34 => "Saw of Mighty Cutting (xp 1,75, gp 12,5)",
            <= 35 => "Scarab of Death (xp —, gp 1)",
            <= 38 => "Scarab of Enraging Enemies (xp 1, gp 10)",
            <= 44 => "Scarab of Insanity (xp 1,5, gp 11)",
            <= 45 => "Scarab of Protection (xp 2,5, gp 25)",
            <= 46 => "E.5 entry missing in provided table (46)",
            <= 47 => "Spade of Colossal Excavation (xp 3,75, gp 6,5)",
            <= 48 => "Sphere of Annihilation (xp —, gp 30)",
            <= 50 => "Stone of Controlling Earth Elementals (xp 1,5, gp 12,5)",
            <= 52 => "Stone of Good Luck (Luckstone) (xp 3, gp 25)",
            <= 54 => "Stone of Weight (Loadstone) (xp —, gp 1)",
            <= 57 => "Talisman of Pure Good (xp 3,5, gp 27,5)",
            <= 58 => "Talisman of the Sphere (xp 3,5, gp 30)",
            <= 60 => "Talisman of Ultimate Evil (xp 3,5, gp 27,5)",
            <= 66 => "Talisman of Zagy (xp —, gp 1)",
            <= 67 => "Tome of Clear Thought (xp 8, gp 48)",
            <= 68 => "Tome of Leadership and Influence (xp 7,5, gp 40)",
            <= 69 => "Tome of Understanding (xp 7,5, gp 40)",
            <= 76 => "Trident of Fish Command (xp 1,5, gp 15)",
            <= 78 => "Trident of Submission (xp 1,5, gp 15)",
            <= 83 => "Trident of Warning (xp 1, gp 10)",
            <= 85 => "Trident of Yearning (xp —, gp 1)",
            <= 87 => "Vacuous Grimoire (xp —, gp 12)",
            <= 90 => "Well of Many Worlds (xp 6, gp 12)",
            _ => "Wings of Flying (xp 750, gp 7,5)"
        };
    }

    private static string ResolveArmorShieldResult(int roll)
    {
        return roll switch
        {
            <= 5 => "Chain Mail +1 (xp 600, gp 3,500)",
            <= 9 => "Chain Mail +2 (xp 1,200, gp 7,500)",
            <= 11 => "Chain Mail +3 (xp 2,000, gp 12,500)",
            <= 19 => "Leather Armor +1 (xp 150, gp 750)",
            <= 26 => "Plate Mail +1 (xp 800, gp 5,000)",
            <= 32 => "Plate Mail +2 (xp 1,750, gp 10,000)",
            <= 35 => "Plate Mail +3 (xp 3,500, gp 20,000)",
            <= 37 => "Plate Mail +4 (xp 7,500, gp 25,000)",
            <= 38 => "Plate Mail +5 (xp 15,000, gp 30,000)",
            <= 39 => "Plate Mail of Etherealness (xp 5,000, gp 30,000)",
            <= 44 => "Plate Mail of Vulnerability (xp —, gp —)",
            <= 50 => "Ring Mail +1 (xp 400, gp 2,000)",
            <= 53 => "Scale Mail +1 (xp 600, gp 3,500)",
            <= 60 => "Scale Mail +2 (xp 1,100, gp 7,500)",
            <= 65 => "Splint Mail +1 (xp 600, gp 3,500)",
            <= 68 => "Splint Mail +2 (xp 1,250, gp 7,500)",
            <= 69 => "Splint Mail +3 (xp 2,500, gp 14,500)",
            <= 75 => "Studded Leather +1 (xp 400, gp 2,000)",
            <= 84 => "Shield +1 (xp 300, gp 1,000)",
            <= 89 => "Shield +2 (xp 500, gp 3,500)",
            <= 93 => "Shield +3 (xp 800, gp 7,500)",
            <= 95 => "Shield +4 (xp 1,200, gp 12,000)",
            <= 96 => "Shield +5 (xp 1,750, gp 15,000)",
            <= 97 => "Shield, large, +1, +4 vs. missiles (xp 400, gp 4,000)",
            _ => "Shield -1, missile attractor (xp —, gp 750)"
        };
    }

    private static string ResolveSwordResult(int roll)
    {
        return roll switch
        {
            <= 25 => "Sword +1 (xp 400, gp 2,000)",
            <= 30 => "Sword +1, +2 vs. magic-using & enchanted creatures (xp 600, gp 3,000)",
            <= 35 => "Sword +1, +3 vs. lycanthropes & shape changers (xp 700, gp 3,500)",
            <= 40 => "Sword +1, +3 vs. regenerating creatures (xp 800, gp 4,000)",
            <= 45 => "Sword +1, +4 vs. reptiles (xp 800, gp 4,000)",
            <= 49 => "Sword +1, Flame Tongue (+2 vs. regenerating, +3 vs. cold-using/inflammable/avian, +4 vs. undead) (xp 900, gp 4,500)",
            <= 50 => "Sword +1, Luck Blade (xp 1,000, gp 5,000)",
            <= 58 => "Sword +2 (xp 800, gp 4,000)",
            <= 62 => "Sword +2, Giant Slayer (xp 900, gp 4,500)",
            <= 66 => "Sword +2, Dragon Slayer (xp 900, gp 4,500)",
            <= 67 => "Sword +2, Nine Lives Stealer (xp 1,600, gp 8,000)",
            <= 71 => "Sword +3 (xp 1,400, gp 7,000)",
            <= 74 => "Sword +3, Frost Brand (+6 vs. fire-using/dwelling creatures) (xp 1,600, gp 8,000)",
            <= 76 => "Sword +4 (xp 2,000, gp 10,000)",
            <= 77 => "Sword +4, Defender (xp 3,000, gp 15,000)",
            <= 78 => "Sword +5 (xp 3,000, gp 15,000)",
            <= 79 => "Sword +5, Defender (xp 3,600, gp 18,000)",
            <= 80 => "Sword +5, Holy Avenger (xp 4,000, gp 20,000)",
            <= 81 => "Sword of Dancing (xp 4,400, gp 22,000)",
            <= 82 => "Sword of Wounding (xp 4,400, gp 22,000)",
            <= 83 => "Sword of Life Stealing (xp 5,000, gp 25,000)",
            <= 84 => "Sword of Sharpness (xp 7,000, gp 35,000)",
            <= 85 => "Sword, Vorpal Weapon (xp 10,000, gp 50,000)",
            <= 90 => "Sword +1, Cursed (xp 400, gp —)",
            _ => "Sword, Cursed Berserking (xp 900, gp —)"
        };
    }

    private static string ResolveMiscWeaponResult(int roll)
    {
        return roll switch
        {
            <= 8 => "Arrow +1, 2-24 in number (xp 20, gp 120)",
            <= 12 => "Arrow +2, 2-16 in number (xp 50, gp 300)",
            <= 14 => "Arrow +3, 2-12 in number (xp 75, gp 450)",
            <= 15 => "Arrow of Slaying (xp 250, gp 2,5)",
            <= 20 => "Axe +1 (xp 300, gp 1,75)",
            <= 22 => "Axe +2 (xp 600, gp 3,5)",
            <= 23 => "Axe +2, Throwing (xp 750, gp 4,5)",
            <= 24 => "Axe +3 (xp 1, gp 7)",
            <= 27 => "Battle Axe +1 (xp 400, gp 2,5)",
            <= 32 => "Bolt +2, 2-20 in number (xp 50, gp 300)",
            <= 35 => "Bow +1 (xp 500, gp 3,5)",
            <= 36 => ResolveCrossbowOfAccuracyResult(),
            <= 37 => ResolveCrossbowOfDistanceResult(),
            <= 38 => ResolveCrossbowOfSpeedResult(),
            <= 46 => "Dagger +1, +2 vs. creatures smaller than man-sized (xp 100, gp 750)",
            <= 50 => "Dagger +2, +3 vs. creatures larger than man-sized (xp 250, gp 2)",
            <= 51 => "Dagger of Venom (xp —, gp 3)",
            <= 56 => "Flail +1 (xp 450, gp 2,5)",
            <= 60 => "Hammer +1 (xp 300, gp 2,5)",
            <= 62 => "Hammer +2 (xp 600, gp 6)",
            <= 63 => "Hammer +3, Dwarven Thrower (xp 1,5, gp 15)",
            <= 64 => "Hammer of Thunderbolts (xp 2,5, gp 25)",
            <= 67 => "Javelin +2 (xp 750, gp 5)",
            <= 72 => "Mace +1 (xp 350, gp 2)",
            <= 75 => "Mace +2 (xp 700, gp 4,5)",
            <= 76 => "Mace of Disruption (xp 1,75, gp 17,5)",
            <= 77 => "Mace +4 (xp 1,5, gp 15)",
            <= 80 => "Military Pick +1 (xp 400, gp 2,5)",
            <= 83 => "Morning Star +1 (xp 400, gp 3)",
            <= 88 => "Scimitar +2 (xp 750, gp 6)",
            <= 89 => "Sling of Seeking +2 (xp 1, gp 8)",
            <= 94 => "Spear +1 (xp 500, gp 3)",
            <= 96 => "Spear +2 (xp 1, gp 6,5)",
            <= 97 => "Spear +3 (xp 1,75, gp 15)",
            <= 99 => "Spear, Cursed Backbiter (xp —, gp —)",
            _ => "Trident (Military Fork) +3 (xp 1,5, gp 12,5)"
        };
    }

    private static string ResolveCrossbowOfAccuracyResult()
    {
        var heavyCrossbowRoll = Random.Shared.Next(1, 101);
        return heavyCrossbowRoll <= 10
            ? "Crossbow of Accuracy, +3 (Heavy Crossbow; all ranges considered short; xp 2, gp 12)"
            : "Crossbow of Accuracy, +3 (all ranges considered short; xp 2, gp 12)";
    }

    private static string ResolveCrossbowOfDistanceResult()
    {
        var heavyCrossbowRoll = Random.Shared.Next(1, 101);
        return heavyCrossbowRoll <= 10
            ? "Crossbow of Distance (Heavy Crossbow; double range all categories; +1 to hit and damage; xp 1,5, gp 7,5)"
            : "Crossbow of Distance (double range all categories; +1 to hit and damage; xp 1,5, gp 7,5)";
    }

    private static string ResolveCrossbowOfSpeedResult()
    {
        var heavyCrossbowRoll = Random.Shared.Next(1, 101);
        return heavyCrossbowRoll <= 10
            ? "Crossbow of Speed (Heavy Crossbow; doubled normal rate of fire; +1 bonus; xp 1,5, gp 7,5)"
            : "Crossbow of Speed (doubled normal rate of fire; +1 bonus; xp 1,5, gp 7,5)";
    }

    private void RollWornEquipmentTreasure(
        string monsterDisplayName,
        int encounterLevel,
        TreasureResult result,
        List<(string page, string message)> linkedRuleMessages,
        string scope,
        double chanceScaleFactor,
        bool suppressFailedRollLogs)
    {
        var rule = GetWornEquipmentRule(encounterLevel);
        var levelLabel = rule.Level;

        var baseChanceStart = result.LogLines.Count;
        if (!RollChance(rule.BaseChancePercent, $"WornEquipment level {levelLabel} base chance", result.LogLines, chanceScaleFactor, suppressFailedRollLogs))
        {
            if (!suppressFailedRollLogs)
                result.LogLines.Add($"{monsterDisplayName}: {scope} worn equipment level {levelLabel} not found.");

            foreach (var line in result.LogLines.Skip(baseChanceStart).Where(l => l.Contains($"WornEquipment level {levelLabel} base chance", StringComparison.OrdinalIgnoreCase)))
                linkedRuleMessages.Add(("PartyMagicItems", line));
            return;
        }

        foreach (var line in result.LogLines.Skip(baseChanceStart).Where(l => l.Contains($"WornEquipment level {levelLabel} base chance", StringComparison.OrdinalIgnoreCase)))
            linkedRuleMessages.Add(("PartyMagicItems", line));

        result.LogLines.Add($"{monsterDisplayName}: {scope} worn equipment triggered for encounter level {levelLabel}.");
        linkedRuleMessages.Add(("PartyMagicItems", $"{monsterDisplayName}: {scope} worn equipment triggered for encounter level {levelLabel}."));

        var itemIndex = 1;
        itemIndex = RollWornTableItems(monsterDisplayName, "I", rule.TableIRolls, itemIndex, result, linkedRuleMessages);
        itemIndex = RollWornTableItems(monsterDisplayName, "II", rule.TableIIRolls, itemIndex, result, linkedRuleMessages);
        itemIndex = RollWornTableItems(monsterDisplayName, "III", rule.TableIIIRolls, itemIndex, result, linkedRuleMessages);
        itemIndex = RollWornTableItems(monsterDisplayName, "IV", rule.TableIVRolls, itemIndex, result, linkedRuleMessages);

        var bonusIILogStart = result.LogLines.Count;
        if (rule.BonusTableIIRolls > 0
            && RollChance(rule.BonusTableIIChancePercent, $"WornEquipment level {levelLabel} bonus Table II", result.LogLines, chanceScaleFactor, suppressFailedRollLogs))
        {
            itemIndex = RollWornTableItems(monsterDisplayName, "II", rule.BonusTableIIRolls, itemIndex, result, linkedRuleMessages);
        }
        foreach (var line in result.LogLines.Skip(bonusIILogStart).Where(l => l.Contains($"WornEquipment level {levelLabel} bonus Table II", StringComparison.OrdinalIgnoreCase)))
            linkedRuleMessages.Add(("PartyTable2", line));

        var bonusIIILogStart = result.LogLines.Count;
        if (rule.BonusTableIIIRolls > 0
            && RollChance(rule.BonusTableIIIChancePercent, $"WornEquipment level {levelLabel} bonus Table III", result.LogLines, chanceScaleFactor, suppressFailedRollLogs))
        {
            itemIndex = RollWornTableItems(monsterDisplayName, "III", rule.BonusTableIIIRolls, itemIndex, result, linkedRuleMessages);
        }
        foreach (var line in result.LogLines.Skip(bonusIIILogStart).Where(l => l.Contains($"WornEquipment level {levelLabel} bonus Table III", StringComparison.OrdinalIgnoreCase)))
            linkedRuleMessages.Add(("PartyTable3", line));

        var bonusIVLogStart = result.LogLines.Count;
        if (rule.BonusTableIVRolls > 0
            && RollChance(rule.BonusTableIVChancePercent, $"WornEquipment level {levelLabel} bonus Table IV", result.LogLines, chanceScaleFactor, suppressFailedRollLogs))
        {
            _ = RollWornTableItems(monsterDisplayName, "IV", rule.BonusTableIVRolls, itemIndex, result, linkedRuleMessages);
        }
        foreach (var line in result.LogLines.Skip(bonusIVLogStart).Where(l => l.Contains($"WornEquipment level {levelLabel} bonus Table IV", StringComparison.OrdinalIgnoreCase)))
            linkedRuleMessages.Add(("PartyTable4", line));
    }

    private int RollWornTableItems(string monsterDisplayName, string table, int rolls, int itemIndex, TreasureResult result, List<(string page, string message)> linkedRuleMessages)
    {
        for (var i = 0; i < rolls; i++)
        {
            var item = RollWornEquipmentItem(table, out var dieSize, out var dieRoll);
            var line = $"    Magic item #{itemIndex}: WornEquipment Table {table} d{dieSize} {dieRoll} => {item}.";
            result.LogLines.Add(line);
            linkedRuleMessages.Add((GetWornEquipmentRulePageForTable(table), line));

            RuleApplicationInfo.Publish(
                "AD&D",
                "DMG WornEquipment",
                $"Roll worn equipment item #{itemIndex}",
                $"Roll 1d{dieSize} on WornEquipment Table {table} for encounter treasure.",
                "1",
                dieSize.ToString(CultureInfo.InvariantCulture),
                dieRoll.ToString(CultureInfo.InvariantCulture),
                $"Result: {item}.");

            result.MagicPlaceholders.Add(new TreasureMagicPlaceholderResult
            {
                Table = $"WornEquipment-{table}: {item}",
                Count = 1,
                SourceTable = "WornEquipment"
            });
            itemIndex++;
        }

        return itemIndex;
    }

    private static string GetWornEquipmentRulePageForTable(string table)
    {
        return table switch
        {
            "I" => "PartyTable1",
            "II" => "PartyTable2",
            "III" => "PartyTable3",
            "IV" => "PartyTable4",
            _ => "PartyMagicItems"
        };
    }

    private string RollWornEquipmentItem(string table, out int dieSize, out int dieRoll)
    {
        var entries = table switch
        {
            "I" => WornTableI,
            "II" => WornTableII,
            "III" => WornTableIII,
            "IV" => WornTableIV,
            _ => WornTableI
        };

        dieSize = entries.Length;
        dieRoll = _random.Next(1, dieSize + 1);
        return entries[dieRoll - 1];
    }

    private static WornEquipmentRule GetWornEquipmentRule(int encounterLevel)
    {
        var level = Math.Clamp(encounterLevel, 1, 13);
        return level switch
        {
            1 => new WornEquipmentRule(1, 10, 1, 0, 0, 0, 0, 0, 0, 0),
            2 => new WornEquipmentRule(2, 20, 2, 0, 0, 0, 0, 0, 0, 0),
            3 => new WornEquipmentRule(3, 30, 2, 0, 0, 0, 1, 10, 0, 0),
            4 => new WornEquipmentRule(4, 40, 2, 0, 0, 0, 1, 20, 0, 0),
            5 => new WornEquipmentRule(5, 50, 2, 0, 0, 0, 1, 30, 0, 0),
            6 => new WornEquipmentRule(6, 60, 3, 2, 0, 0, 0, 0, 0, 0),
            7 => new WornEquipmentRule(7, 70, 3, 2, 0, 0, 0, 0, 0, 0),
            8 => new WornEquipmentRule(8, 80, 0, 3, 0, 0, 0, 0, 1, 20),
            9 => new WornEquipmentRule(9, 90, 0, 3, 0, 0, 2, 70, 1, 30),
            10 => new WornEquipmentRule(10, 80, 3, 0, 0, 0, 2, 80, 1, 40),
            11 => new WornEquipmentRule(11, 90, 3, 0, 0, 0, 2, 90, 1, 50, 1, 10),
            12 => new WornEquipmentRule(12, 100, 3, 2, 0, 0, 1, 60, 0, 0, 1, 20),
            _ => new WornEquipmentRule(13, 100, 3, 2, 1, 0, 0, 0, 0, 0, 1, 60)
        };
    }

    private sealed record WornEquipmentRule(
        int Level,
        int BaseChancePercent,
        int TableIRolls,
        int TableIIRolls,
        int TableIIIRolls,
        int TableIVRolls,
        int BonusTableIIRolls,
        int BonusTableIIChancePercent,
        int BonusTableIIIRolls,
        int BonusTableIIIChancePercent,
        int BonusTableIVRolls = 0,
        int BonusTableIVChancePercent = 0);

    private static readonly string[] WornTableI =
    {
        "Potion of Climbing + Potion of Flying",
        "Potion of Extra Healing + Potion of Polymorph (Self)",
        "Potion of Fire Resistance + Potion of Speed",
        "Potion of Healing + Potion of Giant Strength",
        "Potion of Heroism + Potion of Invulnerability",
        "Potion of Human Control + Potion of Levitation",
        "Potion of Super-Heroism + Potion of Animal Control",
        "Scroll of 1 Spell (Level 1-6)",
        "Scroll of 2 Spells (Level 1-4)",
        "Scroll of Protection from Magic",
        "Ring of Mammal Control",
        "Ring of Protection +1",
        "Leather Armor +1",
        "Shield +1",
        "Sword +1 (No Special Abilities)",
        "Arrow +1 (10)",
        "Bolt +2 (4)",
        "Dagger +1",
        "Javelin +2",
        "Mace +1"
    };

    private static readonly string[] WornTableII =
    {
        "Scroll of 3 Spells (Level 2-9 or 2-7)",
        "Ring of Fire Resistance + Ring of Invisibility",
        "Ring of Protection +3",
        "Staff of Striking",
        "Wand of Illusion",
        "Wand of Negation",
        "Bracers of Defense, AC 4",
        "Brooch of Shielding",
        "Cloak of Elvenkind",
        "Dust of Appearance",
        "Figurine of Wondrous Power: Serpentine Owl",
        "Javelin of Lightning (3)",
        "Chain Mail +1 + Shield +2",
        "Splint Mail +4",
        "Sword +3 (No Special Abilities)",
        "Crossbow of Speed + Hammer +2"
    };

    private static readonly string[] WornTableIII =
    {
        "Ring of Spell Storing",
        "Rod of Cancellation",
        "Staff of the Serpent",
        "Bag of Tricks",
        "Boots of Speed",
        "Boots of Striding and Leaping",
        "Cloak of Displacement",
        "Gauntlets of Ogre Power",
        "Pipe of the Sewers",
        "Robe of Blending",
        "Rope of Climbing + Rope of Entanglement",
        "Plate Mail +3 + Shield +2",
        "Shield +5",
        "Sword +4, Defender",
        "Mace +3",
        "Spear +3"
    };

    private static readonly string[] WornTableIV =
    {
        "Ring of Djinni Summoning",
        "Ring of Spell Turning",
        "Rod of Smiting",
        "Wand of Fire",
        "Cube of Force",
        "Eyes of Charming",
        "Horn of Valhalla",
        "Robe of Scintillating Colors",
        "Talisman of Ultimate Evil or Talisman of Pure Good",
        "Plate Mail +4 + Shield +3",
        "Sword of Wounding",
        "Arrow of Slaying"
    };

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
