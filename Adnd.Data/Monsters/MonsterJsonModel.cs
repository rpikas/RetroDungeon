using System.Collections.Generic;

namespace Adnd.Data.Monsters;

public class MonsterJsonModel
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string ClimateTerain { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string ActivityCycle { get; set; } = "";
    public string Intelligence { get; set; } = "";
    public string Alignment { get; set; } = "";
    public int NumberOfAppearancesMin { get; set; }
    public int NumberOfAppearancesMax { get; set; }
    public int ArmorClass { get; set; }
    public string MovementRate { get; set; } = "";
    public int HitDice { get; set; }
    public int HitDiceType { get; set; }
    public int ExtraHitPoints { get; set; }
    public int THAC0 { get; set; }
    public int NumberOfAttacks { get; set; }
    public string MagicResistance { get; set; } = "";
    public string Size { get; set; } = "";
    public int HitPoints { get; set; }

    public MonsterMovementJson Movement { get; set; } = new();
    public MonsterSavingThrowsJson SavingThrows { get; set; } = new();
    public MonsterMoraleJson Morale { get; set; } = new();

    public List<MonsterAttackJson> Attacks { get; set; } = new();
    public List<MonsterSpecialAbilityJson> SpecialAbilities { get; set; } = new();

    public int BaseXPValue { get; set; }
    public int XPValuePerHitPoint { get; set; }

    public string TreasureType { get; set; } = "None";
    public double? TreasureChanceOverride { get; set; }
    public string Source { get; set; } = "Adnd";
}

public class MonsterLevelJsonModel
{
    public int Level { get; set; }
    public List<MonsterJsonModel> Monsters { get; set; } = new();
}

public class MonsterMovementJson
{
    public int Walk { get; set; }
    public int Fly { get; set; }
    public int Swim { get; set; }
    public int Burrow { get; set; }
    public int Climb { get; set; }
}

public class MonsterSavingThrowsJson
{
    public int ParalyzationPoisonDeath { get; set; }
    public int RodStaffWand { get; set; }
    public int PetrificationPolymorph { get; set; }
    public int BreathWeapon { get; set; }
    public int Spell { get; set; }
}

public class MonsterMoraleJson
{
    public int Value { get; set; }
}

public class MonsterAttackJson
{
    public string Name { get; set; } = "";
    public int NumberOfAttacks { get; set; }
    public string Damage { get; set; } = "";
}

public class MonsterSpecialAbilityJson
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
