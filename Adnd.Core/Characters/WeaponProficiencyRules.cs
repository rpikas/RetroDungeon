using Adnd.Core.Items;

namespace Adnd.Core.Characters;

public static class WeaponProficiencyRules
{
    private sealed record ProficiencyProfile(
        int InitialCount,
        int NonProficiencyPenalty,
        int AddedPerLevels,
        string[] StartingWeapons,
        (int Level, string Weapon)[] LevelWeaponGains);

    private static readonly Dictionary<CharacterClass, ProficiencyProfile> Profiles = new()
    {
        [CharacterClass.Fighter] = new(
            4, -2, 3,
            ["Longsword", "Spear", "Longbow", "Dagger"],
            [(4, "Battle Axe"), (7, "Mace"), (10, "Two-Handed Sword"), (13, "Flail"), (16, "Morning Star"), (19, "Halberd")]),
        [CharacterClass.Paladin] = new(
            3, -2, 3,
            ["Longsword", "Spear", "Longbow", "Dagger"],
            [(4, "Battle Axe"), (7, "Mace"), (10, "Two-Handed Sword"), (13, "Flail"), (16, "Morning Star"), (19, "Halberd")]),
        [CharacterClass.Ranger] = new(
            3, -2, 3,
            ["Longsword", "Spear", "Longbow", "Dagger"],
            [(4, "Battle Axe"), (7, "Mace"), (10, "Two-Handed Sword"), (13, "Flail"), (16, "Morning Star"), (19, "Halberd")]),
        [CharacterClass.Cleric] = new(
            2, -3, 4,
            ["Mace", "Staff"],
            [(5, "Warhammer"), (9, "Flail"), (13, "Morning Star"), (17, "Hammer (throwing)")]),
        [CharacterClass.Druid] = new(
            2, -4, 5,
            ["Scimitar", "Spear"],
            [(6, "Sling"), (11, "Club"), (16, "Dart")]),
        [CharacterClass.Thief] = new(
            2, -3, 4,
            ["Short Sword", "Dagger"],
            [(5, "Sling"), (9, "Shortbow"), (13, "Club"), (17, "Hand Crossbow")]),
        [CharacterClass.Assassin] = new(
            3, -3, 4,
            ["Dagger", "Short Sword", "Light Crossbow"],
            [(5, "Light Crossbow"), (9, "Poisoned Dart"), (13, "Scimitar"), (17, "Blowgun")]),
        [CharacterClass.Bard] = new(
            3, -3, 4,
            ["Longsword", "Dagger", "Shortbow"],
            [(5, "Spear"), (9, "Staff"), (13, "Scimitar"), (17, "Sling")]),
        [CharacterClass.MagicUser] = new(
            1, -5, 6,
            ["Dagger"],
            [(7, "Staff"), (13, "Dart"), (19, "Sling")]),
        [CharacterClass.Illusionist] = new(
            1, -5, 6,
            ["Dagger"],
            [(6, "Staff"), (11, "Dart"), (16, "Sling")]),
        [CharacterClass.Monk] = new(
            1, -3, 2,
            ["Staff"],
            [(3, "Spear"), (5, "Club"), (7, "Dart"), (9, "Sling"), (11, "Jo-stick"), (13, "Hand Axe (Thrown)"), (15, "Quarterstaff"), (17, "Light Crossbow")])
    };

    public static bool EnsureAutoProficiencies(Character character)
    {
        if (character == null)
            return false;

        var cls = character.Classes.Count > 0 ? character.Classes[0] : character.Class;
        if (!Profiles.TryGetValue(cls, out var profile))
            return false;

        character.WeaponProficiencies ??= new List<string>();
        var set = new HashSet<string>(character.WeaponProficiencies, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var weapon in profile.StartingWeapons)
            changed |= set.Add(CanonicalizeProficiencyName(weapon));

        if (cls == CharacterClass.Monk)
            changed |= set.Add(CanonicalizeProficiencyName("Fist or Open Hand"));

        var level = Math.Max(1, character.GetClassLevel(cls));
        foreach (var gain in profile.LevelWeaponGains)
        {
            if (level >= gain.Level)
                changed |= set.Add(CanonicalizeProficiencyName(gain.Weapon));
        }

        if (!changed)
            return false;

        character.WeaponProficiencies = set.ToList();
        return true;
    }

    public static int GetNonProficiencyPenalty(Character character, Item? weapon, bool isRanged)
    {
        if (character == null)
            return 0;

        if (weapon == null)
            return 0;

        var cls = character.Classes.Count > 0 ? character.Classes[0] : character.Class;
        if (!Profiles.TryGetValue(cls, out var profile))
            return 0;

        var weaponKey = ResolveWeaponProficiencyKey(weapon, isRanged, character.IsMonk());
        if (string.IsNullOrWhiteSpace(weaponKey))
            return 0;

        character.WeaponProficiencies ??= new List<string>();
        var isProficient = character.WeaponProficiencies.Any(p => string.Equals(CanonicalizeProficiencyName(p), weaponKey, StringComparison.OrdinalIgnoreCase));
        return isProficient ? 0 : Math.Abs(profile.NonProficiencyPenalty);
    }

