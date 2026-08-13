// Unity-only. The cardboard end of the pipe.
//
// Everything above this class is game-agnostic plumbing. Everything in it is presentation:
// which prefab, where on the table, how it animates, what the DM says. Nothing here may
// derive game facts — if you need one, add it to the snapshot.

using System.Collections.Generic;
using UnityEngine;
using WizardryViewer.Protocol;

namespace WizardryViewer.Unity
{
    public sealed class TableRenderer : MonoBehaviour
    {
        [Header("Scale (keep honest: real table, real 28mm minis)")]
        [Tooltip("Metres per dungeon cell. A 1-inch dungeon tile is 0.0254.")]
        [SerializeField] private float cellSize = 0.0254f;
        [SerializeField] private Transform tableOrigin;

        [Header("Prefabs")]
        [SerializeField] private GameObject floorTilePrefab;
        [SerializeField] private GameObject wallPiecePrefab;
        [SerializeField] private GameObject blankStandeePrefab;   // fallback for unknown ids
        [SerializeField] private StandeeEntry[] standees;         // monsterId/classId -> prefab

        [Tooltip("Stand in one id for another when no figure is registered for it. Lets a game " +
                 "with its own vocabulary (an AD&D Cleric, a Halfling) reach a figure carved for " +
                 "the nearest equivalent, without duplicating prefabs.")]
        [SerializeField] private AliasEntry[] standeeAliases;

        [Header("Level")]
        [Tooltip("Lay the entire level rather than only what the party has mapped. The snapshot " +
                 "carries the whole grid either way, so this reveals nothing the viewer was not " +
                 "already told — it just skips the fog of war.")]
        [SerializeField] private bool revealEntireLevel;

        [Header("Marching order")]
        [Tooltip("Sideways gap between files, as a fraction of a cell. Keep file spacing plus half " +
                 "a figure's width under 0.5 or the outer two clip the corridor walls.")]
        [SerializeField] private float fileSpacing = 0.27f;
        [Tooltip("Front-to-back gap between the two ranks, as a fraction of a cell.")]
        [SerializeField] private float rankSpacing = 0.28f;

        [Tooltip("Yaw correction for the sculpts' own forward direction, in degrees. These figures " +
                 "are not modelled facing their local +Z, so without this the party marches with " +
                 "its back to the way it is going.")]
        [SerializeField] private float standeeYaw = 180f;

        [Header("Motion")]
        [Tooltip("Cells per second when a figure slides. A step must land inside one beat.")]
        [SerializeField] private float slideCellsPerSecond = 2.0f;
        [Tooltip("Cells per second when crossing the town board, where a move is four cells or more " +
                 "rather than one.")]
        [SerializeField] private float townWalkCellsPerSecond = 4.5f;
        [Tooltip("Shoulder to shoulder along the row while standing about in town. Wider than the " +
                 "marching files, since nothing is trying to fit through a corridor.")]
        [SerializeField] private float townFileSpacing = 0.40f;
        [Tooltip("Depth between the two rows in town. Only has to beat a figure's own depth, since the " +
                 "rows are offset half a place sideways as well.")]
        [SerializeField] private float townRankSpacing = 0.30f;
        [Tooltip("Cells forward of the middle of the pad, towards the player. Enough to keep the party off " +
                 "the furniture at the back of each place; no more, because standing closer to a building's " +
                 "front wall means MORE of them is behind it, not less, at a camera looking down.")]
        [SerializeField] private float townStandForward = 0.10f;
        [Tooltip("How far in front of the town board the player is taken to be sitting, in cells. Only " +
                 "decides which way each pad's group turns, so it wants to match the town camera's own " +
                 "distance (about 10 cells horizontally) rather than be exact.")]
        [SerializeField] private float townSeatCells = 10f;
        [SerializeField] private float turnDegreesPerSecond = 540f;
        [Tooltip("Further than this and we snap instead of sliding: that is a catch-up jump, " +
                 "not a move, and gliding across the map would be a lie.")]
        [SerializeField] private float snapBeyondCells = 2.5f;

        [System.Serializable]
        public struct StandeeEntry
        {
            public string id;
            public GameObject prefab;
        }

        [System.Serializable]
        public struct AliasEntry
        {
            public string from;
            public string to;
        }

        private struct Placement
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private readonly Dictionary<string, GameObject> _figurines = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _tiles = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _walls = new Dictionary<string, GameObject>();
        private readonly HashSet<string> _seenThisReconcile = new HashSet<string>();

        // Where each figure is headed. _anchor is the authoritative pose from the snapshot;
        // _target is what Update chases, which a beat may lean away from for effect.
        private readonly Dictionary<string, Placement> _anchor = new Dictionary<string, Placement>();
        private readonly Dictionary<string, Placement> _target = new Dictionary<string, Placement>();

        /// <summary>Centre of the party on the table, or null while nobody is placed.</summary>
        public Vector3? PartyCentre { get; private set; }

        /// <summary>
        /// True when the board is a gathering rather than a crawl -- the tavern and its like, where figures
        /// stand around who are not in the party.
        ///
        /// The prompt dialog uses it to decide what to sit beside. Following the party is right underground,
        /// where the party is the subject; in the tavern the party may be two minis in a corner, or none at
        /// all, and a dialog that follows them lands off the table or nowhere.
        /// </summary>
        public bool HasRoster { get; private set; }

        /// <summary>
        /// The figure standing for an id, or null if nothing on the table answers to it.
        ///
        /// Exposed so a prompt can point AT something rather than describe it: the dialog looks the id up
        /// and makes that figure clickable. Deliberately returns null rather than throwing -- a prompt may
        /// name a figure that has already left the table, and the dialog falls back to a plain button.
        /// </summary>
        public Transform FindFigure(string id)
        {
            GameObject go;
            if (string.IsNullOrEmpty(id) || !_figurines.TryGetValue(id, out go) || go == null) return null;
            return go.transform;
        }

        /// <summary>
        /// Where a town place stands, for a button that should hover over the building rather than sit in a list.
        ///
        /// The pads ARE the map -- a colonnaded temple and a market stall tell you where you are far better than
        /// the words "Temple" and "Shop" -- so going somewhere should be a matter of pointing at it. Buildings
        /// are scenery rather than figures, which is why this exists alongside <see cref="FindFigure"/>: there is
        /// no object with an id to look up, only a known cell per place.
        /// </summary>
        public Vector3? FindPlace(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            var cell = TownCell(id);
            if (cell == null) return null;

            // A little above the pad, so the button floats over the building instead of inside its floor.
            return CellToWorld(cell[0], cell[1]) + Vector3.up * (cellSize * 0.9f);
        }

        /// <summary>
        /// How wide a ring around a FIGURE should be, which depends on how close together the figures stand.
        ///
        /// In a fight or in the maze a figure has a cell to itself, so a cell-wide ring is right and easy to
        /// hit. In town they crowd onto a pad barely two cells across at 0.4 of a cell apart -- so a cell-wide
        /// ring there merges the whole party into one dark blob, where you can see that something is clickable
        /// but not who. Sized from the same spacing the town formation uses, rather than a second guess at it.
        /// </summary>
        public float FigureRingSpan
        {
            get { return cellSize * (_laidTown ? townFileSpacing * 0.85f : 0.9f); }
        }

        /// <summary>Extent of everything laid on the table, for a camera that wants to frame it.</summary>
        public Bounds? LaidBounds { get { return _hasLaid ? _laid : (Bounds?)null; } }

        private Bounds _laid;
        private bool _hasLaid;

        // Which level's geometry is on the table. Tiles and walls are keyed by cell, which is only
        // unique within a level, so laying a second level over a first leaves the old one standing —
        // and LayCell's ContainsKey guard silently skips every cell the two happen to share.
        private int? _laidLevel;
        private int _laidWidth;
        private int _laidHeight;

        // Town and dungeon share the tile dictionaries, so swapping between them has to clear as
        // surely as a level change does.
        private bool _laidTown;
        private string _saidLocation;
        private string _townLocation;
        private DmSubtitle _subtitle;
        private readonly Dictionary<string, GameObject> _townLabels = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _townProps = new Dictionary<string, GameObject>();
        private TownProps _props;

        /// <summary>
        /// Where each place sits on the town board, in cells. This is presentation and belongs
        /// here: the snapshot only names the place the party is standing in. Ids the table does not
        /// know still get a pad, appended after the ones it does.
        /// </summary>
        private static readonly (string Id, int X, int Y)[] TownPlaces =
        {
            ("TrainingGrounds", 0,  0),
            ("Tavern",          4,  0),
            ("Temple",          8,  0),
            ("Shop",           12,  0),
            ("EdgeOfTown",      6,  4),
        };

