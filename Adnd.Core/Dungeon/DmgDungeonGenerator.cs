namespace Adnd.Core.Dungeon;

/// <summary>
/// Builds a thin-wall dungeon level with the random dungeon generation of the AD&amp;D Dungeon Masters Guide
/// (Appendix A, tables I-V). One square is 10 feet. Only 90 degree turns are used: every 45 degree result
/// becomes a 90 degree one. Stairs, tricks/traps, wandering monsters, columns, streams, rivers and chasms
/// have no effect on the map; the passage just continues.
/// </summary>
public sealed class DmgDungeonGenerator
{
    public const int Size = 22;

    /// <summary>The elevator square of the start room.</summary>
    public static readonly (int X, int Y) Start = (1, 2);

    private const int TargetFloorSquares = 190;
    private const int MaxSteps = 4000;

    private static readonly WallSide[] Sides = { WallSide.North, WallSide.East, WallSide.South, WallSide.West };

    private readonly Random _random;
    private readonly WallGrid _grid = new(Size, Size);
    private readonly bool[,] _floor = new bool[Size, Size];
    private readonly Queue<Walker> _walkers = new();
    private int _floorCount;

    private readonly record struct Walker(int X, int Y, WallSide Dir, int Width, int Distance);

    private readonly int _seed;

    private DmgDungeonGenerator(int seed)
    {
        _seed = seed;
        _random = new Random(seed);
    }

    public static WallGrid Generate(int seed) => GenerateLevel(seed).Walls;

    /// <param name="corridorsOnly">Every passage is a single-square corridor; no wide passages or galleries.</param>
    public static DmgDungeonLevel GenerateLevel(int seed, bool corridorsOnly = false)
        => new DmgDungeonGenerator(seed) { _corridorsOnly = corridorsOnly }.Build();

    private bool _corridorsOnly;

    private readonly List<List<(int X, int Y)>> _rooms = new();
    private readonly Dictionary<(int X, int Y), int> _roomOf = new();

    private int RoomId(int x, int y) => _roomOf.TryGetValue((x, y), out var id) ? id : -1;

    private DmgDungeonLevel Build()
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                _grid.SetMask(x, y, WallGrid.AllWalls);

        BuildStartArea();

        var steps = 0;
        while (steps++ < MaxSteps && _floorCount < TargetFloorSquares)
        {
            if (_walkers.Count == 0 && !SpawnFromExistingSquare())
                break;
            if (_walkers.Count == 0)
                continue;

            RunWalker(_walkers.Dequeue());
        }

