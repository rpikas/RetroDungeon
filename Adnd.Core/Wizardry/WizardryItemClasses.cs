namespace Adnd.Core.Wizardry;

/// <summary>
/// Which Wizardry classes may use an item, as given by the "allowed classes" letters in WizardryItems.json
/// (f=Fighter, m=Mage, p=Priest, t=Thief, b=Bishop, s=Samurai, l=Lord, n=Ninja). Keyed by this game's item names.
/// The AD&amp;D class lists on the items cannot say Samurai, Bishop or Ninja, so Wizardry characters use this instead.
/// </summary>
public static class WizardryItemClasses
{
    private const string AllLetters = "fmptbsln";

    private static readonly Dictionary<string, string> Letters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Long Sword"] = "fsln",
        ["Long Sword -1"] = "fsln",
        ["Short Sword"] = "ftsln",
        ["Short Sword -1"] = "ftsln",
        ["Dagger"] = "fmtsln",
        ["Staff"] = "fmptbsln",
        ["Staff +2"] = "fmptbsln",
        ["Staff -2"] = "fmptbsln",
        ["Staff of Mogref"] = "mb",
        ["Anointed Mace"] = "fpbsln",
        ["Anointed Flail"] = "fpsln",
        ["Mace -1"] = "fpbsln",
        ["Mace -2"] = "fpbsln",
        ["Mace of Poison"] = "fpbsln",
        ["Mage Masher"] = "ftsln",
        ["Were Slayer"] = "fsln",
        ["Dragon Slayer"] = "fsln",
        ["Dagger of Speed"] = "mn",
        ["Thieves Dagger"] = "tn",
        ["Shuriken"] = "n",
        ["Muramasa Blade"] = "s",
        ["Evil S-Sword +3"] = "ftsln",
        ["Evil Sword +3"] = "fsln",

        ["Small Shield"] = "fptbsln",
        ["Large Shield"] = "fpsln",
        ["Shield +1"] = "fptsln",
        ["Shield +2"] = "fptsln",
        ["Shield +3"] = "fptsln",
        ["Evil Shield +3"] = "fptsln",
        ["Shield -1"] = "fptsl",
        ["Shield -2"] = "fptsln",

        ["Robes"] = "fmptbsln",
        ["Cursed Robe -2"] = "fmptbsln",
        ["Leather Armor"] = "fptbsln",
        ["Leather +1"] = "fptbsln",
        ["Leather +2"] = "fptbsln",
        ["Leather -1"] = "fptbsl",
        ["Leather -2"] = "fptbsln",
        ["Chain Mail"] = "fpsln",
        ["Chain Mail +1"] = "fpsln",
        ["Chain +2"] = "fpsln",
        ["Evil Chain +2"] = "fpsln",
        ["Chain -1"] = "fpsln",
        ["Chain -2"] = "fpsln",
        ["Breast Plate"] = "fpsln",
        ["Breast Plate +1"] = "fpsln",
        ["Breast Plate +2"] = "fpsln",
        ["Breast Plate +3"] = "fpsln",
        ["Breast Plate -1"] = "fpsln",
        ["Breast Plate -2"] = "fpsln",
        ["Plate Mail"] = "fsln",
        ["Plate Mail +1"] = "fsln",
        ["Plate Mail +2"] = "fpsln",
        ["Neutral Plate +2"] = "fpsln",
        ["Evil Plate +3"] = "fpsln",
        ["Lords Garb +3"] = "l",

        ["Helmet"] = "fsln",
        ["Helm +1"] = "fsln",
        ["Helm +2"] = "fsln",
        ["Helm +2 (E)"] = "fsln",
        ["Cursed Helmet -2"] = "fsln",
        ["Helm of Malor"] = "fmptbsln",
        ["Gloves of Copper"] = "fsln",
        ["Silver Gloves"] = "fsln",
    };

    /// <summary>Whether <paramref name="cls"/> may use the item; null when WizardryItems.json says nothing about it.</summary>
    public static bool? Allows(WizardryClass cls, string itemName)
    {
        if (!Letters.TryGetValue(itemName ?? string.Empty, out var letters))
            return null;

        return letters.Contains(AllLetters[(int)LetterIndex(cls)]);
    }

    private static int LetterIndex(WizardryClass cls) => cls switch
    {
        WizardryClass.Fighter => 0,
        WizardryClass.Mage => 1,
        WizardryClass.Priest => 2,
        WizardryClass.Thief => 3,
        WizardryClass.Bishop => 4,
        WizardryClass.Samurai => 5,
        WizardryClass.Lord => 6,
        _ => 7
    };
}
