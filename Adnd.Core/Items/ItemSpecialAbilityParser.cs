namespace Adnd.Core.Items;

public static class ItemSpecialAbilityParser
{
    private const string CastsPrefix = "Casts ";
    private const string DelusionTrueIdentityPrefix = "DelusionTrueIdentity:";
    private const string DelusionDisguisedAsPrefix = "DelusionDisguisedAs:";

    public static bool HasSpecialAbility(Item item, string abilityName)
    {
        if (item?.SpecialAbilities == null || item.SpecialAbilities.Count == 0 || string.IsNullOrWhiteSpace(abilityName))
            return false;

        return item.SpecialAbilities.Any(a => string.Equals(a?.Trim(), abilityName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasCastsAbility(Item item, string spellLikeName)
    {
        if (string.IsNullOrWhiteSpace(spellLikeName))
            return false;

        return HasSpecialAbility(item, $"Casts {spellLikeName}");
    }

    public static bool TryGetCastedSpellName(Item item, out string spellName)
    {
        spellName = string.Empty;
        if (item?.SpecialAbilities == null || item.SpecialAbilities.Count == 0)
            return false;

        foreach (var ability in item.SpecialAbilities)
        {
            if (string.IsNullOrWhiteSpace(ability))
                continue;

            var trimmed = ability.Trim();
            if (!trimmed.StartsWith(CastsPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var name = trimmed[CastsPrefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            spellName = name;
            return true;
        }

        return false;
    }

    public static bool IsPotionOfDelusion(Item item)
    {
        if (item == null)
            return false;

        if (string.Equals(item.Name, "Potion of Delusion", StringComparison.OrdinalIgnoreCase))
            return true;

        return TryGetDelusionTrueIdentity(item, out _)
               || HasSpecialAbility(item, "DelusionPotion");
    }

    public static bool TryGetDelusionTrueIdentity(Item item, out string trueIdentity)
    {
        trueIdentity = string.Empty;
        if (item?.SpecialAbilities == null || item.SpecialAbilities.Count == 0)
            return false;

        var marker = item.SpecialAbilities
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)
                                 && a.StartsWith(DelusionTrueIdentityPrefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(marker))
            return false;

        trueIdentity = marker[DelusionTrueIdentityPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(trueIdentity);
    }

    public static bool TryGetDelusionDisguiseName(Item item, out string disguiseName)
    {
        disguiseName = string.Empty;
        if (item?.SpecialAbilities == null || item.SpecialAbilities.Count == 0)
            return false;

        var marker = item.SpecialAbilities
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)
                                 && a.StartsWith(DelusionDisguisedAsPrefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(marker))
            return false;

        disguiseName = marker[DelusionDisguisedAsPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(disguiseName);
    }

    public static void SetDelusionDisguise(Item item, string disguiseName)
    {
        if (item == null || string.IsNullOrWhiteSpace(disguiseName))
            return;

        item.SpecialAbilities ??= new List<string>();

        item.SpecialAbilities.RemoveAll(a => !string.IsNullOrWhiteSpace(a)
                                             && (a.StartsWith(DelusionTrueIdentityPrefix, StringComparison.OrdinalIgnoreCase)
                                                 || a.StartsWith(DelusionDisguisedAsPrefix, StringComparison.OrdinalIgnoreCase)));

        if (!HasSpecialAbility(item, "DelusionPotion"))
            item.SpecialAbilities.Add("DelusionPotion");

        item.SpecialAbilities.Add("DelusionTrueIdentity:Potion of Delusion");
        item.SpecialAbilities.Add($"DelusionDisguisedAs:{disguiseName.Trim()}");
        item.Name = disguiseName.Trim();
    }

    public static bool RevealDelusionIdentity(Item item)
    {
        if (!TryGetDelusionTrueIdentity(item, out var trueIdentity))
            return false;

        item.Name = trueIdentity;
        item.SpecialAbilities.RemoveAll(a => !string.IsNullOrWhiteSpace(a)
                                             && (a.StartsWith(DelusionTrueIdentityPrefix, StringComparison.OrdinalIgnoreCase)
                                                 || a.StartsWith(DelusionDisguisedAsPrefix, StringComparison.OrdinalIgnoreCase)));
        return true;
    }
}
