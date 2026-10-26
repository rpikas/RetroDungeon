namespace Adnd.Core.Spells.Casting;

public sealed class SpellCastResult
{
    public bool Success { get; set; }
    public bool SlotConsumed { get; set; }
    public string? Error { get; set; }
    public bool ReturnToTown { get; set; }

    /// <summary>Steps of extra sight the party gains (Light). Zero when the spell grants none.</summary>
    public int LightSteps { get; set; }

    /// <summary>The party now carries a permanent light (Continual Light).</summary>
    public bool ContinualLight { get; set; }

    /// <summary>Dispel Magic cast outside combat: ends the party's Light and Continual Light.</summary>
    public bool DispelsLight { get; set; }
    public List<string> Events { get; set; } = new();
    public Dictionary<string, int> HpChanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SpellCastResult Failure(string error) => new()
    {
        Success = false,
        Error = error,
        Events = new List<string> { error }
    };
}
