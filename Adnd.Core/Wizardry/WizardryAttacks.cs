using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Items;

namespace Adnd.Core.Wizardry;

/// <summary>Wizardry attacks per round: class progression plus weapon swing minimum. See docs\Wizardry-Handbook.md.</summary>
public static class WizardryAttacks
{
    // Swing count of Wizardry weapons; a weapon's swings act as a minimum number of attacks.
    private static readonly Dictionary<string, int> Swings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Long Sword +1"] = 2, ["Short Sword +1"] = 2, ["Mace +1"] = 2,
        ["Short Sword -1"] = 1, ["Mace -1"] = 1, ["Staff +2"] = 1, ["Dragon Slayer"] = 1,
        ["Long Sword +2"] = 3, ["Short Sword +2"] = 3, ["Mace +2"] = 2,
        ["Were Slayer"] = 2, ["Mage Masher"] = 2, ["Mace of Poison"] = 2, ["Mace Pro Poison"] = 2,
        ["Staff of Montino"] = 1, ["Vorpal Blade +1"] = 4, ["Blade Cusinart"] = 4, ["Blade Cuisinart"] = 4,
        ["Short Sword -2"] = 1, ["Dagger +2"] = 2, ["Staff -2"] = 1,
        ["Dagger of Speed"] = 7, ["Evil Sword +3"] = 4, ["Evil S-Sword +3"] = 4, ["Thieves Dagger"] = 4,
        ["Muramasa Blade"] = 3, ["Shuriken"] = 3
    };

    public static int SwingsFor(string? weaponName) =>
        weaponName != null && Swings.TryGetValue(weaponName, out var swings) ? swings : 0;

    /// <summary>Fighter gains one attack every 4 levels from level 4; Samurai, Lord and Ninja gain theirs from level 8; others keep one.</summary>
    public static int ClassAttacks(WizardryClass cls, int level) => cls switch
    {
        WizardryClass.Fighter => 1 + Math.Max(0, level) / 4,
        WizardryClass.Samurai or WizardryClass.Lord or WizardryClass.Ninja => Math.Max(1, level / 4),
        _ => 1
    };

    public static int AttacksPerRound(WizardryClass cls, int level, string? weaponName) =>
        Math.Max(ClassAttacks(cls, level), SwingsFor(weaponName));

    /// <summary>Attacks for a Wizardry character, or null when AD&amp;D rules apply to this character.</summary>
    public static int? AttacksFor(Character member)
    {
        if (member.Ruleset != Ruleset.Wizardry || member.WizardryClass is not { } cls)
            return null;

        member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var weapon);
        return AttacksPerRound(cls, member.Level, weapon?.Name);
    }
}
