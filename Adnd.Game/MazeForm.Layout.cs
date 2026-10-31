using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Dungeon;
using Adnd.Core.Monsters;
using Adnd.Data.Characters;
using Adnd.Data.Dungeon;
using Adnd.Data.Encounters;
using Adnd.Data.Monsters;
using Adnd.Data.Party;
using Adnd.Game.Combat;
using Adnd.Game.Viewer;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using static Adnd.Core.Dices.DiceFormulas;

namespace Adnd.Game;

// Maze construction and wall queries.
public sealed partial class MazeForm
{
    private void BuildMazeForLevel(int level)
    {
        if (!_thinWalls)
        {
            BuildCarvedMazeForLevel(level);
            return;
        }

        // Levels without a hand-drawn layout yet reuse the maze layout, converted to thin walls.
        var walls = _dungeonLayouts.TryLoad(level);
        if (walls is null)
        {
            BuildCarvedMazeForLevel(level);
            walls = WallGrid.FromFloors(_maze.GetLength(0), _maze.GetLength(1), (x, y) => _maze[x, y] == CellType.Floor);
        }

        _walls = walls;
        foreach (var (revealedLevel, x, y, side) in _revealedSecretDoors)
        {
            if (revealedLevel == level)
                walls.RevealSecretDoor(x, y, side);
        }

        _maze = new CellType[walls.Width, walls.Height];
        for (var y = 0; y < walls.Height; y++)
            for (var x = 0; x < walls.Width; x++)
                _maze[x, y] = walls.IsSolid(x, y) ? CellType.Wall : CellType.Floor;
    }

    private static WallSide ToWallSide(Direction direction) => direction switch
    {
        Direction.North => WallSide.North,
        Direction.East => WallSide.East,
        Direction.South => WallSide.South,
        _ => WallSide.West
    };

    private bool CanStepForward()
    {
        if (_walls is not null)
            return _walls.CanMove(_position.X, _position.Y, ToWallSide(_direction));

        var v = GetForwardVector(_direction);
        return IsOpen(new Point(_position.X + v.X, _position.Y + v.Y));
    }

    // Doors are drawn shut but the party walks straight through them.
    private bool IsClosedDoor(Point cell, WallSide side)
        => _walls is not null && _walls.HasDoor(cell.X, cell.Y, side);

    // Whether the way from a square to its front, left or right neighbour is blocked.
    private bool IsFrontBlocked(Point cell) => _walls is not null
        ? _walls.HasWall(cell.X, cell.Y, ToWallSide(_direction)) || IsClosedDoor(cell, ToWallSide(_direction))
        : !IsOpen(GetCellFartherAway(cell, _direction, 1));

    private bool IsLeftBlocked(Point cell) => _walls is not null
        ? _walls.HasWall(cell.X, cell.Y, ToWallSide(TurnLeft(_direction))) || IsClosedDoor(cell, ToWallSide(TurnLeft(_direction)))
        : !IsOpen(GetCellToTheLeft(cell, _direction, 1));

    private bool IsRightBlocked(Point cell) => _walls is not null
        ? _walls.HasWall(cell.X, cell.Y, ToWallSide(TurnRight(_direction))) || IsClosedDoor(cell, ToWallSide(TurnRight(_direction)))
        : !IsOpen(GetCellToTheRight(cell, _direction, 1));

    private bool HasFrontDoor(Point cell) => IsClosedDoor(cell, ToWallSide(_direction));

    private bool HasLeftDoor(Point cell) => IsClosedDoor(cell, ToWallSide(TurnLeft(_direction)));

    private bool HasRightDoor(Point cell) => IsClosedDoor(cell, ToWallSide(TurnRight(_direction)));

