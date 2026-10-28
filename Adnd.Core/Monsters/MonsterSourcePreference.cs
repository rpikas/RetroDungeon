using Adnd.Core.Config;

namespace Adnd.Core.Monsters;

/// <summary>
/// Orders monsters so that, when two share a name (a Kobold in both books), the one matching the configured
/// source option comes first for first-match lookups.
/// </summary>
public static class MonsterSourcePreference
{
    public static List<Monster> Order(IEnumerable<Monster> monsters, SourceOptions sourceOption)
        => monsters.OrderBy(m => Rank(m, sourceOption)).ToList();

    private static int Rank(Monster monster, SourceOptions sourceOption) => sourceOption switch
    {
        SourceOptions.OnlyWizardry => monster.Source switch
        {
            Sources.Wizardry => 0,
            Sources.WizardryAndAdnd => 1,
            _ => 2
        },
        SourceOptions.OnlyAdnd or SourceOptions.OnlyAdndDMGEncounterTable or SourceOptions.AllButWizardry => monster.Source switch
        {
            Sources.Adnd => 0,
            Sources.WizardryAndAdnd => 1,
            _ => 2
        },
        _ => 0
    };
}
