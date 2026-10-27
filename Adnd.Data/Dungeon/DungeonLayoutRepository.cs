using System.Text.Json;
using Adnd.Core.Dungeon;

namespace Adnd.Data.Dungeon;

/// <summary>
/// A square with a fixed encounter: the first time the party steps on it after entering the floor there is
/// a <see cref="ChancePercent"/> chance of an encounter, always in the monsters' lair when they have one.
/// </summary>
public sealed class DungeonEncounterLocation
{
    public int X { get; set; }
    public int Y { get; set; }
    public int ChancePercent { get; set; } = 80;
}

/// <summary>A rectangle of squares (corners inclusive, in any order) filled with magical darkness.</summary>
public sealed class DungeonDarkArea
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }

    public bool Contains(int x, int y)
        => x >= Math.Min(X1, X2) && x <= Math.Max(X1, X2)
           && y >= Math.Min(Y1, Y2) && y <= Math.Max(Y1, Y2);
}

/// <summary>
/// Loads hand-defined thin-wall levels from <c>Data/Dungeon/LevelN.json</c>. A level without a file is simply
/// not defined yet; the caller decides what to do about it.
/// </summary>
public sealed class DungeonLayoutRepository
{
    private sealed class DarkAreasFile
    {
        public int Level { get; set; }
        public List<DungeonDarkArea> Areas { get; set; } = new();
    }
    private sealed class LayoutFile
    {
        public int Level { get; set; }

        // Rows from north to south, squares from west to east. Bits: 1 north, 2 east, 4 south, 8 west.
        public List<List<int>> Walls { get; set; } = new();

        // Doors that look like plain wall until the party finds them.
        public List<SecretDoorEntry> SecretDoors { get; set; } = new();
    }

    private sealed class SecretDoorEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Side { get; set; } = "East";
    }

    private sealed class EncounterLocationsFile
    {
        public int Level { get; set; }
        public List<DungeonEncounterLocation> Locations { get; set; } = new();
    }

    private readonly string _folder;

    public DungeonLayoutRepository(string folder = "Data/Dungeon")
    {
        _folder = folder;
    }

    /// <summary>Fixed encounter squares for a level, from <c>LevelN.Encounters.json</c>. Empty when there is no file.</summary>
    public IReadOnlyList<DungeonEncounterLocation> LoadEncounterLocations(int level)
    {
        var path = Path.Combine(_folder, $"Level{level}.Encounters.json");
        if (!File.Exists(path))
            return Array.Empty<DungeonEncounterLocation>();

        var file = JsonSerializer.Deserialize<EncounterLocationsFile>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"{path} is empty.");
        return file.Locations;
    }

    /// <summary>Magical darkness for a level, from <c>LevelN.Darkness.json</c>. Empty when there is no file.</summary>
    public IReadOnlyList<DungeonDarkArea> LoadDarkAreas(int level)
    {
        var path = Path.Combine(_folder, $"Level{level}.Darkness.json");
        if (!File.Exists(path))
            return Array.Empty<DungeonDarkArea>();

        var file = JsonSerializer.Deserialize<DarkAreasFile>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"{path} is empty.");
        return file.Areas;
    }

    public WallGrid? TryLoad(int level)
    {
        var path = Path.Combine(_folder, $"Level{level}.json");
        if (!File.Exists(path))
            return null;

        var file = JsonSerializer.Deserialize<LayoutFile>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"{path} is empty.");

        try
        {
            var grid = WallGrid.FromRows(file.Walls.Select(row => (IReadOnlyList<int>)row).ToList());
            foreach (var door in file.SecretDoors)
                grid.HideSecretDoor(door.X, door.Y, Enum.Parse<WallSide>(door.Side));
            return grid;
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }
}
