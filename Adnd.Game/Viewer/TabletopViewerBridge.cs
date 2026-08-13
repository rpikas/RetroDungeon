// Optional bridge to the Wizardry tabletop viewer, which draws the crawl as physical miniatures
// on a table (1 inch per dungeon cell, print-scale figures) instead of a first-person view.
//
// Two rules hold this together:
//   1. The game never depends on the viewer. If nothing is listening, every call here is a no-op
//      and the player sees no difference.
//   2. Nothing here may throw into the game loop, and nothing may block it. All I/O is
//      fire-and-forget on a background task with a short timeout.
//
// The wire format is the viewer's snapshot protocol v1: a full statement of what is true right
// now, not a diff. Publishing the whole picture every turn means a dropped or late snapshot can
// never leave the table wrong.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Game.Viewer;

public sealed class TabletopViewerBridge
{
    // The viewer's HttpListener binds 127.0.0.1 specifically, so the literal address is required:
    // a "localhost" Host header does not match its prefix and comes back 400 with nothing logged.
    private const string DefaultEndpoint = "http://127.0.0.1:8787/state";

    /// <summary>Set ADND_VIEWER=0 to stop even trying.</summary>
    private const string DisableVariable = "ADND_VIEWER";

    /// <summary>Set ADND_VIEWER_URL to point at a viewer somewhere other than this machine.</summary>
    private const string EndpointVariable = "ADND_VIEWER_URL";

    // One client for the process. The timeout is the whole point: a turn must never wait on this.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(750) };

    private readonly string _endpoint;
    private readonly string _commandEndpoint;
    private readonly bool _enabled;

    // The viewer drops any snapshot whose Seq is not greater than the last one it accepted, so this
    // must increase on every publish. Process-wide, NOT per instance: two bridges counting
    // independently means whichever starts later is ignored until its count overtakes the other's.
    // That showed up as the maze refusing to appear until the party moved, because the town
    // publishes had already run the number up.
    private static long _seq;

    // Which run of the game this is. Sent with every snapshot so the viewer can tell a restarted game from
    // stale packets: the counter above starts again at 1 when the process does, and the viewer keeps running
    // across any number of game launches. Without this it read a new run's first snapshots as duplicates of
    // the old run's and dropped them, leaving the table showing the previous session until the count caught
    // up -- which is minutes of a board that will not update, or forever if the old run got further.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    // Cells the party has stood in, per level. The game keeps no fog of war of its own and the
    // viewer lays only what has been mapped, so that memory lives here.
    private readonly Dictionary<int, HashSet<(int X, int Y)>> _visited = new();

    // A prompt's id changes only when the question itself changes, NOT on every publish: snapshots
    // go out for all sorts of unrelated reasons (an overlay toggle, a monster dying), and a viewer
    // that eventually echoes this id back with its answer must not have that answer rejected merely
    // because the same question was restated in the meantime.
    // STATIC, for the same reason the sequence counter above is: several bridges exist in one process --
    // the hub holds one, the maze another, a fight builds its own through the pump -- and an id that only
    // counts within an instance is not an id. The hub's first prompt and a fight's first prompt were both
    // 1, and the viewer reads an unchanged id as the same question restated: it kept the buttons it already
    // had. That is why a fight showed the hub's Next place and Into the maze instead of Fight and Parry,
    // with only the line above them changing. Guarded because the key and the counter must move together.
    private static string? _lastPromptKey;
    private static long _promptId;
    private static readonly object PromptGate = new();