        public Vector3 CellToWorld(int x, int y)
        {
            var o = tableOrigin != null ? tableOrigin.position : Vector3.zero;
            return o + new Vector3(x * cellSize, 0f, -y * cellSize);
        }

        /// <summary>
        /// Make the table match the snapshot. Always safe to call, at any time, from any
        /// state — this is the operation that makes a late-joining or lagging viewer correct.
        /// </summary>
        public void Reconcile(Snapshot s)
        {
            ClearIfNewLevel(s);

            if (IsTown(s)) LayTown(s);
            else LayExploredTiles(s);

            _seenThisReconcile.Clear();

            // Marching order: three abreast in front, three behind, oriented by facing.
            // Six 28mm figures do not fit in one 25mm square, so they overflow it slightly —
            // which is exactly what happens on a real table.
            var facing = FacingVector(s.Party.Count > 0 ? s.Party[0].Facing : "North");

            // Underground they face the way they are marching. In town they are not marching
            // anywhere — the game reports "North" regardless — so they turn to face whoever is
            // looking at the board instead of standing with their backs to them.
            var look = IsTown(s) ? Face(Vector3.back) : Face(facing);

            // In town nobody has a dungeon cell, so the whole party would otherwise be skipped and
            // the board would stand empty. They gather on the pad for wherever they are.
            var townCell = IsTown(s) ? (TownCell(s.Location) ?? new[] { TownPlaces.Length * 4, 8 }) : null;

            // Which way they are walking, if this snapshot moved them to a different place in town.
            var travelLook = TownTravelLook(s, townCell);

            foreach (var p in s.Party)
            {
                var ownCell = p.Cell != null && p.Cell.Length == 2 ? p.Cell : null;
                var cell = ownCell ?? townCell;
                if (cell == null) continue;
                _seenThisReconcile.Add(p.Id);

                bool created;
                // A "Dwarf_Fighter" figure is used when one is registered, otherwise any Fighter.
                var go = Ensure(p.Id, p.RaceId + "_" + p.ClassId, p.ClassId, out created);

                // A cell of their own means they are marching; no cell means they are standing about in
                // town, which is a different arrangement entirely -- see TownPose.
                // Shopping stands them square to the stall rather than square to the player's line of sight, which
                // at the rightmost pad on the board runs diagonally: their row came in at an angle to the counter
                // and the end of it stood inside the woodwork.
                var shopping = ownCell == null && s.Location == "Shop" && s.Wares != null && s.Wares.Count > 0;

                var pose = ownCell == null
                         ? TownPose(p.Slot, s.Party.Count, cell, shopping)
                         : PartyPose(p.Slot, cell, facing, look);

                // Shopping happens from the CUSTOMER's side of the counter, facing in.
                //
                // Standing them on the middle of the pad put them inside the stall, behind their own goods and
                // in front of Boltac's, which is the one arrangement where no facing works: the counter holds
                // what they are selling and the wall holds what he is selling, so a party turned to either has
                // its back to the other. Outside the counter, one view takes in all three -- the party, the
                // counter over their shoulders, and the shelves above it.
                if (shopping)
                {
                    pose.Position += Vector3.back * (cellSize * ShoppingStandoff);
                    pose.Rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                }
                if (p.Status != null && p.Status.Contains("Dead")) pose.Rotation = Toppled(pose.Rotation);
                Place(p.Id, go, pose, created, travelLook);
                if (p.ShowName) NameCard(p.Id, p.Name, go);
            }

            HasRoster = s.Roster != null && s.Roster.Count > 0;

            // Everyone else standing on the table: the tavern's bench, and whoever a shop or temple puts
            // out. Each has its own cell and stands in the middle of it -- no marching formation, because
            // they are not marching. Otherwise identical to the party: same figures, same cleanup.
            if (s.Roster != null)
            {
                foreach (var r in s.Roster)
                {
                    if (r.Cell == null || r.Cell.Length != 2) continue;
                    _seenThisReconcile.Add(r.Id);

                    bool createdRosterFigure;
                    var go = Ensure(r.Id, r.RaceId + "_" + r.ClassId, r.ClassId, out createdRosterFigure);

                    var pose = new Placement
                    {
                        Position = CellCentre(r.Cell[0], r.Cell[1]),
                        Rotation = Quaternion.identity,   // facing the player's side of the table
                    };
                    if (r.Status != null && r.Status.Contains("Dead")) pose.Rotation = Toppled(pose.Rotation);

                    Place(r.Id, go, pose, createdRosterFigure);
                    if (r.ShowName) NameCard(r.Id, r.Name, go);
                }
            }

            if (s.Encounter != null && s.Party.Count > 0 && s.Party[0].Cell != null)
            {
                // Monsters stand two squares ahead of the party, facing back at them,
                // each group in its own row so multi-group encounters stay legible.
                var partyCell = s.Party[0].Cell;
                var anchor = CellToWorld(partyCell[0], partyCell[1]) + facing * (cellSize * 2f);
                var right = RightOf(facing);
                var faceParty = Face(-facing);

                for (int gi = 0; gi < s.Encounter.Groups.Count; gi++)
                {
                    var g = s.Encounter.Groups[gi];
                    for (int i = 0; i < g.Members.Count; i++)
                    {
                        var m = g.Members[i];
                        _seenThisReconcile.Add(m.Id);

                        bool created;
                        var go = Ensure(m.Id, g.MonsterId, g.MonsterId, out created);
                        PasteArt(go, g.Art);

                        var file = i - (g.Members.Count - 1) * 0.5f;
                        var pose = new Placement
                        {
                            Position = anchor
                                     + right * (file * cellSize * 0.5f)
                                     + facing * (gi * cellSize * 0.6f),
                            Rotation = m.Alive ? faceParty : Toppled(faceParty),
                        };
                        Place(m.Id, go, pose, created);
                    }
                }
            }

            // Anything the snapshot no longer mentions has left the table.
            var stale = new List<string>();
            foreach (var kv in _figurines)
                if (!_seenThisReconcile.Contains(kv.Key)) stale.Add(kv.Key);

            foreach (var id in stale)
            {
                Destroy(_figurines[id]);
                _figurines.Remove(id);
                _anchor.Remove(id);
                _target.Remove(id);

                // The flag goes with its figure anyway, being a child of it -- but the dictionary entry
                // would linger and be mistaken for a live flag when that id comes back.
                _townLabels.Remove("name:" + id);
            }

            RecomputePartyCentre(s);
        }

        /// <summary>
        /// Play one thing that happened. Dropping any of these must leave the table correct,
        /// because Reconcile follows. So: animation and sound only, never bookkeeping.
        /// </summary>
        public void PlayBeat(LogEntry e, Snapshot context, string spokenLine)
        {
            if (!string.IsNullOrEmpty(spokenLine))
            {
                Debug.Log($"[DM] {spokenLine}");   // TODO TTS

                // Say it on the table too, which is the whole point of having beats: a fight was animating
                // -- figures leaning in, corpses going over -- with every word of it going to a console
                // nobody watching the table can see. One line per beat, and the beat timing paces them.
                if (_subtitle == null) _subtitle = FindAnyObjectByType<DmSubtitle>();
                if (_subtitle != null) _subtitle.Say(spokenLine);
            }

            switch (e.T)
            {
                case LogTypes.Move:
                    SlideParty(e, context);
                    break;
                case LogTypes.Attack:
                    LeanInto(e);
                    break;
                case LogTypes.Death:
                    Topple(e);
                    break;
                case LogTypes.Spawn:
                    // TODO the DM's hand reaches in and sets the standee down.
                    break;
                case LogTypes.Treasure:
                    // TODO scatter coins.
                    break;
            }
        }

        /// <summary>
        /// Figures walk to where they are told rather than teleporting. Constant speed, so a
        /// step reads as a step; MoveTowards converges exactly and needs no easing state.
        /// </summary>
        private void Update()
        {
            if (_target.Count == 0) return;

            // A dungeon step is one cell; crossing the town square is four or more. At the dungeon pace
            // that is a three-second trudge, so town has a brisker one -- still a walk, not a jump.
            var cellsPerSecond = _laidTown ? townWalkCellsPerSecond : slideCellsPerSecond;
            var step = cellSize * cellsPerSecond * Time.deltaTime;
            var turn = turnDegreesPerSecond * Time.deltaTime;

            foreach (var kv in _figurines)
            {
                Placement t;
                if (!_target.TryGetValue(kv.Key, out t)) continue;
                if (kv.Value == null) continue;

                var tr = kv.Value.transform;
                tr.position = Vector3.MoveTowards(tr.position, t.Position, step);
                tr.rotation = Quaternion.RotateTowards(tr.rotation, t.Rotation, turn);

                // Arrived, and still facing the way they walked: turn to the pose the snapshot actually
                // asked for. One assignment settles it -- from here target and anchor are the same object,
                // so this costs a comparison per frame and nothing else.
                Placement home;
                if (tr.position != t.Position) continue;
                if (!_anchor.TryGetValue(kv.Key, out home)) continue;
                if (home.Rotation != t.Rotation) _target[kv.Key] = home;
            }

            // The pole is stuck in the base and needs nothing: walking, turning and falling over carry it
            // the way they would carry a real one. The tag on top is the part that has to be legible, so
            // it swivels to face the player -- which is also why this runs per frame rather than per
            // snapshot. A figure tips over across several frames as it dies, and a tag that snapped flat
            // while its pole was still upright would read as a glitch rather than a death.
            foreach (var kv in _townLabels)
            {
                if (kv.Value == null || !kv.Key.StartsWith("name:")) continue;
                AlignTag(kv.Value.transform);
            }
        }