    // With thin walls a square beside the corridor is only visible if no wall stands in the way, so work
    // out which (depth, position) squares the party can actually see before drawing.
    private HashSet<(int Depth, int Position)> ComputeVisibleCells()
    {
        var visible = new HashSet<(int, int)>();
        var pending = new Queue<(int Depth, int Position)>();
        visible.Add((0, StraightAhead));
        pending.Enqueue((0, StraightAhead));

        while (pending.Count > 0)
        {
            var (depth, position) = pending.Dequeue();
            var cell = GetCellFartherAway(_position, _direction, depth);
            var offset = position - StraightAhead;
            cell = offset < 0
                ? GetCellToTheLeft(cell, _direction, -offset)
                : GetCellToTheRight(cell, _direction, offset);

            // Magical darkness can be seen but nothing can be seen through it.
            if (IsDark(cell))
                continue;

            void Visit(int nextDepth, int nextPosition)
            {
                if (nextDepth >= _depthLevels || nextPosition < 0 || nextPosition >= PositionCount
                    || Math.Abs(nextPosition - StraightAhead) > nextDepth + 1)
                    return;

                if (visible.Add((nextDepth, nextPosition)))
                    pending.Enqueue((nextDepth, nextPosition));
            }

            if (!IsFrontBlocked(cell))
                Visit(depth + 1, position);
            if (offset <= 0 && !IsLeftBlocked(cell))
                Visit(depth, position - 1);
            if (offset >= 0 && !IsRightBlocked(cell))
                Visit(depth, position + 1);
        }

        return visible;
    }

