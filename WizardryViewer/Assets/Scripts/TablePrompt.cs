using System.Collections.Generic;
using TMPro;
using UnityEngine;
using WizardryViewer.Protocol;

namespace WizardryViewer.Unity
{
    /// <summary>
    /// Draws the game's open prompt as a see-through dialog lying on the table, and sends back the
    /// option that is pressed.
    ///
    /// WORLD-space, like the subtitle and for the same reason: a screen overlay does not render in VR,
    /// and a panel resting on the table is a thing a headset user can reach for. Buttons are hit with an
    /// ordinary physics raycast, so the pointer that replaces the mouse later runs this same code -- only
    /// where the ray starts changes. Nothing here is in pixels; the dialog is measured in table cells, so
    /// it is the same physical size on a monitor and in a headset.
    ///
    /// Each button also shows the key that answers it, sent by the game with the prompt. That is a hint
    /// only: the viewer always answers with the option id and the game decides what it means, so a
    /// headset with no keyboard just shows a button with no hint and everything else is unchanged.
    ///
    /// It draws exactly what it is given. Which options exist, and whether they are legal, was settled by
    /// the game before the prompt was sent, so there is no rule here that could disagree with the
    /// keyboard. If the game stops asking, the dialog goes away.
    ///
    /// Placement follows the party rather than sitting at a fixed spot, because the camera follows the
    /// party too -- a dialog pinned to the table's edge would spend most of the game off screen.
    /// </summary>
    public sealed class TablePrompt : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private ViewerReceiver receiver;
        [SerializeField] private TableRenderer table;

        [Tooltip("Font for the dialog text. A reference, not a Resources load, so the build keeps it.")]
        [SerializeField] private TMP_FontAsset font;

        [Header("Materials (assets only -- never created at runtime)")]
        [Tooltip("Smoked glass for the panel. Must be a TRANSPARENT material; see MakeGlass in setup.")]
        [SerializeField] private Material panelMaterial;
        [SerializeField] private Material buttonMaterial;
        [SerializeField] private Material buttonHotMaterial;

        [Header("Layout, in table cells (1 cell = 1 inch)")]
        [SerializeField] private float cellSize = 0.0254f;

        // A button is about 4.4cm x 1.3cm on a table where a dungeon cell is one inch: a comfortable
        // pointer target in a headset, and wide enough for "Choose target group" plus its key.
        //
        // THREE columns, because the follow camera only shows about three and a half cells of table
        // between the party and the bottom of the screen -- measured, not guessed. Two columns pushed a
        // six-option fight onto three rows and the last two rows fell off the screen entirely.
        [SerializeField] private float buttonWidthCells = 1.75f;
        // Tall enough for two wrapped lines of 5mm text plus leading.
        [SerializeField] private float buttonHeightCells = 0.62f;
        [SerializeField] private float gapCells = 0.1f;
        [SerializeField] private float paddingCells = 0.16f;
        [SerializeField] private float headerCells = 0.42f;
        [SerializeField] private int columns = 3;

        // How far past the party the dialog would LIKE to sit. It may end up closer: see FitStandoff.
        // The dialog deliberately overlaps the room rather than clearing it -- that is what smoked glass
        // is for, and the alternative is a panel that does not fit on screen.
        [SerializeField] private float standoffCells = 0.9f;
        [SerializeField] private float minStandoffCells = 0.35f;