        /// <summary>
        /// Drop the previous level's geometry when the party changes level.
        ///
        /// Accumulating cells *within* a level is the fog-of-war reveal and must be kept, so this
        /// deliberately does not clear on every snapshot — only when the level underneath us has
        /// actually changed.
        /// </summary>
        private void ClearIfNewLevel(Snapshot s)
        {
            // Coming up into town, or heading back down: the other place's geometry has to go.
            var town = IsTown(s);
            if (town != _laidTown)
            {
                ClearLevelGeometry();
                _laidTown = town;

                // Force a fresh lay of whatever we return to, since its cells were just destroyed.
                _laidLevel = null;
                _laidWidth = 0;
                _laidHeight = 0;
            }

            if (town) return;             // the town board lays itself
            if (s.Grid == null) return;   // nothing to lay: leave the table alone

            // Only a level we can compare counts. Level is optional on the wire, and reading a
            // missing one as "a different level" would sweep the table mid-crawl — far worse than
            // failing to clear, so an absent Level is never on its own a reason to wipe.
            var changed = s.Level.HasValue && _laidLevel.HasValue && s.Level.Value != _laidLevel.Value;

            // A level's grid never resizes, so different dimensions mean a different level even
            // when the Level field failed to say so. Guarded on having laid something, or the very
            // first snapshot would compare against 0x0 and count as a change.
            if (_tiles.Count > 0 && (s.Grid.Width != _laidWidth || s.Grid.Height != _laidHeight))
                changed = true;

            if (changed) ClearLevelGeometry();

            if (s.Level.HasValue) _laidLevel = s.Level;
            _laidWidth = s.Grid.Width;
            _laidHeight = s.Grid.Height;
        }

        /// <summary>
        /// Take the floor and walls off the table. Figures are left alone — Reconcile's own stale
        /// sweep owns those, and it runs off the snapshot's ids rather than the level.
        /// </summary>
        private void ClearLevelGeometry()
        {
            foreach (var kv in _tiles)
                if (kv.Value != null) Destroy(kv.Value);
            foreach (var kv in _walls)
                if (kv.Value != null) Destroy(kv.Value);

            foreach (var kv in _townLabels)
                if (kv.Value != null) Destroy(kv.Value);

            foreach (var kv in _townProps)
                if (kv.Value != null) Destroy(kv.Value);

            _tiles.Clear();
            _walls.Clear();
            _townLabels.Clear();
            _townProps.Clear();

            // The whole-level camera frames LaidBounds; keeping the old level's extent would leave
            // it framing bare table next to the new one.
            _hasLaid = false;
            _laid = default(Bounds);
        }

        /// <summary>
        /// True while the town board is laid rather than a dungeon level. Lighting keys off this:
        /// the town is out in daylight, the dungeon is lit by whatever the party carries.
        /// </summary>
        public bool IsTownBoard => _laidTown;

        /// <summary>
        /// Above ground: the snapshot carries a place name and no grid.
        /// </summary>
        private static bool IsTown(Snapshot s)
        {
            return !string.IsNullOrEmpty(s.Location) || (s.Grid == null && s.Phase == Phases.Town);
        }

        /// <summary>
        /// Which way the party is walking, when a snapshot puts them at a different place in town than the
        /// one they were at -- or null when they are not going anywhere.
        ///
        /// The walk is worked out HERE rather than sent: the game names a place and nothing more, because
        /// where the places sit on the table is the viewer's business (see <see cref="TownPlaces"/>). That
        /// makes crossing the ground between two of them the viewer's business too. It is theatre in the
        /// architecture's sense -- the snapshot is still the truth about where they are, and a viewer that
        /// misses the transition simply finds them standing at the new place.
        ///
        /// They face the way they are going while they cross, which is the whole point of walking; the
        /// resting pose that faces the player is kept in <c>_anchor</c> and taken up again on arrival by
        /// <see cref="Update"/>.
        /// </summary>
        private Quaternion? TownTravelLook(Snapshot s, int[] townCell)
        {
            if (!IsTown(s))
            {
                // Underground now: forget where they were standing, or coming back to town would be read
                // as a walk from wherever they last were in it.
                _townLocation = null;
                return null;
            }

            var from = TownCell(_townLocation);
            _townLocation = s.Location;

            if (from == null || townCell == null) return null;                       // first arrival, not a walk
            if (from[0] == townCell[0] && from[1] == townCell[1]) return null;       // same place, republished

            var direction = CellCentre(townCell[0], townCell[1]) - CellCentre(from[0], from[1]);
            direction.y = 0f;
            return direction.sqrMagnitude < 1e-8f ? (Quaternion?)null : Face(direction.normalized);
        }

        /// <summary>
        /// Lay the town out as pads on the table, one per place, and name the one the party is
        /// standing in. A 2x2 pad reads as somewhere you go rather than a square you step on.
        /// </summary>
        private void LayTown(Snapshot s)
        {
            if (floorTilePrefab == null) return;

            if (_props == null) _props = FindAnyObjectByType<TownProps>();

            // The pad the party is standing on gives up its name label while a dialog is on the table. The
            // dialog stands in front of the board and says where they are in words, and the town shot has no
            // room to stand it any further forward -- so it lands on that one label, and the label is the
            // redundant half. Keyed off the prompt rather than the location alone: with no dialog up there is
            // nothing else saying where they are, and a nameless pad would be worse than a covered one.
            var covered = s.Prompt != null ? s.Location : null;

            foreach (var place in TownPlaces)
            {
                LayPad(place.X, place.Y);
                LabelPad(place.Id, place.X, place.Y);

                GameObject label;
                if (_townLabels.TryGetValue("label:" + place.Id, out label) && label != null)
                    label.SetActive(place.Id != covered);

                // Terrain over the pad. Built once and kept: the whole town is standing whether or
                // not the party is there, which is what makes it read as a place they walk between.
                if (_props != null && !_townProps.ContainsKey(place.Id))
                {
                    var centre = CellToWorld(place.X, place.Y) + new Vector3(cellSize * 0.5f, 0f, -cellSize * 0.5f);
                    var built = _props.Build(place.Id, centre, cellSize);
                    if (built != null) _townProps[place.Id] = built;
                }
            }

            // A place the table has no spot for still gets a pad, parked clear of the known ones,
            // so an unrecognised id is visible rather than silently missing.
            if (!string.IsNullOrEmpty(s.Location) && TownCell(s.Location) == null)
                LayPad(TownPlaces.Length * 4, 8);

            LayCounter(s);

            // Found lazily rather than wired through setup: the pads alone cannot say which place
            // is which, and this is the one line of genuine display text the viewer owns.
            if (_subtitle == null) _subtitle = FindAnyObjectByType<DmSubtitle>();

            if (_subtitle != null && s.Location != _saidLocation)
            {
                _subtitle.Say(Prettify(s.Location));
                _saidLocation = s.Location;
            }
        }

        // ---- The counter: goods laid out as objects ----------------------------------------------------

        /// <summary>Cards currently on a counter, by ware id. Rebuilt only when the goods themselves change.</summary>
        private readonly Dictionary<string, GameObject> _wareCards = new Dictionary<string, GameObject>();

        /// <summary>
        /// What the counter was last laid out from. A shop republishes on every click -- a purse changes, a
        /// message goes up -- and rebuilding thirty cards each time would flicker the whole stall for nothing.
        /// </summary>
        private string _wareSignature = "";

        /// <summary>
        /// How small a ware card is, in cells. TINY on purpose: about 4mm wide on a 25mm cell, the size of a real
        /// boxed item on a real shelf. It cannot be read from the town's wide shot at all -- you have to go and
        /// look, which is what the wheel is for. Spreading them out big enough to read from across the board was
        /// the previous version, and it emptied the shop to fill the table.
        /// </summary>
        /// <summary>
        /// Cells in front of the pad's middle that a shopping party stands. INSIDE the stall, just in front of
        /// the counter -- which is what you do in a shop, rather than standing in the street looking in.
        /// </summary>
        private const float ShoppingStandoff = 0.62f;

