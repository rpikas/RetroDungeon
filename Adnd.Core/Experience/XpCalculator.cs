using System;
using System.Collections.Generic;
using System.Text;

namespace Adnd.Core.Experience
{

    public class XpCalculator
    {
        // DMG, page 85 Appendix E: Basic XP Value per Hit Die
        public static readonly Dictionary<int, int> BasicXpByHD = new()
    {
        { 1, 10 },
        { 2, 35 },
        { 3, 60 },
        { 4, 90 },
        { 5, 159 },
        { 6, 225 },
        { 7, 375 },
        { 8, 600 },
        { 9, 900 },
        { 10, 900 },
        { 11, 1300 },
        { 12, 1300 },
        { 13, 1800 },
        { 14, 1800 },
        { 15, 2400 },
        { 16, 2400 },
        { 17, 3000 },
        { 18, 3000 },
        { 19, 4000 },
        { 20, 4000 },
        { 21, 5000 }
    };

        // DMG, page 85 Appendix E: XP per HP depending on HD
        public static readonly Dictionary<int, int> XpPerHpByHD = new()
    {
        { 1, 1 },
        { 2, 3 },
        { 3, 5 },
        { 4, 5 },
        { 5, 6 },
        { 6, 8 },
        { 7, 10 },
        { 8, 12 },
        { 9, 14 },
        { 10, 14 },
        { 11, 16 },
        { 12, 16 },
        { 13, 18 },
        { 14, 18 },
        { 15, 20 },
        { 16, 20 },
        { 17, 25 },
        { 18, 25 },
        { 19, 30 },
        { 20, 30 },
        { 21, 35 }
    };

        public int CalculateXp(
            int hitDice,
            int hitPoints,
            int maxDamage, // NOT DMG RAW
            int specialAbilityBonus = 0,
            int exceptionalAbilityAddition = 0
        )
        {
            int baseXp = GetBaseXp(hitDice);

            int hpXp = GetHpXp(hitDice, hitPoints);

            int specialXp = specialAbilityBonus;
            int exceptionalXp = exceptionalAbilityAddition;

            int damageXp = GetDamageXp(maxDamage); // husregel

            return baseXp + hpXp + specialXp + exceptionalXp + damageXp;
        }

        public int GetBaseXp(int hitDice)
        {
            if (BasicXpByHD.TryGetValue(hitDice, out int xp))
                return xp;

            return 5000 + (hitDice - 21) * 2500;
        }

        public int GetHpXp(int hitDice, int hitPoints)
        {
            if (!XpPerHpByHD.TryGetValue(hitDice, out int xpPerHp))
                xpPerHp = 1;

            return xpPerHp * hitPoints;
        }

        public int GetDamageXp(int maxDamage)
        {
            if (maxDamage <= 0)
                return 0;

            return (int)Math.Ceiling(maxDamage / 2.0);
        }
    }

}

