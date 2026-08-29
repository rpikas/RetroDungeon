using System;
using System.Collections.Generic;
using System.Text;

namespace Adnd.Core.Experience
{

    public class XpCalculator
    {
        // DMG Appendix E: Basic XP Value per Hit Die (exakt enligt tabellen du visade)
        private static readonly Dictionary<int, int> BasicXpByHD = new()
    {
        { 1, 10 },
        { 2, 35 },
        { 3, 60 },
        { 4, 90 },
        { 5, 159},
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

        /// <summary>
        /// DMG Appendix E RAW + valfri damage-XP (inte originalregel).
        /// </summary>
        public int CalculateXp(
            int hitDice,
            int hitPoints,
            int maxDamage, // NOT DMG RAW
            int specialAbilityBonus = 0,
            int exceptionalAbilityAddition = 0
        )
        {
            int baseXp = GetBaseXp(hitDice);

            // DMG RAW: +1 XP per hit point
            int hpXp = hitPoints;

            int specialXp = specialAbilityBonus;
            int exceptionalXp = exceptionalAbilityAddition;

            int damageXp = GetDamageXp(maxDamage); // husregel

            return baseXp + hpXp + specialXp + exceptionalXp + damageXp;
        }

        private int GetBaseXp(int hitDice)
        {
            if (BasicXpByHD.TryGetValue(hitDice, out int xp))
                return xp;

            // Enkel extrapolering för HD > 21
            return 5000 + (hitDice - 21) * 2500;
        }

        /// <summary>
        /// Husregel: XP för max damage (inte DMG RAW).
        /// </summary>
        private int GetDamageXp(int maxDamage)
        {
            if (maxDamage <= 0)
                return 0;

            return (int)Math.Ceiling(maxDamage / 2.0);
        }
    }
}

