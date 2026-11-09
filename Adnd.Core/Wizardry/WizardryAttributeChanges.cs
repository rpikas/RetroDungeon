using Adnd.Core.Characters;

namespace Adnd.Core.Wizardry;

/// <summary>Attribute changes when a Wizardry character gains a level. See docs\Wizardry-Handbook.md.</summary>
public static class WizardryAttributeChanges
{
    public const int MaxAttribute = 18;
    public const int ModifyChancePercent = 75;
    public const int AtMaxNoChangeChancePercent = 83; // 5 in 6 (83.3%)

    public static readonly string[] Names = { "STRENGTH", "I.Q.", "PIETY", "VITALITY", "AGILITY", "LUCK" };

    /// <summary>
    /// Rolls every attribute once for one level gained. Returns (attribute name, +1 or -1) for each that changed.
    /// A Vitality that falls below 3 kills the character.
    /// </summary>
    public static List<(string Attribute, int Delta)> Roll(Character c, int peakAge, Random random)
    {
        var changes = new List<(string, int)>();
        var a = c.Abilities;
        var values = new[] { a.Strength, a.Intelligence, a.Wisdom, a.Constitution, a.Dexterity, a.Charisma };
        var range = Math.Max(1, peakAge * 2);

        for (var i = 0; i < values.Length; i++)
        {
            if (random.Next(100) >= ModifyChancePercent)
                continue;

            var roll = random.Next(range);
            var delta = 0;
            if (c.Age > roll)
            {
                if (values[i] >= MaxAttribute)
                    delta = random.Next(6) == 0 ? -1 : 0;
                else
                    delta = -1;
            }
            else if (c.Age < roll && values[i] < MaxAttribute)
            {
                delta = 1;
            }

            if (delta == 0)
                continue;

            values[i] += delta;
            changes.Add((Names[i], delta));
        }

        a.Strength = values[0];
        a.Intelligence = values[1];
        a.Wisdom = values[2];
        a.Constitution = values[3];
        a.Dexterity = values[4];
        a.Charisma = values[5];

        if (a.Constitution < 3)
        {
            c.CurrentHitPoints = 0;
            c.AddStatus(CharacterStatus.Dead);
        }

        return changes;
    }
}
