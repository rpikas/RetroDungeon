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

public sealed class MazeForm : Form
{
    private const string MazeWindowStatePath = "Data/Config/maze-window.json";

    private enum Direction
    {
        North,
        East,
        South,
        West
    }

    private enum CellType { Wall, Floor }

    // Depth levels are the squares drawn, counting the one the party stands on. Normally the party sees 3 squares
    // ahead; Light adds one, Continual Light two; magical darkness leaves only one square ahead.
    private const int BaseDepthLevels = 4;
    private const int LightDepthLevels = BaseDepthLevels + 1;
    private const int ContinualLightDepthLevels = BaseDepthLevels + 2;
    private const int DarkDepthLevels = 1;
    private const int MaxDepthLevels = ContinualLightDepthLevels;
    private const int StraightAhead = MaxDepthLevels;
    private const int PositionCount = (StraightAhead * 2) + 1;
    private const int TopLeft = 0;
    private const int BottomRight = 1;
    private const int MinDungeonLevel = 1;
    private const int MaxDungeonLevel = 16;
  //  private const int EncounterChanceDenominator = 4;

    private static readonly string[] LevelOneMonsters =
    {
        "Goblin",
        "Skeleton"
    };

    private readonly PointF[,,] _backWallCoords = new PointF[MaxDepthLevels, PositionCount, 2];
    private int _depthLevels = BaseDepthLevels;
    private int _lightStepsRemaining;
    private bool _continualLightActive;
    private bool _inMagicalDarkness;
    private string? _centerMessage;
    private IReadOnlyList<DungeonDarkArea> _darkAreas = Array.Empty<DungeonDarkArea>();

    private CellType[,] _maze;
    private Point _position = new(0, 0);

    // The elevator is at (1,2) in the Maze and at (8,10) in the Dungeon.
    private Point ElevatorPosition => _thinWalls ? (_currentDungeonLevel is 8 or 9 ? new Point(0, 10) : new Point(8, 10)) : new Point(1, 2);

    // Level 9 warp zones (W on the map), rows by N and columns by E. Each one sends the party back to Floor 1.
    private static readonly string[] Level9WarpZones =
    [
        "WWWWWW......WWWWWWWW",
        "WW................WW",
        "WW................WW",
        "WW..WWWWWWWWWWWW..WW",
        "WW..WWWWWWWWWWWW..WW",
        "WW..WWWWWWWWWWWW..WW",
        "WW..WWWWWWWWWWWW..WW",
        "....WWWWW...........",
        "WW..WWWWW.........WW",
        "WW..WWWWWWWWWW.WWWWW",
        "WW..W.WW.WW....WWWWW",
        "WW..W.WW.WW.WWWWWWWW",
        "WW...W...WW.WWWWWWWW",
        "WW..W....WW.WWWWWWWW",
        "WW..W....WW.WWWWWWWW",
        "WW..W....WW....WWWWW",
        "WW..WWWWWWWWWW.W..WW",
        "WW................WW",
        "WW................WW",
        "WWWWWW......WWWWWWWW"
    ];

    private static readonly Point Level9Chute = new(2, 8);
    private Direction _direction = Direction.North;
    private readonly Random _random = new();
    private readonly PartyRepository _partyRepository = new();
    private readonly CharacterRepository _characterRepository = new("Data/Characters");
    private readonly MonsterRepository _monsterRepository = new();
    private readonly CombatCoordinator _combatCoordinator = new();
    private bool _partyOverlayVisible = true;
    private int _currentDungeonLevel = 1;
    private readonly TabletopViewerBridge _viewer = new();
    private RuleApplicationInfoForm? _ruleApplicationInfoForm;
    private Action<string>? _ruleApplicationInfoHandler;
    private bool _fullMoonShownForThisDungeonTrip;
    private readonly HashSet<(string Character, string Effect)> _stepEffects = new();
    private DialogResult _effectsNotificationChoice = DialogResult.OK;
    private int _stepsSinceEffectsNotification;

    // Lets the viewer send moves back. Null when no viewer is configured, and idle when none is
    // running; the keyboard is unaffected either way.
    private ViewerControlPump? _viewerControl;

    // Dungeon mode: same engine, but walls sit on square edges (see WallGrid). _maze still marks which
    // squares can be entered, so encounters, treasure and the viewer work unchanged.
    private readonly bool _thinWalls;
    private WallGrid? _walls;
    private HashSet<(int Depth, int Position)>? _visibleCells;
    private readonly DungeonLayoutRepository _dungeonLayouts = new();
    private IReadOnlyList<DungeonEncounterLocation> _encounterLocations = Array.Empty<DungeonEncounterLocation>();
    private readonly HashSet<(int X, int Y)> _visitedEncounterLocations = new();

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

    public MazeForm(bool thinWalls = false)
    {
        _thinWalls = thinWalls;
        Text = thinWalls ? "Dungeon" : "Maze";
        ClientSize = new Size(1200, 820);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BackColor = Color.Black;
        ForeColor = GameRulesProvider.Current.DefaultColor;

        var savedLocation = LoadSavedMazeWindowLocation();
        if (savedLocation.HasValue)
        {
            StartPosition = FormStartPosition.Manual;
            Location = savedLocation.Value;
        }

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        PruneUnavailablePartyMembers();
        BuildMazeForLevel(_currentDungeonLevel);
        LoadEncounterLocationsForLevel(_currentDungeonLevel);
        LoadLightFromParty();
        KeyDown += MazeForm_KeyDown;

        // Follow the fight. These are observers: combat neither waits for them nor changes course.
        _combatCoordinator.EncounterStarted += session =>
        {
            session.IsSpaciousArea = IsSpaciousEncounterArea();
            _viewer.ResetCombatMemory();   // or last fight's corpses read as fresh deaths
            PublishCombatToViewer(session);
        };
        _combatCoordinator.ActionsChosen += (session, actions) => PublishCombatToViewer(session, actions);
        _combatCoordinator.RoundResolved += session => PublishCombatToViewer(session);

        // Whose turn it is, and what they may legally do. Republished as its own snapshot because the
        // choice moves from character to character between rounds, so the round events are too coarse.
        _combatCoordinator.ViewerPromptChanged += (session, prompt) =>
            PublishCombatToViewer(session, prompt: prompt);

        Shown += (_, _) =>
        {
            if (_position == ElevatorPosition)
            {
                ShowDungeonElevatorDialog();
                Invalidate();
            }

            // Lay the party out as soon as the maze is on screen, so the table is not empty until
            // the first step. After the elevator dialog, so it reflects the level actually chosen.
            PublishToViewer();

            // From here the table can also be played FROM. Started after the first publish so the
            // viewer has something drawn before it can be clicked.
            _viewerControl = ViewerControlPump.Start(this, ViewerCommands.Maze, InjectViewerKey, _viewer);
        };

        FormClosed += (_, _) =>
        {
            SaveMazeWindowLocation();
            _viewerControl?.Dispose();
            if (_ruleApplicationInfoHandler != null)
                RuleApplicationInfo.InfoPublished -= _ruleApplicationInfoHandler;

            if (_ruleApplicationInfoForm != null && !_ruleApplicationInfoForm.IsDisposed)
                _ruleApplicationInfoForm.Close();
        };
    }

    private static Point? LoadSavedMazeWindowLocation()
    {
        try
        {
            if (!File.Exists(MazeWindowStatePath))
                return null;

            var json = File.ReadAllText(MazeWindowStatePath);
            var state = JsonSerializer.Deserialize<MazeWindowState>(json);
            if (state == null)
                return null;

            var point = new Point(state.Left, state.Top);
            return Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(point))
                ? point
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void SaveMazeWindowLocation()
    {
        try
        {
            var folder = Path.GetDirectoryName(MazeWindowStatePath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var location = WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location;
            var state = new MazeWindowState(location.X, location.Y);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MazeWindowStatePath, json);
        }
        catch
        {
            // Best effort only.
        }
    }

    private sealed record MazeWindowState(int Left, int Top);

    private void PruneUnavailablePartyMembers()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var filtered = party.Members
            .Where(name =>
                roster.TryGetValue(name, out var c)
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost)
                && !c.HasStatus(CharacterStatus.Out))
            .ToList();

        if (filtered.Count == party.Members.Count)
            return;

        party.Members = filtered;

        if (party.CurrentShopperIndex >= party.Members.Count)
            party.CurrentShopperIndex = party.Members.Count > 0 ? party.Members.Count - 1 : -1;