    private void BuildCarvedMazeForLevel(int level)
    {
        _maze = new CellType[22, 22];

        for (int y = 0; y < 22; y++)
            for (int x = 0; x < 22; x++)
                _maze[x, y] = CellType.Wall;

        // Elevator/start and stairs-up area must always be clear.
        CarveCell(1, 0);
        CarveCell(0, 2);
        CarveCell(1, 2);
        CarveCell(0, 0);

        if (level == 1)
        {
            // Level 1: manually carved layout
            CarveVertical(0, 0, 20);
            CarveVertical(20, 0, 20);
            CarveHorizontal(0, 0, 20);
            CarveHorizontal(20, 0, 20);
            CarveVertical(6, 0, 20);// go east to find this corridor
            CarveVertical(12, 0, 20);// go furhter east to find this corridor
            CarveCell(1, 2);//see this when facing south from start
            CarveCell(4, 2);
            CarveCell(3, 3);
            CarveCell(3, 12);

            CarveCell(4, 4);
            CarveCell(4, 5);
            CarveCell(4, 5);
            CarveCell(5, 5);
            CarveCell(3, 5);
            CarveCell(7, 5);//corridor to room1
            CarveCell(8, 5);//corridor to room1
            CarveCell(10, 5);//room1
            CarveCell(10, 4);//room1
            CarveCell(10, 6);//room1
            CarveCell(9, 5);//room1
            CarveCell(9, 4);//room1
            CarveCell(9, 6);//room1
            CarveCell(13,5);//short corridor to room1
            CarveCell(14, 5);//room2
            CarveCell(14, 4);//room2
            CarveCell(14, 6);//room2
            CarveCell(15, 5);//room2
            CarveCell(15, 4);//room2
            CarveCell(15, 6);//room2
            CarveCell(2, 14);//corridor to room3
            CarveCell(1, 14);//corridor to room3
            CarveCell(3, 15);//room3
            CarveCell(3, 14);//room3
            CarveCell(3, 16);//room3
            CarveCell(4, 15);//room3
            CarveCell(4, 14);//room3
            CarveCell(4, 16);//room3
            CarveCell(2, 9);//random corridor
            CarveCell(1, 9);//
            CarveCell(1, 10);//
            CarveCell(3, 9);//random corridor
            CarveCell(3, 8);//random corridor
            CarveCell(4, 8);//random corridor
            CarveCell(4, 9);//random corridor
            CarveCell(4, 10);//random corridor
            CarveCell(5, 9);//random corridor
            CarveCell(5, 10);//random corridor
            CarveCell(6, 9);//random corridor

            // Extra corridor + extra room (connected)
            CarveCell(6, 10);
            CarveCell(7, 10);
            CarveCell(8, 10);
            CarveCell(8, 11);
            CarveCell(8, 12);
            CarveCell(9, 11);
            CarveCell(9, 12);
            CarveCell(10, 11);
            CarveCell(10, 12);

            // One more corridor + one more room (connected)
            CarveCell(10, 13);
            CarveCell(10, 14);
            CarveCell(10, 15);
            CarveCell(11, 14);
            CarveCell(11, 15);
            CarveCell(12, 14);
            CarveCell(12, 15);

            return;
        }

        if (level == 2)
        {
            // Level 2: similar style/density to level 1, but different geometry.
            CarveVertical(0, 0, 20);
            CarveVertical(20, 0, 20);
            CarveHorizontal(0, 0, 20);
            CarveHorizontal(20, 0, 20);

            CarveVertical(5, 0, 20);
            CarveVertical(14, 0, 20);

            // Entry/elevator linkage and west branch
            CarveCell(1, 2);
            CarveCell(2, 2);
            CarveCell(3, 2);
            CarveCell(3, 4);
            CarveCell(3, 5);
            CarveCell(4, 5);

            // Room A near left spine
            CarveCell(7, 6);
            CarveCell(8, 6);
            CarveCell(7, 7);
            CarveCell(8, 7);
            CarveCell(6, 7);

            // Mid connector and Room B near right spine
            CarveCell(9, 10);
            CarveCell(10, 10);
            CarveCell(11, 10);
            CarveCell(12, 10);
            CarveCell(15, 9);
            CarveCell(16, 9);
            CarveCell(15, 10);
            CarveCell(16, 10);
            CarveCell(15, 11);
            CarveCell(16, 11);

            // Lower-left room cluster
            CarveCell(2, 15);
            CarveCell(3, 15);
            CarveCell(4, 15);
            CarveCell(3, 16);
            CarveCell(4, 16);
            CarveCell(2, 16);

            // Lower-middle-right room cluster
            CarveCell(11, 15);
            CarveCell(12, 15);
            CarveCell(13, 15);
            CarveCell(12, 16);
            CarveCell(13, 16);
            CarveCell(12, 14);

            // Random connector pockets, similar count to level 1 details.
            CarveCell(6, 3);
            CarveCell(7, 3);
            CarveCell(8, 3);
            CarveCell(14, 4);
            CarveCell(15, 4);
            CarveCell(14, 5);
            CarveCell(5, 12);
            CarveCell(6, 12);
            CarveCell(14, 12);
            CarveCell(15, 12);

            // Extra corridor + extra room (connected)
            CarveCell(16, 12);
            CarveCell(17, 12);
            CarveCell(18, 12);
            CarveCell(18, 13);
            CarveCell(18, 14);
            CarveCell(17, 13);
            CarveCell(17, 14);
            CarveCell(16, 13);
            CarveCell(16, 14);

            // One more corridor + one more room (connected)
            CarveCell(10, 6);
            CarveCell(11, 6);
            CarveCell(12, 6);
            CarveCell(12, 5);
            CarveCell(13, 5);
            CarveCell(12, 4);
            CarveCell(13, 4);

            return;
        }
        /* 
        I want to define level 3 in my dungeon in the same way as I defined level 1, but I want level 3 to look differently, 
        but with the same number of rooms and corridors as level 1. Here is the code for level 1
redesign level 3 to have only one boarder corridor and to have 2 more rooms and look totaly differntly layoutwise
         add a coriodors to level 3 and 4 so cell 1,2 is connected to the rest of the map
         
         */

        if (level == 3)
        {
            // --- ONLY ONE BORDER CORRIDOR ---
            CarveHorizontal(0, 0, 20);   // single top border corridor

            // Connect entry cell (1,2) to the main layout.
            CarveHorizontal(1, 3, 15);
            CarveVertical(1, 3, 15);
            CarveCell(1, 3);
            CarveVertical(1, 0, 2);


            // --- MAIN SPINE CORRIDORS (no border) ---
            CarveVertical(5, 2, 18);     // main north-south spine
            CarveVertical(14, 2, 18);    // secondary spine
            CarveHorizontal(2, 10, 18);  // central east-west corridor

            // --- ROOM 1 (connected to main spine) ---
            CarveCell(5, 6);
            CarveCell(5, 5);
            CarveCell(5, 7);
            CarveCell(6, 6);
            CarveCell(6, 5);
            CarveCell(6, 7);

            // --- ROOM 2 (connected to east-west corridor) ---
            CarveCell(10, 10);
            CarveCell(10, 9);
            CarveCell(10, 11);
            CarveCell(11, 10);
            CarveCell(11, 9);
            CarveCell(11, 11);

            // --- ROOM 3 (connected to secondary spine) ---
            CarveCell(14, 14);
            CarveCell(14, 13);
            CarveCell(14, 15);
            CarveCell(15, 14);
            CarveCell(15, 13);
            CarveCell(15, 15);

            // --- EXTRA ROOM 4 (new, connected to central corridor) ---
            CarveCell(8, 3);
            CarveCell(8, 2);
            CarveCell(8, 4);
            CarveCell(9, 3);
            CarveCell(9, 2);
            CarveCell(9, 4);

            // --- EXTRA ROOM 5 (new, connected to secondary spine) ---
            CarveCell(16, 7);
            CarveCell(16, 6);
            CarveCell(16, 8);
            CarveCell(17, 7);
            CarveCell(17, 6);
            CarveCell(17, 8);

            // --- RANDOM CORRIDOR CLUSTER (same count, but new shape) ---
            CarveCell(3, 10);
            CarveCell(3, 11);
            CarveCell(4, 11);
            CarveCell(4, 12);
            CarveCell(5, 12);
            CarveCell(6, 12);
            CarveCell(7, 12);
            CarveCell(7, 11);
            CarveCell(7, 10);
            CarveCell(6, 10);
            CarveCell(5, 10);

            // --- EXTRA CORRIDOR #1 (not border) ---
            CarveCell(12, 5);
            CarveCell(13, 5);
            CarveCell(14, 5);

            // --- EXTRA CORRIDOR #2 (not border) ---
            CarveCell(4, 15);
            CarveCell(5, 15);
            CarveCell(6, 15);

            return;
        }

        if (level == 4)
        {
            // Connect entry cell (1,2) to the main layout.
            CarveHorizontal(1, 3, 15);
            CarveVertical(1, 3, 15);
            CarveCell(1, 3);
            CarveVertical(1, 0, 2);

            // Elevator connection
            CarveVertical(1, 0, 2);
            CarveCell(1, 3);

            // --- MAIN CROSS CORRIDOR NETWORK ---
            CarveVertical(10, 2, 18);   // main north-south
            CarveHorizontal(2, 10, 18); // main east-west

            // --- ROOM 1 (north branch) ---
            CarveCell(10, 6);
            CarveCell(10, 5);
            CarveCell(10, 7);
            CarveCell(11, 6);
            CarveCell(11, 5);
            CarveCell(11, 7);

            // --- ROOM 2 (east branch) ---
            CarveCell(15, 10);
            CarveCell(15, 9);
            CarveCell(15, 11);
            CarveCell(16, 10);
            CarveCell(16, 9);
            CarveCell(16, 11);

            // --- ROOM 3 (south branch) ---
            CarveCell(10, 15);
            CarveCell(10, 14);
            CarveCell(10, 16);
            CarveCell(11, 15);
            CarveCell(11, 14);
            CarveCell(11, 16);

            // --- ROOM 4 (west branch) ---
            CarveCell(5, 10);
            CarveCell(5, 9);
            CarveCell(5, 11);
            CarveCell(6, 10);
            CarveCell(6, 9);
            CarveCell(6, 11);

            // --- ROOM 5 (northwest branch) ---
            CarveCell(6, 6);
            CarveCell(6, 5);
            CarveCell(6, 7);
            CarveCell(7, 6);
            CarveCell(7, 5);
            CarveCell(7, 7);

            // --- EXTRA CORRIDOR #1 ---
            CarveCell(7, 10);

            // --- EXTRA CORRIDOR #2 ---
            CarveCell(8, 6);
            CarveCell(9, 6);

            // --------------------------------------------------------------------
            // 🔥 NEW CORRIDORS TO ENSURE FULL CONNECTIVITY
            // --------------------------------------------------------------------

            // --- RANDOM CLUSTER (Level 1 style) ---
            CarveCell(2, 9);
            CarveCell(3, 9);
            CarveCell(3, 8);
            CarveCell(4, 8);
            CarveCell(4, 9);
            CarveCell(4, 10);
            CarveCell(5, 9);
            CarveCell(5, 10);
            CarveCell(6, 9);

            // Loop cluster into west branch
            CarveCell(6, 10);
            CarveCell(7, 10);
            CarveCell(8, 10);

            // --- ROOM 4 CLUSTER ---
            CarveCell(8, 11);
            CarveCell(8, 12);
            CarveCell(9, 11);
            CarveCell(9, 12);
            CarveCell(10, 11);
            CarveCell(10, 12);

            // Connect room 4 cluster to east-west spine
            CarveCell(9, 10);

            // --- ROOM 5 CLUSTER ---
            CarveCell(10, 14);
            CarveCell(10, 15);
            CarveCell(11, 14);
            CarveCell(11, 15);
            CarveCell(12, 14);
            CarveCell(12, 15);

            // Connect room 5 cluster to east-west spine
            CarveCell(12, 10);

            // --------------------------------------------------------------------
            // 🔥 FINAL CONNECTIVITY FIXES (NEW)
            // --------------------------------------------------------------------

            // Connect northwest room cluster fully
            CarveCell(7, 8);   // connects (7,7) → (7,10)
            CarveCell(7, 9);

            // Connect west cluster fully
            CarveCell(5, 8);   // connects (5,9) → (5,10)
            CarveCell(6, 8);

            // Connect south cluster fully
            CarveCell(10, 13); // connects (10,14) → (10,10)
            CarveCell(10, 12); // already carved but ensures loop

            // Connect east cluster fully
            CarveCell(13, 10); // connects (15,10) → main spine
            CarveCell(14, 10); // already carved but ensures loop

            // Connect north cluster fully
            CarveCell(10, 8);  // connects (10,6) → (10,10)

            return;
        }




        //        bygg level 4 ochså, men skippa korridorerna som går runt om, men gör två extra korridorer som inte går runt om och ett extra rum
        /*
           * 
           * */



        // Deterministic unique connected layout per level.
        var seed = (level * 7919) + 1337;
        var layoutRandom = new Random(seed);

        int xPos = 1;
        int yPos = 1;
        CarveCell(xPos, yPos);

        // Corridor-biased carving for all levels.
        int steps = 180 + (level * 8);
        for (int i = 0; i < steps; i++)
        {
            // small room pockets only
            if (i % 55 == 0)
            {
                var roomW = 2 + layoutRandom.Next(0, 2);
                var roomH = 2 + layoutRandom.Next(0, 2);
                CarveRoom(xPos - (roomW / 2), yPos - (roomH / 2), roomW, roomH);
            }

            var directionRoll = layoutRandom.Next(4);
            var (dx, dy) = directionRoll switch
            {
                0 => (1, 0),
                1 => (-1, 0),
                2 => (0, 1),
                _ => (0, -1)
            };

            var nextX = xPos + dx;
            var nextY = yPos + dy;

            if (nextX < 1 || nextX > 20 || nextY < 1 || nextY > 20)
                continue;

            xPos = nextX;
            yPos = nextY;
            CarveCell(xPos, yPos);

            if (layoutRandom.Next(100) < 30)
            {
                var branchLen = 2 + layoutRandom.Next(0, 5);
                CarveBranch(xPos, yPos, layoutRandom, branchLen);
            }
        }

        // Ensure elevator cell at (1,2) is connected to interior corridor.
        CarveVertical(1, 0, 3);

        // Keep carved ratio under max 40% on every floor.
        EnforceCarvedRatio(layoutRandom, minRatio: 0.0, maxRatio: 0.40);

        // Re-ensure mandatory connectivity after ratio adjustment.
        CarveVertical(1, 0, 3);
    }

