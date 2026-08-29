using System;
using System.Collections.Generic;
using System.Text;

namespace Adnd.Core.Characters.Progression
{
    public static class XpBonusCalculator
    {
        public static int GetXpModifier(CharacterClass cls, AbilityScores a)
        {
            // Determine prime requisites for the class
            List<int> primeReqs = cls switch
            {
                CharacterClass.Fighter => new() { a.Strength },
                CharacterClass.Paladin => new() { a.Strength, a.Charisma },
                CharacterClass.Ranger => new() { a.Strength, a.Intelligence, a.Wisdom },

                CharacterClass.Cleric => new() { a.Wisdom },
                CharacterClass.Druid => new() { a.Wisdom, a.Charisma },

                CharacterClass.MagicUser => new() { a.Intelligence },
                CharacterClass.Illusionist => new() { a.Intelligence },

                CharacterClass.Thief => new() { a.Dexterity },
                CharacterClass.Assassin => new() { a.Strength, a.Dexterity, a.Intelligence },

                CharacterClass.Monk => new() { a.Strength, a.Dexterity, a.Wisdom },
                CharacterClass.Bard => new() { a.Dexterity, a.Intelligence, a.Charisma },

                _ => throw new ArgumentOutOfRangeException(nameof(cls))
            };

            // Lowest prime requisite determines XP bonus
            int lowest = primeReqs.Min();

            return lowest switch
            {
                <= 5 => -20,
                <= 8 => -10,
                <= 12 => 0,
                <= 15 => 5,
                _ => 10
            };
        }
    }
}