using System;
using System.Diagnostics;
using Adnd.Core.Config;

namespace Adnd.Core.Characters;

public class CharacterCreator
{
    // AD&D defensive adjustment from Dexterity.
    // Returned value is added to AC (lower AC is better, so high DEX yields negative values).
    public int GetDexterityArmorClassAdjustment(int dexterity)
    {
        return dexterity switch
        {
            <= 3 => 4,
            4 => 3,
            5 => 2,
            6 => 1,
            <= 14 => 0,
            15 => -1,
            16 => -2,
            17 => -3,
            _ => -4
        };
    }

    public AbilityScores RollAbilities()
    {
        AbilityScores RollThreeD6InOrder()
        {
            return new AbilityScores
            {
                Strength = DiceRoller.Roll3d6(),
                Intelligence = DiceRoller.Roll3d6(),
                Wisdom = DiceRoller.Roll3d6(),
                Dexterity = DiceRoller.Roll3d6(),
                Constitution = DiceRoller.Roll3d6(),
                Charisma = DiceRoller.Roll3d6()
            };
        }

        int Roll4d6DropLowest()
        {
            var a = DiceRoller.Roll(1, 6);
            var b = DiceRoller.Roll(1, 6);
            var c = DiceRoller.Roll(1, 6);
            var d = DiceRoller.Roll(1, 6);
            var lowest = Math.Min(Math.Min(a, b), Math.Min(c, d));
            return a + b + c + d - lowest;
        }

        AbilityScores RollFourD6DropLowest()
        {
            return new AbilityScores
            {
                Strength = Roll4d6DropLowest(),
                Intelligence = Roll4d6DropLowest(),
                Wisdom = Roll4d6DropLowest(),
                Dexterity = Roll4d6DropLowest(),
                Constitution = Roll4d6DropLowest(),
                Charisma = Roll4d6DropLowest()
            };
        }

        AbilityScores RollBestSix()
        {
            return new AbilityScores
            {
                Strength = RollBestOf99(),//TODO: changing this to RollBestOfSix() just for testing.
                Intelligence = RollBestOfSix(),
                Wisdom = RollBestOfSix(),
                Dexterity = RollBestOfSix(),
                Constitution = RollBestOfSix(),
                Charisma = RollBestOfSix()
            };
        }

        int RollBestOfSix()
        {
            int best = 0;
            for (int i = 0; i < 6; i++)
            {
                int v = DiceRoller.Roll3d6();
                if (v > best) best = v;
            }
            return best;
        }

        int RollBestOf99()
        {
            int best = 0;
            for (int i = 0; i < 99; i++)
            {
                int v = DiceRoller.Roll3d6();
                if (v > best) best = v;
            }
            return best;
        }

        var method = GameRulesProvider.Current.AbilityRollMethod;
        return method switch
        {
            AbilityRollMethod.ThreeD6InOrder => RollThreeD6InOrder(),
            AbilityRollMethod.FourD6DropLowest => RollFourD6DropLowest(),
            AbilityRollMethod.BestOfSixSets => RollBestSix(),
            _ => RollBestSix()
        };
    }

    public int RollHitPoints(CharacterClass cls, int constitution, int level = 1)
    {
        int hd = cls switch
        {
            CharacterClass.Fighter => RollHitDie(10, level),
            CharacterClass.Ranger => RollHitDie(10, level),
            CharacterClass.Paladin => RollHitDie(10, level),
            CharacterClass.Cleric => RollHitDie(8, level),
            CharacterClass.Druid => RollHitDie(8, level),
            CharacterClass.Thief => RollHitDie(6, level),
            CharacterClass.Assassin => RollHitDie(6, level),
            CharacterClass.Monk => RollHitDie(6, level),
            CharacterClass.Bard => RollHitDie(6, level),
            CharacterClass.Illusionist => RollHitDie(4, level),
            CharacterClass.MagicUser => RollHitDie(4, level),
            _ => RollHitDie(6, level)
        };
        int conMod = 0;

        if (cls == CharacterClass.Fighter || cls == CharacterClass.Paladin || cls == CharacterClass.Ranger) { 
             conMod = constitution switch
            {
                <= 6 => -1,
                7 or 8 or 9 or 10 or 11 or 12 => 0,
                13 or 14 => 1,
                15 or 16 => 2,
                17 => 3,
                18 or 19 or 20 or 21 => 4,
                22 or 23 => 5,
                24 or 25 => 6,
                _ => 0
            }; 
        }
        else // For non-fighter classes, apply the same logic for Constitution modifier
        {
            conMod = constitution switch
            {
                <= 6 => -1,
                7 or 8 or 9 or 10 or 11 or 12 => 0,
                15 => 1,
                16 or 17 or 18 => 2,
                19 => 3,
                20 or 21 => 4,
                22 or 23 => 5,
                24 or 25 => 6,
                _ => 0
            };
        }

        int hp = hd + conMod;
        return hp < 1 ? 1 : hp;
    }

