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

// Static per-level dungeon tables: stairs, warps, pits, messages, items.
public sealed partial class MazeForm
{
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
        [(10, new Point(10, 0))] = new Point(14, 2),
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
        [(2, new Point(11, 15))] = "A PLACARD NEAR THE GROUND READ: \"A DUNGEON'S DARK ...\"",
        [(2, new Point(11, 14))] = "A PLACARD NEAR THE GROUND READ: \"WHEN IT'S NOT LIT ...\"",
        [(2, new Point(11, 13))] = "A PLACARD NEAR THE GROUND READ: \"WATCH OUT. OR YOU'LL ..\""
        }
    }.SelectMany(m => m).ToDictionary(p => p.Key, p => p.Value);

    // Statues the party can look around at (the prompt on the square, or pressing S there) to find an item.
    private static readonly Dictionary<(int Level, Point Cell), (string Item, string Message)> DungeonLookAroundItems = new()
    {
        [(2, new Point(18, 9))] = ("Statue of Bear", Lines("YOU SEE A STATUE OF A BEAR ON A PEDESTAL.",
            "ON THE WALL IS A SIGN READING \"I'VE GOT A MILLION OF'EM\".")),
        [(2, new Point(4, 12))] = ("Statue of Frog", Lines("ON A SILVERY DISK STANDS A STATUE OF A FROG WEARING A RED AND BLUE CAPE.",
            "THE STATUE ANIMATES AND SHAKES ITS LEGS WHILE IT YELLS \"YEAH .. YEAH ..\"")),
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

    // Doors that only open for a party carrying a named item, keyed by level, square and the side crossed.
    private static readonly Dictionary<(int Level, Point From, Direction Side), (string Item, string Message)> DungeonGatedDoors = new()
    {
        [(4, new Point(13, 17), Direction.West)] = ("Statue of Bear",
            "A LARGE SLIDING WALL WITH THE IMAGE OF A BEAR UPON IT BLOCKS YOUR PATH AND FORCES YOU OUT OF THE ROOM." + Environment.NewLine
            + "POSSESSION OF THE STATUE OF BEAR IS REQUIRED TO PASS THROUGH THIS SECTION.")
    };

    // Squares where all magic is sealed (X = north, Y = east), keyed by level: level 5's Hall of Silence.
    private static readonly Dictionary<int, (int X1, int Y1, int X2, int Y2)> DungeonMagicSealedAreas = new()
    {
        [5] = (7, 10, 12, 16)
    };
}
