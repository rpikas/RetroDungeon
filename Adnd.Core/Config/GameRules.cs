namespace Adnd.Core.Config;

public enum AbilityRollMethod
{
    ThreeD6InOrder,
    FourD6DropLowest,
    BestOfSixSets
}

public enum Sources
{
    Adnd,
    Wizardry,
    WizardryAndAdnd,
    Other
}

public enum SourceOptions
{
    OnlyAdnd,
    OnlyWizardry,
    AllButWizardry,// this will include WizardryAndAdnd
    All
}

public class GameRules
{
    public double TreasureFindChance { get; set; } = 0.80;      // 0.0 - 1.0
    public double MonsterEncounterChance { get; set; } = 0.20;  // 0.0 - 1.0
    public AbilityRollMethod AbilityRollMethod { get; set; } = AbilityRollMethod.ThreeD6InOrder;
    public double XpMultiplier { get; set; } = 1.0;
    public int CharacterCreationMinGold { get; set; } = 31;
    public int CharacterCreationMaxGold { get; set; } = 210;
    public bool AutoMemorizeArcaneSpellsDaily { get; set; } = true;
    public int NumberOfItemsThatCouldBeFound { get; set; } = 5;
    public float ProbabilityFindingEachItem { get; set; } = 0.0f;
    public SourceOptions ItemSourceOptions { get; set; } = SourceOptions.All;
    public SourceOptions MonsterSourceOptions { get; set; } = SourceOptions.OnlyWizardry;


}
