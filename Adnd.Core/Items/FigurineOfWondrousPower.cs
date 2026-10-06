using System;
using System.Collections.Generic;
using System.Linq;

namespace Adnd.Core.Items;

public enum FigurineKind
{
    Unknown = 0,
    EbonyFly,
    GoldenLions,
    IvoryGoats,
    MarbleElephant,
    ObsidianSteed,
    OnyxDog,
    SerpentineOwl
}

public static class FigurineOfWondrousPower
{
    private const string TypePrefix = "FigurineType:";
    private const string ActivePrefix = "FigurineActive:";
    private const string WeekStartPrefix = "FigurineWeekStartDay:";
    private const string UsesInWeekPrefix = "FigurineUsesInWeek:";
    private const string LastUseDayPrefix = "FigurineLastUseDay:";
    private const string SummonDayPrefix = "FigurineSummonDay:";
    private const string RuinedPrefix = "FigurineRuined:";
    private const string OwlGiantUsesRemainingPrefix = "FigurineOwlGiantUsesRemaining:";
    private const string CurrentFormPrefix = "FigurineCurrentForm:";

    public static bool IsFigurine(Item item)
    {
        return ResolveKind(item) != FigurineKind.Unknown
               || string.Equals(item?.Name?.Trim(), "Figurine of Wondrous Power", StringComparison.OrdinalIgnoreCase);
    }

    public static FigurineKind GetKind(Item item) => ResolveKind(item);

    public static bool IsActiveInAnimalForm(Item item)
    {
        if (item == null)
            return false;

        if (!IsFigurine(item))
            return false;

        return IsActive(item) && !IsRuined(item);
    }

