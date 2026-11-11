namespace Adnd.Core.Spells;

/// <summary>Lets the Dumapic spell read the party's position from the game window without Core knowing about it.</summary>
public static class WizardryDungeonPosition
{
    /// <summary>Returns the position text (level, coordinates, facing), or null when the party is not in the dungeon.</summary>
    public static Func<string?>? Provider { get; set; }
}