        _partyRepository.Save(party);
    }

    private void ShowCampDialog()
    {
        while (true)
        {
            var party = _partyRepository.Load();
            var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var activeMembers = party.Members.Where(m => roster.ContainsKey(m)).ToList();

            if (activeMembers.Count == 0)
            {
                SayOnBoth("Camp", "No party members to camp with.");
                RefreshMazeAfterCamp();
                return;
            }

            using var form = new Form();
            form.Text = "Camp";
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size(420, 250);
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;
            form.BackColor = Color.Black;
            form.ForeColor = GameRulesProvider.Current.DefaultColor;
            form.KeyPreview = true;

            var titlePanel = new Panel
            {
                Left = (form.ClientSize.Width - 140) / 2,
                Top = 0,
                Width = 140,
                Height = 52,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black
            };

            var titleLabel = new Label
            {
                Left = 0,
                Top = 10,
                Width = titlePanel.ClientSize.Width,
                Height = 30,
                Text = "CAMP",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 20f, FontStyle.Bold)
            };

            var menuPanel = new Panel
            {
                Left = (form.ClientSize.Width - 260) / 2,
                Top = 74,
                Width = 260,
                Height = 132,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black
            };

            var menuLabel = new Label
            {
                Left = 14,
                Top = 10,
                Width = 232,
                Height = 110,
                Text = "#)INSPECT\nR)EORDER\nL↵EAVE",
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 20f, FontStyle.Bold)
            };

            int? quickInspectIndex = null;

            form.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.R)
                {
                    form.DialogResult = DialogResult.No;
                    form.Close();
                }

                var selectedIndex = GetCampInspectIndexFromKey(e.KeyCode);
                if (selectedIndex > 0 && selectedIndex <= activeMembers.Count)
                {
                    quickInspectIndex = selectedIndex - 1;
                    form.DialogResult = DialogResult.Yes;
                    form.Close();
                }
                else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                {
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                }
            };

            titlePanel.Controls.Add(titleLabel);
            menuPanel.Controls.Add(menuLabel);
            form.Controls.Add(titlePanel);
            form.Controls.Add(menuPanel);

            // The camp menu on the table as well: pressing Camp from the viewer used to open this and stop dead,
            // which made the whole of camp -- reordering, inspecting, equipping, memorising -- unreachable from
            // there. Same three answers, same results, either surface.
            var campPrompt = new ViewerPrompt("choice", "The party camps.", null, new[]
            {
                new ViewerPromptOption("reorder", "Reorder the party"),
                new ViewerPromptOption("inspect", "Inspect a member"),
                new ViewerPromptOption("leave", "Leave camp"),
            });


            var campAnswers = new Dictionary<string, DialogResult>
            {
                ["reorder"] = DialogResult.No,
                ["inspect"] = DialogResult.Yes,
                ["leave"] = DialogResult.Cancel,
            };
            var choice = ViewerDialog.RunModal(form, this, campPrompt, campAnswers, PublishTable);

            if (choice == DialogResult.Yes)
            {
                if (quickInspectIndex.HasValue
                    && quickInspectIndex.Value >= 0
                    && quickInspectIndex.Value < activeMembers.Count)
                {
                    var selectedCharacter = roster[activeMembers[quickInspectIndex.Value]];
                    OpenCampInspectDialog(selectedCharacter, activeMembers);
                }
                else
                {
                    InspectPartyMemberFromCamp(activeMembers, roster);
                }

                continue;
            }

            if (choice == DialogResult.No)
            {
                ShowReorderCampDialog(party, activeMembers, roster);
                continue;
            }

            RefreshMazeAfterCamp();
            return;
        }
    }

    private void RefreshMazeAfterCamp()
    {
        Invalidate();
        PublishToViewer();
    }

    private List<string>? RunOldStyleReorderDialog(List<string> activeMembers, Dictionary<string, Character> roster)
    {
        var picked = new List<int>();
        List<string>? result = null;

        using var form = new Form { Text = "Reorder Party", StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            TabStop = false,
            WordWrap = false,
            ScrollBars = ScrollBars.Vertical
        };
        form.Controls.Add(text);

        string Row(int number, Character c) => $"{number}. {c.Name,-12} HP {c.CurrentHitPoints + "/" + c.MaxHitPoints,-7} AC {c.ArmorClass,2}";

        void Refresh()
        {
            var lines = new List<string> { "TYPE MEMBER NUMBERS IN NEW ORDER", "", "OLD ORDER" };
            for (var i = 0; i < activeMembers.Count; i++)
                lines.Add((picked.Contains(i) ? "*" : " ") + Row(i + 1, roster[activeMembers[i]]));

            lines.Add("");
            lines.Add("NEW ORDER");
            if (picked.Count == 0)
                lines.Add(" (EMPTY)");
            for (var i = 0; i < picked.Count; i++)
                lines.Add(" " + Row(i + 1, roster[activeMembers[picked[i]]]));

            text.Lines = lines.ToArray();
        }

        Refresh();
        OldStyleDialog.Apply(form, text, selectLabel: "CONFIRM    1-9 PICK    BKSP UNDO");
        form.PerformLayout();
        var completeOrderHeight = (6 + 2 * activeMembers.Count) * text.Font.Height;
        var extraHeight = Math.Max(0, completeOrderHeight - text.ClientSize.Height);
        form.Height = Math.Min(Screen.FromControl(this).WorkingArea.Height, form.Height + extraHeight);

        form.KeyDown += (_, e) =>
        {
            var digit = e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9 ? e.KeyCode - Keys.D1
                : e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9 ? e.KeyCode - Keys.NumPad1
                : -1;
            if (digit >= 0 && digit < activeMembers.Count && !picked.Contains(digit))
            {
                picked.Add(digit);
                Refresh();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Back && picked.Count > 0)
            {
                picked.RemoveAt(picked.Count - 1);
                Refresh();
                e.Handled = e.SuppressKeyPress = true;
            }
        };

        form.FormClosing += (_, e) =>
        {
            if (form.DialogResult != DialogResult.OK)
                return;

            if (picked.Count != activeMembers.Count)
            {
                e.Cancel = true;
                form.DialogResult = DialogResult.None;
                return;
            }

            result = picked.Select(i => activeMembers[i]).ToList();
        };

        return ViewerDialog.RunBlocked(form, this, "Reordering the party", PublishTable) == DialogResult.OK ? result : null;
    }

    private void ShowReorderCampDialog(Adnd.Data.Party.Party party, List<string> activeMembers, Dictionary<string, Character> roster)
    {
        if (activeMembers.Count < 2)
        {
            SayOnBoth("Camp", "Need at least two party members to reorder.");
            return;
        }

        if (GameRulesProvider.Current.UIOldStyle)
        {
            var oldStyleResult = RunOldStyleReorderDialog(activeMembers, roster);
            PublishToViewer();
            if (oldStyleResult == null || oldStyleResult.SequenceEqual(activeMembers, StringComparer.OrdinalIgnoreCase))
                return;

            party.Members = oldStyleResult;
            _partyRepository.Save(party);
            SayOnBoth("Camp", "Party order updated.");
            Invalidate();
            return;
        }

        List<string>? reorderedResult = null;
        var picked = new List<int>();
        var remaining = Enumerable.Range(0, activeMembers.Count).ToList();

        using var form = new Form();
        form.Text = "Camp - Reorder Party";
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(980, 215);
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.KeyPreview = true;

        var instructions = new Label
        {
            Left = 12,
            Top = 10,
            Width = 956,
            Height = 24,
            Text = "Select a member and use > to add to new order, < to remove. Enter = confirm, Esc = cancel"
        };

        var oldLabel = new Label
        {
            Left = 12,
            Top = 38,
            Width = 430,
            Height = 24,
            Text = "Old order"
        };

        var newLabel = new Label
        {
            Left = 538,
            Top = 38,
            Width = 430,
            Height = 24,
            Text = "New order"
        };

        var oldList = new ListBox
        {
            Left = 12,
            Top = 62,
            Width = 430,
            Height = 110,
            Font = new Font("Consolas", 10f)
        };

        var newList = new ListBox
        {
            Left = 538,
            Top = 62,
            Width = 430,
            Height = 110,
            Font = new Font("Consolas", 10f)
        };

        var moveRightBtn = new Button
        {
            Text = ">",
            Left = 460,
            Top = 82,
            Width = 60,
            Height = 30
        };

        var moveLeftBtn = new Button
        {
            Text = "<",
            Left = 460,
            Top = 122,
            Width = 60,
            Height = 30
        };

        var confirmBtn = new Button
        {
            Text = "Confirm",
            Left = 812,
            Top = 178,
            Width = 75,
            DialogResult = DialogResult.None
        };

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Left = 893,
            Top = 178,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        string Row(Character c, int row)
        {
            var cls = c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(x => x.ToDisplayString())) : c.Class.ToDisplayString();
            var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
            return $"{row,2}. {c.Name,-12} {cls,-24} HP {hp,-7} AC {c.ArmorClass,2}";
        }

        void RefreshLists()
        {
            oldList.Items.Clear();
            newList.Items.Clear();

            for (int i = 0; i < remaining.Count; i++)
            {
                var c = roster[activeMembers[remaining[i]]];
                oldList.Items.Add(Row(c, i + 1));
            }

            for (int i = 0; i < picked.Count; i++)
            {
                var c = roster[activeMembers[picked[i]]];
                newList.Items.Add(Row(c, i + 1));
            }
        }

        void MoveRight()
        {
            if (oldList.SelectedIndex < 0 || oldList.SelectedIndex >= remaining.Count)
                return;

            var idx = remaining[oldList.SelectedIndex];
            remaining.RemoveAt(oldList.SelectedIndex);
            picked.Add(idx);
            RefreshLists();
        }

        void MoveLeft()
        {
            if (newList.SelectedIndex < 0 || newList.SelectedIndex >= picked.Count)
                return;

            var idx = picked[newList.SelectedIndex];
            picked.RemoveAt(newList.SelectedIndex);
            remaining.Add(idx);
            remaining.Sort();
            RefreshLists();
        }

        void TryConfirm()
        {
            if (picked.Count != activeMembers.Count)
            {
                ViewerMessage.Say(form, "Reorder Party", "Move all members into New order before confirming.", PublishTable);
                return;
            }

            reorderedResult = picked.Select(i => activeMembers[i]).ToList();
            form.DialogResult = DialogResult.OK;
            form.Close();
        }

        moveRightBtn.Click += (_, _) => MoveRight();
        moveLeftBtn.Click += (_, _) => MoveLeft();
        confirmBtn.Click += (_, _) => TryConfirm();

        form.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.Cancel;
                form.Close();
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                TryConfirm();
                e.Handled = true;
                return;
            }
        };

        form.Controls.Add(instructions);
        form.Controls.Add(oldLabel);
        form.Controls.Add(newLabel);
        form.Controls.Add(oldList);
        form.Controls.Add(newList);
        form.Controls.Add(moveRightBtn);
        form.Controls.Add(moveLeftBtn);
        form.Controls.Add(confirmBtn);
        form.Controls.Add(cancelBtn);
        form.CancelButton = cancelBtn;

        RefreshLists();

        // Dragging names between two lists is not a thing the table can offer, so it says where to do it and
        // holds the table's commands until this closes -- otherwise they would fall through and walk the party
        // around underneath an open dialog.
        var result = ViewerDialog.RunBlocked(form, this, "Reordering the party", PublishTable);
        PublishToViewer();

        if (result != DialogResult.OK || reorderedResult == null)
            return;

        if (reorderedResult.SequenceEqual(activeMembers, StringComparer.OrdinalIgnoreCase))
            return;

        party.Members = reorderedResult;
        _partyRepository.Save(party);
        SayOnBoth("Camp", "Party order updated.");
        Invalidate();
    }

    public void ShowCharaterSheet(Character character)
    {
        using var sheet = new Adnd.Game.Windows.CharacterForm(character);
        sheet.ShowDialog(this);
        PublishToViewer();
    }
    private void InspectPartyMemberFromCamp(List<string> activeMembers, Dictionary<string, Character> roster)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Choose party member to inspect:");
        prompt.AppendLine();

        for (int i = 0; i < activeMembers.Count; i++)
        {
            var c = roster[activeMembers[i]];
            var cls = c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(x => x.ToDisplayString())) : c.Class.ToDisplayString();
            prompt.AppendLine($"{i + 1}. {c.Name} ({cls})");
        }

        var selected = PromptForNumber("Camp - Inspect", prompt.ToString(), 1, activeMembers.Count);
        if (!selected.HasValue)
            return;

        var selectedCharacter = roster[activeMembers[selected.Value - 1]];

        OpenCampInspectDialog(selectedCharacter, activeMembers);
    }

    private void OpenCampInspectDialog(Character selectedCharacter, List<string> activeMembers)
    {
     /*   if (!GameRulesProvider.Current.UIOldStyle)
        {
            using var sheet = new Adnd.Game.Windows.CharacterForm(selectedCharacter);
            sheet.ShowDialog(this);
            PublishToViewer();
            return;
        }
     */
        // Handed the maze's publisher, so the whole screen -- menu, item lists, spell lists -- is answerable from
        // the table too.
        using var inspect = new CampCharacterInspectForm(selectedCharacter.Name, activeMembers, PublishTable);
        inspect.ShowDialog(this);
        LoadLightFromParty();
        if (inspect.ReturnToTownRequested)
        {
            LeaveDungeonForTown();
            return;
        }
        PublishToViewer();   // back to the maze's own question
//        MessageBox.Show(this, selectedCharacter.ToString(), $"Inspect - {selectedCharacter.Name}", MessageBoxButtons.OK, MessageBoxIcon.None);
    }

    private static int GetCampInspectIndexFromKey(Keys key)
    {
        return key switch
        {
            Keys.D1 or Keys.NumPad1 => 1,
            Keys.D2 or Keys.NumPad2 => 2,
            Keys.D3 or Keys.NumPad3 => 3,
            Keys.D4 or Keys.NumPad4 => 4,
            Keys.D5 or Keys.NumPad5 => 5,
            Keys.D6 or Keys.NumPad6 => 6,
            _ => 0
        };
    }

    private int? PromptForNumber(string title, string text, int min, int max)
    {
        // One option per number for the table. Both callers -- choosing who to inspect in camp, choosing a floor
        // in the elevator -- already list the choices numbered in the text, so the labels come from there.
        var labels = new List<string>();
        for (var n = min; n <= max; n++)
            labels.Add(ViewerDialog.LabelFor(text, n));

        var input = PromptForText(title, text, labels, min);
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (!int.TryParse(input.Trim(), out var value))
            return null;

        if (value < min || value > max)
            return null;

        return value;
    }

    private void ShowDungeonElevatorDialog()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Choose dungeon level:");
        sb.AppendLine();
        for (var n = MinDungeonLevel; n <= MaxDungeonLevel; n++)
            sb.AppendLine($"{n}. Level {n}");

        sb.AppendLine();
        sb.Append($"Current level: {_currentDungeonLevel}");

        var selected = PromptForNumber("Elevator", sb.ToString(), MinDungeonLevel, MaxDungeonLevel);
        if (!selected.HasValue)
            return;

        _currentDungeonLevel = selected.Value;
        BuildMazeForLevel(_currentDungeonLevel);
        LoadEncounterLocationsForLevel(_currentDungeonLevel);

        if (!_fullMoonShownForThisDungeonTrip && IsFullMoonNight())
        {
            ShowFullMoonDialogRetro();
            _fullMoonShownForThisDungeonTrip = true;
        }

        ApplyLycanthropyStateForDungeonDescent();

        // Elevator landing.
        _position = ElevatorPosition;
        UpdateLighting();

        Invalidate();
        PublishToViewer();
    }

    private void ApplyLycanthropyStateForDungeonDescent()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var isFullMoon = IsFullMoonNight();
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            c.AdvanceLycanthropyByOneDay();

            if (!c.CanBecomeLycanthropeNow())
            {
                _characterRepository.Save(c);
                continue;
            }

            if (isFullMoon && c.TriggerLycanthropeTransformationByFullMoon())
            {
                messages.Add($"{c.Name} transforms under the full moon ({c.LycanthropySourceType}).");
            }

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Lycanthropy", string.Join(Environment.NewLine, messages));
    }

    /// <param name="tableOptions">
    /// The answers to offer on the table, when this prompt is really a numbered choice. Null for genuinely free
    /// text -- naming something -- where the table has nothing sensible to click and the keyboard is the only
    /// way; the player is told that rather than left looking at a board that appears hung.
    /// </param>
    /// <param name="firstNumber">What <paramref name="tableOptions"/>[0] is called, usually 1.</param>
    private string? PromptForText(string title, string text, IReadOnlyList<string>? tableOptions = null,
                                  int firstNumber = 1)
    {
        using var form = new Form();
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(520, 320);
        form.MinimizeBox = false;
        form.MaximizeBox = false;

        var oldStyleChoice = GameRulesProvider.Current.UIOldStyle && tableOptions != null;
        Control label = oldStyleChoice
            ? new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = text
            }
            : new Label
            {
                Left = 12,
                Top = 12,
                Width = 496,
                Height = 220,
                AutoSize = false,
                Text = text
            };

        var input = new TextBox
        {
            Left = 12,
            Top = 244,
            Width = 496
        };

        var ok = new Button
        {
            Text = "OK",
            Left = 352,
            Top = 278,
            Width = 75,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Left = 433,
            Top = 278,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        form.Controls.Add(label);
        form.Controls.Add(input);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (oldStyleChoice)
        {
            OldStyleDialog.Apply(form, label, input);
            form.Shown += (_, _) => input.Focus();
        }

        if (tableOptions is null)
        {
            // Genuinely free text. Nothing for the table to click, so it is told where to type.
            var typed = ViewerDialog.RunBlocked(form, this, title, PublishTable) == DialogResult.OK
                ? input.Text
                : null;
            PublishToViewer();
            return typed;
        }

        var outcome = ViewerDialog.RunPick(form, this, title, null, tableOptions, PublishTable);
        PublishToViewer();

        if (outcome.Picked.HasValue)
            return (firstNumber + outcome.Picked.Value).ToString();

        return outcome.Result == DialogResult.OK ? input.Text : null;
    }

    /// <summary>
    /// A command from the tabletop viewer, replayed as the key it stands for. Deliberately the same
    /// entry point the keyboard uses, so moving, turning, the overlay and the snapshot publish below
    /// all take the ordinary path and nothing downstream knows where the input came from.
    /// </summary>
    internal void InjectViewerKey(Keys key) => MazeForm_KeyDown(this, new KeyEventArgs(key));

    private void MazeForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_partyOverlayVisible)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    _partyOverlayVisible = false;
                    Invalidate();
                    return;
                case Keys.C:
                    ShowCampDialog();
                    return;
                case Keys.T:
                    ShowPartyPopup("Party Status");
                    return;
                case Keys.I:
                    TryRecoverOutCharactersAtCurrentPosition();
                    return;
            }

            // When overlay is visible, any other keys should still be processed below
            // so movement and turning continue to work while party stats are shown.
        }

        switch (e.KeyCode)
        {
            case Keys.S:
                SearchArea();
                Invalidate();
                break;
            case Keys.I:
                TryRecoverOutCharactersAtCurrentPosition();
                Invalidate();
                break;
            case Keys.W:
            case Keys.Up:
                TryMoveForward();
                Invalidate();
                break;
            case Keys.A:
            case Keys.Left:
                _direction = TurnLeft(_direction);
                Invalidate();
                break;
            case Keys.D:
            case Keys.Right:
                _direction = TurnRight(_direction);
                Invalidate();
                break;
            case Keys.O:
                _partyOverlayVisible = true;
                Invalidate();
                break;
            case Keys.Escape:
                _partyOverlayVisible = !_partyOverlayVisible;
                Invalidate();
                break;
        }

        // One call covers moving, turning and anything else a key changed. Overlay toggles publish
        // an identical snapshot, which the viewer simply reconciles to no change.
        PublishToViewer();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(Color.Black);
        e.Graphics.SmoothingMode = SmoothingMode.None;

        using var pen = new Pen(GameRulesProvider.Current.DefaultColor, 2f);
        var topHudSpace = _partyOverlayVisible ? 34f : 12f;
        var footerSpace = _partyOverlayVisible ? 220f : 80f;
        var viewport = new RectangleF(12f, topHudSpace, ClientSize.Width - 24f, ClientSize.Height - footerSpace - (topHudSpace - 12f));
        e.Graphics.DrawRectangle(pen, viewport.X, viewport.Y, viewport.Width, viewport.Height);

        InitWallCoords(viewport);
        DrawScene(e.Graphics, pen, viewport);

        if (_inMagicalDarkness)
        {
            using var darkBrush = new SolidBrush(GameRulesProvider.Current.DefaultColor);
            var size = e.Graphics.MeasureString("Dark, you see nothing.", Font);
            e.Graphics.FillRectangle(Brushes.Black, viewport.X + 8f, viewport.Y + 4f, size.Width + 8f, size.Height + 4f);
            e.Graphics.DrawString("Dark, you see nothing.", Font, darkBrush, new PointF(viewport.X + 12f, viewport.Y + 8f));
        }
        if (_centerMessage is not null)
        {
            using var bigFont = new Font(Font.FontFamily, _centerMessage.Length > 12 ? 30f : 48f, FontStyle.Bold);
            using var bigBrush = new SolidBrush(GameRulesProvider.Current.DefaultColor);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var textSize = e.Graphics.MeasureString(_centerMessage, bigFont, (int)(viewport.Width * 0.8f), format);
            var box = new RectangleF(
                viewport.X + ((viewport.Width - textSize.Width) / 2f) - 24f,
                viewport.Y + ((viewport.Height - textSize.Height) / 2f) - 16f,
                textSize.Width + 48f,
                textSize.Height + 32f);
            e.Graphics.FillRectangle(Brushes.Black, box);
            e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            e.Graphics.DrawString(_centerMessage, bigFont, bigBrush, box, format);
        }

        if (_partyOverlayVisible)
        {
            DrawOverlayLegend(e.Graphics);
            DrawPartyInfo(e.Graphics, viewport);
        }
        else
        {
            DrawStatus(e.Graphics);
        }
    }

    private void InitWallCoords(RectangleF viewport)
    {
        var frames = BuildFrames(viewport, _depthLevels);

        for (int depth = 0; depth < _depthLevels; depth++)
        {
            var centerRect = frames[depth + 1];
            SetWallRect(depth, StraightAhead, centerRect);

            float wallWidth = centerRect.Width;
            for (int offset = 1; offset <= StraightAhead; offset++)
            {
                SetWallRect(depth, StraightAhead - offset, ShiftRect(centerRect, -(wallWidth * offset)));
                SetWallRect(depth, StraightAhead + offset, ShiftRect(centerRect, wallWidth * offset));
            }
        }
    }

    private void DrawScene(Graphics graphics, Pen pen, RectangleF viewport)
    {
        // Magical darkness hides the whole dungeon behind a dithered shade.
        if (_inMagicalDarkness)
        {
            using var shade = new System.Drawing.Drawing2D.HatchBrush(
                System.Drawing.Drawing2D.HatchStyle.Percent50, GameRulesProvider.Current.DefaultColor, Color.Black);
            graphics.FillRectangle(shade, viewport);
            return;
        }

        _visibleCells = _walls is null ? null : ComputeVisibleCells();

        for (int depth = _depthLevels - 1; depth >= 0; depth--)
        {
            var centerCell = GetCellFartherAway(_position, _direction, depth);

            // Outermost squares first and the centre last, so a wall beside the corridor is painted over
            // the squares it hides behind it.
            for (int i = depth + 1; i >= 1; i--)
            {
                var leftCell = GetCellToTheLeft(centerCell, _direction, i);
                DrawCellContents(graphics, pen, viewport, leftCell, depth, StraightAhead - i);

                var rightCell = GetCellToTheRight(centerCell, _direction, i);
                DrawCellContents(graphics, pen, viewport, rightCell, depth, StraightAhead + i);
            }

            DrawCellContents(graphics, pen, viewport, centerCell, depth, StraightAhead);
        }
    }

    private void DrawCellContents(Graphics graphics, Pen pen, RectangleF viewport, Point cell, int depth, int position)
    {
        if (!IsOpen(cell) || depth < 0 || depth >= _depthLevels || position < 0 || position >= PositionCount)
            return;

        if (_visibleCells is not null && !_visibleCells.Contains((depth, position)))
            return;

        var back = GetWallRect(depth, position);
        if (back.Height < 2f)
            return;

        if (IsDark(cell))
        {
            DrawDarkCell(graphics, viewport, depth, position, back);
            return;
        }

        bool backWallDrawn = false;
        bool leftWallDrawn = false;
        bool rightWallDrawn = false;

        var stairs = StairsAt(cell);
        if (stairs != StairsKind.None)
            DrawStairsShading(graphics, pen, viewport, depth, position, back, floor: stairs == StairsKind.Down);

        var frontCell = GetCellFartherAway(cell, _direction, 1);
        if (IsFrontBlocked(cell))
        {
            using var wallBrush = new SolidBrush(Color.Black);
            graphics.FillRectangle(wallBrush, back);
            graphics.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
            backWallDrawn = true;

            if (HasFrontDoor(cell))
            {
                // Slightly narrower and lower than the wall, and reaching all the way down to the floor.
                var door = new RectangleF(
                    back.X + (back.Width * 0.15f),
                    back.Y + (back.Height * 0.12f),
                    back.Width * 0.70f,
                    back.Height * 0.88f);
                graphics.DrawRectangle(pen, door.X, door.Y, door.Width, door.Height);
            }
        }

        if (position <= StraightAhead)
        {
            if (IsLeftBlocked(cell))
            {
                DrawLeftWall(graphics, pen, viewport, depth, position, back, HasLeftDoor(cell));
                leftWallDrawn = true;
            }
        }

        if (position >= StraightAhead)
        {
            if (IsRightBlocked(cell))
            {
                DrawRightWall(graphics, pen, viewport, depth, position, back, HasRightDoor(cell));
                rightWallDrawn = true;
            }
        }

        bool frontLeftBlocked = IsLeftBlocked(frontCell);
        bool frontRightBlocked = IsRightBlocked(frontCell);

        bool drawLeftVertical = (backWallDrawn && (leftWallDrawn || frontLeftBlocked)) ||
                                (leftWallDrawn && frontLeftBlocked);
        bool drawRightVertical = (backWallDrawn && (rightWallDrawn || frontRightBlocked)) ||
                                 (rightWallDrawn && frontRightBlocked);

        if (drawLeftVertical)
        {
            graphics.DrawLine(pen,
                new PointF(back.Left, back.Top),
                new PointF(back.Left, back.Bottom));
        }

        if (drawRightVertical)
        {
            graphics.DrawLine(pen,
                new PointF(back.Right, back.Top),
                new PointF(back.Right, back.Bottom));
        }
    }

    private bool IsDark(Point cell) => _darkAreas.Any(a => a.Contains(cell.X, cell.Y));

    // A dark square is filled with the dithered shade over everything it occupies: the outline of its near and far faces.
    private void DrawDarkCell(Graphics graphics, RectangleF viewport, int depth, int position, RectangleF back)
    {
        var near = depth == 0
            ? ShiftRect(viewport, (position - StraightAhead) * viewport.Width)
            : GetWallRect(depth - 1, position);

        var points = new List<PointF>
        {
            new(near.Left, near.Top), new(near.Right, near.Top), new(near.Right, near.Bottom), new(near.Left, near.Bottom),
            new(back.Left, back.Top), new(back.Right, back.Top), new(back.Right, back.Bottom), new(back.Left, back.Bottom)
        };

        var hull = ConvexHull(points);
        var state = graphics.Save();
        graphics.SetClip(viewport, System.Drawing.Drawing2D.CombineMode.Intersect);
        using var shade = new System.Drawing.Drawing2D.HatchBrush(
            System.Drawing.Drawing2D.HatchStyle.Percent50, GameRulesProvider.Current.DefaultColor, Color.Black);
        graphics.FillPolygon(shade, hull);
        graphics.Restore(state);
    }

    private static PointF[] ConvexHull(List<PointF> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        static float Cross(PointF o, PointF a, PointF b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

        var hull = new List<PointF>();
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        var lowerCount = hull.Count + 1;
        for (var i = sorted.Count - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count >= lowerCount && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }

    // The stairs lead through the ceiling (up) or the floor (down), so that part of the square is shaded
    // with closely spaced parallel lines.
    private void DrawStairsShading(Graphics graphics, Pen pen, RectangleF viewport, int depth, int position, RectangleF back, bool floor)
    {
        var near = depth == 0 ? viewport : GetWallRect(depth - 1, position);
        var nearEdge = floor ? near.Bottom : near.Top;
        var backEdge = floor ? back.Bottom : back.Top;
        var height = backEdge - nearEdge;
        if (Math.Abs(height) < 2f || position != StraightAhead && depth == 0)
            return;

        using var thin = new Pen(pen.Color, 1f);
        // Only the middle of the surface: inset from the side walls and from the near and far edges.
        const float sideInset = 0.25f;
        const float depthInset = 0.2f;
        var steps = (int)(Math.Abs(height) * (1f - (2f * depthInset)) / 3f);
        for (var i = 0; i <= steps; i++)
        {
            var y = nearEdge + (height * depthInset) + (Math.Sign(height) * i * 3f);
            var t = (y - nearEdge) / height;
            var left = near.Left + ((back.Left - near.Left) * t);
            var right = near.Right + ((back.Right - near.Right) * t);
            var inset = (right - left) * sideInset;
            graphics.DrawLine(thin, left + inset, y, right - inset, y);
        }
    }

    private static void DrawSideDoor(Graphics graphics, Pen pen, PointF nearTop, PointF nearBottom, PointF farTop, PointF farBottom)
    {
        // Along the wall from the near edge to the far edge; the top is inset, the bottom stays on the floor.
        static PointF Along(PointF a, PointF b, float t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

        PointF Point(float t, bool top)
        {
            var wallTop = Along(nearTop, farTop, t);
            var wallBottom = Along(nearBottom, farBottom, t);
            return top ? Along(wallTop, wallBottom, 0.12f) : wallBottom;
        }

        var doorNearTop = Point(0.2f, true);
        var doorNearBottom = Point(0.2f, false);
        var doorFarTop = Point(0.8f, true);
        var doorFarBottom = Point(0.8f, false);

        graphics.DrawLine(pen, doorNearTop, doorFarTop);
        graphics.DrawLine(pen, doorNearTop, doorNearBottom);
        graphics.DrawLine(pen, doorFarTop, doorFarBottom);
    }

    private void DrawLeftWall(Graphics graphics, Pen pen, RectangleF viewport, int depth, int position, RectangleF back, bool door = false)
    {
        PointF nearTop;
        PointF nearBottom;

        if (depth == 0)
        {
            nearTop = new PointF(viewport.Left, viewport.Top);
            nearBottom = new PointF(viewport.Left, viewport.Bottom);
        }
        else
        {
            var nearRect = GetWallRect(depth - 1, position);
            nearTop = new PointF(nearRect.Left, nearRect.Top);
            nearBottom = new PointF(nearRect.Left, nearRect.Bottom);
        }

        var farTop = new PointF(back.Left, back.Top);
        var farBottom = new PointF(back.Left, back.Bottom);

        var wall = new[] { nearTop, farTop, farBottom, nearBottom };
        using var wallBrush = new SolidBrush(Color.Black);
        graphics.FillPolygon(wallBrush, wall);

        graphics.DrawLine(pen, nearTop, farTop);
        graphics.DrawLine(pen, nearBottom, farBottom);
        graphics.DrawLine(pen, nearTop, nearBottom);
        graphics.DrawLine(pen, farTop, farBottom);

        if (door)
            DrawSideDoor(graphics, pen, nearTop, nearBottom, farTop, farBottom);
    }

    private void DrawRightWall(Graphics graphics, Pen pen, RectangleF viewport, int depth, int position, RectangleF back, bool door = false)
    {
        PointF nearTop;
        PointF nearBottom;

        if (depth == 0)
        {
            nearTop = new PointF(viewport.Right, viewport.Top);
            nearBottom = new PointF(viewport.Right, viewport.Bottom);
        }
        else
        {
            var nearRect = GetWallRect(depth - 1, position);
            nearTop = new PointF(nearRect.Right, nearRect.Top);
            nearBottom = new PointF(nearRect.Right, nearRect.Bottom);
        }

        var farTop = new PointF(back.Right, back.Top);
        var farBottom = new PointF(back.Right, back.Bottom);

        var wall = new[] { nearTop, farTop, farBottom, nearBottom };
        using var wallBrush = new SolidBrush(Color.Black);
        graphics.FillPolygon(wallBrush, wall);

        graphics.DrawLine(pen, nearTop, farTop);
        graphics.DrawLine(pen, nearBottom, farBottom);
        graphics.DrawLine(pen, nearTop, nearBottom);
        graphics.DrawLine(pen, farTop, farBottom);

        if (door)
            DrawSideDoor(graphics, pen, nearTop, nearBottom, farTop, farBottom);
    }

    private void SetWallRect(int depth, int position, RectangleF rect)
    {
        _backWallCoords[depth, position, TopLeft] = new PointF(rect.Left, rect.Top);
        _backWallCoords[depth, position, BottomRight] = new PointF(rect.Right, rect.Bottom);
    }

    private RectangleF GetWallRect(int depth, int position)
    {
        var tl = _backWallCoords[depth, position, TopLeft];
        var br = _backWallCoords[depth, position, BottomRight];
        return RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
    }

    private static RectangleF ShiftRect(RectangleF rect, float dx) =>
        RectangleF.FromLTRB(rect.Left + dx, rect.Top, rect.Right + dx, rect.Bottom);

    private void DrawStatus(Graphics graphics)
    {
        var heading = _direction switch
        {
            Direction.North => "North",
            Direction.East => "East",
            Direction.South => "South",
            _ => "West"
        };

        var status = $"A/D or ←/→: Turn   W or ↑: Move   Esc: Toggle full dungeon view   Level: {_currentDungeonLevel}   Pos: ({_position.X},{_position.Y})   Facing: {heading}";
        graphics.DrawString(status, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(16f, ClientSize.Height - 54f));
    }

    private void DrawOverlayLegend(Graphics graphics)
    {
        var heading = _direction switch
        {
            Direction.North => "North",
            Direction.East => "East",
            Direction.South => "South",
            _ => "West"
        };

        var legend = $"A/D or ←/→: Turn   W or ↑: Move   C: Camp   K: Kick   Esc: Toggle full dungeon view   Level: {_currentDungeonLevel}   Pos: ({_position.X},{_position.Y})   Facing: {heading}";
        graphics.DrawString(legend, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(16f, 8f));
    }

    private void DrawPartyInfo(Graphics graphics, RectangleF viewport)
    {
        float y = viewport.Bottom + 8f;
        float x = 16f;
        const float numberColWidth = 24f;

        // Headings
        graphics.DrawString("#", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x, y));
        graphics.DrawString("Name", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth, y));
        graphics.DrawString("Align", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 170f, y));
        graphics.DrawString("Class", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 230f, y));
        graphics.DrawString("Lvl", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 390f, y));
        graphics.DrawString("HP", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 450f, y));
        graphics.DrawString("AC", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 530f, y));
        graphics.DrawString("Status", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 600f, y));

        y += 18f;
        graphics.DrawLine(new Pen(GameRulesProvider.Current.DefaultColor), x, y - 2f, viewport.Right - 8f, y - 2f);

        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        if (party.Members.Count == 0)
        {
            graphics.DrawString("No party members.", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x, y));
            return;
        }

        for (int i = 0; i < party.Members.Count; i++)
        {
            var memberName = party.Members[i];
            if (!roster.TryGetValue(memberName, out var c))
            {
                graphics.DrawString((i + 1).ToString(), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x, y));
                graphics.DrawString(memberName, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth, y));
                graphics.DrawString("--", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 170f, y));
                graphics.DrawString("(missing)", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 230f, y));
                y += 18f;
                continue;
            }

            var classes = c.Classes.Count > 0
                ? c.GetClassesDisplayText("/")
                : c.GetClassesDisplayText("/");

            var status = GetMazeStatusDisplay(c);
            var alignment = c.Alignment.ToAbbreviation();

            graphics.DrawString((i + 1).ToString(), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x, y));
            graphics.DrawString(c.Name, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth, y));
            graphics.DrawString(alignment, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 170f, y));
            graphics.DrawString(classes, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 230f, y));
            graphics.DrawString(GetLevelDisplay(c), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 390f, y));
            graphics.DrawString($"{c.CurrentHitPoints}/{c.MaxHitPoints}", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 450f, y));
            graphics.DrawString(c.ArmorClass.ToString(), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 530f, y));
            graphics.DrawString(status, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 600f, y));

            y += 18f;
        }
    }

    private string GetMazeStatusDisplay(Character c)
    {
        var status = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : string.Empty;

        // Invisibility can be active from ring state even when status text would otherwise be empty.
        var invisibleActive = c.HasStatus(CharacterStatus.Invisible)
                              || c.RingInvisibilityActive
                              || c.RingInvisibilityAppliedArmorClassBonus;

        if (invisibleActive)
        {
            if (string.IsNullOrWhiteSpace(status) || status == "-")
                status = "Invisible";
            else if (!status.Contains("Invisible", StringComparison.OrdinalIgnoreCase))
                status += ", Invisible";
        }

        return string.IsNullOrWhiteSpace(status) ? "-" : status;
    }

    private List<string> GetPartyLines()
    {
        var result = new List<string>();
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        if (party.Members.Count == 0)
        {
            result.Add("No party members.");
            return result;
        }

        foreach (var memberName in party.Members)
        {
            if (!roster.TryGetValue(memberName, out var c))
            {
                result.Add($"{memberName} (missing)");
                continue;
            }

            var classes = c.Classes.Count > 0
                ? c.GetClassesDisplayText("/")
                : c.GetClassesDisplayText("/");
            var alignment = c.Alignment.ToAbbreviation();

            result.Add($"{c.Name}  {alignment}  {classes}  L{GetLevelDisplay(c)}  HP {c.CurrentHitPoints}/{c.MaxHitPoints}  AC {c.ArmorClass}");
        }

        return result;
    }

    private static string GetLevelDisplay(Character c)
    {
        c.EnsureClassProgressions();

        if (c.Classes == null || c.Classes.Count <= 1)
            return c.Level.ToString();

        return string.Join("/", c.Classes.Select(c.GetClassLevel));
    }

    private void ShowPartyPopup(string title)
    {
        var sb = new StringBuilder();
        foreach (var line in GetPartyLines())
            sb.AppendLine(line);

        SayOnBoth(title, sb.ToString());
    }

    private static RectangleF[] BuildFrames(RectangleF viewport, int maxDepth)
    {
        var frames = new RectangleF[maxDepth + 1];
        float cx = viewport.Left + (viewport.Width / 2f);
        float cy = viewport.Top + (viewport.Height / 2f);

        for (int d = 0; d <= maxDepth; d++)
        {
            float u = d / (float)(maxDepth + 1);
            float t = 1f - (float)Math.Pow(1f - u, 1.5f);

            float left = Lerp(viewport.Left + 160f, cx, t);
            float right = Lerp(viewport.Right - 160f, cx, t);
            float top = Lerp(viewport.Top + 100f, cy, t);
            float bottom = Lerp(viewport.Bottom - 100f, cy, t);

            frames[d] = RectangleF.FromLTRB(left, top, right, bottom);
        }

        return frames;
    }

    private bool IsOpen(Point tile)
    {
        if (tile.X < 0 || tile.Y < 0 || tile.X >= _maze.GetLength(0) || tile.Y >= _maze.GetLength(1))
            return false;

        return _maze[tile.X, tile.Y] == CellType.Floor;
    }

    /// <summary>
    /// Where a dialog sends its question so the table can answer it. Handed to the camp screens, which have no
    /// business knowing about snapshots or bridges -- they just need somewhere to put a question.
    /// </summary>
    private void PublishTable(ViewerPrompt? prompt) => PublishToViewer(prompt: prompt);

    /// <summary>
    /// Says something the player must acknowledge, on the keyboard AND on the table, then puts the maze's own
    /// question back up. These were plain MessageBoxes, which a viewer can neither read nor dismiss.
    /// </summary>
    private void SayOnBoth(string title, string text)
    {
        ViewerMessage.Say(this, title, text, PublishTable);
        PublishToViewer();
    }

    /// <summary>
    /// Asks a yes/no question on the keyboard AND on the table.
    ///
    /// Built rather than borrowed from MessageBox because a MessageBox cannot be answered from the viewer at
    /// all: "Stairs up, take them?" is the last thing between a party and the surface, and from the table it
    /// was an unanswerable stop right at the exit.
    /// </summary>
    private DialogResult AskOnBoth(string title, string question)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(820, 190),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = string.IsNullOrWhiteSpace(title) ? "QUESTION" : title.ToUpperInvariant(),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var questionLabel = new Label
        {
            Left = 0,
            Top = 52,
            Width = framePanel.ClientSize.Width,
            Height = 80,
            Text = string.IsNullOrWhiteSpace(question) ? "(Y/N)?" : question.ToUpperInvariant(),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 18f, FontStyle.Bold)
        };

        var optionsLabel = new Label
        {
            Left = 0,
            Top = 132,
            Width = framePanel.ClientSize.Width,
            Height = 34,
            Text = "Y)ES    N)O",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = questionLabel.Font
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Y)
            {
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.N || e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(questionLabel);
        framePanel.Controls.Add(optionsLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", question, null, new[]
        {
            new ViewerPromptOption("yes", "Yes"),
            new ViewerPromptOption("no", "No"),
        });

        var answers = new Dictionary<string, DialogResult>
        {
            ["yes"] = DialogResult.Yes,
            ["no"] = DialogResult.No,
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    private DialogResult AskNeutralEncounterOnBoth(string encounterName)
    {
        using var form = new Form
        {
            Text = "Neutral Encounter",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(780, 160),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = "NEUTRAL ENCOUNTER",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 12,
            Top = 50,
            Width = framePanel.ClientSize.Width - 24,
            Height = 62,
            Text = $"A group of neutral {encounterName} is approaching.{Environment.NewLine}Do you L)eave them alone or A)ttack?",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", $"Neutral {encounterName} approach. Leave alone or attack?", null, new[]
        {
            new ViewerPromptOption("leave", "Leave alone"),
            new ViewerPromptOption("attack", "Attack")
        });

        var answers = new Dictionary<string, DialogResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["leave"] = DialogResult.No,
            ["attack"] = DialogResult.Yes
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    private DialogResult AskFriendlyEncounterOnBoth(string encounterName)
    {
        using var form = new Form
        {
            Text = "Friendly Encounter",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(780, 160),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = "FRIENDLY ENCOUNTER",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 12,
            Top = 50,
            Width = framePanel.ClientSize.Width - 24,
            Height = 62,
            Text = $"A group of friendly {encounterName} is approaching.{Environment.NewLine}Do you L)eave them alone or A)ttack?",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 14f, FontStyle.Bold)
        };

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.Yes;
                form.Close();
            }
            else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.No;
                form.Close();
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);

        var prompt = new ViewerPrompt("choice", $"Friendly {encounterName} approach. Leave alone or attack?", null, new[]
        {
            new ViewerPromptOption("leave", "Leave alone"),
            new ViewerPromptOption("attack", "Attack")
        });

        var answers = new Dictionary<string, DialogResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["leave"] = DialogResult.No,
            ["attack"] = DialogResult.Yes
        };

        var result = ViewerDialog.RunModal(form, this, prompt, answers, PublishTable);
        PublishToViewer();
        return result;
    }

    private static bool PartyHasNoEvilCharacters(List<Character> party)
    {
        return party.All(c => c.Alignment != Alignment.LawfulEvil
                              && c.Alignment != Alignment.NeutralEvil
                              && c.Alignment != Alignment.ChaoticEvil);
    }

    private enum EncounterMoralAxis
    {
        Good,
        Neutral,
        Evil
    }

    private static EncounterMoralAxis? GetEncounterMoralAxis(Monster? monster)
    {
        var alignment = monster?.Alignment ?? string.Empty;
        if (string.IsNullOrWhiteSpace(alignment))
            return null;

        if (alignment.IndexOf("good", StringComparison.OrdinalIgnoreCase) >= 0)
            return EncounterMoralAxis.Good;

        if (alignment.IndexOf("evil", StringComparison.OrdinalIgnoreCase) >= 0)
            return EncounterMoralAxis.Evil;

        if (alignment.IndexOf("neutral", StringComparison.OrdinalIgnoreCase) >= 0)
            return EncounterMoralAxis.Neutral;

        return null;
    }

    private bool AreAllEncounterGroupsGood(IEnumerable<string> groupMonsterNames)
    {
        var allMonsters = _monsterRepository.GetAll().ToList();
        var foundAny = false;

        foreach (var name in groupMonsterNames)
        {
            var template = FindMonsterByName(allMonsters, name);
            if (template == null)
                return false;

            foundAny = true;
            if (string.IsNullOrWhiteSpace(template.Alignment)
                || template.Alignment.IndexOf("good", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return foundAny;
    }

    private bool ShouldOfferFriendlyEncounterChoice(List<Character> party, IEnumerable<string> groupMonsterNames)
    {
        return PartyHasNoEvilCharacters(party)
               && AreAllEncounterGroupsGood(groupMonsterNames);
    }

    private void ApplyGoodEncounterAttackConsequences(List<Character> party, string encounterName)
    {
        var goodMembers = party.Where(c => c.IsGoodAlignment()).ToList();
        if (goodMembers.Count == 0)
            return;

        var warningCandidates = goodMembers.Where(c => c.GoodEncounterAttackWarningCount < 2).ToList();
        if (warningCandidates.Count > 0)
        {
            foreach (var member in warningCandidates)
            {
                member.GoodEncounterAttackWarningCount += 1;
                _characterRepository.Save(member);
            }

            var warningNumber = warningCandidates.Max(c => c.GoodEncounterAttackWarningCount);
            var text = warningNumber == 1
                ? "1st warning"
                : "2nd and last warning";

            SayOnBoth("Alignment Warning", $"Attacking good {encounterName} gives good characters a {text}.");
            return;
        }

        var changed = new List<string>();
        foreach (var member in goodMembers)
        {
            var newAlignment = _random.Next(0, 2) == 0 ? Alignment.ChaoticNeutral : Alignment.NeutralEvil;
            member.Alignment = newAlignment;
            member.ApplyAlignmentBasedClassConsequences();
            member.GoodEncounterAttackWarningCount = 3;

            member.RefreshMoveFromArmorAndClass();
            member.RefreshMonkProgressionStats();

            _characterRepository.Save(member);
            changed.Add($"{member.Name} -> {newAlignment.ToAbbreviation()}");
        }

        if (changed.Count > 0)
            SayOnBoth("Alignment Shift", "Good characters attacked good encounters again and changed alignment: " + string.Join(", ", changed));
    }

    private bool AreAllEncounterGroupsNeutral(IEnumerable<string> groupMonsterNames)
    {
        var allMonsters = _monsterRepository.GetAll().ToList();
        var foundAny = false;

        foreach (var name in groupMonsterNames)
        {
            var template = FindMonsterByName(allMonsters, name);
            if (template == null)
                return false;

            foundAny = true;
            var alignment = template.Alignment ?? string.Empty;
            var hasNeutral = alignment.IndexOf("neutral", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasGood = alignment.IndexOf("good", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasEvil = alignment.IndexOf("evil", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasNeutral || hasGood || hasEvil)
                return false;
        }

        return foundAny;
    }

    /// <summary>
    /// Hand the current state to the tabletop viewer. Safe to call as often as we like: the
    /// viewer takes a whole snapshot each time and works out the difference itself.
    /// </summary>
    /// <param name="prompt">
    /// A question to show INSTEAD of the maze's own -- a camp menu, a message, a numbered list. The snapshot is
    /// still the maze, so the table keeps showing where the party is standing while it asks.
    /// </param>
    private void PublishToViewer(IReadOnlyList<TabletopViewerBridge.MonsterGroupView>? groups = null,
                                 ViewerPrompt? prompt = null)
    {
        if (_maze is null)
            return;

        var heading = _direction switch
        {
            Direction.North => "North",
            Direction.East => "East",
            Direction.South => "South",
            _ => "West"
        };

        _viewer.Publish(
            _currentDungeonLevel,
            (x, y) => x >= 0 && x < _maze.GetLength(0)
                   && y >= 0 && y < _maze.GetLength(1)
                   && _maze[x, y] == CellType.Floor,
            _maze.GetLength(0),
            _maze.GetLength(1),
            _position.X,
            _position.Y,
            heading,
            GetViewerParty(),
            groups,
            // A fight states its own choice, per character; the maze's is the party's.
            prompt: prompt ?? (groups is null ? MazePrompt() : null));
    }

    /// <summary>
    /// What the viewer may offer right now. Legality comes from the same predicates the keyboard path
    /// uses -- <see cref="IsOpen"/> for the wall ahead, the overlay flag for the overlay-only choices
    /// -- so a button can never be drawn for something the game would then refuse. Walking into a
    /// wall is a no-op from the keyboard; from a table it should simply not be offered.
    /// </summary>
    private ViewerPrompt MazePrompt()
    {
        var options = new List<ViewerPromptOption>();

        if (CanStepForward())
            options.Add(new ViewerPromptOption("forward", "Forward"));

        options.Add(new ViewerPromptOption("turnLeft", "Turn left"));
        options.Add(new ViewerPromptOption("turnRight", "Turn right"));
        options.Add(new ViewerPromptOption("party", _partyOverlayVisible ? "Hide party" : "Show party"));
        options.Add(new ViewerPromptOption("search", "Search"));

        if (_partyOverlayVisible)
        {
            options.Add(new ViewerPromptOption("camp", "Camp"));
            options.Add(new ViewerPromptOption("status", "Party status"));
            options.Add(new ViewerPromptOption("inspect", "Inspect/Recover"));
        }

        return new ViewerPrompt("maze", "The party waits in the maze.", For: null, options);
    }

    private bool IsSpaciousEncounterArea()
    {
        if (_maze == null)
            return false;

        for (var y = _position.Y - 1; y <= _position.Y + 1; y++)
        for (var x = _position.X - 1; x <= _position.X + 1; x++)
        {
            if (x < 0 || y < 0 || x >= _maze.GetLength(0) || y >= _maze.GetLength(1)
                || _maze[x, y] != CellType.Floor)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Publish a combat round. Party comes from the session rather than the roster on disk, because
    /// mid-fight hit points only exist in memory until the encounter ends.
    /// </summary>
    private void PublishCombatToViewer(
        CombatSession session,
        IReadOnlyDictionary<string, CombatAction>? actions = null,
        ViewerPrompt? prompt = null)
    {
        if (_maze is null || session is null)
            return;

        var heading = _direction switch
        {
            Direction.North => "North",
            Direction.East => "East",
            Direction.South => "South",
            _ => "West"
        };

        _viewer.PublishCombat(
            _currentDungeonLevel,
            (x, y) => x >= 0 && x < _maze.GetLength(0)
                   && y >= 0 && y < _maze.GetLength(1)
                   && _maze[x, y] == CellType.Floor,
            _maze.GetLength(0),
            _maze.GetLength(1),
            _position.X,
            _position.Y,
            heading,
            session.Party,
            session.Monsters,
            session.RoundNumber,
            actions,
            prompt);
    }

    /// <summary>The party in marching order, skipping names the roster no longer knows.</summary>
    private List<Character> GetViewerParty()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAllCached().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<Character>();
        foreach (var memberName in party.Members)
        {
            if (roster.TryGetValue(memberName, out var c))
                ordered.Add(c);
        }

        return ordered;
    }

    private void LeaveDungeonForTown()
    {
        RestoreStrengthDrainedOnDungeonExit();
        RevertLycanthropeTransformationsOnDungeonExit();
        ApplyPartyAgingForDungeonExitDay();
        ApplyDiseaseProgressForDungeonExitDay();
        Close();
    }

    private enum StairsKind { None, Up, Down }

    // Stairs always go up on one floor and down on the other, and land on the matching stairs.
    private static readonly Dictionary<int, (Point Up, Point? Down)> DungeonStairs = new()
    {
        [1] = (new Point(0, 0), new Point(10, 0)),
        [2] = (new Point(7, 12), new Point(15, 16)),
        [3] = (new Point(11, 15), new Point(8, 1)),
        [4] = (new Point(18, 10), new Point(7, 17)),
        [5] = (new Point(0, 0), new Point(8, 8)),
        [6] = (new Point(0, 0), new Point(16, 8)),
        [7] = (new Point(10, 10), new Point(9, 18)),
        [8] = (new Point(0, 0), null)
    };

    // One-way teleports (the X -> X' marks on the maps), keyed by level and square.
    private static readonly Dictionary<(int Level, Point From), Point> DungeonWarps = new()
    {
        [(1, new Point(9, 5))] = new Point(4, 15),
        [(1, new Point(4, 13))] = new Point(4, 15),
        [(6, new Point(2, 19))] = new Point(19, 2),
        [(6, new Point(11, 14))] = new Point(0, 0),
        [(7, new Point(12, 7))] = new Point(8, 2),
        [(7, new Point(9, 9))] = new Point(2, 18),
        [(8, new Point(0, 6))] = new Point(5, 5),
        [(8, new Point(5, 11))] = new Point(18, 3),
        [(8, new Point(15, 11))] = new Point(19, 14),
        [(10, new Point(19, 1))] = new Point(0, 0),
        [(10, new Point(8, 2))] = new Point(0, 0),
        [(10, new Point(12, 8))] = new Point(0, 0),
        [(10, new Point(18, 11))] = new Point(0, 0),
        [(10, new Point(10, 17))] = new Point(0, 0),
        [(10, new Point(12, 18))] = new Point(0, 0),
        [(10, new Point(0, 10))] = new Point(14, 2),
        [(10, new Point(19, 0))] = new Point(19, 4),
        [(10, new Point(19, 10))] = new Point(17, 14),
        [(10, new Point(11, 10))] = new Point(10, 5),
        [(10, new Point(13, 19))] = new Point(9, 18),
        [(10, new Point(11, 14))] = new Point(5, 12)
    };

    // Pits (the P marks on the maps), keyed by level.
    private static readonly Dictionary<int, Point[]> DungeonPits = new()
    {
        [2] = [new Point(11, 12)],
        [3] = [new Point(19, 5), new Point(19, 15), new Point(11, 0), new Point(11, 5), new Point(7, 0)],
        [6] = [new Point(16, 6), new Point(16, 7)],
        [7] = [new Point(6, 9), new Point(6, 10), new Point(5, 9), new Point(5, 10), new Point(4, 9), new Point(4, 10)]
    };

    // Turn tables (the T marks on the maps), keyed by level.
    private static readonly Dictionary<int, Point[]> DungeonTurnTables = new()
    {
        [3] = [new Point(19, 10), new Point(3, 15), new Point(3, 0), new Point(19, 0)],
        [5] = [new Point(5, 5)],
        [8] = DanceHallSquares()
    };

    // The Dance Hall on Level 8: rotating floors at E0-E7 on N15-N19 and N0-N1, except the warp at E6-N0.
    private static Point[] DanceHallSquares()
        => Enumerable.Range(0, 8)
            .SelectMany(y => new[] { 0, 1, 15, 16, 17, 18, 19 }.Select(x => new Point(x, y)))
            .Where(p => p != new Point(0, 6))
            .ToArray();

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    private static IEnumerable<KeyValuePair<(int Level, Point Cell), string>> Mosaics(string text, params (int X, int Y)[] cells)
        => cells.Select(c => KeyValuePair.Create((3, new Point(c.X, c.Y)), $"THE FLOOR HAS A MOSAIC READING: \"{text}\""));

    // Messages shown each time the party steps on a square (the M marks on the maps), keyed by level and square.
    private static readonly Dictionary<(int Level, Point Cell), string> DungeonMessages = new[]
    {
        Mosaics("TURN AROUND.", (3, 5), (15, 10), (15, 15)),
        Mosaics("TURN LEFT.", (15, 0), (7, 10), (7, 15)),
        Mosaics("TURN RIGHT.", (7, 5), (15, 5), (3, 10)),
        new Dictionary<(int Level, Point Cell), string>
        {
        [(1, new Point(19, 9))] = Lines("\"MAPIRO MAHAMA DIROMAT!!\"", "A STRANGE GLOW SEEMS TO EMANATE FROM THIS ROOM.",
            "IN THE CENTER, A SMALLISH MAN IN A LONG ROBE TURNS TOWARDS THE PARTY AND SHOUTS",
            "\"BEGONE, STRANGERS!\" HE SLOWLY WAVES HIS HANDS, AND CHANTS", "\"MAPIRO MAHAMA DIROMAT!!\""),
        [(1, new Point(5, 13))] = Lines("THERE IS A GEM ENCRUSTED STATUE OF A HOODED MAN HERE.", "A GOLDEN LIGHT EMANATES FROM THE HOOD.",
            "IN FRONT OF THE STATUE IS AN ALTAR, IN WHICH PUNGENT INCENSE IS BURNING."),
        [(4, new Point(14, 10))] = Lines("A SIGN ON THE DOOR READS:", "*** TESTING GROUNDS CONTROL CENTER ***", "AUTHORIZED PERSONNEL ONLY!", "*** DO NOT ENTER ***"),
        [(4, new Point(15, 10))] = Lines("SUDDENLY, A LOUD ALARM BELL CAN BE HEARD.", "THEN THE BELLS STOP, AND ARE REPLACED BY THE SOUNDS OF GUARDIAN MONSTERS!"),
        [(4, new Point(15, 8))] = "A SIGN ON THE DOOR READS: \"TREASURE REPOSITORY.\"",
        [(4, new Point(15, 12))] = "A SIGN ON THE DOOR READ: \"MONSTER ALLOCATION CENTER.\"",
        [(10, new Point(0, 0))] = Lines("INSCRIBED ON AN ORNATE, GOLD PLAQUE IS A MESSAGE.", "THE SCRIPT IS ORNATE, AND BLINKS IN VARIOUS COLOR:",
            "\"YOU ARE TRESPASSING ON THE DOMAIN OF ** WERDNA **.", "THERE IS NO POSSIBILITY THAT YOU CAN GET PAST MY GUARDIANS!",
            "SO SURE AM I OF MY DEFENSES THAT I GIVE YOU THIS CLUE:\"", "\"CONTRA-DEXTRA AVENUE\"", "PS - TREBOR SUX!"),
        [(10, new Point(14, 3))] = Lines("ONE GROUP OF GUARDIANS HAVE BEEN DEFEATED, BUT THERE ARE MANY MORE.", "TURN BACK WHILE YOU CAN, FOOLS!"),
        [(10, new Point(4, 17))] = Lines("A DESCREET SIGN ON THE WALL READS:", "LAIR OF THE EVIL WIZARD WERADNA! OFFICE",
            "HOURS 9AM TO 3PM BY APPOINTMENT ONLY", "THE WIZARD IS * IN *!"),
        [(4, new Point(11, 11))] = Lines("IN THIS ROOM, THERE IS A LARGE DESK.",
            "ON IT ARE THE REMAINS OF CRYSTAL BALLS AND OTHER MAGICAL ARTIFACTS ALL NOW BROKEN.",
            "AS YOU ENTERED THE ROOM, A PANEL ON THE DOOR SLAMMED SHUT.", "THEN IT GLOWED A PALE BLUE. NO ONE CAN PRY IT OPEN.",
            "NEXT, THE DOOR ON THE OTHER SIDE OF THE ROOM STARTS TO GLOW BRIGHT ORANGE, INVITING THE PARTY TO USE IT."),
        [(4, new Point(2, 10))] = Lines("A SIGN ON THE DOOR READS:", "PRIVATE EXPRESS SERVICE ELEVATOR!", "AUTHORIZED USERS ONLY!"),
        [(2, new Point(18, 9))] = Lines("YOU SEE A STATUE OF A BEAR ON A PEDESTAL.", "ON THE WALL IS A SIGN READING \"I'VE GOT A MILLION OF'EM\"."),
        [(2, new Point(4, 12))] = Lines("ON A SILVERY DISK STANDS A STATUE OF A FROG WEARING A RED AND BLUE CAPE.",
            "THE STATUE ANIMATES AND SHAKES ITS LEGS WHILE IT YELLS \"YEAH .. YEAH ..\""),
        [(2, new Point(11, 15))] = "A PLACARD NEAR THE GROUND READ: \"A DUNGEON'S DARK ...\"",
        [(2, new Point(11, 14))] = "A PLACARD NEAR THE GROUND READ: \"WHEN IT'S NOT LIT ...\"",
        [(2, new Point(11, 13))] = "A PLACARD NEAR THE GROUND READ: \"WATCH OUT. OR YOU'LL ..\""
        }
    }.SelectMany(m => m).ToDictionary(p => p.Key, p => p.Value);

    // Statues the party picks up by stepping on the square, keyed by level and square.
    private static readonly Dictionary<(int Level, Point Cell), string> DungeonItemPickups = new()
    {
        [(2, new Point(18, 9))] = "Statue of Bear",
        [(2, new Point(4, 12))] = "Statue of Frog"
    };

    // Statues the party can look around at (the prompt on the square, or pressing S there) to find a key.
    private static readonly Dictionary<(int Level, Point Cell), (string Item, string Message)> DungeonLookAroundItems = new()
    {
        [(1, new Point(3, 13))] = ("Key of Bronze", Lines("IN THIS ROOM IS A STATUE OF A MONSTER WITH THE BODY OF A CHICKEN AND THE HEAD OF A CAT.",
            "THE STATUE IS MADE OF BRONZE, AND LIES ON AN ONYX PEDESTAL.",
            "THERE ARE UNUSUAL RUNES ON A PLAQUE THEREON.")),
        [(1, new Point(18, 13))] = ("Key of Silver", Lines("IN THIS ROOM IS A SILVER STATUE OF A BOAR. WITH HORNS AND LONG FANGS.",
            "ON THE WALL BY THE STATUE IS A MESSAGE (PARTIALLY OBSCURED)",
            "THAT APPEARS TO HAVE BEEN LEFT BY PASSING ELVES.",
            "IT IS HARDLY LEGIBLE, BUT SOME COMMENTS WARNING ABOUT GHOSTS AND DEMONS CAN STILL BE MADE OUT.")),
        [(2, new Point(4, 16))] = ("Key of Gold", Lines("IN THIS ROOM IS A STATUE OF A MONSTER WITH THE BODY OF A CHICKEN AND THE HEAD OF A CAT.",
            "THE STATUE IS MADE OF BRONZE, AND LIES ON AN ONYX PEDESTAL.",
            "THERE ARE UNUSUAL RUNES ON A PLAQUE THEREON."))
    };

    // Squares the party is pushed back from unless someone carries the named item.
    private static readonly Dictionary<(int Level, Point Cell), (string Item, string Message)> DungeonRestrictedSquares = new()
    {
        [(2, new Point(12, 8))] = ("Key of Silver", Lines("AS YOU ENTER THIS ROOM, A SILVERY FOG ENVELOPS YOU.", "TERRIBLE DEMONS FORM ALL AROUND YOU.", "IN TERROR, YOU FLEE FROM THE ROOM!")),
        [(2, new Point(7, 8))] = ("Key of Bronze", Lines("AS YOU ENTER THIS ROOM, A BRONZE SMOKE FILLS IT.", "YOU FEEL COMPELLED TO LEAVE IMMEDIATELY!")),
        [(2, new Point(11, 4))] = ("Statue of Bear", "NOBODY IN THE PARTY CAN BREAK THE DOOR DOWN."),
        [(2, new Point(12, 4))] = ("Statue of Frog", "NOBODY IN THE PARTY CAN BREAK THE DOOR DOWN.")
    };

    private static readonly Point Level1WizardRoom = new(19, 9);
    private static readonly Point Level1MurphyShrine = new(5, 13);

    private const string BlueRibbon = "Blue Ribbon";
    private static readonly Point Level4RibbonRoom = new(10, 11);
    private static readonly Point Level4SleepRoom = new(1, 10);
    private static readonly Point Level4SleepRoomOutside = new(2, 10);

    // Square messages plus the level 4 scripted squares. Returns true when the square forces an encounter.
    private bool ProcessSquareMessageStep()
    {
        if (!_thinWalls)
            return false;

        if (DungeonMessages.TryGetValue((_currentDungeonLevel, _position), out var message))
            SayOnBoth("Message", message);

        if (DungeonItemPickups.TryGetValue((_currentDungeonLevel, _position), out var pickup) && !PartyHasItem(pickup))
            FindLookAroundItem(pickup);

        if (DungeonLookAroundItems.TryGetValue((_currentDungeonLevel, _position), out var lookAround) && !PartyHasItem(lookAround.Item))
        {
            SayOnBoth("Message", lookAround.Message);
            if (AskOnBoth("Look around", "Look around?") == DialogResult.Yes)
                FindLookAroundItem(lookAround.Item);
        }

        if (_currentDungeonLevel != 4)
            return false;

        if (_position == Level4RibbonRoom && !PartyHasItem(BlueRibbon))
        {
            SayOnBoth("Message", Lines("AS THE PARTY ENTERS THE ROOM, THE DOOR SLAMS SHUT, GLOWS ORANGE AND VANISHES!",
                "A DOOR APPEARS TO THE RIGHT, A VOICE, COMING FROM THE CEILING ADDRESSES YOU:",
                "\"CONGRATULATIONS, BRAVE ADVENTURERS!",
                "TODAY YOU HAVE PROVED YOURSELF TO ME AND ARE NOW READY TO START YOUR QUEST!",
                "YEARS AGO, MY AMULET WAS STOLEN BY THE EVIL WIZARD WERDNA.",
                "WERDNA HIDES IN THE MAZE SOMEWHERE BELOW US.",
                "FIND HIM, SLAY HIM AND RETURN TO ME THE AMULET!",
                "TO AID YOU IN YOUR QUEST, I WILL GIVE YOU A BLUE RIBBON,",
                "WHICH MAY BE USED TO ENTER THE ELEVATOR ON THIS FLOOR.",
                "GO NOW - AND MAY THE GODS GUIDE YOU!\""));
            var ribbonHolder = GivePartyItem(BlueRibbon, "Given by Trebor. Makes the elevator on this floor accessible.");
            if (ribbonHolder is not null)
                SayOnBoth("Blue Ribbon", $"{ribbonHolder.ToUpperInvariant()} RECEIVES THE BLUE RIBBON!");
        }
        else if (_position == Level4SleepRoom && !PartyHasItem(BlueRibbon))
        {
            SayOnBoth("Message", Lines("YOU ALL FALL ASLEEP! LATER, EVERYONE WAKES UP, FEELING SICK AND WEAK.",
                "YOU ARE BACK OUTSIDE THE DOOR YOU TRIED TO ENTER."));
            _position = Level4SleepRoomOutside;
            UpdateLighting();
            Invalidate();
        }

        return _position == new Point(15, 10);
    }

    private string? GivePartyItem(string name, string description)
    {
        var receiver = GetViewerParty().FirstOrDefault();
        if (receiver is null)
            return null;

        var item = new Adnd.Data.Items.ItemRepository("Data/Items").LoadAll()
            .FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? new Adnd.Core.Items.Item
            {
                Name = name,
                Type = Adnd.Core.Items.ItemType.Misc,
                Weight = 0,
                IsShopBuyable = false,
                Description = description
            };
        receiver.Inventory.Add(item);
        _characterRepository.Save(receiver);
        return receiver.Name;
    }

    // Doors that only open for a party carrying a named item, keyed by level, square and the side crossed.
    private static readonly Dictionary<(int Level, Point From, Direction Side), (string Item, string Message)> DungeonGatedDoors = new()
    {
        [(4, new Point(13, 17), Direction.West)] = ("Statue of Bear",
            "A LARGE SLIDING WALL WITH THE IMAGE OF A BEAR UPON IT BLOCKS YOUR PATH AND FORCES YOU OUT OF THE ROOM." + Environment.NewLine
            + "POSSESSION OF THE STATUE OF BEAR IS REQUIRED TO PASS THROUGH THIS SECTION.")
    };

    private bool PartyHasItem(string itemName)
        => GetViewerParty().Any(c => c.Inventory.Any(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase)));

    private void FindLookAroundItem(string itemName)
    {
        var finder = GivePartyItem(itemName, $"Found on level {_currentDungeonLevel}.");
        SayOnBoth("Key", finder is null
            ? $"THE PARTY FINDS THE {itemName.ToUpperInvariant()}!"
            : $"{finder.ToUpperInvariant()} FINDS THE {itemName.ToUpperInvariant()}!");
    }

    // Level 1 scripted squares: the wizard who throws the party out, and Murphy's shrine with its ghosts.
    // Returns true when the party was sent back to town.
    private bool ProcessLevelOneScriptStep()
    {
        if (!_thinWalls || _currentDungeonLevel != 1)
            return false;

        if (_position == Level1WizardRoom)
        {
            LeaveDungeonForTown();
            return true;
        }

        if (_position == Level1MurphyShrine && LoadEncounterParty().Count > 0)
        {
            _combatCoordinator.ForceMonstersInLair = true;
            _combatCoordinator.CannotRun = true;
            try
            {
                var outcome = _combatCoordinator.StartEncounter(
                    this,
                    "Murphy's Ghost",
                    ResolveEncounterGroupCount("Murphy's Ghost", null),
                    LoadEncounterParty(),
                    _characterRepository,
                    _currentDungeonLevel,
                    dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
                if (outcome == CombatOutcome.Defeat)
                    HandlePartyDefeatAtCurrentCell();
            }
            finally
            {
                _combatCoordinator.ForceMonstersInLair = false;
                _combatCoordinator.CannotRun = false;
            }

            PublishToViewer();
        }

        return false;
    }

    // Level 1's up stairs lead out to town; the others are on a floor that has a neighbour built.
    private StairsKind StairsAt(Point cell)
    {
        if (_currentDungeonLevel == 1 && cell.X == 0 && cell.Y == 0)
            return StairsKind.Up;

        if (!_thinWalls || !DungeonStairs.TryGetValue(_currentDungeonLevel, out var stairs))
            return StairsKind.None;

        // The Dance Hall arrival on Level 8 has no way back up.
        if (cell == stairs.Up)
            return _currentDungeonLevel == 8 ? StairsKind.None : StairsKind.Up;

        return stairs.Down is { } down && cell == down && DungeonStairs.ContainsKey(_currentDungeonLevel + 1)
            ? StairsKind.Down
            : StairsKind.None;
    }

    // Takes the stairs to the neighbouring floor and lands on the stairs there.
    private void TakeStairsToLevel(int level)
    {
        var arrivedGoingDown = level > _currentDungeonLevel;
        _currentDungeonLevel = level;
        BuildMazeForLevel(level);
        LoadEncounterLocationsForLevel(level);
        _position = arrivedGoingDown ? DungeonStairs[level].Up : DungeonStairs[level].Down!.Value;
        UpdateLighting();
        Invalidate();
        PublishToViewer();
    }

    private void TryMoveForward()
    {
        var v = GetForwardVector(_direction);
        var candidate = WrapCell(new Point(_position.X + v.X, _position.Y + v.Y));
        if (!CanStepForward())
        {
            ShowCenteredMessage("YOU CAN'T WALK THROUGH WALLS!");
            return;
        }

        if (_thinWalls && DungeonRestrictedSquares.TryGetValue((_currentDungeonLevel, candidate), out var restriction)
            && !PartyHasItem(restriction.Item))
        {
            SayOnBoth("Message", restriction.Message);
            return;
        }

        if (_thinWalls && DungeonGatedDoors.TryGetValue((_currentDungeonLevel, _position, _direction), out var gate)
            && !PartyHasItem(gate.Item))
        {
            SayOnBoth("Sliding Wall", gate.Message);
            return;
        }

        _position = candidate;
        ApplyPoisonDamageForStep();

        if (_position == ElevatorPosition)
        {
            if (_thinWalls && _currentDungeonLevel is 8 or 9 && !PartyHasItem("Blue Ribbon"))
                SayOnBoth("Elevator", "THE ELEVATOR WON'T MOVE WITHOUT THE BLUE RIBBON.");
            else
                ShowDungeonElevatorDialog();
        }

        if (_thinWalls && _currentDungeonLevel == 1 && _position.X == 12 && _position.Y == 9)
            SayOnBoth("Sign", "A LARGE SIGN ON THE WALL READ:" + Environment.NewLine + "** AREA OUT OF BOUNDS! **");

        ProcessTreasureFindingStep();
        ProcessLightStep();

        if (_thinWalls && DungeonWarps.TryGetValue((_currentDungeonLevel, _position), out var warpTo))
        {
            _position = warpTo;
            UpdateLighting();
            Invalidate();
            SayOnBoth("Teleport", "A strange force teleports the party to another place!");
        }

        if (ProcessLevelNineStep())
            return;

        ProcessPitStep();
        ProcessTurnTableStep();
        var alarm = ProcessSquareMessageStep();
        if (ProcessLevelOneScriptStep())
            return;
        if (_currentDungeonLevel == 1
            && _position.X == 0
            && _position.Y == 0
            && !HasRecoverableOutCharacterAtCurrentPosition())
        {
            var result = AskOnBoth("Stairs", "Stairs up, take them?");

            if (result == DialogResult.Yes)
            {
                LeaveDungeonForTown();
                return;
            }
        }

        if (_thinWalls && StairsAt(_position) is var kind && kind != StairsKind.None
            && !(kind == StairsKind.Up && _currentDungeonLevel == 1))
        {
            var target = _currentDungeonLevel + (kind == StairsKind.Down ? 1 : -1);
            var answer = AskOnBoth("Stairs", kind == StairsKind.Down ? "Stairs down, take them?" : "Stairs up, take them?");
            if (answer == DialogResult.Yes)
            {
                TakeStairsToLevel(target);
                return;
            }
        }

        if (alarm)
        {
            TryRandomEncounter(chanceOverride: 1d, forceInLair: true);
        }
        else if (TryEncounterAtFixedLocation())
        {
            // The square's own encounter replaces the usual random check for this step.
        }
        else
        {
            TryRandomEncounter();
        }

        var delayMs = Math.Max(0, GameRulesProvider.Current.DelayInMsbetweenActions);
        if (delayMs > 0)
            Thread.Sleep(delayMs);
    }

    // Flashes a short message in the middle of the view, then clears it.
    private void ShowCenteredMessage(string text)
    {
        _centerMessage = text;
        Refresh();
        Thread.Sleep(600);
        _centerMessage = null;
        Invalidate();
    }

    private void LoadLightFromParty()
    {
        var party = _partyRepository.Load();
        _lightStepsRemaining = Math.Max(0, party.LightStepsRemaining);
        _continualLightActive = party.ContinualLightActive;
        UpdateLighting();
    }

    // Squares where all magic is sealed (X = north, Y = east), keyed by level: level 5's Hall of Silence.
    private static readonly Dictionary<int, (int X1, int Y1, int X2, int Y2)> DungeonMagicSealedAreas = new()
    {
        [5] = (7, 10, 12, 16)
    };

    private void UpdateMagicSeal()
    {
        var sealedNow = _thinWalls
            && DungeonMagicSealedAreas.TryGetValue(_currentDungeonLevel, out var area)
            && _position.X >= area.X1 && _position.X <= area.X2
            && _position.Y >= area.Y1 && _position.Y <= area.Y2;

        if (sealedNow == Adnd.Core.Spells.Casting.SpellCastingService.MagicSealed)
            return;

        Adnd.Core.Spells.Casting.SpellCastingService.MagicSealed = sealedNow;
        if (!sealedNow)
            return;

        // Magic cast before entering is cancelled as well.
        var hadLight = _lightStepsRemaining > 0 || _continualLightActive;
        _lightStepsRemaining = 0;
        _continualLightActive = false;
        var party = _partyRepository.Load();
        party.LightStepsRemaining = 0;
        party.ContinualLightActive = false;
        _partyRepository.Save(party);

        SayOnBoth("Hall of Silence", "MAGIC IS SEALED IN THIS SPACE." + Environment.NewLine
            + (hadLight ? "ALL MAGIC CAST BEFORE ENTERING IS CANCELED." : "NO SPELL CAN BE CAST HERE."));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Adnd.Core.Spells.Casting.SpellCastingService.MagicSealed = false;
        base.OnFormClosed(e);
    }

    private void UpdateLighting()
    {
        UpdateMagicSeal();
        _inMagicalDarkness = _darkAreas.Any(a => a.Contains(_position.X, _position.Y));
        _depthLevels = _inMagicalDarkness ? DarkDepthLevels
            : _continualLightActive ? ContinualLightDepthLevels
            : _lightStepsRemaining > 0 ? LightDepthLevels
            : BaseDepthLevels;
        Invalidate();
    }

    // Each step uses up one step of Light. Magical darkness snuffs out Continual Light for good.
    private void ProcessLightStep()
    {
        var enteredDarkness = _continualLightActive && IsDark(_position);
        if (!enteredDarkness && _lightStepsRemaining <= 0)
        {
            UpdateLighting();
            return;
        }

        string? message = null;
        var party = _partyRepository.Load();
        var changed = false;

        if (enteredDarkness)
        {
            _continualLightActive = false;
            party.ContinualLightActive = false;
            changed = true;
            message = "The magical darkness snuffs out the Continual Light.";
        }

        if (_lightStepsRemaining > 0)
        {
            _lightStepsRemaining--;
            party.LightStepsRemaining = _lightStepsRemaining;
            changed = true;
            if (_lightStepsRemaining == 0 && message is null && !_continualLightActive)
                message = "The magical light fades.";
        }

        if (changed)
            _partyRepository.Save(party);

        UpdateLighting();

        if (message is not null)
            SayOnBoth("Light", message);
    }

    private void ProcessTreasureFindingStep()
    {
        var party = _partyRepository.Load();
        if (!party.TreasureFindingActive)
            return;

        if (party.TreasureFindingStepsRemaining <= 0)
        {
            party.TreasureFindingActive = false;
            party.TreasureFindingStepsRemaining = 0;
            _partyRepository.Save(party);
            SayOnBoth("Treasure Finding", "The potion's sensing magic fades.");
            return;
        }

        EnsureTreasureFindingTarget(party);

        if (party.TreasureFindingTargetLevel == _currentDungeonLevel
            && party.TreasureFindingTargetX == _position.X
            && party.TreasureFindingTargetY == _position.Y)
        {
            AwardTreasureFindingCacheToParty(party);
            return;
        }

        var direction = GetTreasureFindingDirectionHint(
            _position,
            _currentDungeonLevel,
            new Point(party.TreasureFindingTargetX, party.TreasureFindingTargetY),
            party.TreasureFindingTargetLevel);

        party.TreasureFindingStepsRemaining = Math.Max(0, party.TreasureFindingStepsRemaining - 1);
        var stepsLeft = party.TreasureFindingStepsRemaining;

        if (stepsLeft == 0)
        {
            party.TreasureFindingActive = false;
            _partyRepository.Save(party);
            SayOnBoth("Treasure Finding", "The magical trail fades before the treasure is found.");
            return;
        }

        _partyRepository.Save(party);
        SayOnBoth("Treasure Finding", $"The treasure lies to the {direction}. ({stepsLeft} step(s) remaining)");
    }

    private void EnsureTreasureFindingTarget(Party party)
    {
        if (party.TreasureFindingTargetLevel > 0
            && party.TreasureFindingTargetX >= 0
            && party.TreasureFindingTargetY >= 0)
            return;

        var candidateLevels = Enumerable
            .Range(MinDungeonLevel, MaxDungeonLevel - MinDungeonLevel + 1)
            .OrderBy(_ => _random.Next())
            .ToList();

        foreach (var level in candidateLevels)
        {
            var candidates = GetOpenTiles(level)
                .Where(p => level != _currentDungeonLevel || p.X != _position.X || p.Y != _position.Y)
                .ToList();

            if (candidates.Count == 0)
                candidates = GetOpenTiles(level);

            if (candidates.Count == 0)
                continue;

            var target = candidates[_random.Next(candidates.Count)];
            party.TreasureFindingTargetLevel = level;
            party.TreasureFindingTargetX = target.X;
            party.TreasureFindingTargetY = target.Y;

            RuleApplicationInfo.Publish($"Potion of Treasure Finding cache placed on cleared square at L{party.TreasureFindingTargetLevel} ({party.TreasureFindingTargetX},{party.TreasureFindingTargetY}).");
            return;
        }

        // Fallback: keep effect active but with no target if no valid tile was found.
        party.TreasureFindingTargetLevel = 0;
        party.TreasureFindingTargetX = -1;
        party.TreasureFindingTargetY = -1;
        RuleApplicationInfo.Publish("Potion of Treasure Finding could not place cache: no cleared square found.");
    }

    private List<Point> GetOpenTiles(int level)
    {
        var previousMaze = _maze;
        var previousWalls = _walls;
        BuildMazeForLevel(level);

        var open = new List<Point>();
        for (var y = 0; y < _maze.GetLength(1); y++)
        {
            for (var x = 0; x < _maze.GetLength(0); x++)
            {
                if (_maze[x, y] == CellType.Floor)
                    open.Add(new Point(x, y));
            }
        }

        _maze = previousMaze;
        _walls = previousWalls;
        return open;
    }

    private static string GetTreasureFindingDirectionHint(Point partyPos, int partyLevel, Point targetPos, int targetLevel)
    {
        var horizontal = "none";
        var vertical = "none";
        var level = "same level";

        var dx = targetPos.X - partyPos.X;
        var dy = targetPos.Y - partyPos.Y;

        if (dx > 0)
            horizontal = "east";
        else if (dx < 0)
            horizontal = "west";

        if (dy > 0)
            vertical = "south";
        else if (dy < 0)
            vertical = "north";

        if (targetLevel > partyLevel)
            level = "up";
        else if (targetLevel < partyLevel)
            level = "down";

        return $"horizontal {horizontal}, vertical {vertical}, level {level}";
    }

    private void AwardTreasureFindingCacheToParty(Party party)
    {
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var members = party.Members
            .Where(name => roster.ContainsKey(name))
            .Select(name => roster[name])
            .ToList();

        if (members.Count == 0)
        {
            party.TreasureFindingActive = false;
            party.TreasureFindingStepsRemaining = 0;
            _partyRepository.Save(party);
            SayOnBoth("Treasure Finding", "Treasure found, but no active party members could collect it.");
            return;
        }

        var receiver = members[_random.Next(members.Count)];
        receiver.CopperPieces = Math.Max(0, receiver.CopperPieces + Math.Max(0, party.TreasureFindingCopperPieces));
        receiver.Inventory.Add(new Adnd.Core.Items.Item
        {
            Name = $"Gem Cache x{Math.Max(0, party.TreasureFindingGemCount)}",
            Type = Adnd.Core.Items.ItemType.Misc,
            Weight = 0,
            IsShopBuyable = false,
            Description = "Gem cache found via Potion of Treasure Finding."
        });

        _characterRepository.Save(receiver);

        party.TreasureFindingActive = false;
        party.TreasureFindingStepsRemaining = 0;
        _partyRepository.Save(party);

        RuleApplicationInfo.Publish($"Potion of Treasure Finding cache recovered at L{_currentDungeonLevel} ({_position.X},{_position.Y}): {party.TreasureFindingCopperPieces} cp and {party.TreasureFindingGemCount} gems.");
        SayOnBoth("Treasure Finding", $"Treasure found! {receiver.Name} collects {party.TreasureFindingCopperPieces} cp and {party.TreasureFindingGemCount} gems.");
    }

    private bool HasRecoverableOutCharacterAtCurrentPosition()
    {
        var party = _partyRepository.Load();
        return _characterRepository.GetAll()
            .Where(c => c.HasStatus(CharacterStatus.Out))
            .Where(c => c.DungeonLevel == _currentDungeonLevel
                        && c.DungeonCellX == _position.X
                        && c.DungeonCellY == _position.Y)
            .Where(c => !party.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .Any(c => !c.HasStatus(CharacterStatus.Ashes));
    }

    private void RestoreStrengthDrainedOnDungeonExit()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var restored = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (!c.RestoreStrengthDrainedUntilDungeonExit())
                continue;

            _characterRepository.Save(c);
            restored.Add(c.Name);
        }

        if (restored.Count > 0)
            SayOnBoth("Strength", $"Strength restored after leaving dungeon: {string.Join(", ", restored)}.");
    }

    private void RevertLycanthropeTransformationsOnDungeonExit()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var reverted = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (!c.LycanthropyTriggeredByFullMoon)
                continue;

            if (!c.EndLycanthropeTransformation())
                continue;

            _characterRepository.Save(c);
            reverted.Add(c.Name);
        }

        if (reverted.Count > 0)
            SayOnBoth("Lycanthropy", $"Lycanthrope form fades after leaving dungeon: {string.Join(", ", reverted)}.");
    }

    // A turn table spins the party to face a random direction.
    private void ProcessTurnTableStep()
    {
        if (!_thinWalls || !DungeonTurnTables.TryGetValue(_currentDungeonLevel, out var tables) || !tables.Contains(_position))
            return;

        _direction = (Direction)_random.Next(Enum.GetValues<Direction>().Length);
        Invalidate();
        PublishToViewer();
        SayOnBoth("Turn table", "THE TURN TABLE SPINS THE PARTY AROUND!");
    }

    // Falling into a pit costs every party member 2d6 hit points (1d8 on level 2).
    // Level 9: warp zones send the party to Floor 1, and the chute drops it to Floor 10. Returns true if the party left the floor.
    private bool ProcessLevelNineStep()
    {
        if (_thinWalls && _currentDungeonLevel == 10 && _position == new Point(0, 1))
        {
            SayOnBoth("Teleport", "A strange force teleports the party back to the castle!");
            LeaveDungeonForTown();
            return true;
        }

        if (!_thinWalls || _currentDungeonLevel != 9)
            return false;

        if (Level9WarpZones[_position.X][_position.Y] == 'W')
        {
            SayOnBoth("Teleport", "A strange force teleports the party all the way back to Floor 1!");
            JumpToDungeonLevel(1, new Point(0, 0));
            return true;
        }

        if (_position == Level9Chute)
        {
            if (_dungeonLayouts.TryLoad(10) is null)
            {
                SayOnBoth("Chute", "YOU STEP ON A CHUTE, BUT THE WAY DOWN IS BLOCKED.");
                return false;
            }

            SayOnBoth("Chute", "YOU FALL DOWN A CHUTE TO THE FLOOR BELOW!");
            JumpToDungeonLevel(10, new Point(0, 0));
            return true;
        }

        return false;
    }

    private void JumpToDungeonLevel(int level, Point position)
    {
        _currentDungeonLevel = level;
        BuildMazeForLevel(level);
        LoadEncounterLocationsForLevel(level);
        _position = position;
        UpdateLighting();
        Invalidate();
        PublishToViewer();
    }

    private void ProcessPitStep()
    {
        if (!_thinWalls || !DungeonPits.TryGetValue(_currentDungeonLevel, out var pits) || !pits.Contains(_position))
            return;

        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAllCached().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string> { "YOU FALL INTO A PIT!" };

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c) || c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead))
                continue;

            var damage = _currentDungeonLevel == 2 ? _random.Next(1, 9) : _random.Next(1, 7) + _random.Next(1, 7);
            var before = c.CurrentHitPoints;
            c.CurrentHitPoints = Math.Max(0, before - damage);
            messages.Add($"{c.Name} takes {before - c.CurrentHitPoints} damage. HP {before}->{c.CurrentHitPoints}.");

            if (c.CurrentHitPoints <= 0)
            {
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} dies.");
            }

            _characterRepository.Save(c);
        }

        SayOnBoth("Pit", string.Join(Environment.NewLine, messages));
    }

    private void ApplyPoisonDamageForStep()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAllCached().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var members = party.Members.Where(roster.ContainsKey).Select(name => roster[name]).ToList();
        var effectsBeforeStep = GetStepEffects(members);
        if (!_stepEffects.SetEquals(effectsBeforeStep))
        {
            _effectsNotificationChoice = DialogResult.OK;
            _stepsSinceEffectsNotification = 0;
        }
        _stepsSinceEffectsNotification++;
        var messages = new List<string>();
        var changed = false;

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            var wasLycanthropeDamageTriggered = c.LycanthropyTriggeredByDamage;

            if (c.MummyRotRaiseWindowRoundsRemaining > 0
                && (c.HasStatus(CharacterStatus.Dead) || c.HasStatus(CharacterStatus.Ashes) || c.CurrentHitPoints <= 0))
            {
                var remainingBefore = c.MummyRotRaiseWindowRoundsRemaining;
                var remainingAfter = c.TickMummyRotRaiseWindowRound();
                if (remainingAfter != remainingBefore)
                    changed = true;

                if (remainingAfter <= 0)
                    messages.Add($"{c.Name}'s mummy-rot revival window expires. Raise Dead can no longer restore this character.");
            }

            if (c.RotGrubDeathRoundsRemaining > 0
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost)
                && c.CurrentHitPoints > 0)
            {
                c.RotGrubDeathRoundsRemaining = Math.Max(0, c.RotGrubDeathRoundsRemaining - 1);
                changed = true;

                if (c.RotGrubDeathRoundsRemaining <= 0)
                {
                    c.CurrentHitPoints = 0;
                    c.AddStatus(CharacterStatus.Dead);
                    messages.Add($"{c.Name} dies as rot grubs reach the heart.");
                }
                else
                {
                    messages.Add($"{c.Name} has {c.RotGrubDeathRoundsRemaining} round(s) before rot grub death unless cured.");
                }
            }

            if (c.CurrentHitPoints > 0
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Petrified)
                && c.CurrentHitPoints < c.MaxHitPoints
                && HasEquippedRegeneration(c))
            {
                var beforeRegen = c.CurrentHitPoints;
                c.CurrentHitPoints = Math.Min(c.MaxHitPoints, c.CurrentHitPoints + 1);
                var healed = c.CurrentHitPoints - beforeRegen;
                if (healed > 0)
                {
                    changed = true;
                    messages.Add($"{c.Name} regenerates {healed} HP while walking. HP {beforeRegen}->{c.CurrentHitPoints}.");
                }
            }

            if (c.HasStatus(CharacterStatus.Paralyzed)
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost)
                && c.CurrentHitPoints > 0)
            {
                var beforeRounds = c.ParalyzedRoundsRemaining;
                var afterRounds = c.TickParalysisRound();
                changed = true;

                if (afterRounds > 0)
                    messages.Add($"{c.Name} remains paralyzed ({afterRounds} round(s) remaining). Step consumed 1 round.");
                else if (beforeRounds > 0)
                    messages.Add($"{c.Name} is no longer paralyzed.");
            }

            if (wasLycanthropeDamageTriggered
                && c.HasStatus(CharacterStatus.LycanthropeTransformed)
                && c.CurrentHitPoints >= c.MaxHitPoints)
            {
                c.EndLycanthropeTransformation();
                changed = true;
                messages.Add($"{c.Name} recovers and returns from lycanthrope form.");
            }

            if (c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead) || !c.HasStatus(CharacterStatus.Poisoned))
                continue;

            var roll = _random.Next(1, 101);
            if (roll > 30)
                continue;

            var damage = _random.Next(1, 3);
            var before = c.CurrentHitPoints;
            c.CurrentHitPoints = Math.Max(0, c.CurrentHitPoints - damage);
            var actual = before - c.CurrentHitPoints;

            if (actual <= 0)
                continue;

            changed = true;
            messages.Add($"{c.Name} takes {actual} poison damage while walking. HP {before}->{c.CurrentHitPoints}.");

            if (c.CurrentHitPoints <= 0)
            {
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} dies from poison.");
            }
        }

        var effectsAfterStep = GetStepEffects(members);
        var effectsEnded = !effectsBeforeStep.SetEquals(effectsAfterStep);
        _stepEffects.Clear();
        _stepEffects.UnionWith(effectsAfterStep);

        if (changed)
        {
            foreach (var c in roster.Values)
                _characterRepository.Save(c);
        }

        if (effectsEnded)
        {
            _effectsNotificationChoice = DialogResult.OK;
            _stepsSinceEffectsNotification = 0;
        }

        if (messages.Count == 0
            || (!effectsEnded && (_effectsNotificationChoice == DialogResult.Ignore
                || (_effectsNotificationChoice == DialogResult.Retry && _stepsSinceEffectsNotification < 10))))
            return;

        var choice = ShowEffectsOnBoth(string.Join(Environment.NewLine, messages));
        if (effectsAfterStep.Count == 0)
            _effectsNotificationChoice = DialogResult.OK;
        else if (choice is DialogResult.Ignore or DialogResult.Retry)
            _effectsNotificationChoice = choice;
        _stepsSinceEffectsNotification = 0;
    }

    private static HashSet<(string Character, string Effect)> GetStepEffects(IEnumerable<Character> members)
    {
        var effects = new HashSet<(string Character, string Effect)>();
        foreach (var c in members)
        {
            if (c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes) || c.HasStatus(CharacterStatus.Lost))
                continue;

            if (c.RotGrubDeathRoundsRemaining > 0)
                effects.Add((c.Name, "RotGrubs"));
            if (c.HasStatus(CharacterStatus.Poisoned))
                effects.Add((c.Name, "Poison"));
            if (c.HasStatus(CharacterStatus.Paralyzed))
                effects.Add((c.Name, "Paralysis"));
            if (!c.HasStatus(CharacterStatus.Petrified) && c.CurrentHitPoints < c.MaxHitPoints && HasEquippedRegeneration(c))
                effects.Add((c.Name, "Regeneration"));
            if (c.LycanthropyTriggeredByDamage && c.HasStatus(CharacterStatus.LycanthropeTransformed))
                effects.Add((c.Name, "Lycanthropy"));
        }
        return effects;
    }

    private DialogResult ShowEffectsOnBoth(string text)
    {
        var oldStyle = GameRulesProvider.Current.UIOldStyle;
        using var form = new Form
        {
            Text = "Effects",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = oldStyle ? FormBorderStyle.None : FormBorderStyle.FixedDialog,
            ClientSize = new Size(820, 500),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            KeyPreview = true,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Padding = new Padding(4)
        };
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12)
        };
        var title = new Label
        {
            Text = "EFFECTS",
            Dock = DockStyle.Top,
            Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };
        var body = new TextBox
        {
            Text = text,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = form.BackColor,
            ForeColor = form.ForeColor,
            Font = new Font("Consolas", oldStyle ? 18f : 14f, FontStyle.Bold)
        };
        var choices = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = oldStyle ? 108 : 80 };
        foreach (var (label, result) in new[]
        {
            ("↵ Continue", DialogResult.OK),
            ("D)on't show again", DialogResult.Ignore),
            ("S)how only every 10 steps", DialogResult.Retry)
        })
        {
            choices.Controls.Add(new Button
            {
                Text = label,
                DialogResult = result,
                AutoSize = true,
                BackColor = Color.Black,
                ForeColor = form.ForeColor,
                FlatStyle = oldStyle ? FlatStyle.Flat : FlatStyle.Standard,
                Font = body.Font
            });
        }
        form.KeyDown += (_, e) =>
        {
            var result = e.KeyCode switch
            {
                Keys.D => DialogResult.Ignore,
                Keys.S => DialogResult.Retry,
                Keys.Enter or Keys.Escape => DialogResult.OK,
                _ => DialogResult.None
            };
            if (result == DialogResult.None)
                return;
            e.SuppressKeyPress = true;
            form.DialogResult = result;
            form.Close();
        };
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                e.IsInputKey = true;
        };
        frame.Controls.Add(body);
        frame.Controls.Add(choices);
        frame.Controls.Add(title);
        form.Controls.Add(frame);
        form.Shown += (_, _) => body.Focus();

        var options = new[]
        {
            new ViewerPromptOption("continue", "Continue"),
            new ViewerPromptOption("dontShowAgain", "Don't show again"),
            new ViewerPromptOption("everyTenSteps", "Show only every 10 steps")
        };
        var answers = new Dictionary<string, DialogResult>
        {
            ["continue"] = DialogResult.OK,
            ["dontShowAgain"] = DialogResult.Ignore,
            ["everyTenSteps"] = DialogResult.Retry
        };
        var outcome = ViewerDialog.RunModal(form, this, new ViewerPrompt("choice", text, null, options), answers, PublishTable);
        PublishToViewer();
        return outcome;
    }

    private void ApplyDiseaseProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (!c.HasStatus(CharacterStatus.Diseased)
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            if (c.RottingDiseaseMonthsRemaining > 0)
                continue;

            var beforeCon = c.Abilities.Constitution;
            c.Abilities.Constitution = Math.Max(0, c.Abilities.Constitution - 1);

            if (c.Abilities.Constitution <= 0)
            {
                c.CurrentHitPoints = 0;
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} loses 1 Constitution from disease ({beforeCon}->0) and dies.");
            }
            else
            {
                messages.Add($"{c.Name} loses 1 Constitution from disease ({beforeCon}->{c.Abilities.Constitution}).");
            }

            _characterRepository.Save(c);
        }

        ApplyInfestationProgressForDungeonExitDay();

        if (messages.Count > 0)
            SayOnBoth("Disease", string.Join(Environment.NewLine, messages));
    }

    private void ApplyInfestationProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.InfestationDeathDaysRemaining <= 0
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            c.InfestationDeathDaysRemaining = Math.Max(0, c.InfestationDeathDaysRemaining - 1);

            if (c.InfestationDeathDaysRemaining > 0)
            {
                _characterRepository.Save(c);
                continue;
            }

            c.CurrentHitPoints = 0;
            c.AddStatus(CharacterStatus.Dead);
            messages.Add($"{c.Name} dies from untreated gas spore infestation.");

            var spores = Random.Shared.Next(2, 9);
            messages.Add($"{c.Name}'s body erupts and spawns {spores} gas spore(s).");

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Infestation", string.Join(Environment.NewLine, messages));

        ApplyTickDiseaseProgressForDungeonExitDay();
    }

    private void ApplyTickDiseaseProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.TickDiseaseDeathDaysRemaining <= 0
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            c.TickDiseaseDeathDaysRemaining = Math.Max(0, c.TickDiseaseDeathDaysRemaining - 1);

            if (c.TickDiseaseDeathDaysRemaining > 0)
            {
                messages.Add($"{c.Name} suffers from Tick Disease ({c.TickDiseaseDeathDaysRemaining} day(s) until death unless cured).");
                _characterRepository.Save(c);
                continue;
            }

            c.CurrentHitPoints = 0;
            c.AddStatus(CharacterStatus.Dead);
            messages.Add($"{c.Name} dies from untreated Tick Disease.");
            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Tick Disease", string.Join(Environment.NewLine, messages));

        ApplyPoisonSporesRaiseWindowProgressForDungeonExitDay();

        ApplyRottingDiseaseProgressForDungeonExitDay();
    }

    private void ApplyPoisonSporesRaiseWindowProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.PoisonSporesRaiseWindowDaysRemaining <= 0)
                continue;

            if (!c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && c.CurrentHitPoints > 0)
            {
                continue;
            }

            var remaining = c.TickPoisonSporesRaiseWindowDay();
            if (remaining <= 0)
                messages.Add($"{c.Name}'s poison spores revival window closes. Resurrection/Raise Dead can no longer restore this character.");
            else
                messages.Add($"{c.Name}'s poison spores revival window: {remaining} day(s) remaining.");

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Poison Spores", string.Join(Environment.NewLine, messages));
    }

    private void ApplyRottingDiseaseProgressForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.RottingDiseaseMonthsRemaining <= 0
                || c.HasStatus(CharacterStatus.Dead)
                || c.HasStatus(CharacterStatus.Ashes)
                || c.HasStatus(CharacterStatus.Lost)
                || c.CurrentHitPoints <= 0)
            {
                continue;
            }

            c.RottingDiseaseDaysIntoCurrentMonth = Math.Max(0, c.RottingDiseaseDaysIntoCurrentMonth + 1);
            if (c.RottingDiseaseDaysIntoCurrentMonth < 30)
            {
                _characterRepository.Save(c);
                continue;
            }

            c.RottingDiseaseDaysIntoCurrentMonth = 0;
            c.RottingDiseaseMonthsRemaining = Math.Max(0, c.RottingDiseaseMonthsRemaining - 1);

            var beforeCha = c.Abilities.Charisma;
            c.Abilities.Charisma = Math.Max(0, c.Abilities.Charisma - 2);
            messages.Add($"{c.Name} loses 2 Charisma from Rotting Disease ({beforeCha}->{c.Abilities.Charisma}).");

            if (c.RottingDiseaseMonthsRemaining <= 0)
            {
                c.CurrentHitPoints = 0;
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} dies from untreated Rotting Disease.");
            }
            else
            {
                messages.Add($"{c.RottingDiseaseMonthsRemaining} month(s) remain until death unless cured.");
            }

            _characterRepository.Save(c);
        }

        if (messages.Count > 0)
            SayOnBoth("Rotting Disease", string.Join(Environment.NewLine, messages));
    }

    private void ApplyPartyAgingForDungeonExitDay()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            c.AdvanceAgeByDays(1);
            _characterRepository.Save(c);
        }

        party.DungeonDaysElapsed = Math.Max(0, party.DungeonDaysElapsed) + 1;
        _partyRepository.Save(party);
    }

    private bool IsFullMoonNight()
    {
        var party = _partyRepository.Load();
        var dungeonDaysElapsed = Math.Max(0, party.DungeonDaysElapsed);
        // Every 30th day descending is a full moon night.
        var daysOnNextDescent = dungeonDaysElapsed + 1;
        return daysOnNextDescent % 30 == 0;
    }

    private void ShowFullMoonDialogRetro()
    {
        using var form = new Form
        {
            Text = "Full Moon",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(700, 150),
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = "FULL MOON",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var messageLabel = new Label
        {
            Left = 12,
            Top = 58,
            Width = framePanel.ClientSize.Width - 24,
            Height = 56,
            Text = "Tonight is a full moon as the party descends into the dungeon.",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 13f, FontStyle.Bold)
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(messageLabel);
        form.Controls.Add(framePanel);

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        form.MouseDown += (_, _) =>
        {
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        ViewerDialog.RunModal(form, this, new ViewerPrompt("info", "Full moon tonight.", null, new[]
        {
            new ViewerPromptOption("continue", "Continue")
        }), new Dictionary<string, DialogResult>
        {
            ["continue"] = DialogResult.OK
        }, PublishTable);

        PublishToViewer();
    }

    private static bool HasEquippedRegeneration(Character c)
    {
        return c.Equipment.Values
            .Where(item => item != null)
            .Any(item => item!.SpecialAbilities.Any(a =>
                string.Equals(a?.Trim(), "Regeneration (1)", StringComparison.OrdinalIgnoreCase)));
    }

    private void TryRandomEncounter(double? chanceOverride = null, bool forceInLair = false)
    {
        var configuredChance = chanceOverride
            ?? GameRulesProvider.ClampChance(GameRulesProvider.Current.MonsterEncounterChance);
        var shouldEncounter = configuredChance > 0 && _random.NextDouble() < configuredChance;
        if (!shouldEncounter)
            return;

        _combatCoordinator.ForceMonstersInLair = forceInLair;
        try
        {
            RunRandomEncounter();
        }
        finally
        {
            _combatCoordinator.ForceMonstersInLair = false;
        }
    }

    // Dungeon squares with a fixed encounter: rolled once, the first time the party steps there after
    // entering the floor. Returns true when the square is such a location (and so was not visited before).
    private bool TryEncounterAtFixedLocation()
    {
        if (!_thinWalls)
            return false;

        var location = _encounterLocations.FirstOrDefault(l => l.X == _position.X && l.Y == _position.Y);
        if (location is null || !_visitedEncounterLocations.Add((location.X, location.Y)))
            return false;

        var chance = Math.Clamp(location.ChancePercent, 0, 100);
        var roll = _random.Next(1, 101);
        var encounter = roll <= chance;

        RuleApplicationInfo.Publish(
            "HomeBrew",
            "",
            $"Fixed encounter at ({location.X},{location.Y}) on level {_currentDungeonLevel}",
            "First visit to the square after entering the floor: roll 1d100, an encounter happens on a roll of the chance or less. The monsters are in their lair when they have one.",
            "1",
            "100",
            roll.ToString(),
            $"Chance {chance}% => {(encounter ? "encounter" : "no encounter")}.");

        if (encounter)
            TryRandomEncounter(chanceOverride: 1d, forceInLair: true);

        return true;
    }

    private void LoadEncounterLocationsForLevel(int level)
    {
        _visitedEncounterLocations.Clear();
        _encounterLocations = _thinWalls ? _dungeonLayouts.LoadEncounterLocations(level) : Array.Empty<DungeonEncounterLocation>();
        _darkAreas = _thinWalls ? _dungeonLayouts.LoadDarkAreas(level) : Array.Empty<DungeonDarkArea>();
    }

    private void RunRandomEncounter()
    {
        var firstGroupRoll = RollDungeonMonsterForLevelWithCount(_currentDungeonLevel);
        var monsterName = firstGroupRoll?.MonsterName;

        if (_currentDungeonLevel == 10)
        {
            for (var reroll = 0; reroll < 10 && string.Equals(monsterName, "No Encounter", StringComparison.OrdinalIgnoreCase); reroll++)
            {
                firstGroupRoll = RollDungeonMonsterForLevelWithCount(_currentDungeonLevel);
                monsterName = firstGroupRoll?.MonsterName;
            }
        }

        if (string.IsNullOrWhiteSpace(monsterName))
            monsterName = LevelOneMonsters[_random.Next(LevelOneMonsters.Length)];

        if (string.Equals(monsterName, "No Encounter", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(monsterName, "Adventurer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(monsterName, "Character", StringComparison.OrdinalIgnoreCase))
        {
            ResolveCharacterEncounter(_currentDungeonLevel, firstGroupRoll?.CountOverride);
            PublishToViewer();
            return;
        }

        var party = LoadEncounterParty();
        if (party.Count == 0)
        {
            SayOnBoth("Monsters", $"Encounter!\n\n{monsterName}\n\n(No active party members found)");
            return;
        }

        var groupRoll = _random.Next(1, 101);
        var numberOfGroups = groupRoll switch
        {
            1 => 4,
            <= 2 => 3,
            <= 4 => 2,
            _ => 1
        };

        RuleApplicationInfo.Publish(
            "HomeBrew",
            "",
            "Roll number of monster groups",
            "Chance for multiple groups uses 1d100:\n• 01 → 4 groups (1%)\n• 02-03 → 3 groups (4%)\n• 04-08 → 2 groups (14%)\n• 09-100 → 1 group (81%)",
            "1",
            "100",
            groupRoll.ToString(),
            $"Encounter has {numberOfGroups} monster group(s)."
        );

        CombatOutcome outcome;
        if (numberOfGroups > 1)
        {
            var allMonsters = _monsterRepository.GetAll().ToList();
            var primaryGroupAxis = GetEncounterMoralAxis(FindMonsterByName(allMonsters, monsterName));

            var groups = new List<(string name, int count)>
            {
                (monsterName, ResolveEncounterGroupCount(monsterName, firstGroupRoll?.CountOverride))
            };

            for (int i = 1; i < numberOfGroups; i++)
            {
                EncounterRoll? additionalRoll = null;
                string? additionalMonsterName = null;
                var foundMatchingAlignment = false;
                var filteredOutByAlignment = 0;
                var filteredAlignmentDetails = new List<string>();

                for (int reroll = 0; reroll < 30; reroll++)
                {
                    additionalRoll = RollDungeonMonsterForLevelWithCountExcludingCharacter(_currentDungeonLevel);
                    additionalMonsterName = additionalRoll?.MonsterName;
                    if (string.IsNullOrWhiteSpace(additionalMonsterName))
                        additionalMonsterName = LevelOneMonsters[_random.Next(LevelOneMonsters.Length)];

                    if (!primaryGroupAxis.HasValue)
                    {
                        foundMatchingAlignment = true;
                        break;
                    }

                    var candidateMonster = FindMonsterByName(allMonsters, additionalMonsterName);
                    var candidateAxis = GetEncounterMoralAxis(candidateMonster);
                    if (!candidateAxis.HasValue || candidateAxis.Value == primaryGroupAxis.Value)
                    {
                        foundMatchingAlignment = true;
                        break;
                    }

                    filteredOutByAlignment++;
                    var expectedAlignment = FormatEncounterMoralAxis(primaryGroupAxis.Value);
                    var actualAlignment = string.IsNullOrWhiteSpace(candidateMonster?.Alignment)
                        ? "Unknown"
                        : candidateMonster!.Alignment;
                    filteredAlignmentDetails.Add($"{additionalMonsterName}: expected {expectedAlignment}, actual {actualAlignment}");
                }

                if (filteredOutByAlignment > 0)
                {
                    var detailsText = filteredAlignmentDetails.Count == 0
                        ? string.Empty
                        : $" {string.Join("; ", filteredAlignmentDetails)}.";

                    RuleApplicationInfo.Publish(
                        "Adnd",
                        "Encounter groups",
                        "Filter additional monster groups by alignment",
                        "When multiple monster groups are rolled, extra groups (after the first) are rerolled until alignment matches the first group (or unknown alignment).",
                        "1",
                        "30",
                        filteredOutByAlignment.ToString(),
                        $"Filtered out {filteredOutByAlignment} additional group candidate(s) due to alignment mismatch.{detailsText}"
                    );
                }

                if (!foundMatchingAlignment || string.IsNullOrWhiteSpace(additionalMonsterName))
                    break;

                groups.Add((additionalMonsterName, ResolveEncounterGroupCount(additionalMonsterName, additionalRoll?.CountOverride)));
            }

            var askedLeaveOrAttack = false;

            if (AreAllEncounterGroupsNeutral(groups.Select(g => g.name)) && _random.Next(1, 101) <= 50)
            {
                var distinctNames = groups.Select(g => g.name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var neutralName = distinctNames.Count == 1
                    ? distinctNames[0]
                    : string.Join(", ", distinctNames);

                if (AskNeutralEncounterOnBoth(neutralName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
            }

            var isFriendlyGoodEncounter = ShouldOfferFriendlyEncounterChoice(party, groups.Select(g => g.name));
            if (isFriendlyGoodEncounter)
            {
                var distinctNames = groups.Select(g => g.name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var friendlyName = distinctNames.Count == 1
                    ? distinctNames[0]
                    : string.Join(", ", distinctNames);

                if (AskFriendlyEncounterOnBoth(friendlyName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
                ApplyGoodEncounterAttackConsequences(party, friendlyName);
            }

            foreach (var member in party.Where(c => !c.IsGoodAlignment() && c.GoodEncounterAttackWarningCount > 0))
            {
                member.GoodEncounterAttackWarningCount = 0;
                _characterRepository.Save(member);
            }

            outcome = _combatCoordinator.StartEncounterWithGroupCounts(
                this,
                groups,
                party,
                _characterRepository,
                _currentDungeonLevel,
                skipSurpriseRoll: askedLeaveOrAttack,
                dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        }
        else
        {
            var numberOfMonsters = ResolveEncounterGroupCount(monsterName, firstGroupRoll?.CountOverride);
            var encounterMonsterLevelForDragonProfile = firstGroupRoll?.EncounterMonsterLevel ?? _currentDungeonLevel;
            var dragonHitPointsPerDieProfile = ResolveDragonHitPointsPerDieProfile(
                monsterName,
                encounterMonsterLevelForDragonProfile,
                numberOfMonsters);
            var askedLeaveOrAttack = false;

            if (AreAllEncounterGroupsNeutral(new[] { monsterName }) && _random.Next(1, 101) <= 50)
            {
                if (AskNeutralEncounterOnBoth(monsterName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
            }

            var isFriendlyGoodEncounter = ShouldOfferFriendlyEncounterChoice(party, new[] { monsterName });
            if (isFriendlyGoodEncounter)
            {
                if (AskFriendlyEncounterOnBoth(monsterName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
                ApplyGoodEncounterAttackConsequences(party, monsterName);
            }

            foreach (var member in party.Where(c => !c.IsGoodAlignment() && c.GoodEncounterAttackWarningCount > 0))
            {
                member.GoodEncounterAttackWarningCount = 0;
                _characterRepository.Save(member);
            }

            outcome = _combatCoordinator.StartEncounter(
                this,
                monsterName,
                numberOfMonsters,
                party,
                _characterRepository,
                encounterMonsterLevelForDragonProfile,
                skipSurpriseRoll: askedLeaveOrAttack,
                dragonHitPointsPerDieProfile: dragonHitPointsPerDieProfile,
                dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        }

        if (outcome == CombatOutcome.Defeat)
            HandlePartyDefeatAtCurrentCell();

        PublishToViewer();
    }

    private sealed record EncounterRoll(string MonsterName, int? CountOverride, int? EncounterMonsterLevel);

    private int ResolveEncounterGroupCount(string monsterName, int? tableCount)
    {
        if (tableCount.HasValue && tableCount.Value > 0)
            return tableCount.Value;

        var monster = FindMonsterByName(_monsterRepository.GetAll(), monsterName);
        if (monster != null)
        {
            var min = Math.Max(1, monster.NumberOfAppearancesMin);
            var max = Math.Max(1, monster.NumberOfAppearancesMax);
            if (max < min)
                (min, max) = (max, min);

            var rolled = _random.Next(min, max + 1);
            RuleApplicationInfo.Publish(
                "Adnd",
                "Monster data",
                $"Roll count for {monsterName}",
                "When MonsterLevels CountMin/CountMax is unavailable, roll from monster NumberOfAppearancesMin-NumberOfAppearancesMax.",
                "1",
                (max - min + 1).ToString(),
                (rolled - min + 1).ToString(),
                $"{monsterName} count {rolled} (range {min}-{max}).");
            return rolled;
        }

        var fallback = _random.Next(1, 7);
        RuleApplicationInfo.Publish(
            "Adnd",
            "Fallback",
            $"Roll count for {monsterName}",
            "No monster template found; fallback count roll 1d6.",
            "1",
            "6",
            fallback.ToString(),
            $"{monsterName} count {fallback}.");
        return fallback;
    }

    private static List<int>? ResolveDragonHitPointsPerDieProfile(string monsterName, int encounterMonsterLevel, int monsterCount)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return null;

        if (!monsterName.Contains("Dragon", StringComparison.OrdinalIgnoreCase))
            return null;

        if (monsterCount <= 0)
            return null;

        if (encounterMonsterLevel == 9 && monsterCount >= 2)
        {
            if (monsterName.Equals("Black Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Brass Dragon", StringComparison.OrdinalIgnoreCase))
            {
                return new List<int> { 8, 6 };
            }

            if (monsterName.Equals("White Dragon", StringComparison.OrdinalIgnoreCase))
                return new List<int> { 8, 7 };
        }

        if (encounterMonsterLevel == 10 && monsterCount >= 2)
        {
            if (monsterName.Equals("Gold Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Red Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Silver Dragon", StringComparison.OrdinalIgnoreCase))
            {
                return new List<int> { 8, 6 };
            }

            if (monsterName.Equals("Blue Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Bronze Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Copper Dragon", StringComparison.OrdinalIgnoreCase)
                || monsterName.Equals("Green Dragon", StringComparison.OrdinalIgnoreCase))
            {
                return new List<int> { 8, 7 };
            }
        }

        return null;
    }

    private EncounterRoll? RollDungeonMonsterForLevelWithCountExcludingCharacter(int level)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var rolled = RollDungeonMonsterForLevelWithCount(level);
            if (rolled == null || string.IsNullOrWhiteSpace(rolled.MonsterName))
                continue;

            if (string.Equals(rolled.MonsterName, "No Encounter", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(rolled.MonsterName, "Adventurer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rolled.MonsterName, "Character", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return rolled;
        }

        return null;
    }

    private static string FormatEncounterMoralAxis(EncounterMoralAxis axis)
    {
        return axis switch
        {
            EncounterMoralAxis.Good => "Good",
            EncounterMoralAxis.Neutral => "Neutral",
            EncounterMoralAxis.Evil => "Evil",
            _ => "Unknown"
        };
    }

    private string? RollDungeonMonsterForLevelExcludingCharacter(int level)
        => RollDungeonMonsterForLevelWithCountExcludingCharacter(level)?.MonsterName;

    private void ResolveCharacterEncounter(int dungeonLevel, int? forcedCount = null)
    {
        var party = LoadEncounterParty();
        if (party.Count == 0)
        {
            SayOnBoth("Characters", "Encounter!\n\nCharacter\n\n(No active party members found)");
            return;
        }

        var count = forcedCount.HasValue && forcedCount.Value > 0
            ? forcedCount.Value
            : _random.Next(1, 5) + 1; // 1d4+1
        var classMonsters = RollCharacterEncounterClasses(count, dungeonLevel);
        if (classMonsters.Count == 0)
        {
            SayOnBoth("Characters", $"No class-based character templates were found for dungeon level {Math.Max(1, dungeonLevel)}.");
            return;
        }

        var classSummary = string.Join(", ", classMonsters.GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"));
        RuleApplicationInfo.Publish(
            "Adnd",
            "House Rule",
            "Character encounter composition",
            forcedCount.HasValue
                ? "Use table count for Character encounter, then roll class table once per encountered character. All encountered classes use current dungeon level."
                : "Roll 1d4+1 for count, then roll class table once per encountered character. All encountered classes use current dungeon level.",
            "1",
            "5",
            count.ToString(),
            classSummary);

        var grouped = classMonsters
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => (name: g.Key, count: g.Count()))
            .ToList();

        var outcome = _combatCoordinator.StartEncounterWithGroupCounts(
            this,
            grouped,
            party,
            _characterRepository,
            dungeonLevel,
            dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        if (outcome == CombatOutcome.Defeat)
            HandlePartyDefeatAtCurrentCell();
    }

    private List<string> RollCharacterEncounterClasses(int count, int dungeonLevel)
    {
        var result = new List<string>(count);
        if (count <= 0)
            return result;

        bool TryBuildOneSet()
        {
            result.Clear();
            for (int i = 0; i < count; i++)
            {
                string? mapped = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    mapped = MapCharacterClassRollToMonsterName(_random.Next(1, 101), dungeonLevel);
                    if (!string.IsNullOrWhiteSpace(mapped))
                        break;
                }

                if (string.IsNullOrWhiteSpace(mapped))
                {
                    result.Clear();
                    return false;
                }

                result.Add(mapped);
            }

            return true;
        }

        // Prefer mixed classes for multi-character encounters, while still allowing duplicates.
        var requireMixedClasses = count >= 2;
        if (!requireMixedClasses)
        {
            TryBuildOneSet();
            return result;
        }

        List<string>? fallback = null;
        for (int reroll = 0; reroll < 100; reroll++)
        {
            if (!TryBuildOneSet())
                return result;

            fallback ??= new List<string>(result);

            if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
                return result;
        }

        // Fallback if the available class pool at this level is too narrow.
        return fallback ?? result;
    }

    private string? MapCharacterClassRollToMonsterName(int roll, int dungeonLevel)
    {
        string className;
        if (roll <= 17)
            className = "Cleric";
        else if (roll <= 20)
            className = "Druid";
        else if (roll <= 60)
            className = "Fighter";
        else if (roll <= 62)
            className = "Paladin";
        else if (roll <= 65)
            className = "Ranger";
        else if (roll <= 86)
            className = "Magic-User";
        else if (roll <= 88)
            className = "Illusionist";
        else if (roll <= 98)
            className = "Thief";
        else if (roll == 99)
            className = "Assassin";
        else
            className = _random.Next(0, 2) == 0 ? "Monk" : "Bard";

        var candidates = BuildCharacterClassMonsterNameCandidates(className, dungeonLevel);
        var monsters = _monsterRepository.GetAll().ToList();

        foreach (var candidate in candidates)
        {
            var exact = monsters.FirstOrDefault(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact.Name;
        }

        return null;
    }

    private static List<string> BuildCharacterClassMonsterNameCandidates(string className, int dungeonLevel)
    {
        var level = Math.Max(1, dungeonLevel);
        var names = new List<string>
        {
            $"Lvl {level} {className}",
            $"Lvl {level} {className.Replace("-", " ", StringComparison.Ordinal)}"
        };

        if (string.Equals(className, "Magic-User", StringComparison.OrdinalIgnoreCase))
        {
            names.Add($"Lvl {level} Mage");
            names.Add($"Lvl {level} MagicUser");
        }
        else if (string.Equals(className, "Cleric", StringComparison.OrdinalIgnoreCase))
        {
            names.Add($"Lvl {level} Priest");
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string? RollDungeonMonsterForLevel(int level)
        => RollDungeonMonsterForLevelWithCount(level)?.MonsterName;

    private EncounterRoll? RollDungeonMonsterForLevelWithCount(int level)
    {
        var rolledMonsterLevel = RollMonsterLevelFromEncounterTable(level);
        if (rolledMonsterLevel <= 0)
            return null;

        // Primary flow: MonsterEncounter.json determines monster level,
        // then MonsterLevels.json (1d100) determines creature and count.
        var dmgRoll = RollFromDmgEncounterTable(rolledMonsterLevel, level);
        if (dmgRoll != null)
            return dmgRoll;

        // Fallback when DMG table data is unavailable or cannot be mapped locally.
        var monstersForLevel = _monsterRepository.GetAll()
            .Where(m => m.DungeonLevel == rolledMonsterLevel)
            .ToList();

        if (monstersForLevel.Count == 0)
            return null;

        var sourceOption = GameRulesProvider.Current.MonsterSourceOptions;
        var filteredMonsters = FilterMonstersBySource(monstersForLevel, sourceOption);
        if (filteredMonsters.Count == 0)
            return null;

        var selected = SelectMonsterByFrequencyWeight(filteredMonsters, level, rolledMonsterLevel);
        if (selected == null)
            return null;

        var name = selected.Name;
        int? countOverride = null;
        if (selected.Type == MonsterType.Dragon)
        {
            if (rolledMonsterLevel == 3)
                name = RollLevel3DragonBySubtable();
            else if (rolledMonsterLevel == 4)
                name = RollLevel4DragonBySubtable();
            else if (rolledMonsterLevel == 5)
                name = RollLevel5DragonBySubtable();
            else if (rolledMonsterLevel == 7)
                name = RollLevel7DragonBySubtable();
            else if (rolledMonsterLevel == 8)
                name = RollLevel8DragonBySubtable();
            else if (rolledMonsterLevel == 9)
                name = RollLevel9DragonBySubtable(out countOverride);
            else if (rolledMonsterLevel == 10)
                name = RollLevel10DragonBySubtable(out countOverride);
        }

        return new EncounterRoll(name, countOverride, rolledMonsterLevel);
    }

    private int RollMonsterLevelFromEncounterTable(int dungeonLevel)
    {
        var tablePath = Path.Combine("Data", "Encounters", "MonsterEncounter.json");
        if (!File.Exists(tablePath))
            return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);

        using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
        if (!document.RootElement.TryGetProperty("monsterEncounterTable", out var tableRoot)
            || tableRoot.ValueKind != JsonValueKind.Object)
        {
            return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);
        }

        var levelKey = dungeonLevel.ToString();
        if (!tableRoot.TryGetProperty(levelKey, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);

        var roll = _random.Next(1, 21);

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("roll", out var rollEl)
                || !entry.TryGetProperty("monsterLevel", out var levelEl))
                continue;

            var range = rollEl.GetString();
            if (!TryParseRollRange(range, out var min, out var max))
                continue;

            if (roll < min || roll > max)
                continue;

            var monsterLevel = levelEl.GetInt32();
            RuleApplicationInfo.Publish(
                "DMG",
                "174",
                $"Roll monster level for dungeon level {dungeonLevel}",
                "",
    //           "Use MonsterEncounterTable: roll 1d20 and map to encounter monster level.",
                "1",
                "20",
                roll.ToString(),
                $"Monster level {monsterLevel}.");

            return monsterLevel;
        }

        return MonsterLevelGenerator.RollMonsterLevel(dungeonLevel);
    }

    private static bool TryParseRollRange(string? range, out int min, out int max)
    {
        min = 0;
        max = 0;
        if (string.IsNullOrWhiteSpace(range))
            return false;

        var trimmed = range.Trim();
        var dash = trimmed.IndexOf('-');
        if (dash <= 0 || dash >= trimmed.Length - 1)
        {
            if (!int.TryParse(trimmed, out var single))
                return false;

            min = single;
            max = single;
            return true;
        }

        var left = trimmed[..dash].Trim();
        var right = trimmed[(dash + 1)..].Trim();
        if (!int.TryParse(left, out min) || !int.TryParse(right, out max))
            return false;

        if (max < min)
        {
            var temp = min;
            min = max;
            max = temp;
        }

        return true;
    }
    public static string GetDMGpageForMonsterEncounterTable(int monsterLevel)
    {
        return monsterLevel switch
        {
            1 => "175_Level1",
            2 => "177_Level2",
            3 => "177_Level3",
            4 => "177_Level4",
            5 => "177_Level5",
            6 => "178_Level6",
            7 => "178_Level7",
            8 => "178_Level8",
            9 => "179_Level9",
            10 => "179_Level10",
            _ => "175-177"
        };
    }

    private EncounterRoll? RollFromDmgEncounterTable(int monsterLevel, int dungeonLevel)
    {
        var tablePath = Path.Combine("Data", "Encounters", "MonsterLevels.json");
        if (!File.Exists(tablePath))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(tablePath));
        if (!document.RootElement.TryGetProperty("MonsterLevels", out var allLevels))
            return null;

        var levelKey = $"Level{monsterLevel}";
        if (!allLevels.TryGetProperty(levelKey, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return null;

        var roll = _random.Next(1, 101);

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("DiceMin", out var minEl)
                || !entry.TryGetProperty("DiceMax", out var maxEl)
                || !entry.TryGetProperty("Creature", out var creatureEl))
            {
                continue;
            }

            var min = minEl.GetInt32();
            var max = maxEl.GetInt32();
            if (roll < min || roll > max)
                continue;

            var creature = creatureEl.GetString();
            var substituteBadgerWithHobgoblin = dungeonLevel >= 3
                && roll is >= 3 and <= 4
                && string.Equals(creature?.Trim(), "Badger", StringComparison.OrdinalIgnoreCase);

            if (substituteBadgerWithHobgoblin)
            {
                creature = "Hobgoblin";
                RuleApplicationInfo.Publish(
                    "DMG special case: dungeon level 3+ and encounter roll 03-04 replaces Badger with 2-8 Hobgoblins.");
            }
            string page = GetDMGpageForMonsterEncounterTable(monsterLevel);
            RuleApplicationInfo.Publish(
                "DMG",
                page,
                $"Encounter table level {monsterLevel})",
    //            $"Use encounter table Level{monsterLevel}; roll 1d100 and find matching DiceMin-DiceMax range.",
                $"",
                "1",
                "100",
                roll.ToString(), creature);
            var resolved = ResolveDmgCreatureToMonsterName(creature, monsterLevel, dungeonLevel, out var dragonCountOverride);
            int? countOverride = null;
            int? countMin = null;
            int? countMax = null;

            if (substituteBadgerWithHobgoblin)
            {
                countMin = 2;
                countMax = 8;
            }
            else if (entry.TryGetProperty("CountMin", out var countMinEl)
                && entry.TryGetProperty("CountMax", out var countMaxEl)
                && countMinEl.ValueKind == JsonValueKind.Number
                && countMaxEl.ValueKind == JsonValueKind.Number)
            {
                countMin = countMinEl.GetInt32();
                countMax = countMaxEl.GetInt32();
            }

            if (countMin.HasValue && countMax.HasValue)
            {
                var resolvedCountMin = countMin.Value;
                var resolvedCountMax = countMax.Value;

                if (resolvedCountMax < resolvedCountMin)
                    (resolvedCountMin, resolvedCountMax) = (resolvedCountMax, resolvedCountMin);

                resolvedCountMin = Math.Max(1, resolvedCountMin);
                resolvedCountMax = Math.Max(1, resolvedCountMax);
                countOverride = _random.Next(resolvedCountMin, resolvedCountMax + 1);

                //        public record DiceFormula(int DiceCount, int DiceSides, int Extra);
                //        public static DiceFormula GetDiceFormula(int min, int max)

                DiceFormulas.DiceFormula? diceFormula = DiceFormulas.GetDiceFormula(resolvedCountMin, resolvedCountMax);
                int NumberOfDices = diceFormula?.DiceCount ?? countOverride.Value;
                int numberOfSides = diceFormula?.DiceSides ?? 1;
                int extra = diceFormula?.Extra ?? (resolvedCountMin - 1);

                string numberOfSidesText = numberOfSides.ToString();
                if (extra > 0)
                    numberOfSidesText += "+" + extra.ToString();

                if (creature != "Human")
                {
                    //    public static void Publish(string source, string page, string context, string rule, string numberOfDices, 
                    //string sidesOnDices, string resultOfRoll, string consequenceOfRoll)
                    RuleApplicationInfo.Publish(
                    "DMG",//source
                    page,//page
                    //               $"Roll encounter count for '{creature}' (monster level {monsterLevel})",
                    $"Number of '{creature}s'",//context
                    "",//rule
                //    "Use CountMin-CountMax from MonsterLevels entry.",//rule
                    NumberOfDices.ToString(),//numberOfDices
                    numberOfSidesText,//sidesOnDices
                    countOverride.Value.ToString(),//resultOfRoll
                    countOverride.Value.ToString() + " " + creature + "s"//resultOfRoll
                   );
                }
            }

            if (dragonCountOverride.HasValue && dragonCountOverride.Value > 0)
                countOverride = dragonCountOverride;

            if (!string.IsNullOrWhiteSpace(resolved))
                return new EncounterRoll(resolved, countOverride, monsterLevel);

            break;
        }

        RuleApplicationInfo.Publish($"DMG encounter table Level{monsterLevel} roll did not map to a local monster.");
        return null;
    }

    private string? ResolveDmgCreatureToMonsterName(string? dmgCreature, int monsterLevel, int dungeonLevel, out int? dragonCountOverride)
    {
        dragonCountOverride = null;

        if (string.IsNullOrWhiteSpace(dmgCreature))
            return null;

        if (IsDragonEncounter(dmgCreature))
        {
            if (monsterLevel == 3)
                return RollLevel3DragonBySubtable();

            if (monsterLevel == 4)
                return RollLevel4DragonBySubtable();

            if (monsterLevel == 5)
                return RollLevel5DragonBySubtable();

            if (monsterLevel == 7)
                return RollLevel7DragonBySubtable();

            if (monsterLevel == 8)
                return RollLevel8DragonBySubtable();

            if (monsterLevel == 9)
                return RollLevel9DragonBySubtable(out dragonCountOverride);

            if (monsterLevel == 10)
                return RollLevel10DragonBySubtable(out dragonCountOverride);
        }

        if (string.Equals(dmgCreature.Trim(), "Human", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dmgCreature.Trim(), "Humans", StringComparison.OrdinalIgnoreCase))
        {
            var roll = _random.Next(1, 101);
            var result = roll switch
            {
                <= 25 => "Bandit",
                <= 30 => "Berserker",
                <= 45 => "Brigand",
                _ => "Character"
            };

            RuleApplicationInfo.Publish(
                "DMG",
                "175",
                "Resolve Humans encounter",
                "If Humans are encountered, roll 1d100: 01-25 Bandit, 26-30 Berserker, 31-45 Brigand, 46-100 Character.",
                "1",
                "100",
                roll.ToString(),
                $"Selected {result}.");

            return result;
        }

        if (IsCharacterEncounterEntry(dmgCreature))
            return "Character";

        if (IsDemonPrinceEncounter(dmgCreature))
        {
            var demonPrince = GetRandomDemonPrinceName();
            if (!string.IsNullOrWhiteSpace(demonPrince))
                return demonPrince;
        }

        var allMonsters = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd)
            .ToList();

        var raw = dmgCreature.Trim();
        var candidateNames = new List<string> { raw };

        var comma = raw.IndexOf(',');
        if (comma > 0 && comma < raw.Length - 1)
        {
            var left = raw[..comma].Trim();
            var right = raw[(comma + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                candidateNames.Add($"{right} {left}");
        }

        foreach (var candidate in candidateNames)
        {
            var match = FindMonsterByName(allMonsters, candidate);
            if (match != null)
                return match.Name;
        }

        return null;
    }

    private static bool IsDragonEncounter(string dmgCreature)
    {
        var trimmed = dmgCreature.Trim();
        return trimmed.StartsWith("Dragon", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCharacterEncounterEntry(string dmgCreature)
    {
        var trimmed = dmgCreature.Trim();
        return trimmed.StartsWith("Character", StringComparison.OrdinalIgnoreCase);
    }

    private string GetRandomHumanCharacterEncounterNameForLevel(int monsterLevel)
    {
        var level = Math.Max(1, monsterLevel);
        var candidates = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd)
            .Where(m => m.DungeonLevel == level)
            .Where(m => m.Type == MonsterType.Humanoid)
            .Where(m => m.Name.StartsWith($"Lvl {level} ", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return $"Lvl {level} Fighter";

        return candidates[_random.Next(candidates.Count)];
    }

    private static bool IsDemonPrinceEncounter(string dmgCreature)
    {
        var normalized = NormalizeMonsterName(dmgCreature);
        return normalized.Contains("demon", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("prince", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetRandomDemonPrinceName()
    {
        var demonPrinces = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd
                && string.Equals(m.TypeName?.Trim(), "Demon Prince", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (demonPrinces.Count == 0)
            return null;

        return demonPrinces[_random.Next(demonPrinces.Count)];
    }

    private string RollLevel3DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 28 => "Black Dragon",
            <= 62 => "Brass Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 3 dragon encounter",
            "If dragon is encountered on dungeon level 3, roll 1d100: 01-28 Black Dragon, 29-62 Brass Dragon, 63-100 White Dragon.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel8DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 13 => "Black Dragon",
            <= 24 => "Blue Dragon",
            <= 31 => "Brass Dragon",
            <= 35 => "Bronze Dragon",
            <= 43 => "Copper Dragon",
            <= 47 => "Gold Dragon",
            <= 62 => "Green Dragon",
            <= 78 => "Red Dragon",
            <= 82 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 8 dragon encounter",
            "If a level 8 dragon is encountered, roll 1d100: 01-13 Black, 14-24 Blue, 25-31 Brass, 32-35 Bronze, 36-43 Copper, 44-47 Gold, 48-62 Green, 63-78 Red, 79-82 Silver, 83-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel5DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 21 => "Blue Dragon",
            <= 29 => "Brass Dragon",
            <= 36 => "Bronze Dragon",
            <= 48 => "Copper Dragon",
            <= 52 => "Gold Dragon",
            <= 66 => "Green Dragon",
            <= 80 => "Red Dragon",
            <= 87 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 5 dragon encounter",
            "If a level 5 dragon is encountered, roll 1d100: 01-10 Black, 11-21 Blue, 22-29 Brass, 30-36 Bronze, 37-48 Copper, 49-52 Gold, 53-66 Green, 67-80 Red, 81-87 Silver, 88-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel7DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 21 => "Blue Dragon",
            <= 29 => "Brass Dragon",
            <= 36 => "Bronze Dragon",
            <= 48 => "Copper Dragon",
            <= 52 => "Gold Dragon",
            <= 66 => "Green Dragon",
            <= 80 => "Red Dragon",
            <= 87 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 7 dragon encounter",
            "If a level 7 dragon is encountered, roll 1d100: 01-10 Black, 11-21 Blue, 22-29 Brass, 30-36 Bronze, 37-48 Copper, 49-52 Gold, 53-66 Green, 67-80 Red, 81-87 Silver, 88-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel9DragonBySubtable(out int? countOverride)
    {
        countOverride = null;
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 22 => "Blue Dragon",
            <= 31 => "Brass Dragon",
            <= 34 => "Bronze Dragon",
            <= 42 => "Copper Dragon",
            <= 46 => "Gold Dragon",
            <= 62 => "Green Dragon",
            <= 78 => "Red Dragon",
            <= 82 => "Silver Dragon",
            _ => "White Dragon"
        };

        if (roll <= 10 || (roll >= 23 && roll <= 31) || roll >= 83)
            countOverride = 2;

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 9 dragon encounter",
            "If a level 9 dragon is encountered, roll 1d100: 01-10 Black, 11-22 Blue, 23-31 Brass, 32-34 Bronze, 35-42 Copper, 43-46 Gold, 47-62 Green, 63-78 Red, 79-82 Silver, 83-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string RollLevel10DragonBySubtable(out int? countOverride)
    {
        countOverride = null;
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 20 => "Blue Dragon",
            <= 26 => "Bronze Dragon",
            <= 33 => "Copper Dragon",
            <= 35 => ResolveBestAvailableMonsterName("Tiamat", "Chromatic Dragon", "Red Dragon"),
            <= 40 => "Gold Dragon",
            <= 60 => "Green Dragon",
            <= 63 => ResolveBestAvailableMonsterName("Bahamut", "Platinum Dragon", "Gold Dragon"),
            <= 94 => "Red Dragon",
            _ => "Silver Dragon"
        };

        if ((roll >= 1 && roll <= 33)
            || (roll >= 36 && roll <= 60)
            || (roll >= 64 && roll <= 100))
        {
            countOverride = 2;
        }

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 10 dragon encounter",
            "If a level 10 dragon is encountered, roll 1d100: 01-20 Blue, 21-26 Bronze, 27-33 Copper, 34-35 Chromatic (Tiamat), 36-40 Gold, 41-60 Green, 61-63 Platinum (Bahamut), 64-94 Red, 95-100 Silver.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private string ResolveBestAvailableMonsterName(params string[] candidates)
    {
        var allMonsters = _monsterRepository.GetAll()
            .Where(m => m.Source == Sources.Adnd)
            .ToList();

        foreach (var candidate in candidates)
        {
            var match = FindMonsterByName(allMonsters, candidate);
            if (match != null)
                return match.Name;
        }

        return candidates.Last();
    }

    private string RollLevel4DragonBySubtable()
    {
        var roll = _random.Next(1, 101);
        var selected = roll switch
        {
            <= 10 => "Black Dragon",
            <= 21 => "Blue Dragon",
            <= 29 => "Brass Dragon",
            <= 36 => "Bronze Dragon",
            <= 48 => "Copper Dragon",
            <= 52 => "Gold Dragon",
            <= 66 => "Green Dragon",
            <= 80 => "Red Dragon",
            <= 87 => "Silver Dragon",
            _ => "White Dragon"
        };

        RuleApplicationInfo.Publish(
            "DMG",
            "Dragon Subtable",
            "Resolve level 4 dragon encounter",
            "If a level 4 dragon is encountered, roll 1d100: 01-10 Black, 11-21 Blue, 22-29 Brass, 30-36 Bronze, 37-48 Copper, 49-52 Gold, 53-66 Green, 67-80 Red, 81-87 Silver, 88-100 White.",
            "1",
            "100",
            roll.ToString(),
            $"Selected {selected}.");

        return selected;
    }

    private static string NormalizeMonsterName(string value)
    {
        var normalized = value
            .ToLowerInvariant()
            .Replace("deamon", "demon", StringComparison.Ordinal)
            .Replace(',', ' ')
            .Replace('-', ' ');

        var chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return string.Join(" ", new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string MonsterNameSignature(string value)
    {
        var parts = NormalizeMonsterName(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        return string.Join(" ", parts);
    }

    private static Monster? FindMonsterByName(IEnumerable<Monster> monsters, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return null;

        var exact = monsters.FirstOrDefault(m => string.Equals(m.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var normalizedRequested = NormalizeMonsterName(requestedName);
        var signatureRequested = MonsterNameSignature(requestedName);

        foreach (var monster in monsters)
        {
            if (string.Equals(NormalizeMonsterName(monster.Name), normalizedRequested, StringComparison.OrdinalIgnoreCase))
                return monster;

            if (string.Equals(MonsterNameSignature(monster.Name), signatureRequested, StringComparison.OrdinalIgnoreCase))
                return monster;
        }

        return null;
    }

    private List<Monster> FilterMonstersBySource(List<Monster> monsters, SourceOptions sourceOption)
    {
        return sourceOption switch
        {
            SourceOptions.OnlyAdnd => monsters.Where(m => m.Source == Sources.Adnd).ToList(),
            SourceOptions.OnlyAdndDMGEncounterTable => monsters.Where(m => m.Source == Sources.Adnd).ToList(),
            SourceOptions.OnlyWizardry => monsters.Where(m => m.Source == Sources.Wizardry || m.Source == Sources.WizardryAndAdnd).ToList(),
            SourceOptions.AllButWizardry => monsters.Where(m => m.Source != Sources.Wizardry).ToList(),
            SourceOptions.All => monsters,
            _ => monsters
        };
    }

    private Monster? SelectMonsterByFrequencyWeight(List<Monster> monsters, int dungeonLevel, int rolledMonsterLevel)
    {
        // Define frequency weights (similar to rarity weights for items)
        var frequencyWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Common", 50 },
            { "Uncommon", 30 },
            { "Rare", 15 },
            { "Very Rare", 4 },
            { "Legendary", 1 },
            { "Not Implemented", 0 }

        };
        //filter out monsters with "Not Implemented" frequency
        monsters = monsters.Where(m => !string.Equals(m.Frequency, "Not Implemented", StringComparison.OrdinalIgnoreCase)).ToList();
        if (monsters.Count == 0)
            return null;
        // Calculate total weight
        var totalWeight = 0;
        var monsterWeights = new List<(Monster monster, int weight)>();

        foreach (var monster in monsters)
        {
            var weight = frequencyWeights.ContainsKey(monster.Frequency) 
                ? frequencyWeights[monster.Frequency] 
                : frequencyWeights["Common"]; // Default to Common if frequency not recognized

            monsterWeights.Add((monster, weight));
            totalWeight += weight;
        }

        if (totalWeight == 0)
            return monsters[_random.Next(monsters.Count)]; // Fallback to uniform random

        // Select random monster based on weight
        var randomValue = _random.Next(totalWeight);
        var cumulativeWeight = 0;

        foreach (var (monster, weight) in monsterWeights)
        {
            cumulativeWeight += weight;
            if (randomValue < cumulativeWeight)
            {
                RuleApplicationInfo.Publish(
                    "Adnd",
                    "House Rule",
                    $"Roll encounter creature for dungeon level {dungeonLevel} (monster level {rolledMonsterLevel})",
                    "Roll 1dN where N is total frequency weight after source filtering; choose creature by cumulative weight.",
                    "1",
                    totalWeight.ToString(),
                    (randomValue + 1).ToString(),
                    $"Selected {monster.Name} (weight {weight} of total {totalWeight}).");
                return monster;
            }
        }

        // Fallback (should never reach here)
        return monsters[_random.Next(monsters.Count)];
    }

    private void HandlePartyDefeatAtCurrentCell()
    {
        var partyData = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var movedOut = new List<string>();

        foreach (var memberName in partyData.Members)
        {
            if (!roster.TryGetValue(memberName, out var character))
                continue;

            character.DungeonLevel = _currentDungeonLevel;
            character.DungeonCellX = _position.X;
            character.DungeonCellY = _position.Y;
            character.AddStatus(CharacterStatus.Out);
            movedOut.Add(character.Name);
            _characterRepository.Save(character);
        }

        if (movedOut.Count > 0)
        {
            RuleApplicationInfo.Publish(
                $"Party defeat: moved characters to dungeon position L{_currentDungeonLevel} ({_position.X},{_position.Y}) and marked OUT: {string.Join(", ", movedOut)}.");
        }

        partyData.Members.Clear();
        partyData.CurrentShopperIndex = -1;
        _partyRepository.Save(partyData);

        Close();
    }

    private List<Character> LoadEncounterParty()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<Character>();

        foreach (var memberName in party.Members)
        {
            if (roster.TryGetValue(memberName, out var c)
                && !c.HasStatus(CharacterStatus.Out))
                result.Add(c);
        }

        return result;
    }

    private readonly HashSet<(int Level, int X, int Y, WallSide Side)> _revealedSecretDoors = new();

    // S)earch: looks for secret doors on the square's edges and for lost characters lying here.
    // Each hidden door is found on a 1 in 6; elves and half-elves search at 2 in 6.
    private void SearchArea()
    {
        var foundSomething = false;

        if (_thinWalls && _walls is not null)
        {
            var chance = GetViewerParty().Any(c => c.Race is Race.Elf or Race.HalfElf && !c.HasStatus(CharacterStatus.Dead)) ? 2 : 1;
            foreach (var side in _walls.HiddenSecretDoorSides(_position.X, _position.Y).ToList())
            {
                var roll = _random.Next(1, 7);
                var found = roll <= chance;
                RuleApplicationInfo.Publish(
                    "AD&D 1e",
                    "PHB",
                    "Searching for secret doors",
                    "Roll 1d6 per hidden door on the square: found on a 1 (1-2 when an elf or half-elf searches).",
                    "1",
                    "6",
                    roll.ToString(),
                    found ? $"Secret door found to the {side}." : "Nothing found.");

                if (!found || !_walls.RevealSecretDoor(_position.X, _position.Y, side))
                    continue;

                _revealedSecretDoors.Add((_currentDungeonLevel, _position.X, _position.Y, side));
                foundSomething = true;
                SayOnBoth("Search", $"THE PARTY FINDS A SECRET DOOR TO THE {side.ToString().ToUpperInvariant()}!");
            }
        }

        if (_thinWalls && DungeonLookAroundItems.TryGetValue((_currentDungeonLevel, _position), out var hidden) && !PartyHasItem(hidden.Item))
        {
            FindLookAroundItem(hidden.Item);
            foundSomething = true;
        }

        if (HasLostCharacterAtCurrentPosition())
        {
            TryRecoverOutCharactersAtCurrentPosition();
            return;
        }

        if (!foundSomething)
            SayOnBoth("Search", "The party searches but finds nothing.");
    }

    private bool HasLostCharacterAtCurrentPosition()
    {
        var party = _partyRepository.Load();
        return _characterRepository.GetAll().Any(c => c.HasStatus(CharacterStatus.Out)
            && c.DungeonLevel == _currentDungeonLevel
            && c.DungeonCellX == _position.X
            && c.DungeonCellY == _position.Y
            && !party.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
    }

    private void TryRecoverOutCharactersAtCurrentPosition()
    {
        var party = _partyRepository.Load();
        if (party.Members.Count >= 6)
            return;

        var roster = _characterRepository.GetAll().ToList();
        var outAtCurrentPosition = roster
            .Where(c => c.HasStatus(CharacterStatus.Out))
            .Where(c => c.DungeonLevel == _currentDungeonLevel
                        && c.DungeonCellX == _position.X
                        && c.DungeonCellY == _position.Y)
            .Where(c => !party.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var recoverable = outAtCurrentPosition
            .Where(c => !c.HasStatus(CharacterStatus.Ashes))
            .ToList();

        if (recoverable.Count == 0)
        {
            if (outAtCurrentPosition.Count > 0)
            {
                var unavailable = string.Join(", ", outAtCurrentPosition.Select(c => $"{c.Name} ({c.Status})"));
                SayOnBoth("Inspect Area", $"Found OUT character(s) here, but unavailable for recovery: {unavailable}.");
                return;
            }

            SayOnBoth("Inspect Area", "No out character found here.");
            return;
        }

        var ask = AskOnBoth("Inspect Area", "Add found character to party (Y/N)?");
        if (ask != DialogResult.Yes)
            return;

        var freeSlots = Math.Max(0, 6 - party.Members.Count);
        if (freeSlots <= 0)
        {
            SayOnBoth("Inspect Area", "Party is full.");
            return;
        }

        var picks = new List<Character>();
        var remaining = recoverable.ToList();
        while (remaining.Count > 0 && picks.Count < freeSlots)
        {
            Character chosen;
            if (remaining.Count == 1)
            {
                chosen = remaining[0];
            }
            else
            {
                var prompt = new StringBuilder();
                prompt.AppendLine("Choose character to add:");
                prompt.AppendLine();
                for (int i = 0; i < remaining.Count; i++)
                    prompt.AppendLine($"{i + 1}. {remaining[i].Name}");

                var selected = PromptForNumber("Recover Character", prompt.ToString(), 1, remaining.Count);
                if (!selected.HasValue)
                    break;

                chosen = remaining[selected.Value - 1];
            }

            chosen.RemoveStatus(CharacterStatus.Out);
            chosen.RemoveStatus(CharacterStatus.Lost);
            if (chosen.CurrentHitPoints <= 0 && !chosen.HasStatus(CharacterStatus.Dead) && !chosen.HasStatus(CharacterStatus.Ashes) && !chosen.HasStatus(CharacterStatus.Lost))
                chosen.CurrentHitPoints = 1;

            _characterRepository.Save(chosen);

            if (!party.Members.Contains(chosen.Name, StringComparer.OrdinalIgnoreCase))
                party.Members.Add(chosen.Name);

            picks.Add(chosen);
            remaining.RemoveAll(c => string.Equals(c.Name, chosen.Name, StringComparison.OrdinalIgnoreCase));

            if (remaining.Count > 0 && picks.Count < freeSlots)
            {
                var addMore = AskOnBoth("Recover Character", "Add another out character from this location?");
                if (addMore != DialogResult.Yes)
                    break;
            }
        }

        if (picks.Count > 0)
        {
            _partyRepository.Save(party);
            RuleApplicationInfo.Publish(
                $"Recovered out character(s) at L{_currentDungeonLevel} ({_position.X},{_position.Y}): {string.Join(", ", picks.Select(c => c.Name))}. Status changed OUT->IN and added to party.");
            SayOnBoth("Inspect Area", $"Added to party: {string.Join(", ", picks.Select(c => c.Name))}.");
        }
    }

    private static string GetStatusDisplay(Character c)
    {
        var statuses = new List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (c.HasStatus(CharacterStatus.Diseased)) statuses.Add("Diseased");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (c.HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (c.HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (c.HasStatus(CharacterStatus.Out)) statuses.Add("Out");
        if (c.HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        return string.Join(", ", statuses);
    }

    private Point GetCellFartherAway(Point referencePoint, Direction direction, int distance)
    {
        return WrapCell(direction switch
        {
            Direction.North => new Point(referencePoint.X, referencePoint.Y - distance),
            Direction.South => new Point(referencePoint.X, referencePoint.Y + distance),
            Direction.East => new Point(referencePoint.X + distance, referencePoint.Y),
            _ => new Point(referencePoint.X - distance, referencePoint.Y)
        });
    }

    private Point GetCellToTheLeft(Point referencePoint, Direction referenceDirection, int distance)
    {
        return WrapCell(referenceDirection switch
        {
            Direction.North => new Point(referencePoint.X - distance, referencePoint.Y),
            Direction.South => new Point(referencePoint.X + distance, referencePoint.Y),
            Direction.East => new Point(referencePoint.X, referencePoint.Y - distance),
            _ => new Point(referencePoint.X, referencePoint.Y + distance)
        });
    }

    private Point GetCellToTheRight(Point referencePoint, Direction referenceDirection, int distance)
    {
        return WrapCell(referenceDirection switch
        {
            Direction.North => new Point(referencePoint.X + distance, referencePoint.Y),
            Direction.South => new Point(referencePoint.X - distance, referencePoint.Y),
            Direction.East => new Point(referencePoint.X, referencePoint.Y + distance),
            _ => new Point(referencePoint.X, referencePoint.Y - distance)
        });
    }

    // Dungeon maps wrap around; the maze keeps its outer walls and never leaves the grid.
    private Point WrapCell(Point cell)
    {
        if (_walls is null)
            return cell;

        var (x, y) = _walls.Wrap(cell.X, cell.Y);
        return new Point(x, y);
    }

    private static Direction TurnLeft(Direction direction) => direction switch
    {
        Direction.North => Direction.West,
        Direction.West => Direction.South,
        Direction.South => Direction.East,
        _ => Direction.North
    };

    private static Direction TurnRight(Direction direction) => direction switch
    {
        Direction.North => Direction.East,
        Direction.East => Direction.South,
        Direction.South => Direction.West,
        _ => Direction.North
    };

    private static Point GetForwardVector(Direction direction) => direction switch
    {
        Direction.North => new Point(0, -1),
        Direction.East => new Point(1, 0),
        Direction.South => new Point(0, 1),
        _ => new Point(-1, 0)
    };

    private static float Lerp(float start, float end, float t) => start + ((end - start) * t);

    private void ShowElevatorDialog()
    {
        var selected = PromptElevatorLevelOnBoth();
        if (!selected.HasValue)
            return;

        var targetLevel = selected.Value;
        if (targetLevel == _currentDungeonLevel)
            return;

        _currentDungeonLevel = targetLevel;
        BuildMazeForLevel(_currentDungeonLevel);
        LoadEncounterLocationsForLevel(_currentDungeonLevel);
        _position = ElevatorPosition;
        _direction = Direction.North;
        UpdateLighting();
        // Publish here rather than waiting for the next keypress, so the table swaps levels as the
        // elevator doors close. The viewer clears the old level when it sees the new number.
        PublishToViewer();
    }

    private int? PromptElevatorLevelOnBoth()
    {
        using var form = new Form
        {
            Text = "Elevator",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(520, 620)
        };

        var framePanel = new Panel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };

        var titleLabel = new Label
        {
            Left = 0,
            Top = 10,
            Width = framePanel.ClientSize.Width,
            Height = 36,
            Text = "ELEVATOR",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var questionLabel = new Label
        {
            Left = 0,
            Top = 52,
            Width = framePanel.ClientSize.Width,
            Height = 28,
            Text = $"CURRENT LEVEL {_currentDungeonLevel}  ↵  CHOOSE ({MinDungeonLevel}-{MaxDungeonLevel})",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 13f, FontStyle.Bold)
        };

        var floorsLabel = new Label
        {
            Left = 0,
            Top = 88,
            Width = framePanel.ClientSize.Width,
            Height = 380,
            Text = string.Join(Environment.NewLine, Enumerable.Range(MinDungeonLevel, MaxDungeonLevel - MinDungeonLevel + 1).Select(n => $"{n}. Level {n}")),
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 13f, FontStyle.Bold)
        };

        var input = new TextBox
        {
            Left = (framePanel.ClientSize.Width - 90) / 2,
            Top = 486,
            Width = 90,
            MaxLength = 2,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Consolas", 16f, FontStyle.Bold)
        };

        var hintLabel = new Label
        {
            Left = 0,
            Top = 528,
            Width = framePanel.ClientSize.Width,
            Height = 24,
            Text = "ENTER=CONFIRM   ESC=CANCEL",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 10f, FontStyle.Bold)
        };

        int? selected = null;

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                form.DialogResult = DialogResult.Cancel;
                form.Close();
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (int.TryParse(input.Text.Trim(), out var parsed) && parsed >= MinDungeonLevel && parsed <= MaxDungeonLevel)
                {
                    selected = parsed;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }

                return;
            }
        };

        framePanel.Controls.Add(titleLabel);
        framePanel.Controls.Add(questionLabel);
        framePanel.Controls.Add(floorsLabel);
        framePanel.Controls.Add(input);
        framePanel.Controls.Add(hintLabel);
        form.Controls.Add(framePanel);

        var labels = Enumerable.Range(MinDungeonLevel, MaxDungeonLevel - MinDungeonLevel + 1)
            .Select(n => $"Level {n}")
            .ToList();

        var outcome = ViewerDialog.RunPick(form, this, "Elevator", null, labels, PublishTable);
        PublishToViewer();

        if (outcome.Picked.HasValue)
            return outcome.Picked.Value + 1;

        if (outcome.Result == DialogResult.OK)
        {
            if (selected.HasValue)
                return selected.Value;

            // Viewer choice path defaults to the first option if no keyboard value was entered.
            if (int.TryParse(input.Text.Trim(), out var parsed) && parsed >= MinDungeonLevel && parsed <= MaxDungeonLevel)
                return parsed;
        }

        return null;
    }
}
