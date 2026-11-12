using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Wizardry;

namespace Adnd.Core.Treasure;

/// <summary>Who a chest trap that picks on a class of character hurts. See docs\Wizardry-Handbook.md.</summary>
public static class ChestTrapTargets
{
    /// <summary>Priest Blaster: Priests and Bishops in Wizardry, Clerics and Druids in AD&amp;D.</summary>
    public static bool IsHitByPriestBlaster(Character c) =>
        c.Ruleset == Ruleset.Wizardry
            ? c.WizardryClass is WizardryClass.Priest or WizardryClass.Bishop
            : c.Classes.Contains(CharacterClass.Cleric) || c.Classes.Contains(CharacterClass.Druid);
}
