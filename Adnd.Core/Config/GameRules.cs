using System.Drawing;
using System.Text.Json.Serialization;

namespace Adnd.Core.Config;

public enum AbilityRollMethod
{
    ThreeD6InOrder,
    FourD6DropLowest,
    FiveD6Drop2Lowest,
    BestOfSixSets
}

public enum PossibleForegroupdColors
{
    /*
    Color.Green,
    Color.Red,
    Color.Blue,
    Color.Yellow,
    Color.Cyan,
    Color.Magenta,
    Color.White*/
    //    Color.Black//will be an option when background color is not black, or when foreground color is not black
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
    OnlyAdndDMGEncounterTable,
    OnlyWizardry,
    AllButWizardry,// this will include WizardryAndAdnd
    All
}

public class GameRules
{
    public double TreasureFindChance { get; set; } = 0.80;      // 0.0 - 1.0
    public double MonsterEncounterChance { get; set; } = 0.20;  // 0.0 - 1.0
    public AbilityRollMethod AbilityRollMethod { get; set; } = AbilityRollMethod.ThreeD6InOrder;
    public double XpMultiplier { get; set; } = 10.0;
    public int CharacterCreationMinGold { get; set; } = 31;
    public int CharacterCreationMaxGold { get; set; } = 210;
    public bool AutoMemorizeArcaneSpellsDaily { get; set; } = true;
    public int NumberOfItemsThatCouldBeFound { get; set; } = 5;
    public float ProbabilityFindingEachItem { get; set; } = 0.05f;
    public SourceOptions ItemSourceOptions { get; set; } = SourceOptions.All;
    public SourceOptions MonsterSourceOptions { get; set; } = SourceOptions.OnlyWizardry;
    public bool UIOldStyle { get; set; } = true;
    public int DelayInMsbetweenActions { get; set; } =0;
    public bool ShowDiceRollAndRuleApplicationInfo { get; set; } = true;
    public int MaxSizeEncounter { get; set; } = 2;
    public Color ForegroundColor { get; set; } = Color.White;
    [JsonIgnore]
    public Color DefaultColor
    {
        get => ForegroundColor;
        set => ForegroundColor = value;
    }


}
