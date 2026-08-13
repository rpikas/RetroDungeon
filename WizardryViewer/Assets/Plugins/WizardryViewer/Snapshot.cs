#nullable enable
using System.Collections.Generic;

namespace WizardryViewer.Protocol
{

    /// <summary>
    /// Wire format v1. Plain DTOs with no serializer attributes so the same source files can be
    /// dropped into Unity (Newtonsoft) or consumed here (System.Text.Json).
    /// See docs/viewer-protocol.md.
    /// </summary>
    public sealed class Snapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public long Seq { get; set; }

        /// <summary>
        /// Which RUN of the game this came from -- one value per game process, any value that changes.
        ///
        /// Sequence numbers only order snapshots within a run. They start again at 1 when the game restarts,
        /// and the viewer outlives any number of game processes, so without this a fresh run's first
        /// snapshots look exactly like stale duplicates and are dropped: the table silently keeps showing the
        /// previous session until the new one's counter climbs past wherever the old one stopped. Restart the
        /// game with the table up and that is minutes of a board that will not update.
        ///
        /// Null from a game too old to send it, which reads as "same run as before" -- the previous
        /// behaviour, and harmless, since such a build also never restarts its counter mid-session.
        /// </summary>
        public string? Run { get; set; }
        public string Phase { get; set; } = Phases.Town;

        public int? Level { get; set; }
        public Grid? Grid { get; set; }
        public List<string> Explored { get; set; } = new();

        /// <summary>
        /// Where the party is when not in the maze — a stable id such as "Tavern", never a display
        /// name. Which places exist and where they sit on the table is the viewer's business; the
        /// game only says which one the party is standing in. Null underground.
        /// </summary>
        public string? Location { get; set; }

        public List<PartyMember> Party { get; set; } = new();

        /// <summary>
        /// Figures on the table who are NOT in the party -- the tavern's bench, and later a shop's
        /// customers or a temple's queue.
        ///
        /// Its own list rather than extra entries in <see cref="Party"/>, because half the viewer keys off
        /// the party: the camera frames it, the lamp follows it, the party overlay lists it. Twenty
        /// benched characters in there would light the whole tavern from the middle of a crowd and call
        /// them all party members. Same shape, drawn the same way, different meaning.
        /// </summary>
        public List<PartyMember> Roster { get; set; } = new();

        public Encounter? Encounter { get; set; }

        /// <summary>
        /// Goods laid out on a counter, when the party is somewhere that trades. Empty everywhere else.
        ///
        /// A shop is the one place where the things being chosen are not people, and a price list read off a
        /// dialog is a menu wearing a tabletop's clothes. These are laid on the stall as tagged goods you point
        /// at -- Boltac's stock on his side, the shopper's own pack on theirs.
        /// </summary>
        public List<Ware> Wares { get; set; } = new();

        /// <summary>
        /// What the game is waiting for, so the table can offer it. Null when the game is not asking
        /// anything the viewer could answer — mid-animation, or with the whole party down.
        /// </summary>
        public Prompt? Prompt { get; set; }

