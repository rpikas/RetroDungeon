namespace Adnd.Core.Dungeon;

/// <summary>The four edges of a square. The values are the bits stored in a wall mask.</summary>
[Flags]
public enum WallSide
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8
}

/// <summary>
/// A grid of squares whose walls sit on the edges between squares (thin walls) instead of occupying
/// squares of their own. Each square holds a mask: 1 = wall to the north, 2 = east, 4 = south, 8 = west,
/// added together, so 3 is north and east and 15 is walled in on every side. Doors use the next four bits:
/// 16 = door north, 32 = east, 64 = south, 128 = west. A door is drawn as a wall with a door in it and
/// blocks the view, but the party can walk through it.
/// Row 0 is the northernmost row and column 0 the westernmost, matching the maze's coordinates.
/// The map wraps around: the square beyond the last column is column 0, and likewise for rows.
/// </summary>
public sealed class WallGrid
{
    public const int AllWalls = 15;
    public const int DoorShift = 4;
    public const int MaxMask = 255;

    private readonly byte[,] _masks;

    public int Width { get; }
    public int Height { get; }

    public WallGrid(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A wall grid needs at least one square.");

        Width = width;
        Height = height;
        _masks = new byte[width, height];
    }

    /// <summary>Builds a grid from rows listed north to south, each running west to east.</summary>
    public static WallGrid FromRows(IReadOnlyList<IReadOnlyList<int>> rows)
    {
        if (rows.Count == 0 || rows[0].Count == 0)
            throw new ArgumentException("The wall rows are empty.", nameof(rows));

        var grid = new WallGrid(rows[0].Count, rows.Count);
        for (var y = 0; y < rows.Count; y++)
        {
            if (rows[y].Count != grid.Width)
                throw new ArgumentException($"Row {y} has {rows[y].Count} squares, expected {grid.Width}.", nameof(rows));

            for (var x = 0; x < grid.Width; x++)
                grid.SetMask(x, y, rows[y][x]);
        }

        return grid;
    }

    /// <summary>
    /// Converts a layout where walls are squares of their own: every walkable square gets a wall on each
    /// side that borders a non-walkable square (or the edge), and every non-walkable square becomes solid.
    /// </summary>
    public static WallGrid FromFloors(int width, int height, Func<int, int, bool> isFloor)
    {
        var grid = new WallGrid(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!isFloor(x, y))
                {
                    grid._masks[x, y] = AllWalls;
                    continue;
                }

                var mask = 0;
                if (y == 0 || !isFloor(x, y - 1)) mask |= (int)WallSide.North;
                if (x == width - 1 || !isFloor(x + 1, y)) mask |= (int)WallSide.East;
                if (y == height - 1 || !isFloor(x, y + 1)) mask |= (int)WallSide.South;
                if (x == 0 || !isFloor(x - 1, y)) mask |= (int)WallSide.West;
                grid._masks[x, y] = (byte)mask;
            }
        }

        return grid;
    }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int GetMask(int x, int y) => Contains(x, y) ? _masks[x, y] : AllWalls;

    public void SetMask(int x, int y, int mask)
    {
        if (!Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"Square ({x},{y}) is outside the grid.");
        if (mask < 0 || mask > MaxMask)
            throw new ArgumentOutOfRangeException(nameof(mask), $"Wall mask {mask} at ({x},{y}) must be between 0 and 255.");

        _masks[x, y] = (byte)mask;
    }

    /// <summary>A square walled in on all four sides. Nobody can enter it, so it plays as solid rock.</summary>
    public bool IsSolid(int x, int y) => (GetMask(x, y) & AllWalls) == AllWalls;

    /// <summary>Whether there is a door on the given edge, recorded on either of the two squares that share it.</summary>
    public bool HasDoor(int x, int y, WallSide side)
    {
        if (!Contains(x, y))
            return false;

        var bit = (int)side << DoorShift;
        if ((_masks[x, y] & bit) != 0)
            return true;

        var (nx, ny, opposite) = Neighbour(x, y, side);
        return (_masks[nx, ny] & ((int)opposite << DoorShift)) != 0;
    }

    /// <summary>The map wraps around: stepping off one edge puts you on the opposite edge.</summary>
    public (int X, int Y) Wrap(int x, int y)
        => (((x % Width) + Width) % Width, ((y % Height) + Height) % Height);

    /// <summary>Whether the view across the given edge is stopped: a wall or a (closed) door.</summary>
    public bool BlocksSight(int x, int y, WallSide side) => HasWall(x, y, side) || HasDoor(x, y, side);

    /// <summary>
    /// Whether there is a wall on the given edge of a square. A wall recorded on either of the two squares
    /// that share the edge counts, so a hand-entered map only has to get it right once. Anything outside the
    /// grid is wall.
    /// </summary>
    public bool HasWall(int x, int y, WallSide side)
    {
        if (!Contains(x, y))
            return true;

        if ((_masks[x, y] & (int)side) != 0)
            return true;

        var (nx, ny, opposite) = Neighbour(x, y, side);
        return (_masks[nx, ny] & (int)opposite) != 0;
    }

    /// <summary>Whether a step across the given edge is possible: no wall, and a non-solid square beyond it.</summary>
    public bool CanMove(int x, int y, WallSide side)
    {
        if (HasWall(x, y, side))
            return false;

        var (nx, ny, _) = Neighbour(x, y, side);
        return !IsSolid(nx, ny);
    }

    /// <summary>Edges where the two squares disagree about whether there is a wall. Useful when checking a hand-drawn map.</summary>
    public IEnumerable<string> FindInconsistentEdges()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (x + 1 < Width && ((_masks[x, y] & (int)WallSide.East) != 0) != ((_masks[x + 1, y] & (int)WallSide.West) != 0))
                    yield return $"({x},{y}) east vs ({x + 1},{y}) west";

                if (y + 1 < Height && ((_masks[x, y] & (int)WallSide.South) != 0) != ((_masks[x, y + 1] & (int)WallSide.North) != 0))
                    yield return $"({x},{y}) south vs ({x},{y + 1}) north";
            }
        }
    }

    public static (int X, int Y) Step(int x, int y, WallSide side) => side switch
    {
        WallSide.North => (x, y - 1),
        WallSide.East => (x + 1, y),
        WallSide.South => (x, y + 1),
        WallSide.West => (x - 1, y),
        _ => throw new ArgumentOutOfRangeException(nameof(side), "Expected a single side.")
    };

    private (int X, int Y, WallSide Opposite) Neighbour(int x, int y, WallSide side)
    {
        var (sx, sy) = Step(x, y, side);
        var (nx, ny) = Wrap(sx, sy);
        var opposite = side switch
        {
            WallSide.North => WallSide.South,
            WallSide.East => WallSide.West,
            WallSide.South => WallSide.North,
            _ => WallSide.East
        };
        return (nx, ny, opposite);
    }
}
