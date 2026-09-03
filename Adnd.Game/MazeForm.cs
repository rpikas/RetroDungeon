using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;
using Adnd.Data.Characters;
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

namespace Adnd.Game;

public sealed class MazeForm : Form
{
    private enum Direction
    {
        North,
        East,
        South,
        West
    }

    private enum CellType { Wall, Floor }

    private const int MaxVisibilityDepth = 5;
    private const int DepthLevels = MaxVisibilityDepth - 1;
    private const int StraightAhead = MaxVisibilityDepth - 1;
    private const int PositionCount = (StraightAhead * 2) + 1;
    private const int TopLeft = 0;
    private const int BottomRight = 1;
  //  private const int EncounterChanceDenominator = 4;

    private static readonly string[] LevelOneMonsters =
    {
        "Goblin",
        "Skeleton"
    };

    private readonly PointF[,,] _backWallCoords = new PointF[DepthLevels, PositionCount, 2];

    private CellType[,] _maze;
    private Point _position = new(0, 0);
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

    // Lets the viewer send moves back. Null when no viewer is configured, and idle when none is
    // running; the keyboard is unaffected either way.
    private ViewerControlPump? _viewerControl;

    private void BuildMazeForLevel(int level)
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

    public MazeForm()
    {
        Text = "Maze";
        ClientSize = new Size(1200, 820);
        KeyPreview = true;
        BackColor = Color.Black;
        ForeColor = GameRulesProvider.Current.DefaultColor;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        PruneUnavailablePartyMembers();
        BuildMazeForLevel(_currentDungeonLevel);
        KeyDown += MazeForm_KeyDown;

        // Follow the fight. These are observers: combat neither waits for them nor changes course.
        _combatCoordinator.EncounterStarted += session =>
        {
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
            if (GameRulesProvider.Current.ShowDiceRollAndRuleApplicationInfo)
            {
                _ruleApplicationInfoForm = new RuleApplicationInfoForm();
                _ruleApplicationInfoHandler = message => _ruleApplicationInfoForm?.AppendInfo(message);
                RuleApplicationInfo.InfoPublished += _ruleApplicationInfoHandler;
                _ruleApplicationInfoForm.Show();
                _ruleApplicationInfoForm.BringToFront();
                RuleApplicationInfo.Publish("Rule/dice diagnostics enabled.");
            }

            if (_position.X == 1 && _position.Y == 2)
            {
                ShowElevatorDialog();
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
            _viewerControl?.Dispose();
            if (_ruleApplicationInfoHandler != null)
                RuleApplicationInfo.InfoPublished -= _ruleApplicationInfoHandler;

            if (_ruleApplicationInfoForm != null && !_ruleApplicationInfoForm.IsDisposed)
                _ruleApplicationInfoForm.Close();
        };
    }

    private void PruneUnavailablePartyMembers()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var filtered = party.Members
            .Where(name =>
                roster.TryGetValue(name, out var c)
                && !c.HasStatus(CharacterStatus.Dead)
                && !c.HasStatus(CharacterStatus.Ashes)
                && !c.HasStatus(CharacterStatus.Lost))
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

            return;
        }
    }

    private void ShowReorderCampDialog(Adnd.Data.Party.Party party, List<string> activeMembers, Dictionary<string, Character> roster)
    {
        if (activeMembers.Count < 2)
        {
            SayOnBoth("Camp", "Need at least two party members to reorder.");
            return;
        }

        List<string>? reorderedResult = null;
        var picked = new List<int>();
        var remaining = Enumerable.Range(0, activeMembers.Count).ToList();

        using var form = new Form();
        form.Text = "Camp - Reorder Party";
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(860, 215);
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.KeyPreview = true;

        var instructions = new Label
        {
            Left = 12,
            Top = 10,
            Width = 836,
            Height = 24,
            Text = "Select a member and use > to add to new order, < to remove. Enter = confirm, Esc = cancel"
        };

        var oldLabel = new Label
        {
            Left = 12,
            Top = 38,
            Width = 370,
            Height = 24,
            Text = "Old order"
        };

        var newLabel = new Label
        {
            Left = 478,
            Top = 38,
            Width = 370,
            Height = 24,
            Text = "New order"
        };

        var oldList = new ListBox
        {
            Left = 12,
            Top = 62,
            Width = 370,
            Height = 110,
            Font = new Font("Consolas", 10f)
        };

        var newList = new ListBox
        {
            Left = 478,
            Top = 62,
            Width = 370,
            Height = 110,
            Font = new Font("Consolas", 10f)
        };

        var moveRightBtn = new Button
        {
            Text = ">",
            Left = 400,
            Top = 82,
            Width = 60,
            Height = 30
        };

        var moveLeftBtn = new Button
        {
            Text = "<",
            Left = 400,
            Top = 122,
            Width = 60,
            Height = 30
        };

        var confirmBtn = new Button
        {
            Text = "Confirm",
            Left = 692,
            Top = 178,
            Width = 75,
            DialogResult = DialogResult.None
        };

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Left = 773,
            Top = 178,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        string Row(Character c, int row)
        {
            var cls = c.Classes.Count > 0 ? string.Join("/", c.Classes.Select(x => x.ToDisplayString())) : c.Class.ToDisplayString();
            var hp = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
            return $"{row,2}. {c.Name,-12} {cls,-14} HP {hp,-7} AC {c.ArmorClass,2}";
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

        var label = new Label
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
                case Keys.S:
                    ShowPartyPopup("Party Status");
                    return;
                case Keys.I:
                    ShowPartyPopup("Party Inspect");
                    return;
            }

            // When overlay is visible, any other keys should still be processed below
            // so movement and turning continue to work while party stats are shown.
        }

        switch (e.KeyCode)
        {
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
        var frames = BuildFrames(viewport, DepthLevels);

        for (int depth = 0; depth < DepthLevels; depth++)
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
        for (int depth = DepthLevels - 1; depth >= 0; depth--)
        {
            var centerCell = GetCellFartherAway(_position, _direction, depth);
            DrawCellContents(graphics, pen, viewport, centerCell, depth, StraightAhead);

            for (int i = 1; i <= depth + 1; i++)
            {
                var leftCell = GetCellToTheLeft(centerCell, _direction, i);
                DrawCellContents(graphics, pen, viewport, leftCell, depth, StraightAhead - i);

                var rightCell = GetCellToTheRight(centerCell, _direction, i);
                DrawCellContents(graphics, pen, viewport, rightCell, depth, StraightAhead + i);
            }
        }
    }

    private void DrawCellContents(Graphics graphics, Pen pen, RectangleF viewport, Point cell, int depth, int position)
    {
        if (!IsOpen(cell) || depth < 0 || depth >= DepthLevels || position < 0 || position >= PositionCount)
            return;

        var back = GetWallRect(depth, position);
        if (back.Height < 2f)
            return;

        bool backWallDrawn = false;
        bool leftWallDrawn = false;
        bool rightWallDrawn = false;

        var frontCell = GetCellFartherAway(cell, _direction, 1);
        if (!IsOpen(frontCell))
        {
            using var wallBrush = new SolidBrush(Color.Black);
            graphics.FillRectangle(wallBrush, back);
            graphics.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
            backWallDrawn = true;
        }

        if (position <= StraightAhead)
        {
            var leftCell = GetCellToTheLeft(cell, _direction, 1);
            if (!IsOpen(leftCell))
            {
                DrawLeftWall(graphics, pen, viewport, depth, position, back);
                leftWallDrawn = true;
            }
        }

        if (position >= StraightAhead)
        {
            var rightCell = GetCellToTheRight(cell, _direction, 1);
            if (!IsOpen(rightCell))
            {
                DrawRightWall(graphics, pen, viewport, depth, position, back);
                rightWallDrawn = true;
            }
        }

        var frontLeft = GetCellToTheLeft(frontCell, _direction, 1);
        var frontRight = GetCellToTheRight(frontCell, _direction, 1);

        bool drawLeftVertical = (backWallDrawn && (leftWallDrawn || !IsOpen(frontLeft))) ||
                                (leftWallDrawn && !IsOpen(frontLeft));
        bool drawRightVertical = (backWallDrawn && (rightWallDrawn || !IsOpen(frontRight))) ||
                                 (rightWallDrawn && !IsOpen(frontRight));

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

    private void DrawLeftWall(Graphics graphics, Pen pen, RectangleF viewport, int depth, int position, RectangleF back)
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
    }

    private void DrawRightWall(Graphics graphics, Pen pen, RectangleF viewport, int depth, int position, RectangleF back)
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
        graphics.DrawString("Class", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 190f, y));
        graphics.DrawString("Lvl", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 360f, y));
        graphics.DrawString("HP", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 420f, y));
        graphics.DrawString("AC", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 520f, y));
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
                graphics.DrawString("(missing)", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 190f, y));
                y += 18f;
                continue;
            }

            var classes = c.Classes.Count > 0
                ? string.Join("/", c.Classes.Select(m => m.ToDisplayString()))
                : c.Class.ToDisplayString();

            var status = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";

            graphics.DrawString((i + 1).ToString(), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x, y));
            graphics.DrawString(c.Name, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth, y));
            graphics.DrawString(classes, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 190f, y));
            graphics.DrawString(GetLevelDisplay(c), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 360f, y));
            graphics.DrawString($"{c.CurrentHitPoints}/{c.MaxHitPoints}", Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 420f, y));
            graphics.DrawString(c.ArmorClass.ToString(), Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 520f, y));
            graphics.DrawString(status, Font, new SolidBrush(GameRulesProvider.Current.DefaultColor), new PointF(x + numberColWidth + 600f, y));

            y += 18f;
        }
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
                ? string.Join("/", c.Classes.Select(x => x.ToDisplayString()))
                : c.Class.ToDisplayString();

            result.Add($"{c.Name}  {classes}  L{GetLevelDisplay(c)}  HP {c.CurrentHitPoints}/{c.MaxHitPoints}  AC {c.ArmorClass}");
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
            float t = 1f - (float)Math.Pow(1f - u, 1.8f);

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
            ClientSize = new Size(430, 110),
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
            Text = "STAIRS UP",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        var questionLabel = new Label
        {
            Left = 0,
            Top = 48,
            Width = framePanel.ClientSize.Width,
            Height = 34,
            Text = "TAKE THEM (Y/N) ?",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
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

        var ahead = GetForwardVector(_direction);
        if (IsOpen(new Point(_position.X + ahead.X, _position.Y + ahead.Y)))
            options.Add(new ViewerPromptOption("forward", "Forward"));

        options.Add(new ViewerPromptOption("turnLeft", "Turn left"));
        options.Add(new ViewerPromptOption("turnRight", "Turn right"));
        options.Add(new ViewerPromptOption("party", _partyOverlayVisible ? "Hide party" : "Show party"));

        if (_partyOverlayVisible)
        {
            options.Add(new ViewerPromptOption("camp", "Camp"));
            options.Add(new ViewerPromptOption("status", "Party status"));
            options.Add(new ViewerPromptOption("inspect", "Inspect"));
        }

        return new ViewerPrompt("maze", "The party waits in the maze.", For: null, options);
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
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<Character>();
        foreach (var memberName in party.Members)
        {
            if (roster.TryGetValue(memberName, out var c))
                ordered.Add(c);
        }

        return ordered;
    }

    private void TryMoveForward()
    {
        var v = GetForwardVector(_direction);
        var candidate = new Point(_position.X + v.X, _position.Y + v.Y);
        if (IsOpen(candidate))
        {
            _position = candidate;
            ApplyPoisonDamageForStep();

            if (_position.X == 1 && _position.Y == 2)
            {
                ShowElevatorDialog();
            }

            if (_currentDungeonLevel == 1 && _position.X == 0 && _position.Y == 0)
            {
                var result = AskOnBoth("Stairs", "Stairs up, take them?");

                if (result == DialogResult.Yes)
                {
                    Close();
                    return;
                }
            }

            TryRandomEncounter();

            var delayMs = Math.Max(0, GameRulesProvider.Current.DelayInMsbetweenActions);
            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }
    }

    private void ApplyPoisonDamageForStep()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        var changed = false;

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (c.CurrentHitPoints > 0
                && !c.HasStatus(CharacterStatus.Dead)
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

        if (!changed)
            return;

        foreach (var c in roster.Values)
            _characterRepository.Save(c);

        SayOnBoth("Effects", string.Join(Environment.NewLine, messages));
    }

    private static bool HasEquippedRegeneration(Character c)
    {
        return c.Equipment.Values
            .Where(item => item != null)
            .Any(item => item!.SpecialAbilities.Any(a =>
                string.Equals(a?.Trim(), "Regeneration (1)", StringComparison.OrdinalIgnoreCase)));
    }

    private void TryRandomEncounter()
    {
        var configuredChance = GameRulesProvider.ClampChance(GameRulesProvider.Current.MonsterEncounterChance);

        bool shouldEncounter;

        if (configuredChance > 0)
        {
            shouldEncounter = _random.NextDouble() < configuredChance;
        }
        else
        {
            shouldEncounter = false;
        }

        if (!shouldEncounter)
            return;

        var monsterName = RollDungeonMonsterForLevel(_currentDungeonLevel);
        if (string.IsNullOrWhiteSpace(monsterName))
            monsterName = LevelOneMonsters[_random.Next(LevelOneMonsters.Length)];

        if (string.Equals(monsterName, "No Encounter", StringComparison.OrdinalIgnoreCase))
            return;

        var party = LoadEncounterParty();
        if (party.Count == 0)
        {
            SayOnBoth("Monsters", $"Encounter!\n\n{monsterName}\n\n(No active party members found)");
            return;
        }

        // Determine number of monster groups that appear
        // 60% = 1 group, 25% = 2 groups, 10% = 3 groups, 5% = 4 groups
        int numberOfGroups = 1;
        var roll = _random.NextDouble();
        if (roll < 0.01) // 1%
        {
            numberOfGroups = 4;
        }
        else if (roll < 0.06) // 5% (0.01 + 0.05)
        {
            numberOfGroups = 3;
        }
        else if (roll < 0.20) // 14% (0.06 + 0.14)
        {
            numberOfGroups = 2;
        }
        // else: 80% (remaining) = 1 group

        CombatOutcome outcome;
        if (numberOfGroups > 1)
        {
            // Generate multiple groups
            var monsterNames = new string[numberOfGroups];
            monsterNames[0] = monsterName;

            for (int i = 1; i < numberOfGroups; i++)
            {
                var additionalMonsterName = RollDungeonMonsterForLevel(_currentDungeonLevel);
                if (string.IsNullOrWhiteSpace(additionalMonsterName))
                    additionalMonsterName = LevelOneMonsters[_random.Next(LevelOneMonsters.Length)];
                monsterNames[i] = additionalMonsterName;
            }

            outcome = _combatCoordinator.StartEncounterWithMultipleGroups(
                this,
                monsterNames,
                party,
                _characterRepository,
                _monsterRepository,
                _currentDungeonLevel);
        }
        else
        {
            // Single group encounter
            var monster = _monsterRepository.GetAll().FirstOrDefault(m => string.Equals(m.Name, monsterName, StringComparison.OrdinalIgnoreCase));
            int numberOfMonsters;
            if (monster != null)
            {
                numberOfMonsters = _random.Next(monster.NumberOfAppearancesMin, monster.NumberOfAppearancesMax + 1);
            }
            else
            {
                numberOfMonsters = _random.Next(1, 7); // 1d6 fallback
            }


            // Monsters reach the table from the coordinator's own events, which carry the real
            // instances — no need to guess counts here.
            outcome = _combatCoordinator.StartEncounter(this, monsterName, numberOfMonsters, party, _characterRepository, _currentDungeonLevel);
        }
        if (outcome == CombatOutcome.Defeat)
        {
            HandlePartyDefeatAtCurrentCell();
        }

        // The fight is over: clear the monsters and show the damage taken.
        PublishToViewer();
    }

    private string? RollDungeonMonsterForLevel(int level)
    {
        var rolledMonsterLevel = RollMonsterLevelFromEncounterTable(level);
        if (rolledMonsterLevel <= 0)
            return null;

        if (GameRulesProvider.Current.MonsterSourceOptions == SourceOptions.OnlyAdndDMGEncounterTable)
        {
            return RollFromDmgEncounterTable(rolledMonsterLevel, level);
        }

        // Get all monsters for the rolled monster level
        var monstersForLevel = _monsterRepository.GetAll()
            .Where(m => m.DungeonLevel == rolledMonsterLevel)
            .ToList();

        if (monstersForLevel.Count == 0)
            return null;

        // Filter by source based on game rules
        var sourceOption = GameRulesProvider.Current.MonsterSourceOptions;
        var filteredMonsters = FilterMonstersBySource(monstersForLevel, sourceOption);

        if (filteredMonsters.Count == 0)
            return null;

        // Use frequency-based weighted selection
        return SelectMonsterByFrequencyWeight(filteredMonsters, level, rolledMonsterLevel)?.Name;
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
                "Use MonsterEncounterTable: roll 1d20 and map to encounter monster level.",
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

    private string? RollFromDmgEncounterTable(int monsterLevel, int dungeonLevel)
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

        // Strict DMG table mode: use this level's 1d100 table only (no house-rule fallback).
        // Retry a few times in case a rolled creature cannot be mapped to a local monster name.
        for (int attempt = 0; attempt < 20; attempt++)
        {
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
                var resolved = ResolveDmgCreatureToMonsterName(creature);
                RuleApplicationInfo.Publish(
                    "DMG",
                    "174",
                    $"Roll encounter creature for dungeon level {dungeonLevel} (monster level {monsterLevel})",
                    $"Use encounter table Level{monsterLevel}; roll 1d100 and find matching DiceMin-DiceMax range.",
                    "1",
                    "100",
                    roll.ToString(),
                    string.IsNullOrWhiteSpace(resolved)
                        ? $"Matched '{creature}', but no monster mapping was found. Rerolling on Level{monsterLevel}."
                        : $"Matched '{creature}', mapped to '{resolved}'.");

                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;

                break;
            }
        }

        RuleApplicationInfo.Publish($"DMG encounter table Level{monsterLevel} could not map a rolled creature to a local monster after multiple rerolls.");
        return null;
    }

    private string? ResolveDmgCreatureToMonsterName(string? dmgCreature)
    {
        if (string.IsNullOrWhiteSpace(dmgCreature))
            return null;

        if (string.Equals(dmgCreature.Trim(), "Character", StringComparison.OrdinalIgnoreCase))
            return "Adventurer";

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
            var exact = allMonsters.FirstOrDefault(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact.Name;
        }

        var normalizedCandidates = candidateNames
            .Select(NormalizeMonsterName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var monster in allMonsters)
        {
            var normalizedMonster = NormalizeMonsterName(monster.Name);
            if (normalizedCandidates.Contains(normalizedMonster, StringComparer.OrdinalIgnoreCase))
                return monster.Name;
        }

        return null;
    }

    private static string NormalizeMonsterName(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return string.Join(" ", new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
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

        foreach (var memberName in partyData.Members)
        {
            if (!roster.TryGetValue(memberName, out var character))
                continue;

            character.DungeonLevel = _currentDungeonLevel;
            character.DungeonCellX = _position.X;
            character.DungeonCellY = _position.Y;
            _characterRepository.Save(character);
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
            if (roster.TryGetValue(memberName, out var c))
                result.Add(c);
        }

        return result;
    }

    private static string GetStatusDisplay(Character c)
    {
        var statuses = new List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (c.HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (c.HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (c.HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        return string.Join(", ", statuses);
    }

    private static Point GetCellFartherAway(Point referencePoint, Direction direction, int distance)
    {
        return direction switch
        {
            Direction.North => new Point(referencePoint.X, referencePoint.Y - distance),
            Direction.South => new Point(referencePoint.X, referencePoint.Y + distance),
            Direction.East => new Point(referencePoint.X + distance, referencePoint.Y),
            _ => new Point(referencePoint.X - distance, referencePoint.Y)
        };
    }

    private static Point GetCellToTheLeft(Point referencePoint, Direction referenceDirection, int distance)
    {
        return referenceDirection switch
        {
            Direction.North => new Point(referencePoint.X - distance, referencePoint.Y),
            Direction.South => new Point(referencePoint.X + distance, referencePoint.Y),
            Direction.East => new Point(referencePoint.X, referencePoint.Y - distance),
            _ => new Point(referencePoint.X, referencePoint.Y + distance)
        };
    }

    private static Point GetCellToTheRight(Point referencePoint, Direction referenceDirection, int distance)
    {
        return referenceDirection switch
        {
            Direction.North => new Point(referencePoint.X + distance, referencePoint.Y),
            Direction.South => new Point(referencePoint.X - distance, referencePoint.Y),
            Direction.East => new Point(referencePoint.X, referencePoint.Y + distance),
            _ => new Point(referencePoint.X, referencePoint.Y - distance)
        };
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
        var sb = new StringBuilder();
        sb.AppendLine("Dungeon elevator");
        sb.AppendLine();
        sb.AppendLine($"Current level: {_currentDungeonLevel}");
        sb.AppendLine("Choose destination level (1-10):");

        var selected = PromptForNumber("Elevator", sb.ToString(), 1, 10);
        if (!selected.HasValue)
            return;

        var targetLevel = selected.Value;
        if (targetLevel == _currentDungeonLevel)
            return;

        _currentDungeonLevel = targetLevel;
        BuildMazeForLevel(_currentDungeonLevel);
        _position = new Point(1, 2);
        _direction = Direction.North;

        // Publish here rather than waiting for the next keypress, so the table swaps levels as the
        // elevator doors close. The viewer clears the old level when it sees the new number.
        PublishToViewer();
    }
}