        private const float WareWide = 0.13f;
        private const float WareTall = 0.10f;
        private const float WareThin = 0.02f;

        /// <summary>
        /// Lays the wares out IN the shop: Boltac's stock standing on his shelves, and the shopper's own goods on
        /// the counter between them.
        ///
        /// Upright, because that is how stock stands on a shelf, and the wall behind them is what makes the stall
        /// read as a shop rather than a table with things on it. The shelves belong to <see cref="TownProps"/> and
        /// are found by NAME rather than measured again here, so the two cannot drift apart.
        /// </summary>
        private void LayCounter(Snapshot s)
        {
            var wares = s.Wares ?? new List<Ware>();

            // Only worth zooming to when the party is standing there. The goods are on display from anywhere,
            // but coming in close on a shop nobody is visiting would take the camera away from the party.
            _wareFocusIsHere = s.Location == "Shop";

            var signature = CounterSignature(wares, s.Location);
            if (signature == _wareSignature) return;
            _wareSignature = signature;

            ClearCounter();
            if (wares.Count == 0) return;

            // The shop's own prop, wherever the party is standing. Keyed on the place the SHELVES are rather than
            // on the party's location, so the stock is on display from across the square as well as from inside.
            GameObject shop;
            if (!_townProps.TryGetValue("Shop", out shop) || shop == null) return;

            var shelves = new List<Transform>();
            for (var i = 0; i < TownProps.ShelfCount; i++)
            {
                var shelf = shop.transform.Find("Shelf_" + i);
                if (shelf != null) shelves.Add(shelf);
            }

            var counter = shop.transform.Find("CounterTop");
            if (shelves.Count == 0 && counter == null) return;

            var stock = CountOn(wares, "shop");
            var perShelf = shelves.Count > 0 ? Mathf.Max(1, Mathf.CeilToInt(stock / (float)shelves.Count)) : 1;
            var packCount = CountOn(wares, "pack");

            var shelfAt = 0;
            var onShelf = 0;
            var packAt = 0;

            foreach (var w in wares)
            {
                if (w.Side == "pack")
                {
                    if (counter == null) continue;
                    BuildWareCard(w, "pack", OnCounter(counter, packAt, packCount), flat: true);
                    packAt++;
                    continue;
                }

                if (shelves.Count == 0) continue;

                if (onShelf >= perShelf && shelfAt < shelves.Count - 1)
                {
                    shelfAt++;
                    onShelf = 0;
                }

                BuildWareCard(w, "shop", OnShelf(shelves[shelfAt], onShelf, perShelf));
                onShelf++;
            }

            // Aimed BETWEEN the counter and the wall, not at the wall itself.
            //
            // The camera sits its standoff back from whatever it aims at, so aiming at the shelves parked it
            // inside the stall with the counter behind it -- and the counter is where the player's own goods are
            // lying. Aiming at the midpoint puts the camera outside the counter looking in, which is the one
            // position that sees both sides of the trade at once.
            if (shelves.Count > 0)
            {
                // Aimed at the COUNTER, at the height of the middle shelf. The camera sits its standoff back from
                // whatever it aims at, so this puts it in front of the counter with the party between -- and the
                // shelves, now only half a cell further back, standing directly behind what it is aimed at.
                var wall = shelves[shelves.Count / 2].position;
                _wareFocus = counter != null
                          ? new Vector3(counter.position.x, wall.y, counter.position.z)
                          : wall;
            }
            else
            {
                _wareFocus = counter != null ? counter.position : (Vector3?)null;
            }
        }

        private static int CountOn(List<Ware> wares, string side)
        {
            var n = 0;
            foreach (var w in wares)
            {
                var own = w.Side == "pack" ? "pack" : "shop";
                if (own == side) n++;
            }

            return n;
        }

        /// <summary>A slot standing on a shelf, counted from its left end.</summary>
        private Vector3 OnShelf(Transform shelf, int slot, int slots)
        {
            var span = cellSize * TownProps.ShelfWidth;
            var pitch = span / Mathf.Max(1, slots);
            var x = -span * 0.5f + pitch * (slot + 0.5f);

            return shelf.position
                 + shelf.right * x
                 + Vector3.up * (shelf.localScale.y * 0.5f + cellSize * WareTall * 0.5f);
        }

        /// <summary>A slot on the counter top, which is where what YOU are selling goes.</summary>
        private Vector3 OnCounter(Transform counter, int slot, int slots)
        {
            var span = counter.localScale.x * 0.8f;
            var pitch = span / Mathf.Max(1, slots);
            var x = -span * 0.5f + pitch * (slot + 0.5f);

            return counter.position
                 + counter.right * x
                 + Vector3.up * (counter.localScale.y * 0.5f + cellSize * WareThin * 0.5f);
        }

        private void BuildWareCard(Ware w, string side, Vector3 at, bool flat = false)
        {
            var id = Ids.Ware(side, w.Id);

            var go = new GameObject("Ware:" + id);
            go.transform.SetParent(transform, false);
            go.transform.position = at;

            // Facing the player's side of the table, standing up. The stall faces that way too, so a card on one
            // of its shelves is square to the wall behind it.
            go.transform.rotation = Quaternion.identity;

            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Card";
            slab.transform.SetParent(go.transform, false);

            // Standing up on a shelf, or lying down on a counter. Goods put out on a counter lie on it -- and
            // upright cards there stood between the customer and the shelves, which is the whole problem this
            // arrangement is solving.
            slab.transform.localScale = flat
                ? new Vector3(cellSize * WareWide, cellSize * WareThin, cellSize * WareTall)
                : new Vector3(cellSize * WareWide, cellSize * WareTall, cellSize * WareThin);

            // Slate with chalk on it, not paper with ink. The stall shades itself under its awning, and a pale
            // card with dark lettering went to mud in there -- the same reason the name-card plaques are slate.
            var stock = Slate() ?? CardStock();
            if (stock != null) slab.GetComponent<Renderer>().sharedMaterial = stock;

            // The slab KEEPS its collider: the card itself is the click target, unlike a name card's pole,
            // which is scenery beside a figure that owns the click.

            var tag = new GameObject("Tag");
            tag.transform.SetParent(go.transform, false);

            // Unrotated reads right way round from the player's side -- see the note on plaques in NameCard. A
            // flat card's letters are tipped to lie on its face instead, and both sit just clear of the surface
            // so they are not inside it.
            if (flat)
            {
                tag.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tag.transform.localPosition = new Vector3(0f, cellSize * (WareThin * 0.5f + 0.004f), 0f);
            }
            else
            {
                tag.transform.localPosition = new Vector3(0f, 0f, -cellSize * (WareThin * 0.5f + 0.004f));
            }
            tag.transform.localScale = Vector3.one * (cellSize * 0.055f);

            var tmp = tag.AddComponent<TMPro.TextMeshPro>();
            tmp.text = string.IsNullOrEmpty(w.Note)
                     ? w.Name + System.Environment.NewLine + w.Price
                     : w.Name + System.Environment.NewLine + w.Price + "  (" + w.Note + ")";
            // Ink on a small paper tag. Sized to the CARD rather than to legibility from across the room: a smudge
            // from the town shot, clear from a hand's breadth away, which is the point of putting it on a shelf.
            tmp.fontSize = 2.6f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = new Color(0.95f, 0.92f, 0.84f);

            // Sized to the CARD, in the tag's own units, so a long name wraps and then gives up with an ellipsis
            // instead of spilling across its neighbours -- which is what "Potion of Healing" did, printing itself
            // over the two goods either side of it.
            var unit = cellSize * 0.055f;
            tmp.rectTransform.sizeDelta = new Vector2(cellSize * WareWide / unit, cellSize * WareTall / unit);
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TMPro.TextOverflowModes.Ellipsis;

            _wareCards[id] = go;
        }

        /// <summary>
        /// The card standing for a ware id, so a prompt can make the object itself clickable rather than
        /// drawing a button for it. Null when nothing on the counter answers to it.
        /// </summary>
        public Transform FindWareCard(string id)
        {
            GameObject go;
            if (string.IsNullOrEmpty(id) || !_wareCards.TryGetValue(id, out go) || go == null) return null;
            return go.transform;
        }

        /// <summary>
        /// The middle of the goods on display, when the party is somewhere that trades. Null everywhere else.
        ///
        /// The camera zooms towards THIS rather than the party when it exists. The party is not the subject in a
        /// shop -- the shelves are, and they are a good 20mm behind where the party stands, which at the distance
        /// a wide town shot sits is the difference between reading a price and seeing a speck.
        /// </summary>
        public Vector3? WareFocus => _wareFocusIsHere ? _wareFocus : null;