    public TabletopViewerBridge()
    {
        var disable = Environment.GetEnvironmentVariable(DisableVariable);
        _enabled = !string.Equals(disable, "0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(disable, "false", StringComparison.OrdinalIgnoreCase);

        var configured = Environment.GetEnvironmentVariable(EndpointVariable);
        _endpoint = string.IsNullOrWhiteSpace(configured) ? DefaultEndpoint : configured!;

        // Commands come back from the same viewer, from /state's sibling. Derived rather than
        // configured separately, so pointing ADND_VIEWER_URL at another machine moves both
        // directions at once and they cannot drift apart.
        _commandEndpoint = SiblingEndpoint(_endpoint, "command");
    }

    /// <summary>Whether a viewer is wanted at all. False when ADND_VIEWER=0.</summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Where the viewer offers queued player commands, e.g. "http://127.0.0.1:8787/command".
    /// Polled by <see cref="ViewerControlPump"/>; nothing here reads it.
    /// </summary>
    public string CommandEndpoint => _commandEndpoint;

    private static string SiblingEndpoint(string endpoint, string leaf)
    {
        var cut = endpoint.LastIndexOf('/');

        // No path to replace (someone configured a bare host): append instead of mangling it.
        if (cut < 0 || cut < endpoint.IndexOf("//", StringComparison.Ordinal) + 2)
            return endpoint.TrimEnd('/') + "/" + leaf;

        return endpoint.Substring(0, cut + 1) + leaf;
    }

    /// <summary>One monster group as the viewer wants to hear about it.</summary>
    public sealed record MonsterGroupView(string MonsterId, int Total, int Alive, int Asleep);

    // Who was alive and how hurt, as of the last combat publish. Round-to-round changes are found
    // by comparing against this: CombatEvent carries only a display string, so nothing structured
    // can be recovered from the round's own output.
    private readonly Dictionary<string, int> _lastHp = new();
    private readonly HashSet<string> _lastDead = new();

    /// <summary>
    /// Publish a combat round: the monsters actually in the session, plus beats for what the party
    /// chose to do and who died. Beats are theatre — the viewer animates them but reconciles to the
    /// state either way, so a dropped one costs nothing.
    /// </summary>
    public void PublishCombat(
        int level,
        Func<int, int, bool> isFloor,
        int width,
        int height,
        int cellX,
        int cellY,
        string facing,
        IReadOnlyList<Character> party,
        IReadOnlyList<MonsterInstance> monsters,
        int roundNumber,
        IReadOnlyDictionary<string, CombatAction> chosenActions = null,
        ViewerPrompt? prompt = null)
    {
        if (!_enabled) return;

        try
        {
            var beats = new List<object>();

            // What the party chose. These are ids and enum names, never display text.
            if (chosenActions != null)
            {
                foreach (var kv in chosenActions)
                {
                    var action = kv.Value;
                    if (action == null) continue;

                    // Only actions that are a visible gesture get a beat. Parry, Run and UseItem
                    // are choices, not motions, and inventing a lunge for them would misreport
                    // what the character did.
                    string type;
                    switch (action.Type)
                    {
                        case CombatActionType.Fight: type = "attack"; break;
                        case CombatActionType.Spell:
                        case CombatActionType.CastSpell: type = "cast"; break;
                        default: continue;
                    }

                    // Who the blow is aimed at, in order of how much we actually know:
                    //
                    // A monster the player picked out is the truth, so it is used as-is. Otherwise the choice
                    // was a group at best and which member gets hit is decided during resolution, so lean
                    // toward the first living one -- the gesture then points the right way without inventing
                    // a victim. This used to be the ONLY case, which meant an attack aimed at the third orc
                    // was animated and narrated against the first: the table said "Brann swings at Orc #1"
                    // while the dice were being rolled against Orc #3.
                    var aimed = action.TargetMonsterId;
                    var target = string.IsNullOrEmpty(aimed)
                        ? FirstAliveIn(monsters, action.TargetGroupId)
                        : MonsterIdOf(monsters, aimed) ?? FirstAliveIn(monsters, action.TargetGroupId);

                    beats.Add(new
                    {
                        T = type,
                        By = "char:" + kv.Key,
                        At = target,
                        SpellId = action.SpellId,
                    });
                }
            }

            // Deaths and damage, found by diffing rather than by reading round messages.
            foreach (var m in monsters)
            {
                var id = MonsterId(m);
                if (!m.IsAlive && _lastDead.Add(id))
                    beats.Add(new { T = "death", At = id });
            }

            foreach (var c in party)
            {
                var id = "char:" + c.Name;
                var dead = c.CurrentHitPoints <= 0 || c.Status.HasFlag(CharacterStatus.Dead);
                if (dead)
                {
                    if (_lastDead.Add(id)) beats.Add(new { T = "death", At = id });
                }
                else if (_lastHp.TryGetValue(id, out var was) && c.CurrentHitPoints < was)
                {
                    beats.Add(new
                    {
                        T = "damage",
                        At = id,
                        Amount = was - c.CurrentHitPoints,
                        Hp = new[] { c.CurrentHitPoints, c.MaxHitPoints },
                    });
                }
                _lastHp[id] = c.CurrentHitPoints;
            }

            var groups = GroupsOf(monsters);
            var payload = BuildPayload(level, isFloor, width, height, cellX, cellY, facing,
                                       party, groups, monsters, roundNumber, beats, prompt);
            Post(JsonSerializer.Serialize(payload));
        }
        catch
        {
            // Never spoil a fight for the sake of a picture.
        }
    }

    /// <summary>
    /// Tell the viewer to lay out the tavern: the party in a row of slots, everyone else on a bench
    /// behind them, every figure named, and a prompt whose options point at the figures.
    ///
    /// Sends a GRID rather than a town location, which is what keeps this cheap: a room with a floor and
    /// walls is something the viewer already draws, so the tavern needs no scenery of its own. The town
    /// board with its place pads is a different view -- outside, choosing where to go -- and mixing the
    /// two would put a crowd of figurines among the signposts.
    ///
    /// Cells are decided HERE because the game knows how many people there are; the viewer only stands
    /// figures where it is told, exactly as underground.
    /// </summary>
    public void PublishTavern(IReadOnlyList<Character> party, IReadOnlyList<Character> bench,
                              ViewerPrompt prompt)
    {
        if (!_enabled) return;

        try
        {
            const int benchColumns = 6;
            const int margin = 1;

            var benchRows = Math.Max(1, (bench.Count + benchColumns - 1) / benchColumns);

            // Party row at the front, a gap, then the bench behind it. Width fits the wider of the two.
            var interiorWidth = Math.Max(TavernSession.PartySlots, Math.Min(bench.Count, benchColumns));
            var width = interiorWidth + margin * 2;
            var height = benchRows + 3 + margin * 2;   // bench rows + gap + party row + margins

            var partyRowY = height - margin - 1;
            var firstBenchY = margin + 1;

            var rows = new List<string>(height);
            for (int y = 0; y < height; y++)
            {
                var row = new char[width];
                for (int x = 0; x < width; x++)
                    row[x] = (x < margin || x >= width - margin || y < margin || y >= height - margin)
                           ? '#' : '.';
                rows.Add(new string(row));
            }

            var explored = new List<string>();
            for (int y = margin; y < height - margin; y++)
                for (int x = margin; x < width - margin; x++)
                    explored.Add(x + "," + y);

            var partyFigures = new List<object>(party.Count);
            for (int i = 0; i < party.Count; i++)
                partyFigures.Add(TavernFigure(party[i], margin + i, partyRowY, i));

            var benchFigures = new List<object>(bench.Count);
            for (int i = 0; i < bench.Count; i++)
            {
                var x = margin + i % benchColumns;
                var y = firstBenchY + i / benchColumns;
                benchFigures.Add(TavernFigure(bench[i], x, y, 0));
            }

            Post(JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Run = RunId,
                Seq = System.Threading.Interlocked.Increment(ref _seq),
                Phase = "town",
                Level = 0,
                Grid = new { Width = width, Height = height, Rows = rows },
                Explored = explored,
                Party = partyFigures,
                Roster = benchFigures,
                Prompt = PromptPayload(prompt),
                Log = new List<object>(),
            }));
        }
        catch
        {
            // A tavern that cannot draw itself is still a tavern you can use from the console.
        }
    }

    /// <summary>One figurine standing in the tavern, named so the player can tell them apart.</summary>
    private static object TavernFigure(Character c, int x, int y, int slot) => new
    {
        Id = ViewerIds.Character(c.Name),
        c.Name,
        ClassId = c.Class.ToString(),
        RaceId = c.Race.ToString(),
        Slot = slot,
        Cell = new[] { x, y },
        Facing = "North",
        Hp = new[] { c.CurrentHitPoints, c.MaxHitPoints },
        Ac = c.ArmorClass,
        Status = StatusNames(c.Status),
        ShowName = true,
    };

    /// <summary>
    /// Tell the viewer the party is above ground and where. No grid and no cells: which places
    /// exist and where they sit on the table is the viewer's business, so this sends only the id.
    ///
    /// The prompt is what makes the board usable rather than only watchable: without it the table has
    /// nothing to click and the town can only be walked from the console.
    /// </summary>
    /// <param name="wares">
    /// Goods to lay out on a counter here, or null for a place that does not trade. Labels rather than item
    /// ids: what a thing costs, and whether this shopper can lift it, are the game's business.
    /// </param>
    public void PublishTown(string locationId, IReadOnlyList<Character> party, ViewerPrompt? prompt = null,
                            IReadOnlyList<object>? wares = null)
    {
        if (!_enabled) return;

        try
        {
            var members = new List<object>(party.Count);
            for (int i = 0; i < party.Count; i++)
            {
                var c = party[i];
                members.Add(new
                {
                    Id = ViewerIds.Character(c.Name),
                    c.Name,
                    ClassId = c.Class.ToString(),
                    RaceId = c.Race.ToString(),
                    Slot = i,
                    Cell = (int[])null,
                    Facing = "North",
                    Hp = new[] { c.CurrentHitPoints, c.MaxHitPoints },
                    Ac = c.ArmorClass,
                    Status = StatusNames(c.Status),
                });
            }

            Post(JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Run = RunId,
                Seq = System.Threading.Interlocked.Increment(ref _seq),
                Phase = "town",
                Level = (int?)null,
                Grid = (object)null,
                Explored = Array.Empty<string>(),
                Location = locationId,
                Party = members,
                Encounter = (object)null,
                Wares = wares ?? (IReadOnlyList<object>)Array.Empty<object>(),
                Prompt = PromptPayload(prompt),
                Log = Array.Empty<object>(),
            }));
        }
        catch
        {
            // As everywhere here: the town is not worth a crash.
        }
    }

    /// <summary>Forget the previous fight, so the next one's first round is not read as deaths.</summary>
    public void ResetCombatMemory()
    {
        _lastHp.Clear();
        _lastDead.Clear();
    }

    private static string MonsterId(MonsterInstance m) => ViewerIds.Monster(m.GroupId, m.Index);

    /// <summary>
    /// The wire id for a monster named the rules' way ("group#index"), or null if nothing answers to it.
    ///
    /// Two spellings for one thing, deliberately kept apart: the rules know monsters by group and index and
    /// have no business carrying viewer ids, while the table keys its figures on "mon:group#index". This is
    /// the one place that translates.
    /// </summary>
    private static string? MonsterIdOf(IReadOnlyList<MonsterInstance> monsters, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        foreach (var m in monsters)
        {
            if (!m.IsAlive) continue;
            if (CombatAction.MonsterKey(m.GroupId, m.Index) == key) return MonsterId(m);
        }

        return null;
    }

    private static string FirstAliveIn(IReadOnlyList<MonsterInstance> monsters, string groupId)
    {
        foreach (var m in monsters)
        {
            if (!m.IsAlive) continue;
            if (groupId != null && m.GroupId != groupId) continue;
            return MonsterId(m);
        }

        // No group named, or that group is wiped: any living monster will do for a direction.
        foreach (var m in monsters)
            if (m.IsAlive) return MonsterId(m);

        return null;
    }

    /// <summary>Real groups, real counts — taken from the session rather than re-rolled.</summary>
    private static List<MonsterGroupView> GroupsOf(IReadOnlyList<MonsterInstance> monsters)
    {
        var byGroup = new Dictionary<string, (string Name, int Total, int Alive, int Asleep)>();
        foreach (var m in monsters)
        {
            byGroup.TryGetValue(m.GroupId, out var acc);
            var name = acc.Name ?? m.Name;
            var asleep = acc.Asleep + (m.IsAlive && m.HasStatus(MonsterStatus.Asleep) ? 1 : 0);
            byGroup[m.GroupId] = (name, acc.Total + 1, acc.Alive + (m.IsAlive ? 1 : 0), asleep);
        }

        var result = new List<MonsterGroupView>(byGroup.Count);
        foreach (var kv in byGroup)
            result.Add(new MonsterGroupView(kv.Value.Name, kv.Value.Total, kv.Value.Alive, kv.Value.Asleep));

        return result;
    }

    /// <summary>
    /// Tell the viewer where the party is standing and what it can see.
    /// </summary>
    /// <param name="level">Dungeon level. The viewer clears the table when this changes.</param>
    /// <param name="isFloor">Walkable test for the level's grid, in game coordinates.</param>
    /// <param name="width">Grid width in cells.</param>
    /// <param name="height">Grid height in cells.</param>
    /// <param name="cellX">Party cell, x.</param>
    /// <param name="cellY">Party cell, y.</param>
    /// <param name="facing">"North", "East", "South" or "West".</param>
    /// <param name="party">Party in marching order; the first three are the front rank.</param>
    /// <param name="groups">Monsters currently faced, or null outside combat.</param>
    public void Publish(
        int level,
        Func<int, int, bool> isFloor,
        int width,
        int height,
        int cellX,
        int cellY,
        string facing,
        IReadOnlyList<Character> party,
        IReadOnlyList<MonsterGroupView>? groups = null,
        ViewerPrompt? prompt = null)
    {
        if (!_enabled) return;

        try
        {
            var payload = BuildPayload(level, isFloor, width, height, cellX, cellY, facing, party, groups, prompt: prompt);
            var json = JsonSerializer.Serialize(payload);
            Post(json);
        }
        catch
        {
            // A viewer that cannot be drawn must never spoil a turn.
        }
    }

    private object BuildPayload(
        int level,
        Func<int, int, bool> isFloor,
        int width,
        int height,
        int cellX,
        int cellY,
        string facing,
        IReadOnlyList<Character> party,
        IReadOnlyList<MonsterGroupView>? groups,
        IReadOnlyList<MonsterInstance>? monsters = null,
        int roundNumber = 1,
        List<object>? beats = null,
        ViewerPrompt? prompt = null)
    {
        // '#' is solid, anything else is walkable. The viewer only ever asks "is this floor".
        var rows = new string[height];
        for (int y = 0; y < height; y++)
        {
            var row = new StringBuilder(width);
            for (int x = 0; x < width; x++)
                row.Append(isFloor(x, y) ? '.' : '#');
            rows[y] = row.ToString();
        }

        if (!_visited.TryGetValue(level, out var seen))
        {
            seen = new HashSet<(int X, int Y)>();
            _visited[level] = seen;
        }
        seen.Add((cellX, cellY));

        // Standing in a cell maps its neighbours too, otherwise the party walks a corridor with no
        // walls beside it and the table looks like a bare floor plan.
        foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (1, 0), (-1, 0) })
        {
            var nx = cellX + dx;
            var ny = cellY + dy;
            if (nx >= 0 && nx < width && ny >= 0 && ny < height && isFloor(nx, ny))
                seen.Add((nx, ny));
        }

        var members = new List<object>(party.Count);
        for (int i = 0; i < party.Count; i++)
        {
            var c = party[i];
            members.Add(new
            {
                Id = ViewerIds.Character(c.Name),
                c.Name,
                ClassId = c.Class.ToString(),
                RaceId = c.Race.ToString(),
                Slot = i,
                Cell = new[] { cellX, cellY },
                Facing = facing,
                Hp = new[] { c.CurrentHitPoints, c.MaxHitPoints },
                Ac = c.ArmorClass,
                Status = StatusNames(c.Status),
            });
        }

        object? encounter = null;
        if (monsters is { Count: > 0 })
        {
            // Straight from the session: every standee is a real MonsterInstance, so its id is
            // stable across rounds and the viewer can topple the one that actually died.
            var byGroup = new Dictionary<string, List<MonsterInstance>>();
            foreach (var m in monsters)
            {
                if (!byGroup.TryGetValue(m.GroupId, out var list))
                {
                    list = new List<MonsterInstance>();
                    byGroup[m.GroupId] = list;
                }
                list.Add(m);
            }

            var groupViews = new List<object>(byGroup.Count);
            foreach (var kv in byGroup)
            {
                var groupMembers = new List<object>(kv.Value.Count);
                var alive = 0;
                var asleep = 0;
                foreach (var m in kv.Value)
                {
                    if (m.IsAlive) alive++;
                    var isAsleep = m.IsAlive && m.HasStatus(MonsterStatus.Asleep);
                    if (isAsleep) asleep++;

                    groupMembers.Add(new
                    {
                        Id = MonsterId(m),
                        m.Index,
                        Alive = m.IsAlive,
                        Status = isAsleep ? new List<string> { "Asleep" } : new List<string>(),
                    });
                }

                // The old UI's artwork for this monster, so the table can paste it on the standee instead of
                // standing a blank card there. A PATH, not the bytes: both halves are on one machine in every
                // setup that exists, the files are up to 130kB, and a snapshot goes out every turn -- sending
                // the picture each time would put megabytes through a loopback socket to say nothing new.
                // The viewer loads it once per monster and keeps it. Null when there is no art, which is the
                // ordinary case for anything the old UI never had a drawing of.
                var first = kv.Value.Count > 0 ? kv.Value[0] : null;
                var art = first == null
                    ? null
                    : MonsterArt.FindPath(first.Name, level, MonsterArt.WizardryFirst(first.Template));

                // Absolute, because some of the candidates searched are relative to the working directory and
                // the viewer's is not the game's -- a relative hit would simply not be found at the other end.
                if (!string.IsNullOrEmpty(art))
                {
                    try { art = System.IO.Path.GetFullPath(art); }
                    catch { /* a path this odd is not worth a crash; send it as found */ }
                }

                groupViews.Add(new
                {
                    GroupId = kv.Key,
                    MonsterId = kv.Value.Count > 0 ? kv.Value[0].Name : kv.Key,
                    Alive = alive,
                    Asleep = asleep,
                    Art = art,
                    Members = groupMembers,
                });
            }

            encounter = new { Round = roundNumber, Groups = groupViews };
        }

        return new
        {
            SchemaVersion = 1,
            Run = RunId,
            Seq = System.Threading.Interlocked.Increment(ref _seq),
            Phase = encounter != null ? "combat" : "maze",
            Level = level,
            Grid = new { Width = width, Height = height, Rows = rows },
            Explored = seen.Select(c => c.X + "," + c.Y).ToArray(),
            Party = members,
            Encounter = encounter,
            Prompt = PromptPayload(prompt),
            Log = beats ?? new List<object>(),
        };
    }

    /// <summary>
    /// Shapes the open prompt for the wire and stamps it with an id that survives restatement.
    /// Returns null when the game is not waiting for anything the viewer could answer.
    /// </summary>
    private object? PromptPayload(ViewerPrompt? prompt)
    {
        long id;

        lock (PromptGate)
        {
            if (prompt is null)
            {
                _lastPromptKey = null;
                return null;
            }

            // Identity is the question, not the moment it was asked: same character, same legal options,
            // same id. See the note on _promptId.
            var key = prompt.Kind + "|" + (prompt.For ?? "-") + "|" + string.Join(",", prompt.Options.Select(o => o.Id));
            if (key != _lastPromptKey)
            {
                _lastPromptKey = key;
                _promptId++;
            }

            id = _promptId;
        }

        return new
        {
            Id = id,
            prompt.Kind,
            prompt.Text,
            For = prompt.For,
            // The key each option answers to travels with it, so the viewer can print "F  Fight" and
            // teach the keyboard while offering the button. Filled HERE rather than at the fifteen
            // places that build options: the key belongs to the command vocabulary, not to whoever
            // happens to be asking, and one lookup cannot fall out of step with the pump the way
            // fifteen hand-written hints would.
            //
            // Keyed a whole prompt at a time, because the ones that name a thing rather than a word -- the
            // third slime, the axe on the shelf -- are numbered by their place in this question. See KeyLabels.
            Options = Keyed(prompt),
        };
    }

    /// <summary>The prompt's options as the wire carries them, each with the key that answers it.</summary>
    private static List<object> Keyed(ViewerPrompt prompt)
    {
        var keys = ViewerCommands.KeyLabels(prompt);
        var options = new List<object>(prompt.Options.Count);

        for (var i = 0; i < prompt.Options.Count; i++)
        {
            var o = prompt.Options[i];
            options.Add(new { o.Id, o.Label, Key = keys[i], o.Target });
        }

        return options;
    }

    /// <summary>Flag names the viewer can read, e.g. Dead so a standee is laid on its side.</summary>
    private static List<string> StatusNames(CharacterStatus status)
    {
        var names = new List<string>();
        if (status == CharacterStatus.None) return names;

        foreach (CharacterStatus flag in Enum.GetValues<CharacterStatus>())
        {
            if (flag == CharacterStatus.None) continue;
            if (status.HasFlag(flag)) names.Add(flag.ToString());
        }

        return names;
    }

    /// <summary>
    /// Takes the next command the player gave the viewer, or null if they have not clicked anything.
    ///
    /// For callers with no form to feed -- the console menus. <see cref="ViewerControlPump"/> exists to
    /// turn a command into a keypress on a Windows form, which is right for the maze and a fight, where
    /// the game's own key handler owns the rules. The tavern has no rules and no form: it applies the
    /// command to the roster directly.
    ///
    /// Blocking, briefly, and deliberately so: a console loop has nothing else to do while it waits, and
    /// the HttpClient timeout caps it at well under a second.
    ///
    /// Do NOT call this while a pump is running. Collecting a command REMOVES it from the viewer's queue,
    /// so two readers means each swallows commands meant for the other. Nothing enforces that here
    /// because nothing can: it is a fact about the queue, not about this method.
    /// </summary>
    public string? TryTakeCommand()
    {
        if (!_enabled) return null;

        try
        {
            using var response = Http.GetAsync(_commandEndpoint).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode
                || response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("option", out var option)
                 ? option.GetString()
                 : null;
        }
        catch
        {
            // No viewer, or it went away. The console menu is still there and still works.
            return null;
        }
    }

    /// <summary>
    /// Fire and forget. The viewer answers 204 before it does any work, so this is quick when it
    /// is there and harmless when it is not: a refused connection on loopback fails immediately,
    /// off the UI thread, and is swallowed.
    /// </summary>
    private void Post(string json)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(_endpoint, content).ConfigureAwait(false);
            }
            catch
            {
                // No viewer running, or it went away. Nothing to do and nobody to tell.
            }
        });
    }
}
