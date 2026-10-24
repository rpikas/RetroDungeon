namespace Adnd.Core.Spells.Casting;

public sealed class SpellCastResult
{
    public bool Success { get; set; }
    public bool SlotConsumed { get; set; }
    public string? Error { get; set; }
    public bool ReturnToTown { get; set; }
    public List<string> Events { get; set; } = new();
    public Dictionary<string, int> HpChanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SpellCastResult Failure(string error) => new()
    {
        Success = false,
        Error = error,
        Events = new List<string> { error }
    };
}
