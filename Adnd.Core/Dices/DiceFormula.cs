using System;
using System.Collections.Generic;
using System.Text;

namespace Adnd.Core.Dices
{
    public static class DiceFormulas
    {
        public record DiceFormula(int DiceCount, int DiceSides, int Extra);

        public static DiceFormula GetDiceFormula(int min, int max)
        {
            // Try dice counts from 1 to something reasonable
            for (int dice = 1; dice <= 20; dice++)
            {
                // Try common dice sizes
                int[] sides = { 2, 3, 4, 6, 8, 10, 12, 20 };

                foreach (int side in sides)
                {
                    // Minimum possible roll with these dice
                    int possibleMin = dice * 1;
                    // Maximum possible roll with these dice
                    int possibleMax = dice * side;

                    // We want a shift Z so that:
                    // min = possibleMin + Z
                    // max = possibleMax + Z
                    int shift = min - possibleMin;

                    if (possibleMax + shift == max)
                    {
                        return new DiceFormula(dice, side, shift);
                    }
                }
            }

            throw new InvalidOperationException($"Cannot express {min}-{max} as XdY+Z");
        }

    }
}