        AddLoops();
        var encounterSquares = PickEncounterSquares();
        var traps = PickTraps(encounterSquares);
        var oneWay = MakeOneWayDoors();
        var secret = MakeSecretDoors(oneWay);
        return new DmgDungeonLevel(_grid, encounterSquares, traps, secret, oneWay);
    }

    // Extra openings between passages that already exist, so there is more than one way around and
    // one-way doors can never trap the party.
    private void AddLoops()
    {
        var candidates = new List<(int X, int Y, WallSide Side)>();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!_floor[x, y])
                    continue;

                foreach (var side in new[] { WallSide.East, WallSide.South })
                {
                    var (nx, ny) = WallGrid.Step(x, y, side);
                    if (nx >= Size || ny >= Size || !_floor[nx, ny] || !_grid.HasWall(x, y, side))
                        continue;
                    if (RoomId(x, y) >= 0 && RoomId(x, y) == RoomId(nx, ny))
                        continue;

                    candidates.Add((x, y, side));
                }
            }
        }

        foreach (var (x, y, side) in Shuffle(candidates).Take(5 + _random.Next(4)))
            OpenEdge(x, y, side, door: true);
    }

    private List<T> Shuffle<T>(IEnumerable<T> items) => items.OrderBy(_ => _random.Next()).ToList();

    // 5-15 room-like areas hold a fixed encounter square; dead ends fill in when there are too few rooms.
    private List<(int X, int Y)> PickEncounterSquares()
    {
        var squares = new List<(int X, int Y)>();
        foreach (var room in _rooms)
        {
            var cx = room.Average(c => c.X);
            var cy = room.Average(c => c.Y);
            squares.Add(room.OrderBy(c => Math.Abs(c.X - cx) + Math.Abs(c.Y - cy)).First());
        }

        squares = Shuffle(squares).Take(15).ToList();
        if (squares.Count < 5)
        {
            var deadEnds = new List<(int X, int Y)>();
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (_floor[x, y] && RoomId(x, y) < 0 && Sides.Count(s => _grid.HasWall(x, y, s)) == 3)
                        deadEnds.Add((x, y));

            squares.AddRange(Shuffle(deadEnds).Take(5 - squares.Count));
        }

        return squares;
    }

    private List<(int X, int Y)> PickTraps(List<(int X, int Y)> encounterSquares)
    {
        var options = new List<(int X, int Y)>();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (_floor[x, y] && RoomId(x, y) < 0 && !encounterSquares.Contains((x, y))
                    && Math.Abs(x - Start.X) + Math.Abs(y - Start.Y) > 4)
                    options.Add((x, y));

        return Shuffle(options).Take(5 + _random.Next(4)).ToList();
    }

    private List<(int X, int Y, WallSide Side)> DoorEdges()
    {
        var doors = new List<(int X, int Y, WallSide Side)>();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                foreach (var side in Sides)
                    if ((_grid.GetMask(x, y) & ((int)side << WallGrid.DoorShift)) != 0)
                        doors.Add((x, y, side));
        return doors;
    }

    // The door stays on the square the party enters from; from the far side it looks like a plain wall.
    private List<(int X, int Y, WallSide Side)> MakeOneWayDoors()
    {
        var result = new List<(int X, int Y, WallSide Side)>();
        var wanted = 3 + _random.Next(3);
        foreach (var (x, y, side) in Shuffle(DoorEdges()))
        {
            if (result.Count >= wanted)
                break;

            var (nx, ny) = WallGrid.Step(x, y, side);
            if (nx < 0 || ny < 0 || nx >= Size || ny >= Size)
                continue;

            var before = _grid.GetMask(nx, ny);
            _grid.SetMask(nx, ny, before | (int)Opposite(side));
            if (IsStronglyConnected())
                result.Add((x, y, side));
            else
                _grid.SetMask(nx, ny, before);
        }

        return result;
    }

    private List<(int X, int Y, WallSide Side)> MakeSecretDoors(List<(int X, int Y, WallSide Side)> oneWay)
    {
        var result = new List<(int X, int Y, WallSide Side)>();
        var wanted = 3 + _random.Next(3);
        foreach (var (x, y, side) in Shuffle(DoorEdges().Where(d => !oneWay.Contains(d))))
        {
            if (result.Count >= wanted)
                break;

            _grid.HideSecretDoor(x, y, side);
            result.Add((x, y, side));
        }

        return result;
    }

    // Every open square can reach the elevator and be reached from it, taking one-way doors into account.
    private bool IsStronglyConnected()
    {
        var forward = new Dictionary<(int, int), List<(int, int)>>();
        var backward = new Dictionary<(int, int), List<(int, int)>>();
        var floor = new List<(int X, int Y)>();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!_floor[x, y])
                    continue;

                floor.Add((x, y));
                foreach (var side in Sides)
                {
                    if (!_grid.CanMove(x, y, side))
                        continue;

                    var next = WallGrid.Step(x, y, side);
                    if (!forward.TryGetValue((x, y), out var f)) forward[(x, y)] = f = new();
                    f.Add(next);
                    if (!backward.TryGetValue(next, out var b)) backward[next] = b = new();
                    b.Add((x, y));
                }
            }
        }

        bool ReachesAll(Dictionary<(int, int), List<(int, int)>> graph)
        {
            var seen = new HashSet<(int, int)> { Start };
            var queue = new Queue<(int, int)>(seen);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (graph.TryGetValue(cell, out var next))
                    foreach (var n in next)
                        if (seen.Add(n))
                            queue.Enqueue(n);
            }

            return floor.All(c => seen.Contains(c));
        }

        return ReachesAll(forward) && ReachesAll(backward);
    }

    // The start chamber around the elevator differs per seed: each seed picks its own size and position.
    private void BuildStartArea()
    {
        var variants = new List<(int Across, int Depth, int X, int Y)>();
        for (var across = 2; across <= 4; across++)
            for (var depth = 2; depth <= 4; depth++)
                for (var x = 0; x <= 1; x++)
                    for (var y = 0; y <= 2; y++)
                        if (x + across > Start.X && y + depth > Start.Y)
                            variants.Add((across, depth, x, y));

        var order = new Random(12345);
        variants = variants.OrderBy(_ => order.Next()).ToList();
        var (a, d, rx, ry) = variants[_seed % variants.Count];

        CarveRoom(rx, ry, a, d);
        var cells = new List<(int X, int Y)>();
        for (var dy = 0; dy < d; dy++)
            for (var dx = 0; dx < a; dx++)
                cells.Add((rx + dx, ry + dy));
        foreach (var cell in cells)
            _roomOf[cell] = 0;
        AddExits(cells, null);
    }

    private int D20() => _random.Next(1, 21);

    // TABLE I: periodic check.
    private void RunWalker(Walker w)
    {
        var (x, y, dir) = (w.X, w.Y, w.Dir);
        var width = w.Width;

        if (w.Distance > 0)
        {
            var (ax, ay, first) = Advance6(x, y, dir, width, w.Distance);
            if (!first)
                return;
            (x, y) = (ax, ay);
        }

        for (var step = 0; step < 40; step++)
        {
            var roll = D20();
            if (roll <= 2)
            {
                (x, y, var moved) = Advance6(x, y, dir, width, 6);
                if (!moved)
                    return;
            }
            else if (roll <= 5)
            {
                var continues = Door(x, y, dir, width);
                if (!continues)
                    return;
                (x, y, var moved) = Advance6(x, y, dir, width, 3);
                if (!moved)
                    return;
            }
            else if (roll <= 10)
            {
                SidePassage(x, y, dir);
                (x, y, var moved) = Advance6(x, y, dir, width, 3);
                if (!moved)
                    return;
            }
            else if (roll <= 13)
            {
                // TABLE IV: 1-8 left, 9-10 left (45/135 degrees become 90), 11-18 right, 19-20 right.
                var left = D20() <= 10;
                dir = left ? TurnLeft(dir) : TurnRight(dir);
                width = RollWidth();
                var (nx, ny, moved) = Advance6(x, y, dir, width, 3);
                if (!moved)
                    return;
                (x, y) = (nx, ny);
            }
            else if (roll <= 16)
            {
                Chamber(x, y, dir, chamber: true);
                return;
            }
            else if (roll == 18)
            {
                return;
            }
            else
            {
                // Stairs (17), trick/trap (19) and wandering monster (20): the passage continues.
                var (nx, ny, moved) = Advance6(x, y, dir, width, 3);
                if (!moved)
                    return;
                (x, y) = (nx, ny);
            }
        }
    }

    private (int X, int Y, bool Moved) Advance6(int x, int y, WallSide dir, int width, int distance)
    {
        var any = false;
        for (var i = 0; i < distance; i++)
        {
            var (nx, ny) = WallGrid.Step(x, y, dir);
            if (!Free(nx, ny))
                break;

            Carve(nx, ny);
            OpenEdge(x, y, dir, door: false);
            CarveWidth(nx, ny, dir, width);
            (x, y) = (nx, ny);
            any = true;
        }

        return (x, y, any);
    }

    // Wider passages grow to the right of the walking direction, and to the left for 30' and more.
    private void CarveWidth(int x, int y, WallSide dir, int width)
    {
        var right = TurnRight(dir);
        var left = TurnLeft(dir);
        for (var i = 1; i < width; i++)
        {
            var side = i % 2 == 1 ? right : left;
            var reach = (i + 1) / 2;
            var (px, py) = Offset(x, y, side, reach);
            if (!Free(px, py))
                continue;

            Carve(px, py);
            var (bx, by) = Offset(x, y, side, reach - 1);
            OpenEdge(bx, by, side, door: false);
        }
    }

    private static (int X, int Y) Offset(int x, int y, WallSide side, int count)
    {
        for (var i = 0; i < count; i++)
            (x, y) = WallGrid.Step(x, y, side);
        return (x, y);
    }

    // TABLE III.A: passage width in squares (5' and 10' = 1, 20' = 2, 30' = 3, special passages 4-5).
    private int RollWidth()
    {
        var roll = D20();
        if (_corridorsOnly)
            return 1;

        if (roll <= 12) return 1;
        if (roll <= 16) return 2;
        if (roll == 17) return 3;
        if (roll == 18) return 1;
        return _random.Next(1, 21) <= 10 ? 4 : 5;
    }

    // TABLE III: side passages, with 45 degree results turned into 90 degree ones.
    private void SidePassage(int x, int y, WallSide dir)
    {
        var roll = D20();
        bool left = false, right = false;
        if (roll <= 2) left = true;
        else if (roll <= 4) right = true;
        else if (roll <= 10) { if (roll % 2 == 1) left = true; else right = true; }
        else if (roll <= 15) left = right = true; // T and Y
        else left = right = true;                   // four-way and X

        if (left) StartBranch(x, y, TurnLeft(dir));
        if (right) StartBranch(x, y, TurnRight(dir));
    }

    private void StartBranch(int x, int y, WallSide dir)
    {
        var (nx, ny) = WallGrid.Step(x, y, dir);
        if (!Free(nx, ny))
            return;

        var width = RollWidth();
        Carve(nx, ny);
        OpenEdge(x, y, dir, door: false);
        CarveWidth(nx, ny, dir, width);
        _walkers.Enqueue(new Walker(nx, ny, dir, width, 3));
    }

    // TABLE II: doors. Returns whether the passage can go on straight (it cannot when the door is ahead).
    private bool Door(int x, int y, WallSide dir, int width)
    {
        var location = D20();
        var side = location <= 6 ? TurnLeft(dir) : location <= 12 ? TurnRight(dir) : dir;
        var ahead = side == dir;

        var (bx, by) = WallGrid.Step(x, y, side);
        if (!Free(bx, by))
            return !ahead;

        var beyond = D20();
        if (beyond <= 4)
        {
            if (ahead)
            {
                Room(x, y, side, 1, 1, door: true);
            }
            else
            {
                // Parallel passage, 30' in both directions.
                Carve(bx, by);
                OpenEdge(x, y, side, door: true);
                var left = TurnLeft(side);
                var right = TurnRight(side);
                var (lx, ly, _) = Advance6(bx, by, left, 1, 3);
                var (rx, ry, _) = Advance6(bx, by, right, 1, 3);
                _walkers.Enqueue(new Walker(lx, ly, left, 1, 0));
                _walkers.Enqueue(new Walker(rx, ry, right, 1, 0));
            }
        }
        else if (beyond <= 10)
        {
            // Straight ahead, or 45 degrees (turned into 90 degrees: it simply goes on past the door).
            var passageWidth = RollWidth();
            Carve(bx, by);
            OpenEdge(x, y, side, door: true);
            CarveWidth(bx, by, side, passageWidth);
            _walkers.Enqueue(new Walker(bx, by, side, passageWidth, 3));
        }
        else
        {
            Chamber(x, y, side, chamber: beyond >= 19, door: true);
        }

        return !ahead;
    }

    // TABLE V: shape and size, in 10' squares (width across, depth away from the entrance).
    private void Chamber(int x, int y, WallSide dir, bool chamber, bool door = false)
    {
        var roll = D20();
        (int Across, int Depth) size = roll switch
        {
            <= 2 => chamber ? (2, 2) : (1, 1),
            <= 4 => (2, 2),
            <= 6 => (3, 3),
            <= 8 => (4, 4),
            <= 10 => chamber ? (2, 3) : (1, 2),
            <= 13 => (2, 3),
            <= 15 => chamber ? (3, 5) : (2, 4),
            <= 17 => chamber ? (4, 6) : (3, 4),
            _ => chamber ? (3, 4) : (4, 5)
        };

        if (_random.Next(2) == 0)
            size = (size.Depth, size.Across);

        Room(x, y, dir, size.Across, size.Depth, door);
    }

    private void Room(int x, int y, WallSide dir, int across, int depth, bool door)
    {
        while (true)
        {
            if (TryPlaceRoom(x, y, dir, across, depth, door))
                return;

            if (across <= 1 && depth <= 1)
                return;

            across = Math.Max(1, across - 1);
            depth = Math.Max(1, depth - 1);
        }
    }

    private bool TryPlaceRoom(int x, int y, WallSide dir, int across, int depth, bool door)
    {
        var (ex, ey) = WallGrid.Step(x, y, dir);
        var shift = _random.Next(0, across);
        var cells = new List<(int X, int Y)>();
        for (var d = 0; d < depth; d++)
        {
            var (rx, ry) = Offset(ex, ey, dir, d);
            for (var a = 0; a < across; a++)
            {
                var lateral = a - shift;
                var (cx, cy) = lateral >= 0
                    ? Offset(rx, ry, TurnRight(dir), lateral)
                    : Offset(rx, ry, TurnLeft(dir), -lateral);
                if (!Free(cx, cy))
                    return false;
                cells.Add((cx, cy));
            }
        }

        foreach (var (cx, cy) in cells)
            Carve(cx, cy);
        _rooms.Add(cells);
        foreach (var cell in cells)
            _roomOf[cell] = _rooms.Count;
        OpenInterior(cells);
        OpenEdge(x, y, dir, door);
        AddExits(cells, dir);
        return true;
    }

    private void OpenInterior(List<(int X, int Y)> cells)
    {
        var set = new HashSet<(int, int)>(cells);
        foreach (var (cx, cy) in cells)
        {
            if (set.Contains((cx + 1, cy))) OpenEdge(cx, cy, WallSide.East, door: false);
            if (set.Contains((cx, cy + 1))) OpenEdge(cx, cy, WallSide.South, door: false);
        }
    }

    // Exits in a room: 1-3 doors in the walls other than the entrance, each leading to a passage.
    private void AddExits(List<(int X, int Y)> cells, WallSide? entranceDir)
    {
        var set = new HashSet<(int, int)>(cells);
        var candidates = new List<(int X, int Y, WallSide Side)>();
        foreach (var (cx, cy) in cells)
        {
            foreach (var side in Sides)
            {
                if (entranceDir is { } entrance && side == Opposite(entrance))
                    continue;

                var (nx, ny) = WallGrid.Step(cx, cy, side);
                if (!set.Contains((nx, ny)) && Free(nx, ny))
                    candidates.Add((cx, cy, side));
            }
        }

        var exits = Math.Min(candidates.Count, 1 + _random.Next(cells.Count > 4 ? 3 : 2));
        for (var i = 0; i < exits && candidates.Count > 0; i++)
        {
            var pick = _random.Next(candidates.Count);
            var (cx, cy, side) = candidates[pick];
            candidates.RemoveAt(pick);

            var (nx, ny) = WallGrid.Step(cx, cy, side);
            if (!Free(nx, ny))
                continue;

            var width = RollWidth() > 2 ? 2 : 1;
            Carve(nx, ny);
            OpenEdge(cx, cy, side, door: _random.Next(4) != 0);
            CarveWidth(nx, ny, side, width);
            _walkers.Enqueue(new Walker(nx, ny, side, width, 3));
        }
    }

    // When every passage has ended before the level is big enough, a new passage leaves an existing square.
    private bool SpawnFromExistingSquare()
    {
        var options = new List<(int X, int Y, WallSide Side)>();
        for (var y = 1; y < Size - 1; y++)
        {
            for (var x = 1; x < Size - 1; x++)
            {
                if (!_floor[x, y])
                    continue;

                foreach (var side in Sides)
                {
                    var (nx, ny) = WallGrid.Step(x, y, side);
                    if (Free(nx, ny) && !_grid.HasDoor(x, y, side))
                        options.Add((x, y, side));
                }
            }
        }

        if (options.Count == 0)
            return false;

        var (ox, oy, oside) = options[_random.Next(options.Count)];
        var (sx, sy) = WallGrid.Step(ox, oy, oside);
        Carve(sx, sy);
        OpenEdge(ox, oy, oside, door: _random.Next(2) == 0);
        _walkers.Enqueue(new Walker(sx, sy, oside, 1, 3));
        return true;
    }

    private void CarveRoom(int x, int y, int across, int depth)
    {
        var cells = new List<(int X, int Y)>();
        for (var dy = 0; dy < depth; dy++)
        {
            for (var dx = 0; dx < across; dx++)
            {
                Carve(x + dx, y + dy);
                cells.Add((x + dx, y + dy));
            }
        }

        OpenInterior(cells);
    }

    // Squares on the outer ring stay solid so the wrap-around map never joins opposite edges.
    private bool Free(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && !_floor[x, y];

    private void Carve(int x, int y)
    {
        if (_floor[x, y])
            return;

        _floor[x, y] = true;
        _floorCount++;
    }

    private void OpenEdge(int x, int y, WallSide side, bool door)
    {
        var (nx, ny) = WallGrid.Step(x, y, side);
        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size)
            return;

        var opposite = Opposite(side);
        _grid.SetMask(x, y, _grid.GetMask(x, y) & ~(int)side);
        _grid.SetMask(nx, ny, _grid.GetMask(nx, ny) & ~(int)opposite);
        if (door)
            _grid.SetMask(x, y, _grid.GetMask(x, y) | ((int)side << WallGrid.DoorShift));
    }

    private static WallSide Opposite(WallSide side) => side switch
    {
        WallSide.North => WallSide.South,
        WallSide.East => WallSide.West,
        WallSide.South => WallSide.North,
        _ => WallSide.East
    };

    private static WallSide TurnLeft(WallSide side) => side switch
    {
        WallSide.North => WallSide.West,
        WallSide.West => WallSide.South,
        WallSide.South => WallSide.East,
        _ => WallSide.North
    };

    private static WallSide TurnRight(WallSide side) => Opposite(TurnLeft(side));
}

/// <summary>A generated floor: the walls plus the squares and doors worth marking on a map.</summary>
public sealed record DmgDungeonLevel(
    WallGrid Walls,
    IReadOnlyList<(int X, int Y)> EncounterSquares,
    IReadOnlyList<(int X, int Y)> Traps,
    IReadOnlyList<(int X, int Y, WallSide Side)> SecretDoors,
    IReadOnlyList<(int X, int Y, WallSide Side)> OneWayDoors);
