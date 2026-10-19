namespace Adnd.Core.Items;

public static class PotionOfHumanControl
{
    private const string RollPrefix = "PotionHumanControlRoll:";

    public static bool IsPotionOfHumanControl(Item? item)
    {
        if (item == null)
            return false;

        return ItemSpecialAbilityParser.HasCastsAbility(item, "Human Control")
               || string.Equals(item.Name?.Trim(), "Potion of Human Control", StringComparison.OrdinalIgnoreCase);
    }

    public static int EnsureAssignedControlRoll(Item item, Func<int, int> rollDie)
    {
        if (TryGetAssignedControlRoll(item, out var existing))
            return existing;

        var rolled = Math.Clamp(rollDie(20), 1, 20);
        SetAssignedControlRoll(item, rolled);
        return rolled;
    }

    public static bool TryGetAssignedControlRoll(Item item, out int roll)
    {
        roll = 0;
        if (item?.SpecialAbilities == null || item.SpecialAbilities.Count == 0)
            return false;

        var marker = item.SpecialAbilities
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)
                                 && a.StartsWith(RollPrefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(marker))
            return false;

        var raw = marker[RollPrefix.Length..].Trim();
        if (!int.TryParse(raw, out var parsed))
            return false;

        roll = Math.Clamp(parsed, 1, 20);
        return true;
    }

    public static string GetControlCategory(int d20)
    {
        var roll = Math.Clamp(d20, 1, 20);
        return roll switch
        {
            <= 2 => "Dwarves",
            <= 4 => "Elves/Half-Elves",
            <= 6 => "Gnomes",
            <= 8 => "Halflings",
            <= 10 => "Half-Orcs",
            <= 16 => "Humans",
            <= 19 => "Humanoids",
            _ => "Elves, Half-Elves, and Humans"
        };
    }

    public static string GetRangeText(int d20)
    {
        var roll = Math.Clamp(d20, 1, 20);
        return roll switch
        {
            <= 2 => "1-2",
            <= 4 => "3-4",
            <= 6 => "5-6",
            <= 8 => "7-8",
            <= 10 => "9-10",
            <= 16 => "11-16",
            <= 19 => "17-19",
            _ => "20"
        };
    }

    public static string GetDisplayDescriptor(Item item)
    {
        if (!TryGetAssignedControlRoll(item, out var roll))
            return "Unassigned";

        return $"{GetControlCategory(roll)} ({GetRangeText(roll)})";
    }

    private static void SetAssignedControlRoll(Item item, int roll)
    {
        item.SpecialAbilities ??= new List<string>();
        item.SpecialAbilities.RemoveAll(a => !string.IsNullOrWhiteSpace(a)
                                             && a.StartsWith(RollPrefix, StringComparison.OrdinalIgnoreCase));
        item.SpecialAbilities.Add($"{RollPrefix}{Math.Clamp(roll, 1, 20)}");
    }
}
