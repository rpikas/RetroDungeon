using System;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Characters;

public static class DiceRoller
{
    private static readonly Random _rng = new();

    public static int Roll(int count, int sides)
    {
        int sum = 0;
        var rolls = new int[count];
        for (int i = 0; i < count; i++)
        {
            var value = _rng.Next(1, sides + 1);
            rolls[i] = value;
            sum += value;
        }

        RuleApplicationInfo.Publish($"Rolled {count}d{sides}: [{string.Join(", ", rolls)}] => {sum}");
        return sum;
    }

    public static int Roll3d6() => Roll(3, 6);
}