    // Roll a hit die with level-1 logic: at level 1, roll must be above average (>= half dice size + 1)
    private int RollHitDie(int dieSize, int level)
    {
        if (level == 1)
        {
            // First level: roll must be above average (e.g., d6 = 4-6, d8 = 5-8, d10 = 6-10)
            int minRoll = (dieSize / 2) + 1;
            int roll;
            do
            {
                roll = DiceRoller.Roll(1, dieSize);
            } while (roll < minRoll);
            return roll;
        }
        else
        {
            // Above level 1: normal roll
            return DiceRoller.Roll(1, dieSize);
        }
    }

    // Roll starting hit points for a multiclass character: roll the appropriate
    // hit die for each class, average the dice results (rounded), then add
    // Constitution modifier once.
    public int RollStartingHitPointsForClasses(System.Collections.Generic.List<CharacterClass> classes, int constitution, int level = 1)
    {
        if (classes == null || classes.Count == 0)
            return RollHitPoints(CharacterClass.Fighter, constitution, level);

        double sumDice = 0.0;
        foreach (var cls in classes)
        {
            int dieSize = cls switch
            {
                CharacterClass.Fighter => 10,
                CharacterClass.Ranger => 10,
                CharacterClass.Paladin => 10,
                CharacterClass.Cleric => 8,
                CharacterClass.Druid => 8,
                CharacterClass.Thief => 6,
                CharacterClass.Assassin => 6,
                CharacterClass.Monk => 6,
                CharacterClass.Bard => 6,
                CharacterClass.Illusionist => 4,
                CharacterClass.MagicUser => 4,
                _ => 6
            };

            sumDice += RollHitDie(dieSize, level);
        }

        int avgDie = (int)Math.Round(sumDice / classes.Count, MidpointRounding.AwayFromZero);

        int conMod = constitution switch
        {
            <= 6 => -1,
            7 or 8 or 9 or 10 or 11 or 12 => 0,
            13 or 14 => 1,
            15 or 16 => 2,
            17 => 3,
            18 => 4,
            _ => 0
        };

        int hp = avgDie + conMod;
        return hp < 1 ? 1 : hp;
    }

    public AbilityScores ApplyRaceModifiers(AbilityScores abilities, Race race)
    {
        var adjusted = new AbilityScores
        {
            Strength = abilities.Strength,
            Intelligence = abilities.Intelligence,
            Wisdom = abilities.Wisdom,
            Dexterity = abilities.Dexterity,
            Constitution = abilities.Constitution,
            Charisma = abilities.Charisma
        };

        switch (race)
        {
            case Race.Human:
                break;
            case Race.Elf:
                adjusted.Intelligence += 1;
                adjusted.Dexterity += 1;
                adjusted.Constitution -= 1;
                break;
            case Race.Dwarf:
                adjusted.Constitution += 1;
                adjusted.Charisma -= 1;
                break;
            case Race.Gnome:
                adjusted.Constitution += 1;
                adjusted.Intelligence += 1;
                adjusted.Strength -= 1;
                break;
            case Race.Halfling:
                adjusted.Dexterity += 1;
                adjusted.Strength -= 1;
                break;
            case Race.HalfElf:
                break;
            case Race.HalfOrc:
                adjusted.Strength += 1;
                adjusted.Intelligence -= 1;
                adjusted.Charisma -= 1;
                break;
        }

        return adjusted;
    }

    public Character Create(string name, Race race, CharacterClass cls)
    {
        var abilities = RollAbilities();
        abilities = ApplyRaceModifiers(abilities, race);
        int hp = RollHitPoints(cls, abilities.Constitution);
        int armorClass = 10 + GetDexterityArmorClassAdjustment(abilities.Dexterity);

        var minGold = GameRulesProvider.Current.CharacterCreationMinGold;
        var maxGold = GameRulesProvider.Current.CharacterCreationMaxGold;
        var startingGold = minGold == maxGold
            ? minGold
            : Random.Shared.Next(minGold, maxGold + 1);

        return new Character
        {
            Name = name,
            Race = race,
            Classes = new System.Collections.Generic.List<CharacterClass> { cls },
            Abilities = abilities,
            Level = 1,
            MaxHitPoints = hp,
            CurrentHitPoints = hp,
            Experience = 0,
            GoldPieces = startingGold,
            ArmorClass = armorClass,
            Gender = Gender.Male,
            Alignment = Alignment.TrueNeutral,
            NumberOfAttacks = 1,
            Damage = "1d2",
            Age = Random.Shared.Next(17, 29)
        };
    }
}