        // The dialog HOVERS rather than resting flush on the table, because the room's walls stand about
        // 0.68 cells proud of the floor the party stands on -- measured -- and a flush panel slides under
        // the near wall, hiding its own top row and header. Floating just clear of the walls also suits a
        // headset, where a tray of buttons above the board is easier to reach than one lying under a wall.
        [SerializeField] private float hoverCells = 0.8f;
        [Tooltip("Most of the frame's width the dialog may take. The same dialog has to work in the town's " +
                 "wide shot and the maze's close one, so it is fitted to the shot rather than to the table.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float maxFrameWidth = 0.55f;

        [Header("Bubble (a choice asked of one figure)")]
        [Tooltip("Most of the frame's width a bubble may take. Smaller than a board dialog on purpose: it " +
                 "belongs to one figure and should not cover the fight going on behind it.")]
        [Range(0.15f, 0.8f)]
        [SerializeField] private float bubbleFrameWidth = 0.36f;
        [Tooltip("Cells above the figure's feet that the bubble's lower edge sits at -- clear of a standing " +
                 "figure's head, which is about 0.85 of a cell.")]
        [SerializeField] private float bubbleLiftCells = 1.05f;

        [Header("Colours")]
        [SerializeField] private Color headerColour = new Color(0.94f, 0.91f, 0.84f, 1f);
        [SerializeField] private Color labelColour = new Color(0.96f, 0.94f, 0.88f, 1f);
        [SerializeField] private Color keyColour = new Color(0.92f, 0.78f, 0.36f, 1f);

        private const string RootName = "Dialog";
        private const string LegacyRootName = "Cards";   // what this drew before it was a dialog

        /// <summary>
        /// Metres of rendered glyph per unit of TMP fontSize, for the font this dialog uses.
        ///
        /// Measured, not derived: rendering known sizes and reading the mesh bounds back gives 0.127, and
        /// the numbers below are text heights in metres divided by it. Treating fontSize as metres -- which
        /// it looks like, next to every other dimension here -- makes the text eight times too small, and
        /// it still reports the size you asked for, so the mistake is invisible from the inspector.
        /// </summary>
        private const float MmPerUnit = 0.127f;

        private static float SizeFor(float metresTall) => metresTall / MmPerUnit;

        private readonly List<TablePromptButton> _buttons = new List<TablePromptButton>();

        // Rings live on the FIGURES, not in the dialog, so they follow whatever they are marking without
        // this having to place them every frame. Tracked separately because they are cleared differently:
        // a figure can leave the table and take its ring with it.
        private readonly List<GameObject> _rings = new List<GameObject>();

        // The key drawn beside a figure the game has numbered. Paired with the ring it belongs to because it is
        // NOT parented to it: a ring is squashed flat and a figure is turned to face whatever it is fighting,
        // and a digit inheriting either comes out flattened or upside down -- a 6 that reads as a 9 is worse
        // than no digit at all. So these are laid out in world space each frame, like the dialog itself.
        private readonly List<KeyGlyph> _ringKeys = new List<KeyGlyph>();

        // The key drawn on a card, which needs none of that: a card's own tag has already solved which way up
        // its lettering goes, so the glyph is a child of the tag and inherits the answer.
        private readonly List<GameObject> _cardKeys = new List<GameObject>();

        private struct KeyGlyph
        {
            public Transform Glyph;
            public Transform Ring;
        }

        private Transform _root;
        private TablePromptButton _hovered;
        private long _shownPromptId = -1;

        // Whose choice this is, when the game named someone. See Anchor().
        private string _askedOf;
        private bool _subscribed;
        private float _panelDepth;   // set when the dialog is built, used when placing it
        private float _panelWidth;   // likewise, for fitting the dialog to what the camera can see

        private float ButtonWidth => buttonWidthCells * cellSize;
        private float ButtonHeight => buttonHeightCells * cellSize;
        private float Gap => gapCells * cellSize;
        private float Padding => paddingCells * cellSize;
        private float HeaderHeight => headerCells * cellSize;
        private float PanelThickness => cellSize * 0.06f;
        private float ButtonThickness => cellSize * 0.05f;

        /// <summary>
        /// Re-entrant on purpose. Recompiling during play reloads the domain: subscriptions are dropped
        /// and plain fields reset, but the dialog itself is a real GameObject that survives. Rebuilding
        /// from whatever is actually in the scene is the only way back from that; see the same problem
        /// and the same answer in <see cref="ViewerReceiver"/>.
        /// </summary>
        private void EnsureSubscribed()
        {
            if (_subscribed || receiver == null) return;

            receiver.PromptChanged += Show;
            _subscribed = true;

            DestroyChildrenOf(transform.Find(RootName));
            DestroyChildrenOf(transform.Find(LegacyRootName));

            // Rings sit on the figures, outside this object's hierarchy, so clearing the dialog root does
            // not reach them. Any left over belong to a previous domain: they answer to a prompt id that
            // no longer exists and would still take clicks.
            foreach (var stray in FindObjectsByType<TablePromptButton>(FindObjectsSortMode.None))
            {
                if (stray != null && stray.transform.IsChildOf(transform)) continue;
                if (stray != null) Destroy(stray.gameObject);
            }

            // Keys chalked by a previous domain, for the same reason: the list that knew about them is gone, so
            // they would sit on the table promising a keystroke nothing is listening for. A ring's key is a
            // direct child of this object and a card's hangs off the card's tag, which is what tells the two
            // apart from the key on a dialog BUTTON -- that one goes with the dialog root above.
            foreach (var stray in FindObjectsByType<TextMeshPro>(FindObjectsSortMode.None))
            {
                if (stray == null || stray.name != KeyGlyphName) continue;

                var parent = stray.transform.parent;
                if (parent == transform || (parent != null && parent.name == "Tag")) Destroy(stray.gameObject);
            }

            _buttons.Clear();
            _rings.Clear();
            _ringKeys.Clear();
            _cardKeys.Clear();
            _shownPromptId = -1;

            Show(receiver.CurrentPrompt);
        }

        private static void DestroyChildrenOf(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }

        private void OnDisable()
        {
            if (receiver != null && _subscribed) receiver.PromptChanged -= Show;
            _subscribed = false;
        }

        private void Update()
        {
            EnsureSubscribed();

            // Rings count: an all-targets prompt has no buttons but still has to answer a click.
            if (_buttons.Count == 0 && _rings.Count == 0) return;

            if (_buttons.Count > 0) Place();
            FaceRingKeys();
            Aim();
        }

        /// <summary>Rebuilds the dialog for a new question. Cheap: prompts change per player choice.</summary>
        private void Show(Prompt prompt)
        {
            var id = prompt != null ? prompt.Id : -1;
            if (id == _shownPromptId)
            {
                RetypeHeader(prompt);
                return;
            }
            _shownPromptId = id;
            _askedOf = prompt != null ? prompt.For : null;

            Clear();

            if (prompt == null || prompt.Options == null || prompt.Options.Count == 0) return;

            if (panelMaterial == null || buttonMaterial == null || font == null)
            {
                Debug.LogWarning("[viewer] the prompt dialog needs panel and button materials and a font " +
                                 "assigned; re-run Wizardry Viewer > Build Sample Table, or assign them " +
                                 "on TablePrompt.");
                return;
            }

            EnsureRoot();
            Build(prompt);

            Place();
            Aim();
        }

        /// <summary>
        /// Keeps the line above the buttons on the latest snapshot, without rebuilding the dialog.
        ///
        /// A prompt's id is deliberately the identity of the QUESTION and not of the moment it was asked, so
        /// restating the same choice keeps the same id and an answer already on its way stays valid. The TEXT
        /// is not part of the question though -- it is the state around it. In town the question never
        /// changes: one button, "Next place", from the first pad to the last. Only the line naming where the
        /// party is standing changes, so keyed on the id alone the dialog went on insisting they were at the
        /// gate while they stood at the training ground.
        ///
        /// Only the words are replaced. The buttons keep their identity, and with it any click in flight.
        /// A prompt that gains or loses its header entirely still needs the rebuild -- the panel is sized
        /// around it -- but that is a different question by then, so the id has changed anyway.
        /// </summary>
        private void RetypeHeader(Prompt prompt)
        {
            if (prompt == null || _root == null) return;

            var header = _root.Find("Header");
            if (header == null) return;

            var text = header.GetComponent<TextMeshPro>();
            if (text == null) return;

            var wanted = prompt.Text ?? string.Empty;
            if (text.text != wanted) text.text = wanted;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;

            _root = transform.Find(RootName);
            if (_root != null) return;

            var go = new GameObject(RootName);
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }

        /// <summary>
        /// Builds the panel, the header and one button per option.
        ///
        /// Everything is laid out in the ROOT's local axes and given no local rotation of its own, which
        /// is what keeps the text readable: <see cref="Place"/> gives the root the one orientation that
        /// reads correctly from the camera, and every child inherits it. Text laid out any other way
        /// wants a per-frame correction, and getting that correction wrong is invisible until someone
        /// looks at the table and finds the labels mirrored.
        /// </summary>
        private void Build(Prompt prompt)
        {
            // Options that point at something on the table become rings under it; the rest become buttons.
            // Partitioned FIRST because the panel is sized from the buttons alone -- counting the targeted
            // ones would leave a dialog full of gaps where the figurines are doing the work.
            var plain = new List<PromptOption>();

            foreach (var option in prompt.Options)
            {
                if (option == null || string.IsNullOrEmpty(option.Id)) continue;

                if (string.IsNullOrEmpty(option.Target)) { plain.Add(option); continue; }

                // A ware is the one target that IS the button: a card on a counter is already a labelled,
                // clickable object, so ringing it would be marking a price tag with a price tag. Everything
                // else gets a ring at its feet.
                if (MakeCardClickable(option)) continue;

                // A target that is not on the table falls back to a button, so an option is never
                // unanswerable just because the thing it names has gone.
                var ring = BuildRing(option);
                if (ring == null) plain.Add(option);
            }

            var count = plain.Count;
            var cols = Mathf.Max(1, columns);
            var rows = Mathf.CeilToInt(count / (float)cols);

            var hasHeader = !string.IsNullOrEmpty(prompt.Text);

            // An all-targets prompt ("pick a figurine") has no buttons at all. It still gets a panel if
            // there is a header to carry, because the question itself has to be readable somewhere; with
            // nothing to say and nothing to press there is no dialog to draw.
            if (count == 0 && !hasHeader) return;

            var header = hasHeader ? HeaderHeight + (count > 0 ? Gap : 0f) : 0f;

            var panelWidth = Padding * 2f + cols * ButtonWidth + (cols - 1) * Gap;
            var panelDepth = Padding * 2f + header
                           + (count > 0 ? rows * ButtonHeight + (rows - 1) * Gap : 0f);
            _panelDepth = panelDepth;
            _panelWidth = panelWidth;

            // Local +X runs along the camera's right, +Y away from the viewer, +Z downward -- see Place.
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(_root, false);
            panel.transform.localScale = new Vector3(panelWidth, panelDepth, PanelThickness);
            panel.GetComponent<Renderer>().sharedMaterial = panelMaterial;

            // The panel is scenery, not a target: leaving its collider on would let the gaps between
            // buttons swallow a click that should have missed everything.
            var panelCollider = panel.GetComponent<Collider>();
            if (panelCollider != null) Destroy(panelCollider);

            var top = panelDepth * 0.5f - Padding;

            if (hasHeader)
            {
                var text = MakeText("Header", _root, panelWidth - Padding * 2f, HeaderHeight,
                                    headerColour, TextAlignmentOptions.Left, SizeFor(0.0058f));
                text.text = prompt.Text;
                text.transform.localPosition = new Vector3(
                    -panelWidth * 0.5f + Padding,
                    top - HeaderHeight * 0.5f,
                    -(PanelThickness * 0.5f + 0.0002f));
                // Left-aligned text is positioned by its left edge, so anchor the box there.
                text.rectTransform.pivot = new Vector2(0f, 0.5f);
            }

            var firstRow = top - header;

            for (int i = 0; i < count; i++)
            {
                var option = plain[i];

                var row = i / cols;
                var column = i % cols;

                // Centre a final short row under the full ones, so three options do not leave a hole.
                var inThisRow = Mathf.Min(cols, count - row * cols);
                var rowWidth = inThisRow * ButtonWidth + (inThisRow - 1) * Gap;

                var x = -rowWidth * 0.5f + ButtonWidth * 0.5f + column * (ButtonWidth + Gap);
                var y = firstRow - ButtonHeight * 0.5f - row * (ButtonHeight + Gap);

                _buttons.Add(BuildButton(option, new Vector3(x, y, 0f)));
            }
        }

        /// <summary>
        /// A ring on the table under the figure an option points at, which is what makes the figure
        /// itself clickable. Returns null when nothing on the table answers to the target.
        ///
        /// A ring rather than tinting the figure: the standees share their materials, so colouring one
        /// mini would colour every mini of that class, and the fix for that -- an instanced copy -- means
        /// a material made at runtime, whose shader a player build is free to strip. A ring is its own
        /// object with its own renderer, and it reads as "you may pick this up" instead of "this is lit".
        ///
        /// Parented to the figure, so it follows the mini wherever the table puts it next.
        /// </summary>
        /// <summary>
        /// Turns a ware's card into the button for its option, and reports whether it did.
        ///
        /// The card belongs to the renderer, not to this dialog, so what is added here is only the button
        /// component -- and it has to come off again when the prompt changes, or a card would still answer a
        /// question nobody is asking. That is why these are tracked separately from the rings, which this
        /// dialog creates and can simply destroy.
        /// </summary>
        private bool MakeCardClickable(PromptOption option)
        {
            if (table == null) return false;

            var card = table.FindWareCard(option.Target);
            if (card == null) return false;

            var slab = card.GetComponentInChildren<Renderer>();
            if (slab == null) return false;

            var button = card.gameObject.GetComponent<TablePromptButton>();
            if (button == null) button = card.gameObject.AddComponent<TablePromptButton>();

            button.Configure(option.Id, slab, slab.sharedMaterial, buttonHotMaterial ?? slab.sharedMaterial);
            _cardButtons.Add(button);

            MarkCard(card, option.Key);
            return true;
        }

        /// <summary>
        /// Chalks the key in the corner of a card, so the letter that buys it is written on the thing itself.
        ///
        /// A card is not a button in a dialog and has no room for a key column, but it is the only place the
        /// hint can go: the option was never drawn as a button, so a player reading the table would otherwise
        /// have no way of knowing the counter answers to the keyboard at all.
        ///
        /// Hung off the card's own TAG rather than the card, because the tag already carries the rotation that
        /// makes lettering read the right way round -- upright on a shelf, lying flat on the counter -- and
        /// working that out a second time here is how the two would come to disagree.
        /// </summary>
        private void MarkCard(Transform card, string key)
        {
            if (string.IsNullOrEmpty(key) || font == null) return;

            var tag = card.Find("Tag");
            if (tag == null) return;

            var existing = tag.Find(KeyGlyphName);
            if (existing != null) Destroy(existing.gameObject);

            // The card's own face, measured from the tag that already covers it, so a glyph lands on the slate
            // whatever a card happens to measure and this file needs no copy of the renderer's dimensions.
            var written = tag.GetComponent<TextMeshPro>();
            var box = written != null ? written.rectTransform.sizeDelta : new Vector2(2.4f, 1.8f);

            var text = MakeText(KeyGlyphName, tag, box.x, box.y, keyColour,
                                TextAlignmentOptions.BottomLeft, TagKeySize);
            text.text = key;

            // Just clear of the slate, on the same side of it as the tag's own lettering, so it is not inside
            // the card looking out.
            text.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            _cardKeys.Add(text.gameObject);
        }

        // In the TAG's units, where the renderer sets a card's own lettering to 2.6: a shade larger, because it
        // is one character standing for the whole card and it is read at a glance rather than a squint.
        private const float TagKeySize = 3.4f;
        private const string KeyGlyphName = "Key";

        /// <summary>
        /// Turns the digit beside a numbered figure to lie flat and read from where the camera is, which is the
        /// same orientation the board's dialogs take and for the same reason -- see the note on Place.
        /// </summary>
        private void FaceRingKeys()
        {
            if (_ringKeys.Count == 0) return;

            var cam = Camera.main;
            if (cam == null) return;

            for (int i = _ringKeys.Count - 1; i >= 0; i--)
            {
                var pair = _ringKeys[i];

                // The figure it was marking has left the table, and taken its ring with it.
                if (pair.Glyph == null || pair.Ring == null)
                {
                    if (pair.Glyph != null) Destroy(pair.Glyph.gameObject);
                    _ringKeys.RemoveAt(i);
                    continue;
                }

                var toCamera = cam.transform.position - pair.Ring.position;
                toCamera.y = 0f;
                toCamera = toCamera.sqrMagnitude < 1e-8f ? Vector3.back : toCamera.normalized;

                // Beside the ring rather than on it: a digit inside the disc is read over the top of the mini
                // standing in it.
                //
                // Off to the SIDE, not toward the viewer. Toward the viewer is where the dialog is: it hovers
                // just clear of the table between the camera and the board, so for the figures at the near
                // edge -- the whole party, at the tavern and at the counter -- the number was drawn on the
                // table underneath the panel and could not be seen at all. Sideways stays on the open table at
                // any camera angle, and a row of figures stands a cell apart, which is three times this offset.
                var beside = Vector3.Cross(Vector3.up, -toCamera).normalized;

                pair.Glyph.SetPositionAndRotation(
                    pair.Ring.position + beside * (cellSize * 0.34f) + Vector3.up * (cellSize * 0.05f),
                    Quaternion.LookRotation(Vector3.down, -toCamera));
            }
        }

        // Buttons living on objects this dialog did not make. Cleared by REMOVING them, since destroying the
        // object would take Boltac's stock off the table with the question.
        private readonly List<TablePromptButton> _cardButtons = new List<TablePromptButton>();

        private GameObject BuildRing(PromptOption option)
        {
            if (table == null) return null;

            var figure = table.FindFigure(option.Target);

            // A place rather than a figure: the ring goes on the building's pad, so "go to the temple" is
            // pointing at the temple. Parented to the table itself, since a pad is scenery and does not move.
            var place = figure == null ? table.FindPlace(option.Target) : null;
            if (figure == null && place == null) return null;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Pick_" + option.Id;

            if (figure != null)
            {
                ring.transform.SetParent(figure, false);
            }
            else
            {
                ring.transform.SetParent(transform, false);
                ring.transform.position = place.Value;
            }

            // A disc at the figure's feet, wider than its base. Thick enough to read at a shallow camera
            // angle: at half a millimetre it was geometrically present, visually absent, and clickable --
            // the worst of the three, since the table looked like it was ignoring the prompt.
            // A figure's ring is sized to the FIGURE, not to its cell. A mini is about half a cell across, and a
            // cell-wide ring only looked right where figures stand one per cell -- in a fight. Six party members
            // clustered on a town pad merged into a single dark blob, so you could see that something was
            // clickable but not who: exactly the case the temple and the camp reorder both need. A building's
            // marker stays cell-wide, because it is marking a building.
            var diameter = figure != null ? table.FigureRingSpan : cellSize * 0.9f;
            ring.transform.localScale = new Vector3(diameter, cellSize * 0.06f, diameter);
            ring.transform.localRotation = Quaternion.identity;

            // A figure's ring sits at its feet. A place's is already placed -- above the roofless walls, where
            // it reads as a marker over the building and cannot be hidden behind a column.
            if (figure != null)
                ring.transform.localPosition = Vector3.up * (cellSize * 0.04f);

            // A cylinder arrives with a CapsuleCollider, which does not take non-uniform scale: squashed
            // flat it stays a fat blob, so clicks land where nothing is drawn. A box matches the disc.
            var capsule = ring.GetComponent<Collider>();
            if (capsule != null) Destroy(capsule);
            // Unity's cylinder mesh is two units tall, so the box has to be too or it covers half the disc.
            ring.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);

            var button = ring.AddComponent<TablePromptButton>();
            button.Configure(option.Id, ring.GetComponent<Renderer>(),
                             buttonMaterial, buttonHotMaterial ?? buttonMaterial);

            MarkRing(ring.transform, option.Key);

            _rings.Add(ring);
            return ring;
        }

        /// <summary>
        /// The number the game gave this figure, on the table beside its ring.
        ///
        /// Its own object in world space rather than a child of the ring -- see the note on _ringKeys for why
        /// inheriting a ring's squash or a figure's facing will not do.
        /// </summary>
        private void MarkRing(Transform ring, string key)
        {
            if (string.IsNullOrEmpty(key) || font == null) return;

            var text = MakeText(KeyGlyphName, transform, cellSize * 0.5f, cellSize * 0.5f,
                                keyColour, TextAlignmentOptions.Center, SizeFor(0.005f));
            text.text = key;

            _ringKeys.Add(new KeyGlyph { Glyph = text.transform, Ring = ring });
        }

        /// <summary>
        /// One button: a slab that takes the raycast, the key hint, and the label.
        ///
        /// The slab is a scaled child rather than a scaled root, because scaling the root would squash
        /// the text by the same non-uniform factor -- and cancelling that with an inverse scale on the
        /// text is the sort of thing that works until someone changes a dimension.
        /// </summary>
        private TablePromptButton BuildButton(PromptOption option, Vector3 centre)
        {
            var button = new GameObject("Button_" + option.Id);
            button.transform.SetParent(_root, false);
            button.transform.localPosition = centre;

            var lift = -(PanelThickness * 0.5f + ButtonThickness * 0.5f);   // local +Z points down

            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);      // brings its own BoxCollider
            slab.name = "Slab";
            slab.transform.SetParent(button.transform, false);
            slab.transform.localPosition = new Vector3(0f, 0f, lift);
            slab.transform.localScale = new Vector3(ButtonWidth, ButtonHeight, ButtonThickness);

            var component = button.AddComponent<TablePromptButton>();
            component.Configure(option.Id, slab.GetComponent<Renderer>(),
                                buttonMaterial, buttonHotMaterial ?? buttonMaterial);

            var textZ = lift - (ButtonThickness * 0.5f + 0.0002f);
            var hasKey = !string.IsNullOrEmpty(option.Key);

            // The key sits in a fixed column on the left so the hints line up down the dialog rather
            // than drifting with each label's length.
            var keyColumn = hasKey ? cellSize * 0.5f : 0f;

            if (hasKey)
            {
                var key = MakeText("Key", button.transform, keyColumn, ButtonHeight,
                                   keyColour, TextAlignmentOptions.Center, SizeFor(0.0052f));
                key.text = option.Key;
                key.transform.localPosition = new Vector3(
                    -ButtonWidth * 0.5f + Padding * 0.5f + keyColumn * 0.5f, 0f, textZ);
            }

            var labelWidth = ButtonWidth - Padding - keyColumn - cellSize * 0.12f;
            // Wrapped, so "Choose target group" can take two lines at a readable size instead of shrinking
            // to fit one. That is what sets the button height below.
            var label = MakeText("Label", button.transform, labelWidth, ButtonHeight * 0.92f,
                                 labelColour, TextAlignmentOptions.Left, SizeFor(0.0050f), wrap: true);
            label.text = string.IsNullOrEmpty(option.Label) ? option.Id : option.Label;
            label.transform.localPosition = new Vector3(
                -ButtonWidth * 0.5f + Padding * 0.5f + keyColumn + cellSize * 0.12f, 0f, textZ);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);