    public static string ResolveWeaponProficiencyKey(Item? weapon, bool isRanged, bool isMonk)
    {
        if (weapon == null)
            return string.Empty;

        var normalized = weapon.Name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized.Contains("two") && normalized.Contains("handed") && normalized.Contains("sword")) return CanonicalizeProficiencyName("Two-Handed Sword");
        if (normalized.Contains("long") && normalized.Contains("sword")) return CanonicalizeProficiencyName("Longsword");
        if (normalized.Contains("short") && normalized.Contains("sword")) return CanonicalizeProficiencyName("Short Sword");
        if (normalized.Contains("broad") && normalized.Contains("sword")) return CanonicalizeProficiencyName("Broad Sword");
        if ((normalized.StartsWith("sword ") || normalized.Contains("sword +") || normalized.Contains("sword of ") || normalized.Contains("vorpal") || normalized.Contains("flame tongue") || normalized.Contains("frost brand"))
            && !normalized.Contains("short") && !normalized.Contains("broad") && !normalized.Contains("two handed")) return CanonicalizeProficiencyName("Longsword");

        if (normalized.Contains("hand axe") && normalized.Contains("thrown")) return CanonicalizeProficiencyName("Hand Axe (Thrown)");
        if (normalized.Contains("battle axe") || normalized.StartsWith("axe")) return CanonicalizeProficiencyName("Battle Axe");
        if (normalized.Contains("halberd")) return CanonicalizeProficiencyName("Halberd");
        if (normalized.Contains("morning star")) return CanonicalizeProficiencyName("Morning Star");
        if (normalized.Contains("warhammer")) return CanonicalizeProficiencyName("Warhammer");
        if (normalized.Contains("hammer") && (normalized.Contains("thrown") || normalized.Contains("thunderbolts"))) return CanonicalizeProficiencyName("Hammer (throwing)");
        if (normalized.Contains("hammer")) return CanonicalizeProficiencyName("Warhammer");
        if (normalized.Contains("mace")) return CanonicalizeProficiencyName("Mace");
        if (normalized.Contains("flail")) return CanonicalizeProficiencyName("Flail");
        if (normalized.Contains("jo stick") || normalized.Contains("jo-stick") || normalized.Contains("bo stick")) return CanonicalizeProficiencyName("Jo-stick");
        if (normalized.Contains("quarterstaff")) return CanonicalizeProficiencyName("Quarterstaff");
        if (normalized.Contains("staff")) return CanonicalizeProficiencyName("Staff");
        if (normalized.Contains("scimitar")) return CanonicalizeProficiencyName("Scimitar");
        if (normalized.Contains("spear")) return CanonicalizeProficiencyName("Spear");
        if (normalized.Contains("dagger")) return CanonicalizeProficiencyName("Dagger");
        if (normalized.Contains("club")) return CanonicalizeProficiencyName("Club");
        if (normalized.Contains("poisoned") && normalized.Contains("dart")) return CanonicalizeProficiencyName("Poisoned Dart");
        if (normalized.Contains("dart")) return CanonicalizeProficiencyName("Dart");
        if (normalized.Contains("sling")) return CanonicalizeProficiencyName("Sling");
        if (normalized.Contains("blowgun")) return CanonicalizeProficiencyName("Blowgun");
        if (normalized.Contains("hand crossbow")) return CanonicalizeProficiencyName("Hand Crossbow");
        if (normalized.Contains("light crossbow")) return CanonicalizeProficiencyName("Light Crossbow");
        if (normalized.Contains("crossbow")) return CanonicalizeProficiencyName("Light Crossbow");
        if (normalized.Contains("long bow") || (normalized.Contains("bow +") && !normalized.Contains("short")) || normalized.Contains("composite long")) return CanonicalizeProficiencyName("Longbow");
        if (normalized.Contains("short bow") || normalized.Contains("composite short")) return CanonicalizeProficiencyName("Shortbow");

        if (isRanged && normalized.Contains("bow"))
            return CanonicalizeProficiencyName("Longbow");

        return string.Empty;
    }

    public static string CanonicalizeProficiencyName(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return string.Empty;

        var value = weaponName.Trim();
        return value.ToLowerInvariant() switch
        {
            "long sword" => "Longsword",
            "longsword" => "Longsword",
            "short bow" => "Shortbow",
            "shortbow" => "Shortbow",
            "long bow" => "Longbow",
            "longbow" => "Longbow",
            "fist/open hand" => "Fist or Open Hand",
            "jo stick" => "Jo-stick",
            "jo-stick" => "Jo-stick",
            "bo stick" => "Jo-stick",
            "quarter staff" => "Quarterstaff",
            _ => value
        };
    }
}
