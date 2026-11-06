using Adnd.Core.Characters;
using Adnd.Core.Config;

namespace Adnd.Core.Wizardry;

/// <summary>Builds new level 1 Wizardry characters. See docs\Wizardry-Handbook.md.</summary>
public static class WizardryCharacterFactory
{
    // Closest AD&D class, so systems that still read Classes (saves, spell tracks, equipment) have something valid.
    public static CharacterClass AdndEquivalent(WizardryClass cls) => cls switch
    {
        WizardryClass.Mage or WizardryClass.Bishop => CharacterClass.MagicUser,
        WizardryClass.Priest => CharacterClass.Cleric,
        WizardryClass.Thief => CharacterClass.Thief,
        WizardryClass.Lord => CharacterClass.Paladin,
        WizardryClass.Ninja => CharacterClass.Assassin,
        _ => CharacterClass.Fighter
    };

    public static Alignment AdndAlignment(WizardryAlignment alignment) => alignment switch
    {
        WizardryAlignment.Good => Alignment.LawfulGood,
        WizardryAlignment.Evil => Alignment.ChaoticEvil,
        _ => Alignment.TrueNeutral
    };

    public static Character Create(Race race, WizardryStats stats, WizardryAlignment alignment, WizardryClass cls, Random random)
    {
        var hp = WizardryRules.StartingHitPoints;

        var character = new Character
        {
            Name = "Unknown",
            Ruleset = Ruleset.Wizardry,
            Race = race,
            WizardryClass = cls,
            Classes = new List<CharacterClass> { AdndEquivalent(cls) },
            Abilities = new AbilityScores
            {
                Strength = stats.Strength,
                Intelligence = stats.Iq,
                Wisdom = stats.Piety,
                Constitution = stats.Vitality,
                Dexterity = stats.Agility,
                Charisma = stats.Luck
            },
            Alignment = AdndAlignment(alignment),
            Gender = random.Next(2) == 0 ? Gender.Male : Gender.Female,
            Level = 1,
            Experience = 0,
            MaxHitPoints = hp,
            CurrentHitPoints = hp,
            GoldPieces = WizardryRules.RollStartingGold(random),
            ArmorClass = 10,
            NumberOfAttacks = 1,
            Damage = cls == WizardryClass.Ninja ? "2d4" : "2d2",
            Age = WizardryRules.MinStartingAge + random.Next(0, WizardryRules.StartingAgeSpread)
        };

        character.EnsureClassProgressions();
        WizardrySpellLearning.Initialize(character, cls);
        return character;
    }
}
