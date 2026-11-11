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

public sealed partial class MazeForm : Form
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
    private bool _protectActive;

    private IReadOnlyList<DungeonDarkArea> _darkAreas = Array.Empty<DungeonDarkArea>();

    private CellType[,] _maze;

    private Point _position = new(0, 0);

    // The elevator is at (1,2) in the Maze and at (8,10) in the Dungeon on levels 1-4. The private express
    // elevator is at (0,10) on levels 5-9, and level 4 has it as well (see PrivateElevatorSquare).
    private static readonly Point PrivateElevatorSquare = new(0, 10);

    private Point ElevatorPosition => _thinWalls ? (_currentDungeonLevel >= 5 ? PrivateElevatorSquare : new Point(8, 10)) : new Point(1, 2);

    private bool IsElevatorSquare(Point cell)
        => cell == ElevatorPosition || (_thinWalls && _currentDungeonLevel == 4 && cell == PrivateElevatorSquare);

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

    private readonly bool _cityMode;

    private string DungeonName => _thinWalls ? "Proving Grounds" : "Maze";

    /// <summary>The window in the city: the same RetroDungeon form, showing the castle and the text menus.</summary>
    internal static MazeForm CreateCityHost() => new(thinWalls: false, cityMode: true);

    public MazeForm(bool thinWalls = false, bool cityMode = false)
    {
        _thinWalls = thinWalls;
        _cityMode = cityMode;
        Text = cityMode ? "RetroDungeon" : $"RetroDungeon - {DungeonName}";
        if (!cityMode)
            Adnd.Core.Spells.WizardryDungeonPosition.Provider = () => $"You are at {PositionText} on dungeon level {_currentDungeonLevel}, facing {CompassHeading()}.";
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

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        if (cityMode)
        {
            InitCityHost();
            return;
        }

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
            if (IsElevatorSquare(_position))
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

    /// <summary>
    /// A command from the tabletop viewer, replayed as the key it stands for. Deliberately the same
    /// entry point the keyboard uses, so moving, turning, the overlay and the snapshot publish below
    /// all take the ordinary path and nothing downstream knows where the input came from.
    /// </summary>
    internal void InjectViewerKey(Keys key) => MazeForm_KeyDown(this, new KeyEventArgs(key));

    private EncounterForm? _encounterForm;

    /// <summary>A fight is shown inside this window: the maze graphics give way to it until it is disposed.</summary>
    internal void AttachEncounter(EncounterForm form)
    {
        _encounterForm = form;
        Invalidate();
    }

    internal void DetachEncounter(EncounterForm form)
    {
        if (_encounterForm != form)
            return;

        _encounterForm = null;
        RefreshProtectSign();
        if (!IsDisposed)
        {
            Invalidate();
            Focus();
        }
    }

    private void MazeForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // Key preview sees the fight's keys first; the maze must not walk while one is on.
        if (_encounterForm != null)
            return;

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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Adnd.Core.Spells.Casting.SpellCastingService.MagicSealed = false;
        base.OnFormClosed(e);
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
}
