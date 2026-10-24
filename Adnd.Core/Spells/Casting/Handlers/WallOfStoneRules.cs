using System.Text.RegularExpressions;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public static class WallOfStoneRules
{
    private static readonly string[] BurrowerNames = { "Purple Worm", "Umber Hulk", "Earth Elemental", "Xorn" };

    private static readonly string[] BreacherNames =
    {
        "Lich", "Beholder", "Orcus", "Demogorgon", "Juiblex", "Yeenoghu", "Asmodeus", "Baalzebul", "Dispater", "Geryon",
        "Mephistopheles", "Mammon", "Belial", "Arch Mage", "High Priest", "Bishop", "Master Thief"
    };

    public static bool IsBurrower(MonsterInstance monster) =>
        BurrowerNames.Any(n => monster.Template.Name?.Contains(n, StringComparison.OrdinalIgnoreCase) == true);

    public static bool IsBreacher(MonsterInstance monster)
    {
        var name = monster.Template.Name ?? string.Empty;
        var type = monster.Template.TypeName ?? string.Empty;
        if (BreacherNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (type.Contains("Demon Prince", StringComparison.OrdinalIgnoreCase)
            || type.Contains("Demon Lord", StringComparison.OrdinalIgnoreCase)
            || type.Contains("Arch", StringComparison.OrdinalIgnoreCase) && type.Contains("Devil", StringComparison.OrdinalIgnoreCase))
            return true;

        var m = Regex.Match(name, @"^Lvl\s+(\d+)\s+(Magic-User|Cleric|Illusionist|Druid)", RegexOptions.IgnoreCase);
        return m.Success && int.Parse(m.Groups[1].Value) >= 9;
    }
}