    private void EnforceCarvedRatio(Random random, double minRatio, double maxRatio)
    {
        int total = 22 * 22;
        int carved = CountCarvedCells();

        int targetMin = (int)Math.Ceiling(total * minRatio);
        int targetMax = (int)Math.Floor(total * maxRatio);

        while (carved < targetMin)
        {
            if (!TryCarveRandomWallCell(random))
                break;

            carved++;
        }

        while (carved > targetMax)
        {
            if (!TryFillRandomFloorCell(random))
                break;

            carved--;
        }
    }

    private bool TryFillRandomFloorCell(Random random)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            int x = random.Next(1, 21);
            int y = random.Next(1, 21);

            if (_maze[x, y] != CellType.Floor)
                continue;

            // Preserve mandatory start/elevator access path.
            bool protectedCell = (x == 0 && y == 0)
                                 || (x == 1 && y == 0)
                                 || (x == 1 && y == 1)
                                 || (x == 1 && y == 2)
                                 || (x == 1 && y == 3);

            if (protectedCell)
                continue;

            _maze[x, y] = CellType.Wall;
            return true;
        }

        return false;
    }

    private int CountCarvedCells()
    {
        int count = 0;
        for (int y = 0; y < 22; y++)
        {
            for (int x = 0; x < 22; x++)
            {
                if (_maze[x, y] == CellType.Floor)
                    count++;
            }
        }

        return count;
    }

    private bool TryCarveRandomWallCell(Random random)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int x = random.Next(1, 21);
            int y = random.Next(1, 21);

            if (_maze[x, y] == CellType.Floor)
                continue;

            _maze[x, y] = CellType.Floor;
            return true;
        }

        return false;
    }

    private void CarveBranch(int startX, int startY, Random random, int length)
    {
        int x = startX;
        int y = startY;

        var directionRoll = random.Next(4);
        var (dx, dy) = directionRoll switch
        {
            0 => (1, 0),
            1 => (-1, 0),
            2 => (0, 1),
            _ => (0, -1)
        };

        for (int i = 0; i < length; i++)
        {
            var nx = x + dx;
            var ny = y + dy;

            if (nx < 1 || nx > 20 || ny < 1 || ny > 20)
                break;

            x = nx;
            y = ny;
            CarveCell(x, y);
        }
    }

    private void CarveHorizontal(int y, int fromX, int toX)
    {
        if (y < 0 || y >= 22)
            return;

        var min = Math.Max(0, Math.Min(fromX, toX));
        var max = Math.Min(21, Math.Max(fromX, toX));
        for (int x = min; x <= max; x++)
            CarveCell(x, y);
    }

    private void CarveVertical(int x, int fromY, int toY)
    {
        if (x < 0 || x >= 22)
            return;

        var min = Math.Max(0, Math.Min(fromY, toY));
        var max = Math.Min(21, Math.Max(fromY, toY));
        for (int y = min; y <= max; y++)
            CarveCell(x, y);
    }

    private void CarveRoom(int x, int y, int width, int height)
    {
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                CarveCell(xx, yy);
            }
        }
    }

    private void CarveCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= 22 || y >= 22)
            return;

        _maze[x, y] = CellType.Floor;
    }

    private void BuildMaze()
    {
        _maze = new CellType[22, 22];

        for (int y = 0; y < 22; y++)
            for (int x = 0; x < 22; x++)
                _maze[x, y] = CellType.Wall;

        for (int y = 1; y <= 8; y++)
            _maze[3, y] = CellType.Floor;

        for (int y = 0; y <= 8; y++)
            _maze[0, y] = CellType.Floor;

        for (int x = 0, y = 0; x <= 8; x++, y++)
            _maze[x, y] = CellType.Floor;
    }
}