        private Vector3? _wareFocus;
        private bool _wareFocusIsHere;

        private void ClearCounter()
        {
            foreach (var kv in _wareCards)
                if (kv.Value != null) Destroy(kv.Value);

            _wareCards.Clear();
            _wareFocus = null;
        }

        /// <summary>What the counter is laid out from, so an unchanged stall is left standing.</summary>
        private static string CounterSignature(List<Ware> wares, string location)
        {
            var sb = new System.Text.StringBuilder(location);
            foreach (var w in wares)
            {
                sb.Append('|').Append(w.Side).Append(':').Append(w.Id)
                  .Append(':').Append(w.Price).Append(':').Append(w.Note);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Which file each place in a rank stands in: -1 left, 0 middle, +1 right. First place is the
        /// middle, giving 2-1-3 across the front rank. See <see cref="PartyPose"/>.
        /// </summary>
        private static readonly int[] FileOfPlaceInRank = { 0, -1, 1 };

        /// <summary>The middle of a cell, which is where a figure standing alone belongs.</summary>
        private Vector3 CellCentre(int x, int y) =>
            CellToWorld(x, y) + new Vector3(cellSize * 0.5f, 0f, -cellSize * 0.5f);

        /// <summary>
        /// A name on a flag, stuck into the figure's base, for the situations where knowing which mini is
        /// which is the whole point -- picking six out of twelve in the tavern, above all.
        ///
        /// Upright rather than lying flat on the table, because the camera sits 38 degrees above the
        /// horizon and everything flat is foreshortened to about 62% of its height -- and height is what
        /// carries letterforms. Upright at that same angle keeps 79%. It also puts the name directly above
        /// its figure in SCREEN space, so the eye reads the two as one object; a flat card's projection
        /// drifts back over the square behind the mini as the camera tilts, which is what made the first
        /// attempt feel unmoored.
        ///
        /// The pole is not decoration. A name hanging in mid-air is HUD, and this table has no HUD; a name
        /// on a stick is a thing someone pushed into a plastic base, which is the only kind of object this
        /// scene contains. It also earns the upright tag physically instead of asking for it.
        ///
        /// The letters sit on a dark plaque cut to fit them, for the same reason: bare glyphs over the middle
        /// of the lamp pool were pale-on-pale and barely read, while the same glyphs against the dark end of
        /// the table were fine. The plaque fixes the contrast wherever the figure stands, and it turns the
        /// name into an object the light falls on rather than letters printed on the air.
        ///
        /// A child of the figure, NOT of the table: walking, turning and being toppled on death then carry
        /// the flag exactly as they carry the sculpt, with no per-frame upkeep and no chance of a name
        /// standing to attention over a corpse.
        /// </summary>
        private void NameCard(string id, string name, GameObject figure)
        {
            var key = "name:" + id;

            GameObject existing;
            if (_townLabels.TryGetValue(key, out existing) && existing != null) return;

            var go = new GameObject("Flag:" + id);
            go.transform.SetParent(figure.transform, false);

            // Measured in the figure's own space, so a toppled figure reports the same height as a standing
            // one: it is the mini that is lying down, not the mini that got shorter.
            var top = LocalTop(figure);
            var poleTop = top + cellSize * 0.10f;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            Destroy(pole.GetComponent<Collider>());   // the figure owns the click target; see TablePromptButton
            var card = CardStock();
            if (card != null) pole.GetComponent<Renderer>().sharedMaterial = card;
            pole.transform.SetParent(go.transform, false);
            // Unity's cylinder is two units tall and centred, hence the halves.
            pole.transform.localScale = new Vector3(cellSize * 0.03f, poleTop * 0.5f, cellSize * 0.03f);
            pole.transform.localPosition = new Vector3(0f, poleTop * 0.5f, 0f);

            var tag = new GameObject("Tag");
            tag.transform.SetParent(go.transform, false);
            tag.transform.localScale = Vector3.one * (cellSize * 0.16f);

            var text = tag.AddComponent<TMPro.TextMeshPro>();
            text.text = name;
            text.fontSize = 8f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.95f, 0.92f, 0.84f);
            text.rectTransform.sizeDelta = new Vector2(20f, 6f);

            // Cut the plaque to the name that is actually on it, which needs the glyphs laid out now rather
            // than at the end of the frame -- a card sized off an unbuilt mesh comes out the size of the
            // whole 20-unit text box.
            text.ForceMeshUpdate();
            var b = text.textBounds;

            var plaque = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plaque.name = "Plaque";
            Destroy(plaque.GetComponent<Collider>());   // as with the pole, the figure owns the click target
            var slate = Slate();
            if (slate != null) plaque.GetComponent<Renderer>().sharedMaterial = slate;
            plaque.transform.SetParent(tag.transform, false);
            plaque.transform.localScale = new Vector3(b.size.x + PlaqueMarginX * 2f,
                                                      b.size.y + PlaqueMarginY * 2f,
                                                      PlaqueThickness);
            // Behind the letters, not in front of them: the tag's own +Z faces away from the player, since
            // an unrotated tag is the one that reads right way round (see AlignTag).
            plaque.transform.localPosition =
                new Vector3(b.center.x, b.center.y, PlaqueThickness * 0.5f + 0.01f);

            _townLabels[key] = go;
            AlignTag(go.transform);   // seats the tag on the pole, right on the frame it appears
        }

        /// <summary>
        /// Points the tag at the player, or lays it flat once its figure has gone over.
        ///
        /// The tag swivels on its pole rather than being welded to it. Welded is the more honest object,
        /// but the tavern does not turn every figure the same way -- the bench stands at 0 degrees and the
        /// party at 180 -- so half the names would be read from behind, which is to say back-to-front. A
        /// tag that turns on its pole is a thing that exists; an unreadable name is not worth the purity.
        ///
        /// No billboarding is involved: the camera is pinned to one side of the table, so facing the
        /// player is a fixed rotation rather than a per-frame aim.
        ///
        /// Toppled, it lies flat and face-up, where the shallow camera angle still reads it. Standing a
        /// name to attention over a corpse was the thing to avoid.
        /// </summary>
        private void AlignTag(Transform flag)
        {
            var tag = flag.Find("Tag");
            if (tag == null) return;

            // Back to sitting on top of its pole, undoing any lift from a previous frame: a raise dead
            // stands the figure up again, and the tag has to come back up with it.
            tag.localPosition = new Vector3(0f, TagRestY(flag), 0f);

            // The flag is a child of the figure, so its own up vector has already fallen over with it.
            var down = flag.up.y < 0.5f;
            tag.rotation = down ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            if (!down) return;

            // Toppling turns about the base, which leaves everything attached centred on the figure's
            // origin -- and that origin is the MIDDLE of the floor tile, not its surface: the tiles are
            // slabs about 1.5mm thick. A tag laid at the origin is inside the tile and loses to it, which
            // is exactly how a dead man's name went missing while every other name was fine. Clear the
            // slab, so it rests ON the table the way a dropped flag would -- and clear it by the plaque's
            // depth as well, since flat on its back the plaque is what is underneath, not the letters.
            var plaque = tag.Find("Plaque");
            var clearance = cellSize * 0.05f;
            if (plaque != null)
                clearance += (plaque.localPosition.z + plaque.localScale.z * 0.5f) * tag.localScale.y;

            var p = tag.position;
            p.y = flag.position.y + clearance;
            tag.position = p;
        }

        /// <summary>
        /// Where the tag sits in the flag's own space: high enough that the bottom edge of the plaque meets
        /// the top of the pole, so the two read as one object someone assembled. A sign floating a
        /// millimetre clear of its post is the sort of thing that makes a scene feel authored.
        ///
        /// Derived from the plaque rather than stored, because <see cref="AlignTag"/> re-seats the tag every
        /// frame and two copies of this number would eventually disagree.
        /// </summary>
        private float TagRestY(Transform flag)
        {
            var pole = flag.Find("Pole");
            var poleTop = pole != null ? pole.localScale.y * 2f : 0f;   // the cylinder is two units tall

            var tag = flag.Find("Tag");
            var plaque = tag != null ? tag.Find("Plaque") : null;
            if (plaque == null) return poleTop + cellSize * 0.10f;

            var bottom = (plaque.localPosition.y - plaque.localScale.y * 0.5f) * tag.localScale.y;
            return poleTop - bottom;
        }

        /// <summary>
        /// How tall the figure is in its OWN space, ignoring where it happens to be pointing.
        ///
        /// Mesh bounds rather than renderer bounds: renderer bounds are world-axis-aligned, so a toppled
        /// figure reports its length as its height and the flag would sink into the table just as its owner
        /// died. Its own pick marker does not count towards height either.
        /// </summary>
        private float LocalTop(GameObject figure)
        {
            var toFigure = figure.transform.worldToLocalMatrix;
            var top = 0f;

            var filters = figure.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].GetComponent<TablePromptButton>() != null) continue;
                var mesh = filters[i].sharedMesh;
                if (mesh == null) continue;