        public List<LogEntry> Log { get; set; } = new();
    }

    public static class Phases
    {
        public const string Town = "town";
        public const string Maze = "maze";
        public const string Combat = "combat";
    }

    /// <summary>
    /// One open choice, stated by the game. Every option listed is one the game will accept right
    /// now: legality is decided where the rules live, so the viewer can render this literally and
    /// still never offer an illegal move. An option's <see cref="PromptOption.Id"/> is the same
    /// command id the viewer sends back, so a drawn button and a pressed key are indistinguishable
    /// by the time the game sees them.
    /// </summary>
    public sealed class Prompt
    {
        /// <summary>
        /// Identifies the question, not the moment it was asked: restating the same choice keeps the
        /// same id, so an answer in flight is not invalidated by an unrelated snapshot.
        /// </summary>
        public long Id { get; set; }

        /// <summary>See <see cref="PromptKinds"/>.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Human-readable statement of the question, e.g. "Grond is choosing".</summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Who the choice belongs to, as a party member id (<see cref="Ids.Character"/>), or null when
        /// it is the party's as a whole. Matches the figure on the table, so it can be highlighted.
        /// </summary>
        public string? For { get; set; }

        public List<PromptOption> Options { get; set; } = new();
    }

    public sealed class PromptOption
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";

        /// <summary>
        /// The key this option also answers to, already written the way it should be shown ("F", "Up",
        /// "Esc"), or null when this build has no key for it.
        ///
        /// A hint for the player, never a thing the viewer acts on: the viewer answers with <see
        /// cref="Id"/> and the game decides what that means. A headset with no keyboard simply shows
        /// nothing here, which is why it is optional rather than assumed.
        /// </summary>
        public string? Key { get; set; }

        /// <summary>
        /// A thing already ON the table that answers this option -- a figure id such as "char:Grond", or
        /// later an item or a statue. Null for an option that is only a button.
        ///
        /// This is what turns a menu into a tabletop: the viewer draws no button for a targeted option,
        /// it makes the object itself clickable. "Who joins the party?" stops being a numbered list and
        /// becomes picking up a figurine, which is the same gesture in a headset as with a mouse.
        ///
        /// The answer sent back is still <see cref="Id"/>, so the game cannot tell how it was chosen and
        /// nothing downstream has to care.
        /// </summary>
        public string? Target { get; set; }
    }

    public static class PromptKinds
    {
        public const string Maze = "maze";
        public const string Combat = "combat";
    }

    public sealed class Grid
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public List<string> Rows { get; set; } = new();

        /// <summary>Glyph at (x,y), or '#' outside the grid. Unknown glyphs are caller's problem.</summary>
        public char At(int x, int y)
        {
            if (y < 0 || y >= Rows.Count) return '#';
            var row = Rows[y];
            if (x < 0 || x >= row.Length) return '#';
            return row[x];
        }

        /// <summary>Anything that is not a wall is walkable, including reserved glyphs.</summary>
        public bool IsFloor(int x, int y) => At(x, y) != '#';
    }

    public sealed class PartyMember
    {
        public string Id { get; set; } = "";

        /// <summary>
        /// Whether to stand a name card beside this figure.
        ///
        /// The GAME decides, rather than the viewer inferring it from the phase. Six named cards are what
        /// makes a tavern usable and what makes a dungeon crawl unreadable, and the difference is the
        /// situation, not the place -- a shop with two customers wants names, a corridor with six marching
        /// minis does not.
        /// </summary>
        public bool ShowName { get; set; }

        /// <summary>Player-entered proper noun. The one string that is content, not an id.</summary>
        public string Name { get; set; } = "";

        public string ClassId { get; set; } = "";
        public string RaceId { get; set; } = "";
        public int Slot { get; set; }
        public int[]? Cell { get; set; }
        public string Facing { get; set; } = "North";
        public int[]? Hp { get; set; }
        public int Ac { get; set; }
        public List<string> Status { get; set; } = new();
    }

    /// <summary>
    /// One thing laid out on a counter -- a suit of armour on Boltac's shelf, or a dagger out of your own pack.
    ///
    /// Carries its own LABEL rather than an item id, because the viewer has no idea what a Battle Axe is worth
    /// or whether this character can lift it. Both sides of a trade are the same kind of object: what differs
    /// is which side of the counter it is on, which is what <see cref="Side"/> says.
    /// </summary>
    public sealed class Ware
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";

        /// <summary>A warning to show with it -- too heavy, cannot use -- or empty when it is simply for sale.</summary>
        public string Note { get; set; } = "";

        /// <summary>"shop" for Boltac's side of the counter, "pack" for the shopper's own.</summary>
        public string Side { get; set; } = "shop";
    }

    public sealed class Encounter
    {
        public int Round { get; set; }
        public List<MonsterGroup> Groups { get; set; } = new();
    }

    public sealed class MonsterGroup
    {
        public string GroupId { get; set; } = "";
        public string MonsterId { get; set; } = "";
        public int Alive { get; set; }
        public int Asleep { get; set; }

        /// <summary>
        /// File the game found this monster's picture in, or null when it has none.
        ///
        /// A path rather than the image itself: a snapshot goes out every turn and the art is up to 130kB, so
        /// sending it each time would push megabytes over the wire to repeat something the viewer already
        /// has. Loaded once per monster and kept. Unreachable paths are simply not drawn -- a viewer on
        /// another machine cannot open the game's folders, and a blank standee is the right fallback.
        /// </summary>
        public string? Art { get; set; }

        public List<MonsterMember> Members { get; set; } = new();
    }

    public sealed class MonsterMember
    {
        public string Id { get; set; } = "";
        public int Index { get; set; }
        public bool Alive { get; set; } = true;
        public List<string> Status { get; set; } = new();
    }

    /// <summary>
    /// One thing that happened, expressed as meaning rather than wording.
    ///
    /// There is deliberately NO text field: all narration, localisation and TTS belong to the
    /// viewer. Every string here is a stable identifier from the game's enums or data files,
    /// never a display string. Consumers MUST ignore unrecognised <see cref="T"/> values and
    /// tolerate unknown fields.
    /// </summary>
    public sealed class LogEntry
    {
        public string T { get; set; } = "";
        public string? By { get; set; }
        public string? At { get; set; }
        public bool? Hit { get; set; }
        public int? Amount { get; set; }
        public int? Damage { get; set; }
        public int[]? Hp { get; set; }
        public string? SpellId { get; set; }
        public string? MonsterId { get; set; }
        public string? Cause { get; set; }
        public string? Vs { get; set; }
        public string? Result { get; set; }
        public List<string>? Add { get; set; }
        public List<string>? Remove { get; set; }
        public int[]? From { get; set; }
        public int[]? To { get; set; }
        public int? Gold { get; set; }
        public List<string>? ItemIds { get; set; }
    }

    public static class LogTypes
    {
        public const string Attack = "attack";
        public const string Damage = "damage";
        public const string Heal = "heal";
        public const string Cast = "cast";
        public const string Save = "save";
        public const string Status = "status";
        public const string Death = "death";
        public const string Spawn = "spawn";
        public const string Move = "move";
        public const string Treasure = "treasure";
        public const string Experience = "experience";
    }

    public static class SaveResults
    {
        public const string Success = "success";
        public const string Failure = "failure";
    }

    public static class Ids
    {
        public static string Character(string name) => "char:" + name;
        public static string Monster(string groupId, int index) => $"mon:{groupId}#{index}";
        public static string Group(string groupId) => "group:" + groupId;

        /// <summary>
        /// A card on a counter. The side is part of the id because the same item can lie on both at once --
        /// Boltac selling a dagger while the shopper carries one -- and those are two cards with two meanings.
        /// Must agree with the game's own ViewerIds.Ware.
        /// </summary>
        public static string Ware(string side, string id) => $"ware:{side}:{id}";
    }

}