            return component;
        }

        /// <summary>
        /// A text object for the dialog. Auto-sized within a ceiling, because labels run from "Run" to
        /// "Choose target group" in a box a couple of centimetres wide: one fixed size would either
        /// overflow the slab or be unreadable across the table.
        ///
        /// The sizes passed in are NOT metres, which is worth stating because assuming they were is how
        /// this ended up with labels a millimetre tall. TMP's fontSize is an em size measured against the
        /// font asset's own point size, and for this asset a glyph comes out about <see cref="MmPerUnit"/>
        /// of it -- measured by rendering known sizes and reading the mesh bounds back, since nothing about
        /// the number in the inspector says so. Divide the height you want by that ratio.
        /// </summary>
        private TextMeshPro MakeText(string name, Transform parent, float width, float height,
                                     Color colour, TextAlignmentOptions alignment,
                                     float maxSize, bool wrap = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.color = colour;
            text.alignment = alignment;
            text.enableAutoSizing = true;
            text.fontSizeMin = maxSize * 0.45f;
            text.fontSizeMax = maxSize;
            text.enableWordWrapping = wrap;
            text.rectTransform.sizeDelta = new Vector2(width, height);
            return text;
        }

        /// <summary>
        /// Puts the dialog beside the party, on whichever side the camera is watching from, and gives it
        /// the orientation that reads correctly.
        ///
        /// That orientation is worth stating plainly, because the obvious choice is the broken one.
        /// Unity builds a rotation with right = cross(up, forward), so orienting flat text to FACE the
        /// camera flips its right axis and the label renders mirrored -- correct letters, reversed
        /// reading order, and no negative scale anywhere to give it away. Pointing the root's forward
        /// DOWN instead lands right on the camera's right, which is both readable and the order the game
        /// listed the options in. TMP's glyphs read from the far side of their own normal and its shader
        /// draws both faces, so the panel and the text agree. Established by measuring, not by reasoning.
        /// </summary>
        private void Place()
        {
            var centre = Anchor();
            var cam = Camera.main;
            if (centre == null || cam == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            var toCamera = cam.transform.position - centre.Value;
            toCamera.y = 0f;
            toCamera = toCamera.sqrMagnitude < 1e-8f ? Vector3.back : toCamera.normalized;

            // Lift first, then fit: how far the dialog can reach toward the viewer depends on how high it
            // is sitting, so the standoff has to be solved at the height it will actually occupy.
            // A question asked of one figure stands UP over that figure, facing the player.
            //
            // The board's dialogs lie flat because they are things on the table, and that reads well from the
            // town's distant shot. It falls apart for a bubble: a fight is seen from the seated camera a
            // quarter of a metre away, and a flat panel that close is so foreshortened that its near edge is
            // twice the size of its far one -- the buttons came out sheared into a fan and half of it hung off
            // the side of the screen. Upright, the panel has no foreshortening to suffer, and a speech bubble
            // over the character whose turn it is happens to be exactly the right object anyway.
            //
            // The layout is untouched: the same rows and columns, only the plane they sit in changes. Local +Y
            // becomes world up instead of table-depth, and the text offset along -Z faces the camera rather
            // than the ceiling -- which is why this is a different rotation and not a different builder.
            if (BubbleAt(out var figure))
            {
                var bubbleScale = FitScale(cam, figure, bubbleFrameWidth);
                var lift = bubbleLiftCells * cellSize + _panelDepth * bubbleScale * 0.5f;

                _root.SetPositionAndRotation(figure + Vector3.up * lift,
                                             Quaternion.LookRotation(-toCamera, Vector3.up));
                _root.localScale = Vector3.one * bubbleScale;
                return;
            }

            var lifted = centre.Value
                       + toCamera * BoardReach(toCamera)
                       + Vector3.up * (hoverCells * cellSize + PanelThickness * 0.5f);

            // Cut the dialog to the shot before placing it, since how far it has to stand off depends on how
            // deep it ends up being.
            var scale = FitScale(cam, lifted);
            var depth = _panelDepth * scale;

            var standoff = FitStandoff(cam, lifted, toCamera, depth);
            var position = lifted + toCamera * (standoff + depth * 0.5f);

            _root.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, -toCamera));
            _root.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// True when the dialog belongs to the BOARD rather than to the party: a gathering like the tavern,
        /// and the town, where the camera frames the whole thing at once and the party is somewhere in the
        /// middle of it.
        ///
        /// The town needs this for the same reason the tavern did, and shows it more plainly. Anchored to the
        /// party, the town dialog sat on top of the pads, then WALKED with them to the next place and skewed
        /// as it went -- the panel turns to face the camera from its anchor, so an anchor away to one side
        /// comes out at an angle. A control that belongs to the board should not follow one figure round it.
        /// </summary>
        private bool BoardIsSubject =>
            table != null && (table.HasRoster || table.IsTownBoard);

        /// <summary>
        /// What the dialog sits beside: the party underground, the whole board in a gathering or in town.
        ///
        /// Following the party is right in a dungeon -- the party is what the player is looking at, and the
        /// camera follows it too. In the tavern it is wrong twice over: the party may be two figures in a
        /// corner, putting the dialog off the edge of the table, and it may be EMPTY, which left the dialog
        /// with nothing to anchor to and so invisible -- in precisely the state where the player most needs
        /// it, since an empty party is where you start.
        /// </summary>
        private Vector3? Anchor()
        {
            if (table == null) return null;

            // A choice the game asked of ONE character is asked over that character -- a bubble at the
            // figure whose turn it is, rather than a panel by the board that says a name in small print.
            // In a fight the party stands in a huddle and every round asks the same six words of a
            // different person, so which figure the question belongs to is the only thing that changes,
            // and putting it anywhere else makes the player read instead of look.
            if (!string.IsNullOrEmpty(_askedOf))
            {
                var figure = table.FindFigure(_askedOf);
                if (figure != null) return figure.position;
            }

            if (BoardIsSubject && table.LaidBounds.HasValue)
            {
                var board = table.LaidBounds.Value;
                // At the height the figures stand at -- the bounds' own centre is halfway up the walls,
                // which would float the dialog above the table.
                var y = table.PartyCentre.HasValue ? table.PartyCentre.Value.y : board.min.y;
                return new Vector3(board.center.x, y, board.center.z);
            }

            return table.PartyCentre;
        }

        /// <summary>
        /// How far from the anchor the board reaches toward the viewer, so the dialog can stand off from
        /// the board's EDGE rather than its middle.
        ///
        /// Anchoring at the centre put the panel across the bench, hiding the very name cards that make the
        /// tavern usable. In town it put it across the pads. Zero underground, where the anchor is the party
        /// and standing beside them is the whole idea.
        /// </summary>
        private float BoardReach(Vector3 toCamera)
        {
            if (!BoardIsSubject || !table.LaidBounds.HasValue) return 0f;

            var extents = table.LaidBounds.Value.extents;
            return Mathf.Abs(toCamera.x) * extents.x + Mathf.Abs(toCamera.z) * extents.z;
        }

        /// <summary>
        /// Shrinks the dialog until it takes no more than <see cref="maxFrameWidth"/> of the frame.
        ///
        /// The dialog is built in cells, and a fixed size in cells cannot suit both cameras: the town shot
        /// takes in fourteen cells, the maze's seated shot about seven and a half. The seven-option maze
        /// dialog measures 6.3 cells across, which is a comfortable 45% of the town's frame and 82% of the
        /// maze's -- wider than the corridor, hanging off the left edge, buttons sheared by perspective. It
        /// looked like a broken layout and was really a dialog built for the wrong shot.
        ///
        /// Scaled rather than re-laid out because the on-screen size is what matters: a dialog half as wide
        /// in world units, seen from half the distance, reads exactly the same. Never enlarged past its own
        /// size, so the town dialog is untouched.
        /// </summary>
        private float FitScale(Camera cam, Vector3 at) => FitScale(cam, at, maxFrameWidth);

        private float FitScale(Camera cam, Vector3 at, float fraction)
        {
            if (_panelWidth <= 0f) return 1f;

            var distance = Vector3.Distance(cam.transform.position, at);
            var visibleWidth = 2f * distance
                             * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                             * Mathf.Max(0.1f, cam.aspect);

            return Mathf.Min(1f, visibleWidth * fraction / _panelWidth);
        }

        /// <summary>
        /// The figure this question was asked of, when there is one on the table to ask it over.
        ///
        /// Both halves matter: the game names a character only for a choice that belongs to one of them, and
        /// the figure has to actually be standing there -- a character who died between the question being
        /// asked and the table drawing it leaves the dialog with nothing to hover over, and it goes back to
        /// standing beside the board rather than hanging in the air.
        /// </summary>
        private bool BubbleAt(out Vector3 position)
        {
            position = Vector3.zero;
            if (table == null || string.IsNullOrEmpty(_askedOf)) return false;

            var figure = table.FindFigure(_askedOf);
            if (figure == null) return false;

            position = figure.position;
            return true;
        }

        /// <summary>
        /// How far past the party the dialog can sit and still be fully on screen.
        ///
        /// Solved against the live camera rather than tuned to a number, because the two camera modes see
        /// wildly different amounts of table -- the close follow camera shows about three and a half cells
        /// past the party, the wide one far more -- and a dialog that fits one would either fall off the
        /// other or waste half of it. A prompt the player cannot see is a game that looks frozen, so this
        /// gives up standoff, not visibility, and only ever pulls the dialog closer than asked.
        /// </summary>
        private float FitStandoff(Camera cam, Vector3 centre, Vector3 toCamera, float depth)
        {
            var wanted = standoffCells * cellSize;
            var closest = minStandoffCells * cellSize;

            if (NearEdgeOnScreen(cam, centre, toCamera, wanted, depth)) return wanted;

            // Visibility falls off monotonically as the dialog moves toward the viewer, so bisect for the
            // largest standoff whose near edge is still in view. Ten steps is well under a millimetre.
            var near = closest;
            var far = wanted;
            for (int i = 0; i < 10; i++)
            {
                var mid = (near + far) * 0.5f;
                if (NearEdgeOnScreen(cam, centre, toCamera, mid, depth)) near = mid;
                else far = mid;
            }

            return near;
        }

        private static bool NearEdgeOnScreen(Camera cam, Vector3 centre, Vector3 toCamera,
                                             float standoff, float depth)
        {
            var nearEdge = centre + toCamera * (standoff + depth);
            var viewport = cam.WorldToViewportPoint(nearEdge);
            return viewport.z > 0f && viewport.y > 0.04f;
        }

        /// <summary>Hover and press. A VR build swaps the ray's origin and nothing else here changes.</summary>
        private void Aim()
        {
            var cam = Camera.main;
            if (cam == null) return;

            TablePromptButton over = null;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            // The collider is on the slab, the component on the button root above it.
            if (Physics.Raycast(ray, out hit, 10f))
                over = hit.collider.GetComponentInParent<TablePromptButton>();

            if (over != _hovered)
            {
                if (_hovered != null) _hovered.SetHovered(false);
                if (over != null) over.SetHovered(true);
                _hovered = over;
            }

            if (over == null || !Input.GetMouseButtonDown(0)) return;

            over.Flash();
            Press(over.OptionId);
        }

        /// <summary>
        /// Queues the choice for the game exactly as a keypress in this window would. Public so a test,
        /// or a VR interactor, can press a button without a mouse.
        /// </summary>
        public void Press(string optionId)
        {
            if (receiver == null || string.IsNullOrEmpty(optionId)) return;
            receiver.Send(optionId);
        }

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        private void Clear()
        {
            DestroyChildrenOf(_root);
            _buttons.Clear();

            // Null-checked because a ring's figure may have been destroyed already, taking the ring with
            // it -- a character who left the table between one prompt and the next.
            foreach (var ring in _rings)
            {
                if (ring != null) Destroy(ring);
            }

            _rings.Clear();

            // Card buttons are REMOVED, not destroyed: the card is Boltac's stock and stays on the counter
            // whether or not it is currently answering anything.
            foreach (var button in _cardButtons)
            {
                if (button != null) Destroy(button);
            }

            _cardButtons.Clear();

            // The chalked keys DO go, since they are this dialog's own objects and a letter left on a card would
            // go on promising a key that answers nothing.
            foreach (var glyph in _cardKeys)
            {
                if (glyph != null) Destroy(glyph);
            }

            _cardKeys.Clear();

            foreach (var pair in _ringKeys)
            {
                if (pair.Glyph != null) Destroy(pair.Glyph.gameObject);
            }

            _ringKeys.Clear();
            _hovered = null;
        }
    }
}
