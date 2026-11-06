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
    All,
    LocationBased // Proving Grounds: only Wizardry monsters; Maze: only AD&D monsters
}

public enum BoltacItemSourceOptions
{
    OnlyAdnd,
    OnlyWizardry,
    BothAdndAndWizardry
}

public enum Ruleset
{
    ADND,
    Wizardry
}

public class GameRules
{
    // Which game system is active; characters belong to exactly one ruleset.
    public Ruleset Rules { get; set; } = Ruleset.ADND;

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
    public SourceOptions MonsterSourceOptions { get; set; } = SourceOptions.LocationBased;

    // Set while the party is in the Proving Grounds (the Wizardry maps); false in the Maze and in town.
    [JsonIgnore]
    public bool InProvingGrounds { get; set; }

    // The source filter to apply right now: LocationBased resolves to Wizardry in the Proving Grounds, AD&D elsewhere.
    [JsonIgnore]
    public SourceOptions EffectiveMonsterSourceOptions => MonsterSourceOptions == SourceOptions.LocationBased
        ? (InProvingGrounds ? SourceOptions.OnlyWizardry : SourceOptions.OnlyAdnd)
        : MonsterSourceOptions;
    public BoltacItemSourceOptions BoltacItemSourceFilter { get; set; } = BoltacItemSourceOptions.BothAdndAndWizardry;
    public bool UIOldStyle { get; set; } = true;
    public int DelayInMsbetweenActions { get; set; } =0;
    // Milliseconds between messages in Wizardry-style combat.
    public int DelayInbetweenCombatMessages { get; set; } = 1000;
    // Percent chance (0-100) that Wizardry monsters offer to leave the party alone; their alignment is ignored.
    public int WizardryMonstersWillLeaveYouAloneChanse { get; set; } = 10;
    public bool ShowDiceRollAndRuleApplicationInfo { get; set; } = true;
    public bool ShowReferencesInRuleAndDiceInfo { get; set; } = true;
    public bool ShowToHitRoll { get; set; } = false;
    public bool PlayerSelectsWeaponProficiencies { get; set; } = false;
    public bool PlayerSelectsLanguagesOnCreation { get; set; } = false;
    public int MaxSizeEncounter { get; set; } = 2;
    public Color ForegroundColor { get; set; } = Color.White;
    [JsonIgnore]
    public Color DefaultColor
    {
        get => ForegroundColor;
        set => ForegroundColor = value;
    }


}
