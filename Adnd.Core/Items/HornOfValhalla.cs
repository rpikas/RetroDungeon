using Adnd.Core.Characters;

namespace Adnd.Core.Items;

public enum HornOfValhallaType
{
    Unknown = 0,
    Silver,
    Brass,
    Bronze,
    Iron
}

public static class HornOfValhalla
{
    private const string TypePrefix = "HornOfValhallaType:";
    private const string AlignedPrefix = "HornOfValhallaAligned:";
    private const string AlignmentPrefix = "HornOfValhallaAlignment:";
    private const string LastUseDayPrefix = "HornOfValhallaLastUseDay:";

    public static bool IsHornOfValhalla(Item? item)
    {
        if (item == null)
            return false;

        return string.Equals(item.Name?.Trim(), "Horn of Valhalla", StringComparison.OrdinalIgnoreCase);
    }

    public static HornOfValhallaType GetOrAssignType(Item item, int typeRoll)
    {
        var existing = GetType(item);
        if (existing != HornOfValhallaType.Unknown)
            return existing;

        var type = ResolveTypeByRoll(typeRoll);
        SetString(item, TypePrefix, type.ToString());
        return type;
    }

    public static HornOfValhallaType GetType(Item item)
    {
        var raw = GetString(item, TypePrefix);
        return Enum.TryParse<HornOfValhallaType>(raw, true, out var parsed) ? parsed : HornOfValhallaType.Unknown;
    }

    public static bool IsAligned(Item item)
    {
        var raw = GetString(item, AlignedPrefix);
        return bool.TryParse(raw, out var parsed) && parsed;
    }

    public static bool TryGetAlignment(Item item, out Alignment alignment)
    {
        alignment = default;
        var raw = GetString(item, AlignmentPrefix);
        return Enum.TryParse(raw, true, out alignment);
    }

    public static void EnsureAlignmentProfile(Item item, bool aligned, Alignment? alignment)
    {
        SetString(item, AlignedPrefix, aligned.ToString());
        if (aligned && alignment.HasValue)
            SetString(item, AlignmentPrefix, alignment.Value.ToString());
    }

    public static bool IsOnCooldown(Item item, int dungeonDay)
    {
        var lastUseDay = GetInt(item, LastUseDayPrefix, int.MinValue);
        if (lastUseDay == int.MinValue)
            return false;

        return dungeonDay - lastUseDay < 7;
    }

    public static int GetCooldownDaysRemaining(Item item, int dungeonDay)
    {
        var lastUseDay = GetInt(item, LastUseDayPrefix, int.MinValue);
        if (lastUseDay == int.MinValue)
            return 0;

        return Math.Max(0, 7 - Math.Max(0, dungeonDay - lastUseDay));
    }

    public static void MarkUsed(Item item, int dungeonDay)
    {
        SetInt(item, LastUseDayPrefix, Math.Max(0, dungeonDay));
    }

    public static int RollBerserkerCount(HornOfValhallaType type, Func<int, int> rollDie)
    {
        return type switch
        {
            HornOfValhallaType.Silver => rollDie(7) + 3,
            HornOfValhallaType.Brass => rollDie(7) + 2,
            HornOfValhallaType.Bronze => rollDie(7) + 1,
            HornOfValhallaType.Iron => rollDie(4) + 1,
            _ => 0
        };
    }

    public static int GetFighterLevel(HornOfValhallaType type)
    {
        return type switch
        {
            HornOfValhallaType.Silver => 2,
            HornOfValhallaType.Brass => 3,
            HornOfValhallaType.Bronze => 4,
            HornOfValhallaType.Iron => 5,
            _ => 0
        };
    }

    private static HornOfValhallaType ResolveTypeByRoll(int roll)
    {
        var normalized = Math.Clamp(roll, 1, 20);
        return normalized switch
        {
            <= 8 => HornOfValhallaType.Silver,
            <= 15 => HornOfValhallaType.Brass,
            <= 18 => HornOfValhallaType.Bronze,
            _ => HornOfValhallaType.Iron
        };
    }

    private static int GetInt(Item item, string prefix, int defaultValue)
    {
        var raw = GetString(item, prefix);
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static string GetString(Item item, string prefix)
    {
        if (item.SpecialAbilities == null || item.SpecialAbilities.Count == 0)
            return string.Empty;

        var marker = item.SpecialAbilities.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(marker))
            return string.Empty;

        return marker[prefix.Length..].Trim();
    }

    private static void SetInt(Item item, string prefix, int value)
    {
        SetString(item, prefix, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void SetString(Item item, string prefix, string value)
    {
        item.SpecialAbilities ??= new List<string>();
        item.SpecialAbilities.RemoveAll(a => !string.IsNullOrWhiteSpace(a)
                                             && a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        item.SpecialAbilities.Add($"{prefix}{value}");
    }
}
