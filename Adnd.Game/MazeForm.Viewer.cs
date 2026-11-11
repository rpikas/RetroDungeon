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

// Publishing the maze and combat state to the tabletop viewer.
public sealed partial class MazeForm
{
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
        AwardWerdnaAmuletReward();
        RestoreStrengthDrainedOnDungeonExit();
        ClearProtectOnDungeonExit();
        RevertLycanthropeTransformationsOnDungeonExit();
        ApplyPartyAgingForDungeonExitDay();
        ApplyDiseaseProgressForDungeonExitDay();
        Close();
    }
}