    public static bool IsSerpentineOwlGiantForm(Item item)
    {
        if (ResolveKind(item) != FigurineKind.SerpentineOwl)
            return false;

        return string.Equals(GetCurrentForm(item), "Giant", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TickDayTransition(Item item, int dungeonDay, out List<string> events)
    {
        events = new List<string>();
        if (item == null)
            return false;

        if (!IsFigurine(item))
            return false;

        var kind = ResolveKind(item);
        if (kind == FigurineKind.Unknown)
            return false;

        var wasActive = IsActive(item);
        ExpireIfNeeded(item, dungeonDay, kind, events);
        return wasActive && !IsActive(item);
    }

    public static bool TryUse(Item item, int dungeonDay, out List<string> events, bool preferGiantForm = false)
    {
        events = new List<string>();
        if (item == null)
            return false;

        EnsureAbilities(item);

        ResolveGenericTypeIfNeeded(item, events);
        var kind = ResolveKind(item);
        if (kind == FigurineKind.Unknown)
            return false;

        if (IsRuined(item))
        {
            events.Add($"{item.Name} is exhausted and has lost its magic.");
            return false;
        }

        ExpireIfNeeded(item, dungeonDay, kind, events);

        if (IsActive(item))
        {
            SetActive(item, false);
            SetCurrentForm(item, string.Empty);
            events.Add($"{item.Name} returns to statuette form.");
            return true;
        }

        switch (kind)
        {
            case FigurineKind.EbonyFly:
                return TryActivateEbonyFly(item, dungeonDay, events);
            case FigurineKind.GoldenLions:
                return TryActivateGoldenLions(item, dungeonDay, events);
            case FigurineKind.SerpentineOwl:
                return TryActivateSerpentineOwl(item, events, preferGiantForm);
            default:
                events.Add($"{item.Name} type is recognized, but this variant is not implemented yet.");
                return false;
        }
    }

    private static bool TryActivateEbonyFly(Item item, int dungeonDay, List<string> events)
    {
        var weekStart = GetInt(item, WeekStartPrefix, dungeonDay);
        var usesInWeek = GetInt(item, UsesInWeekPrefix, 0);

        if (dungeonDay - weekStart >= 7)
        {
            weekStart = dungeonDay;
            usesInWeek = 0;
        }

        if (usesInWeek >= 3)
        {
            var daysLeft = Math.Max(0, 7 - (dungeonDay - weekStart));
            events.Add($"{item.Name} has no uses left this week ({usesInWeek}/3 used). {daysLeft} day(s) until reset.");
            return false;
        }

        usesInWeek++;
        SetInt(item, WeekStartPrefix, weekStart);
        SetInt(item, UsesInWeekPrefix, usesInWeek);
        SetInt(item, SummonDayPrefix, dungeonDay);
        SetActive(item, true);
        SetCurrentForm(item, "Standard");

        events.Add("Ebony Fly enlarges to pony size and obeys its owner.");
        events.Add($"Uses this week: {usesInWeek}/3. It will remain until dismissed or day end.");
        return true;
    }

    private static bool TryActivateGoldenLions(Item item, int dungeonDay, List<string> events)
    {
        var lastUseDay = GetInt(item, LastUseDayPrefix, int.MinValue);
        if (lastUseDay == dungeonDay)
        {
            events.Add("Golden Lions have already been used today and cannot be summoned again until tomorrow.");
            return false;
        }

        SetInt(item, LastUseDayPrefix, dungeonDay);
        SetInt(item, SummonDayPrefix, dungeonDay);
        SetActive(item, true);
        SetCurrentForm(item, "Standard");

        events.Add("Two Golden Lions enlarge and obey their owner.");
        events.Add("They remain until dismissed or day end.");
        return true;
    }

    private static bool TryActivateSerpentineOwl(Item item, List<string> events, bool preferGiantForm)
    {
        var remainingGiantUses = GetInt(item, OwlGiantUsesRemainingPrefix, 3);

        if (preferGiantForm)
        {
            if (remainingGiantUses <= 0)
            {
                events.Add("This serpentine owl can no longer assume giant form; its magic is exhausted.");
                SetRuined(item, true);
                return false;
            }

            remainingGiantUses = Math.Max(0, remainingGiantUses - 1);
            SetInt(item, OwlGiantUsesRemainingPrefix, remainingGiantUses);
            SetActive(item, true);
            SetCurrentForm(item, "Giant");
            events.Add("Serpentine Owl becomes a giant owl and obeys its owner.");

            if (remainingGiantUses <= 0)
            {
                SetRuined(item, true);
                events.Add("The final giant use is spent; the figurine loses all magical properties after this manifestation ends.");
            }
            else
            {
                events.Add($"Giant-form uses remaining: {remainingGiantUses}.");
            }

            return true;
        }

        SetActive(item, true);
        SetCurrentForm(item, "Normal");
        events.Add("Serpentine Owl becomes a horned owl and obeys its owner.");
        events.Add("The normal-size form can be used repeatedly.");
        return true;
    }

    private static void ExpireIfNeeded(Item item, int dungeonDay, FigurineKind kind, List<string> events)
    {
        if (!IsActive(item))
            return;

        var summonDay = GetInt(item, SummonDayPrefix, dungeonDay);
        if (summonDay >= dungeonDay)
            return;

        if (kind is FigurineKind.EbonyFly or FigurineKind.GoldenLions)
        {
            SetActive(item, false);
            SetCurrentForm(item, string.Empty);
            events.Add($"{item.Name} duration has ended and it reverts to statuette form.");
        }
    }

    private static void ResolveGenericTypeIfNeeded(Item item, List<string> events)
    {
        if (!string.Equals(item.Name?.Trim(), "Figurine of Wondrous Power", StringComparison.OrdinalIgnoreCase))
            return;

        var roll = Random.Shared.Next(1, 101);
        var kind = roll switch
        {
            <= 15 => FigurineKind.EbonyFly,
            <= 30 => FigurineKind.GoldenLions,
            <= 40 => FigurineKind.IvoryGoats,
            <= 55 => FigurineKind.MarbleElephant,
            <= 65 => FigurineKind.ObsidianSteed,
            <= 85 => FigurineKind.OnyxDog,
            _ => FigurineKind.SerpentineOwl
        };

        SetKind(item, kind);
        item.Name = kind switch
        {
            FigurineKind.EbonyFly => "Figurine of Wondrous Power: Ebony Fly",
            FigurineKind.GoldenLions => "Figurine of Wondrous Power: Golden Lions",
            FigurineKind.IvoryGoats => "Figurine of Wondrous Power: Ivory Goats",
            FigurineKind.MarbleElephant => "Figurine of Wondrous Power: Marble Elephant",
            FigurineKind.ObsidianSteed => "Figurine of Wondrous Power: Obsidian Steed",
            FigurineKind.OnyxDog => "Figurine of Wondrous Power: Onyx Dog",
            _ => "Figurine of Wondrous Power: Serpentine Owl"
        };

        events.Add($"Figurine type roll: {roll} -> {item.Name}.");
    }

    private static FigurineKind ResolveKind(Item item)
    {
        if (item == null)
            return FigurineKind.Unknown;

        var typeAbility = item.SpecialAbilities?
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && a.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(typeAbility))
        {
            var value = typeAbility[TypePrefix.Length..].Trim();
            if (Enum.TryParse<FigurineKind>(value, ignoreCase: true, out var parsed))
                return parsed;
        }

        var name = item.Name?.Trim() ?? string.Empty;
        if (name.Contains("Ebony Fly", StringComparison.OrdinalIgnoreCase)) return FigurineKind.EbonyFly;
        if (name.Contains("Golden Lions", StringComparison.OrdinalIgnoreCase)) return FigurineKind.GoldenLions;
        if (name.Contains("Ivory Goats", StringComparison.OrdinalIgnoreCase)) return FigurineKind.IvoryGoats;
        if (name.Contains("Marble Elephant", StringComparison.OrdinalIgnoreCase)) return FigurineKind.MarbleElephant;
        if (name.Contains("Obsidian Steed", StringComparison.OrdinalIgnoreCase)) return FigurineKind.ObsidianSteed;
        if (name.Contains("Onyx Dog", StringComparison.OrdinalIgnoreCase)) return FigurineKind.OnyxDog;
        if (name.Contains("Serpentine Owl", StringComparison.OrdinalIgnoreCase)) return FigurineKind.SerpentineOwl;

        return FigurineKind.Unknown;
    }

    private static void SetKind(Item item, FigurineKind kind)
    {
        EnsureAbilities(item);
        item.SpecialAbilities!.RemoveAll(a => a.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase));
        item.SpecialAbilities.Add($"{TypePrefix}{kind}");
    }