                var toWorld = filters[i].transform.localToWorldMatrix;
                var b = mesh.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x,
                                        (corner & 2) == 0 ? b.min.y : b.max.y,
                                        (corner & 4) == 0 ? b.min.z : b.max.z);
                    var y = toFigure.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(p)).y;
                    if (y > top) top = y;
                }
            }

            return top;
        }

        // One texture and one material per artwork FILE, not per monster: five orcs are five standees printed
        // from the same picture, and a snapshot arrives every turn. A path that failed to load is remembered
        // as a null material so a missing or unreadable file is not re-read twelve times a fight.
        private readonly Dictionary<string, Material> _artPrints = new Dictionary<string, Material>();

        /// <summary>
        /// Pastes the old UI's picture of a monster onto its standee.
        ///
        /// The blank standee is already the right object -- a paper cylinder for a base and a 12.7 x 22 x 1.5mm
        /// card standing in it -- so this is genuinely printing on the card rather than adding scenery: the
        /// picture goes on the card's own renderer. Anything with a sculpted figure has no such card and is
        /// left alone, which is the correct outcome: the party's minis are carved, the monsters are cardboard.
        ///
        /// The card is then made the picture's own shape (within reason), because a goblin drawn wider than
        /// tall looks wrong stretched into a portrait card, and getting that right costs one division.
        /// </summary>
        private void PasteArt(GameObject figure, string path)
        {
            if (figure == null || string.IsNullOrEmpty(path)) return;
            if (figure.transform.Find(ArtCardName) != null) return;   // already printed

            // Something whose shader survives a player build, and the right one for a mini: the figure's own.
            var stock = figure.GetComponentInChildren<Renderer>();
            if (stock == null) return;

            Material print;
            if (!_artPrints.TryGetValue(path, out print))
            {
                print = Print(stock.sharedMaterial, path);
                _artPrints[path] = print;   // null included: a file that failed once will fail again
            }

            if (print == null) return;

            var texture = print.mainTexture;
            if (texture == null || texture.height <= 0) return;

            // The picture replaces the sculpt but NOT the plinth: a printed standee is a card in a base, and
            // keeping the base means it still stands on the table like everything else. Goblins arrive as a
            // little primitive body, head and club -- those go away rather than being wrapped in a drawing of
            // themselves.
            foreach (Transform child in figure.transform)
            {
                if (child.name == "Base") continue;
                child.gameObject.SetActive(false);
            }

            var height = cellSize * ArtCardCells;
            var width = Mathf.Min(height * texture.width / (float)texture.height, cellSize * 0.8f);

            // A thin box rather than a quad, for the same reason the name plaques are: a quad faces one way
            // only, and the one time it faces away it vanishes rather than looking wrong.
            var card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            card.name = ArtCardName;
            Destroy(card.GetComponent<Collider>());
            card.GetComponent<Renderer>().sharedMaterial = print;
            card.transform.SetParent(figure.transform, false);
            card.transform.localScale = new Vector3(width, height, cellSize * 0.06f);
            card.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

            // Turned to show its printed side. The figures are modelled facing their own -Z and corrected by
            // standeeYaw (see Face), so a card inheriting that correction presents its BACK to the player --
            // and a cube's far face carries the picture mirrored and upside down. Half a turn here rather than
            // flipping the texture, because the geometry is what is backwards.
            card.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        private const string ArtCardName = "Art";

        /// <summary>
        /// How tall a printed standee stands, in cells. Matches the blank standee's own card (22mm) so the
        /// monsters and the party read as the same kind of object at the same scale.
        /// </summary>
        private const float ArtCardCells = 0.85f;

        /// <summary>
        /// A material carrying one artwork file, cloned from the card's own so it keeps that shader.
        ///
        /// Cloned rather than created for the reason given on <see cref="Slate"/>: a material built from a
        /// shader nothing in a scene references is stripped from the player, and the standee would come out
        /// invisible in a build while looking right in the editor. Returns null if the file cannot be read,
        /// which leaves a blank standee -- correct for a monster the old UI never had a drawing of, and for a
        /// viewer running on a machine that cannot see the game's folders.
        /// </summary>
        private Material Print(Material stock, string path)
        {
            if (stock == null) return null;

            try
            {
                if (!System.IO.File.Exists(path)) return null;

                var bytes = System.IO.File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);

                // LoadImage reads PNG and JPG only. The art folders are PNG; a .webp among them simply
                // leaves the standee blank rather than throwing.
                if (!texture.LoadImage(bytes))
                {
                    Destroy(texture);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;

                var print = new Material(stock);
                print.color = Color.white;          // the card's own tint would darken the picture
                print.mainTexture = texture;
                return print;
            }
            catch
            {
                // Unreadable, locked, or a path this viewer cannot reach. A blank standee is the fallback.
                return null;
            }
        }

        /// <summary>
        /// The card the floor tiles are cut from, borrowed for the flagpole.
        ///
        /// Borrowed rather than made here on purpose: a material built at runtime references a shader that
        /// nothing in any scene points at, and the player strips exactly those -- the pole would be fine in
        /// the editor and invisible in a build, which is a bad afternoon.
        /// </summary>
        private Material CardStock()
        {
            if (floorTilePrefab == null) return null;
            var r = floorTilePrefab.GetComponentInChildren<Renderer>();
            return r != null ? r.sharedMaterial : null;
        }

        /// <summary>
        /// The dark stock the name plaques are cut from, made once and shared by every flag.
        ///
        /// A clone of the floor material rather than a fresh one, so it inherits a shader some scene object
        /// already points at -- see <see cref="CardStock"/> for what happens otherwise. Cloned rather than
        /// tinted in place because tinting the shared material would paint every floor tile black.
        /// </summary>
        private Material Slate()
        {
            if (_slate != null) return _slate;

            var stock = CardStock();
            if (stock == null) return null;

            _slate = new Material(stock);
            _slate.color = new Color(0.07f, 0.06f, 0.06f);
            return _slate;
        }

        private Material _slate;

        /// <summary>Room around the name, in the tag's own text units.</summary>
        private const float PlaqueMarginX = 0.55f;
        private const float PlaqueMarginY = 0.30f;

        /// <summary>
        /// Thin, but not zero: a plaque with real depth catches the lamp on its edge, and it cannot lose a
        /// coin-flip against the letters sitting on its face the way a coplanar quad would.
        /// </summary>
        private const float PlaqueThickness = 0.12f;

        /// <summary>
        /// A name card lying on the table beside each pad. Permanent, unlike the DM subtitle, which
        /// holds for a couple of seconds and clears — fine for a line of narration, useless for
        /// telling one place from another once it has faded.
        /// </summary>
        private void LabelPad(string id, int x, int y)
        {
            var key = "label:" + id;
            if (_townLabels.ContainsKey(key)) return;

            var go = new GameObject("Label:" + id);
            go.transform.SetParent(transform, false);

            var text = go.AddComponent<TMPro.TextMeshPro>();
            text.text = Prettify(id);
            text.fontSize = 8f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.92f, 0.88f, 0.78f);
            text.rectTransform.sizeDelta = new Vector2(20f, 6f);

            // Flat on the table, reading from the player's side, clear of the pad's south edge. The
            // pad covers cells y and y+1, so anything less than 1.9 cells out lands on top of it.
            // Scaled so the longest name is about a pad wide: TMP sizes are in points and a cell
            // here is a real 25mm, so the default would be microscopic.
            go.transform.localScale = Vector3.one * (cellSize * 0.22f);
            go.transform.position = CellToWorld(x, y)
                                  + new Vector3(cellSize * 0.5f, 0.0015f, -cellSize * 1.9f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _townLabels[key] = go;
        }

        private void LayPad(int x, int y)
        {
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    var key = "town:" + (x + dx) + "," + (y + dy);
                    if (_tiles.ContainsKey(key)) continue;

                    var centre = CellToWorld(x + dx, y + dy);
                    _tiles[key] = Instantiate(floorTilePrefab, centre, Quaternion.identity, transform);

                    if (_hasLaid) _laid.Encapsulate(centre);
                    else { _laid = new Bounds(centre, Vector3.zero); _hasLaid = true; }
                }
        }

        /// <summary>Cell for a place id, or null when the table has no spot for it.</summary>
        private static int[] TownCell(string location)
        {
            if (string.IsNullOrEmpty(location)) return null;

            foreach (var place in TownPlaces)
                if (place.Id == location)
                    return new[] { place.X, place.Y };

            return null;
        }

        /// <summary>"TrainingGrounds" -> "Training Grounds", for the one line that is display text.</summary>
        private static string Prettify(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";

            var sb = new System.Text.StringBuilder(id.Length + 4);
            for (int i = 0; i < id.Length; i++)
            {
                if (i > 0 && char.IsUpper(id[i]) && !char.IsUpper(id[i - 1])) sb.Append(' ');
                sb.Append(id[i]);
            }

            return sb.ToString();
        }

        private void LayExploredTiles(Snapshot s)
        {
            if (s.Grid == null) return;

            if (revealEntireLevel)
            {
                for (int y = 0; y < s.Grid.Height; y++)
                    for (int x = 0; x < s.Grid.Width; x++)
                        if (s.Grid.IsFloor(x, y)) LayCell(s.Grid, x, y);
                return;
            }

            if (s.Explored == null) return;

            foreach (var key in s.Explored)
            {
                var parts = key.Split(',');
                if (parts.Length != 2) continue;
                int x, y;
                if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) continue;
                LayCell(s.Grid, x, y);
            }
        }

        private void LayCell(Protocol.Grid grid, int x, int y)
        {
            var centre = CellToWorld(x, y);
            var key = x + "," + y;

            if (!_tiles.ContainsKey(key))
                _tiles[key] = Instantiate(floorTilePrefab, centre, Quaternion.identity, transform);

            if (_hasLaid) _laid.Encapsulate(centre);
            else { _laid = new Bounds(centre, Vector3.zero); _hasLaid = true; }

            // A wall piece goes on every edge where the neighbouring cell is solid. Because a
            // solid cell is never laid, no edge is ever built twice.
            RaiseWall(grid, x, y, 0, -1, "N");
            RaiseWall(grid, x, y, 0, 1, "S");
            RaiseWall(grid, x, y, 1, 0, "E");
            RaiseWall(grid, x, y, -1, 0, "W");
        }

        // Qualified: UnityEngine has a Grid of its own and it is not this one.
        private void RaiseWall(Protocol.Grid grid, int x, int y, int dx, int dy, string side)
        {
            if (wallPiecePrefab == null) return;
            if (grid.IsFloor(x + dx, y + dy)) return;

            var key = $"{x},{y},{side}";
            if (_walls.ContainsKey(key)) return;

            // The piece is a thin slab: local X spans the edge, Y is its height, Z is thickness.
            // Taken from the prefab rather than restated here, so the two cannot drift apart.
            var scale = wallPiecePrefab.transform.localScale;
            var height = scale.y;
            var thickness = scale.z;

            // Pushed out by half its thickness so the slab's inner face lands exactly on the cell
            // boundary. Centred on the boundary instead, a wall eats 1.75mm into the corridor and
            // the outer files of a marching party clip it — the space it moves into is solid rock.
            var step = (cellSize + thickness) * 0.5f;

            // Grid y grows south, so a north neighbour sits at +z in world space.
            var offset = new Vector3(dx * step, height * 0.5f, -dy * step);
            var rotation = dx != 0 ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

            _walls[key] = Instantiate(wallPiecePrefab, CellToWorld(x, y) + offset, rotation, transform);
        }

        private GameObject Ensure(string id, string preferredKey, string fallbackKey, out bool created)
        {
            GameObject existing;
            if (_figurines.TryGetValue(id, out existing))
            {
                created = false;
                return existing;
            }

            var prefab = Resolve(preferredKey) ?? Resolve(fallbackKey) ?? blankStandeePrefab;

            // Unknown id -> blank standee. Cheaper than a placeholder and funnier in context.
            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity, transform);
            go.name = id;
            _figurines[id] = go;
            created = true;
            return go;
        }

        /// <summary>
        /// Find a figure for an id, following aliases when nothing is registered under it directly.
        /// Chains are followed a few hops so "Human_Cleric -> Human_Priest" works even if the
        /// target is itself an alias, with a visited set because a typo could otherwise loop.
        /// </summary>
        private GameObject Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            var direct = Lookup(key);
            if (direct != null) return direct;

            if (standeeAliases == null || standeeAliases.Length == 0) return null;

            var seen = new HashSet<string>();
            var current = key;

            for (int hop = 0; hop < 4; hop++)
            {
                if (!seen.Add(current)) return null;   // alias points back at itself

                string next = null;
                foreach (var alias in standeeAliases)
                {
                    if (alias.from != current || string.IsNullOrEmpty(alias.to)) continue;
                    next = alias.to;
                    break;
                }

                if (next == null) return null;

                var found = Lookup(next);
                if (found != null) return found;

                current = next;
            }

            return null;
        }

        private GameObject Lookup(string key)
        {
            if (string.IsNullOrEmpty(key) || standees == null) return null;

            foreach (var entry in standees)
                if (entry.id == key && entry.prefab != null) return entry.prefab;

            return null;
        }

        /// <summary>
        /// Records where a figure belongs. Snaps rather than slides when it has just been set
        /// down, or when the distance means we are catching up rather than moving.
        ///
        /// <paramref name="travelLook"/> means this is a walk between two places in town, and says which way
        /// to face while covering the ground. It waives the far test, because town pads are four cells apart
        /// at the closest and every crossing would otherwise trip a threshold meant for dungeon steps and
        /// arrive by teleport. Here the long slide IS the move.
        ///
        /// Only a known walk waives it, not merely being in town: coming back up from the dungeon puts the
        /// party on a pad while their figures are still standing wherever they were underground, and that is
        /// a catch-up jump with no meaning above ground -- they should be set down at the gate, not glide to
        /// it from the middle of the table. A figure being set down for the first time snaps too; that is
        /// placement, not travel.
        ///
        /// The travel facing is kept out of <c>_anchor</c> so the resting pose survives the trip and can be
        /// taken up again on arrival -- see <see cref="Update"/>.
        /// </summary>
        private void Place(string id, GameObject go, Placement pose, bool created, Quaternion? travelLook = null)
        {
            _anchor[id] = pose;
            _target[id] = travelLook.HasValue
                ? new Placement { Position = pose.Position, Rotation = travelLook.Value }
                : pose;

            var far = Vector3.Distance(go.transform.position, pose.Position) > cellSize * snapBeyondCells;
            if (created || (far && !travelLook.HasValue))
            {
                go.transform.position = pose.Position;
                go.transform.rotation = pose.Rotation;
            }
        }

        /// <summary>
        /// Where a marching order slot stands within its cell.
        ///
        /// The first of each rank takes the MIDDLE, so the six of them read:
        ///
        ///     2 1 3      <- front rank, and 1 is the leader
        ///     5 4 6
        ///
        /// rather than 123/456 left to right. The point is that the party has a leader and the table should
        /// say so: whoever is first is the one out in front, which is also whose eyes a first-person view
        /// would sit behind.
        ///
        /// Purely how they stand. The front rank is still slots 0-2 and the back still 3-5, so who may
        /// choose Fight is untouched -- that is the game's ruling and it is not restated here.
        /// </summary>
        private Placement PartyPose(int slot, int[] cell, Vector3 facing, Quaternion look)
        {
            var rank = slot / 3;                        // 0 = front, 1 = back
            var file = FileOfPlaceInRank[slot % 3];     // middle, then left, then right

            // Ranks straddle the cell centre (-0.5, +0.5) rather than starting on it, so the
            // formation sits in the middle of its square instead of hanging out the back.
            var offset = RightOf(facing) * (file * cellSize * fileSpacing)
                       - facing * ((rank - 0.5f) * cellSize * rankSpacing);

            return new Placement
            {
                Position = CellToWorld(cell[0], cell[1]) + offset,
                Rotation = look,
            };
        }

        /// <summary>
        /// How the party stands at a place in town: two staggered rows facing the player, centred on the pad.
        ///
        /// Marching order is the wrong shape here and was hiding half of them. In a column each back-rank
        /// figure stands directly behind a front-rank one, which is what a column IS -- correct underground,
        /// where it also happens to be edge-on to the camera. In town the camera looks at the column from the
        /// FRONT, so slots 3 to 5 stood squarely in front of slots 0 to 2 and, measured at the gate, took 59%
        /// of the party's silhouette with them; one figure was 100% hidden behind another. The board was
        /// showing three figures and a suggestion of three more.
        ///
        /// So: rows offset by half a place, and the front row nearer the CAMERA rather than nearer the
        /// direction of march. Every figure then stands in a gap rather than behind a back. It reads as a
        /// group standing together looking at you, which is what they are doing.
        ///
        /// Laid out along world x and z rather than around a facing, because in town there is no direction of
        /// march to orient to -- only the one side of the table the player sits on.
        ///
        /// Centred on the pad, which is NOT what the marching version did: it centred on the first of the
        /// pad's four tiles, standing the whole party in the back-left quarter and leaving three quarters of
        /// the pad empty. The buildings are centred there too, so this also puts them in the room rather than
        /// against its corner.
        ///
        /// Then stood forward of that centre, because every place keeps its furniture at the back -- the
        /// altar, the tavern's table, the weapon rack -- and two rows centred on the pad put the back row
        /// standing INSIDE it, measurably: two millimetres into the altar at this scale. Forward also happens
        /// to be where a group that has just walked in through the doorway would be, and it brings them
        /// nearer the camera, which is worth something on a board this wide.
        /// </summary>
        /// <param name="squareToBoard">
        /// Lay the rows along the board's own axes instead of across the line to the player. Wanted where the
        /// party is arranged against something BUILT -- a shop counter -- rather than posing for the camera.
        /// </param>
        private Placement TownPose(int slot, int count, int[] cell, bool squareToBoard = false)
        {
            var file = cellSize * townFileSpacing;
            var rank = cellSize * townRankSpacing;

            var centre = CellCentre(cell[0], cell[1]);

            // Laid out towards THIS pad's own view of the player, not towards one side of the table.
            //
            // The town shot takes in fourteen cells from about a third of a metre away, so the outer pads are
            // seen from well off to one side -- the shop sits six cells right of the middle. A row laid along
            // world x is edge-on from there and the party lines up along the line of sight instead of across
            // it: measured at the shop, one figure took eighteen samples of another's body. Underground this
            // never comes up, because the camera is always over the party and their own facing decides the
            // formation anyway.
            //
            // The seat is worked out from the BOARD rather than read off the live camera, which was the first
            // attempt and is wrong on the one path that matters most: coming up from the dungeon, the town
            // snapshot lands while the camera is still over the party's last dungeon cell, so every row was
            // laid to a view nobody would ever have and stayed that way until the next keypress. The board
            // does not move, so this does not either.
            var toPlayer = Vector3.back;
            if (_hasLaid && !squareToBoard)
            {
                var seat = _laid.center + Vector3.back * (cellSize * townSeatCells);
                var v = seat - centre;
                v.y = 0f;
                if (v.sqrMagnitude > 1e-8f) toPlayer = v.normalized;
            }

            var across = Vector3.Cross(Vector3.up, toPlayer).normalized;

            // Three abreast at most: six in a line would not fit between a building's walls, and the pads
            // are only two cells across.
            var front = count <= 3 ? count : (count + 1) / 2;
            var inFront = slot < front;

            var place = inFront ? slot : slot - front;
            var places = inFront ? front : count - front;

            // Each row centred on the pad, then pulled half a place apart from each other -- that half place
            // is the whole trick, and it survives an odd party size because each row is centred on its own
            // count rather than on the other row's.
            var x = (place - (places - 1) * 0.5f) * file + (inFront ? -file * 0.25f : file * 0.25f);
            var back = (inFront ? -rank * 0.5f : rank * 0.5f)
                     - cellSize * townStandForward;

            return new Placement
            {
                // Positive `back` is away from the player, so it runs against the direction to them.
                Position = centre + across * x - toPlayer * back,
                // toPlayer points AT the player, which is the direction to face -- the same sense as the
                // Face(Vector3.back) this replaces, since back is the direction of the seat.
                Rotation = Face(toPlayer),
            };
        }

        /// <summary>The party walks to the new cell, keeping marching order on the way.</summary>
        private void SlideParty(LogEntry e, Snapshot s)
        {
            if (e.To == null || e.To.Length != 2) return;

            var facing = FacingVector(s.Party.Count > 0 ? s.Party[0].Facing : "North");
            var look = Face(facing);

            foreach (var p in s.Party)
            {
                if (!_figurines.ContainsKey(p.Id)) continue;
                var pose = PartyPose(p.Slot, e.To, facing, look);
                _anchor[p.Id] = pose;
                _target[p.Id] = pose;
            }
        }

        /// <summary>
        /// The attacker steps into the blow. Reconcile at the end of the step puts them back,
        /// so this needs no timer of its own — and losing the beat costs nothing.
        /// </summary>
        private void LeanInto(LogEntry e)
        {
            if (e.By == null || e.At == null) return;

            GameObject victim;
            Placement home;
            if (!_figurines.TryGetValue(e.At, out victim)) return;
            if (!_anchor.TryGetValue(e.By, out home)) return;

            var toward = victim.transform.position - home.Position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-8f) return;

            home.Position += toward.normalized * (cellSize * 0.3f);
            _target[e.By] = home;
        }

        /// <summary>Tip the standee over where it stands, so the kill reads before Reconcile.</summary>
        private void Topple(LogEntry e)
        {
            if (e.At == null) return;

            Placement pose;
            if (!_anchor.TryGetValue(e.At, out pose)) return;

            pose.Rotation = Toppled(pose.Rotation);
            _anchor[e.At] = pose;
            _target[e.At] = pose;
        }

        private void RecomputePartyCentre(Snapshot s)
        {
            var sum = Vector3.zero;
            var n = 0;
            var tallest = 0f;
            var lead = Vector3.zero;
            var hasLead = false;

            foreach (var p in s.Party)
            {
                Placement pose;
                if (!_anchor.TryGetValue(p.Id, out pose)) continue;
                sum += pose.Position;
                n++;

                // The tallest head in the party, whoever it belongs to. A first-person view taken from the
                // shortest would have the halfling looking at the fighter's belt.
                GameObject figure;
                if (_figurines.TryGetValue(p.Id, out figure) && figure != null)
                    tallest = Mathf.Max(tallest, LocalTop(figure));

                // Slot 0 leads the marching order, and is the one standing where the party is looking.
                if (p.Slot == 0) { lead = pose.Position; hasLead = true; }
            }

            PartyCentre = n > 0 ? sum / n : (Vector3?)null;
            PartyFacing = FacingVector(s.Party.Count > 0 ? s.Party[0].Facing : "North");
            PartyEyeHeight = tallest;
            PartyLead = hasLead ? lead : PartyCentre;
        }

        /// <summary>Which way the party is looking. Vector3.forward when there is nobody to ask.</summary>
        public Vector3 PartyFacing { get; private set; } = Vector3.forward;

        /// <summary>
        /// The tallest party figure's height above the floor they stand on, in metres.
        ///
        /// Exposed for a camera that wants to sit at their eye rather than above their hats. Measured from the
        /// sculpts themselves, because a 28mm figure and a printed standee are not the same height and neither
        /// is a number in this file.
        /// </summary>
        public float PartyEyeHeight { get; private set; }

        /// <summary>Where the front of the marching order stands, or the party centre if nobody claims slot 0.</summary>
        public Vector3? PartyLead { get; private set; }

        /// <summary>Grid directions. +x is east, +z is north; the grid runs south as y grows.</summary>
        private static Vector3 FacingVector(string facing)
        {
            switch (facing)
            {
                case "East":  return Vector3.right;
                case "West":  return Vector3.left;
                case "North": return Vector3.forward;
                default:      return Vector3.back;   // South, and anything unrecognised
            }
        }

        private static Vector3 RightOf(Vector3 facing)
        {
            return new Vector3(facing.z, 0f, -facing.x);
        }

        /// <summary>
        /// Turn a direction into a figure's rotation.
        ///
        /// The sculpts do not face down their own +Z, so pointing them with a plain LookRotation
        /// stood the whole party with its back to the way it was marching. <see cref="standeeYaw"/>
        /// is the correction, kept as a field because a different set of figures may well be
        /// modelled facing the other way.
        /// </summary>
        private Quaternion Face(Vector3 direction)
        {
            return Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, standeeYaw, 0f);
        }

        /// <summary>
        /// Topple around the figure's own base rather than resetting rotation, so the facing
        /// set during placement survives -- as a roll along the figure's length, once it is down.
        ///
        /// Pitched about the WORLD x axis, which drops every figure the same way: head towards the player.
        /// Pitching in the figure's own space instead makes the fall direction depend on which way it
        /// happened to be facing, and the tavern faces its two rows opposite ways -- so half the dead fell
        /// away from the camera and landed with their own base, sculpt and pick marker between the player and
        /// the name on their flag. Measured on Yorick: three occluders, the name shaved to its top 3 pixels.
        /// Falling towards the player puts the flag down in clear table in front of the body.
        /// </summary>
        private static Quaternion Toppled(Quaternion upright)
        {
            return Quaternion.AngleAxis(-90f, Vector3.right) * upright;
        }
    }
}
