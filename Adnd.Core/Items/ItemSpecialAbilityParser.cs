namespace Adnd.Core.Items;

public static class ItemSpecialAbilityParser
{
    private const string CastsPrefix = "Casts ";

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
}