    private static bool IsActive(Item item) => GetInt(item, ActivePrefix, 0) == 1;

    private static void SetActive(Item item, bool active) => SetInt(item, ActivePrefix, active ? 1 : 0);

    private static int GetInt(Item item, string prefix, int fallback)
    {
        if (item.SpecialAbilities == null)
            return fallback;

        var raw = item.SpecialAbilities.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        var payload = raw.Split(':', 2).Length == 2 ? raw.Split(':', 2)[1] : string.Empty;
        return int.TryParse(payload, out var value) ? value : fallback;
    }

    private static void SetInt(Item item, string prefix, int value)
    {
        EnsureAbilities(item);
        item.SpecialAbilities!.RemoveAll(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        item.SpecialAbilities.Add($"{prefix}{value}");
    }

    private static string GetCurrentForm(Item item)
    {
        if (item.SpecialAbilities == null)
            return string.Empty;

        var raw = item.SpecialAbilities.FirstOrDefault(a => a.StartsWith(CurrentFormPrefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var idx = raw.IndexOf(':');
        if (idx < 0 || idx + 1 >= raw.Length)
            return string.Empty;

        return raw[(idx + 1)..].Trim();
    }

    private static void SetCurrentForm(Item item, string form)
    {
        EnsureAbilities(item);
        item.SpecialAbilities!.RemoveAll(a => a.StartsWith(CurrentFormPrefix, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(form))
            item.SpecialAbilities.Add($"{CurrentFormPrefix}{form}");
    }

    private static bool IsRuined(Item item) => GetInt(item, RuinedPrefix, 0) == 1;

    private static void SetRuined(Item item, bool ruined) => SetInt(item, RuinedPrefix, ruined ? 1 : 0);

    private static void EnsureAbilities(Item item) => item.SpecialAbilities ??= new List<string>();
}
