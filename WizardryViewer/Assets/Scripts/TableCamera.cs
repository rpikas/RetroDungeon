// Unity-only. Presentation.
//
// Frames the party at a seated player's distance. The dungeon is rendered at honest 1:1
// miniature scale — one inch per cell — so a whole corridor is only a few centimetres of
// table. Framing the entire tabletop therefore shows a postage stamp; the camera has to come
// in close and follow instead.
//
// This moves the RIG, never the camera, because that is what an XR rig does: the headset owns
// the camera's pose inside the rig, and the application owns where the rig stands.

using UnityEngine;

namespace WizardryViewer.Unity
{
    public sealed class TableCamera : MonoBehaviour
    {
        /// <summary>
        /// At 1:1 scale these two cannot be the same shot. Ten cells is close enough to tell a
        /// priest from a thief; a 22x22 level is 56cm of table and reduces a figure to a speck.
        /// So it is a toggle, not a compromise.
        /// </summary>
        public enum Framing { FollowParty, WholeLevel }

        [SerializeField] private TableRenderer table;

        [Header("Framing")]
        [SerializeField] private Framing framing = Framing.FollowParty;
        [Tooltip("Press to switch between following the party and seeing the whole level.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
        [Tooltip("Fraction of the frame the level should fill in WholeLevel.")]
        [SerializeField] private float levelFill = 0.85f;

        [Header("Seat")]
        [Tooltip("True metres from the party to the camera — not a horizontal offset, so the visible " +
                 "area follows from it directly. At 40 degrees vertical FOV, 0.23 shows about 6.5 cells.")]
        [SerializeField] private float distance = 0.23f;
        [Tooltip("Degrees below horizontal. Shallow enough to see figures side-on rather than from above.")]
        [SerializeField] private float pitchDegrees = 38f;

        [Header("Follow")]
        [Tooltip("Seconds to settle. Long enough not to chase every step, short enough to keep up.")]
        [SerializeField] private float followSeconds = 0.5f;
        [Tooltip("Metres of party movement to ignore, so a single figure shuffling does not drift " +
                 "the whole view.")]
        [SerializeField] private float deadZone = 0.01f;
        [Tooltip("How far the view may hang past the edge of the level, as a fraction of a half-window. " +
                 "0 keeps every pixel on the dungeon but pushes an edge-hugging party to the side; " +
                 "1 ignores the level outline entirely and always centres the party.")]
        [Range(0f, 1f)]
        [SerializeField] private float edgeOverhang = 0.55f;

        [Header("Town")]
        [Tooltip("Fraction of the frame the whole town should fill.")]
        [SerializeField] private float townFill = 0.9f;

        [Header("Zoom (scroll wheel)")]
        [Tooltip("How much one notch of the wheel changes the distance, as a fraction of the current one. " +
                 "Proportional rather than fixed metres, so a notch feels the same close up and far out.")]
        [SerializeField] private float zoomPerNotch = 0.12f;
        [Tooltip("Closest seat, as a fraction of the shot this framing would otherwise use. VERY small: the town's " +
                 "own shot is over half a metre out and Boltac's price tags are under 3mm of slate, so this ends " +
                 "up around 15mm -- about where your eye would be if you leant over the counter. The camera's " +
                 "10mm near plane is the real floor under this; below it the shelves start being clipped away.")]
        [SerializeField] private float zoomIn = 0.028f;
        [Tooltip("Furthest seat, as a fraction of the framing's own shot. A little slack to pull back past it.")]
        [SerializeField] private float zoomOut = 1.35f;
        [Tooltip("Seconds for the wheel to settle. Short: a wheel should feel connected to the hand.")]
        [SerializeField] private float zoomSeconds = 0.08f;
        [Tooltip("Metres the leader's nose stands proud of the middle of his figure. The eye parks a near-plane " +
                 "behind THAT, so his own face is the thing being clipped and nothing beyond it is. Pushing the " +
                 "camera in FRONT of him instead put it on the cell boundary, where the wall he was facing fell " +
                 "inside the 10mm near plane and was clipped away -- leaving a view of the sky past the table.")]
        [SerializeField] private float noseAhead = 0.005f;
        [Tooltip("Metres BEHIND the leader that the approach stops before running in -- two cells. The camera " +
                 "gets down to eye height and behind the party by here, so the only thing left is the straight " +
                 "run forward. Everything interesting about the path happens before this point.")]
        [SerializeField] private float stageBehind = 0.05f;
        [Tooltip("Zoom at which the straight run begins. Above this the camera is swinging round and dropping; " +
                 "below it the aim is already down the corridor and the motion is forward only.")]
        [SerializeField] private float straightFrom = 0.3f;
        [Tooltip("Degrees below horizontal at the closest zoom. Nearly level, so coming in ends up looking " +
                 "ACROSS the table at the miniatures rather than down onto their hats -- which is as near to " +
                 "first person as a camera outside the maze can get.")]
        [SerializeField] private float eyePitchDegrees = 8f;

        // 1 means the shot this framing chose for itself; below that is closer, above is further back.
        private float _zoom = 1f;
        private float _zoomWanted = 1f;

        private Vector3 _velocity;
        private Vector3 _focus;
        private bool _hasFocus;
        private bool _wasTown;

        // The view stays on one side of the table rather than swinging round behind the party:
        // a camera that rotates with facing makes a dungeon crawl unreadable, and in VR it
        // would be worse than unreadable.
        private static readonly Vector3 ViewFrom = new Vector3(0f, 0f, -1f);

        private void LateUpdate()
        {
            if (table == null) return;

            // Each board has a different right shot, so changing board resets to it. Underground the party
            // is the subject and the level is too big to take in; in town the BOARD is the subject -- the
            // places you choose between, and the party walking from one to another. Following the party
            // there shows one pad and a lot of bare table, and the town is wider than the close window can
            // ever hold, so the far end simply never appears. Tab still overrides, per board.
            if (table.IsTownBoard != _wasTown)
            {
                _wasTown = table.IsTownBoard;
                framing = _wasTown ? Framing.WholeLevel : Framing.FollowParty;
                _hasFocus = false;

                // Back to the board's own shot. A zoom is a look at THIS board -- leaning in to read Boltac's
                // price tags -- and carrying it down a staircase would land the player nose-first in a wall.
                _zoom = 1f;
                _zoomWanted = 1f;
            }

            TakeWheel();

            if (Input.GetKeyDown(toggleKey))
            {
                framing = framing == Framing.FollowParty ? Framing.WholeLevel : Framing.FollowParty;
                _hasFocus = false;   // re-seat immediately rather than gliding across the table
            }

            var centre = framing == Framing.WholeLevel ? LevelFocus() : table.PartyCentre;
            if (centre == null) return;

            // Coming in on a board shot, come in on whatever is worth looking at.
            //
            // The wide shot is centred on the board's own middle, which in town is a patch of bare table in front
            // of the gate -- so zooming in walked into the gate no matter where the party was standing. Now it
            // converges on the SHELVES where there are any and the party otherwise, which is the difference
            // between arriving at a shop and arriving behind the people standing in it. Blended rather than
            // switched, so the wide shot still frames the whole town and the ends are one movement apart.
            if (framing == Framing.WholeLevel)
            {
                var near = table.WareFocus ?? table.PartyCentre;
                if (near != null)
                {
                    var howFarIn = Mathf.InverseLerp(zoomIn, 1f, _zoom);
                    centre = Vector3.Lerp(near.Value, centre.Value, howFarIn);
                }
            }

            if (!_hasFocus)
            {
                _focus = centre.Value;
                _hasFocus = true;
                Vector3 seatNow;
                float blendNow;
                Compose(_focus, out seatNow, out blendNow);
                transform.position = seatNow;
                transform.rotation = Facing(seatNow, _focus, blendNow);
                return;
            }

            if ((centre.Value - _focus).magnitude > deadZone)
                _focus = centre.Value;

            if (framing == Framing.FollowParty) _focus = KeepOnLevel(_focus);

            Vector3 seat;
            float blend;
            Compose(_focus, out seat, out blend);
            transform.position = Vector3.SmoothDamp(transform.position, seat, ref _velocity, followSeconds);
            transform.rotation = Facing(transform.position, _focus, blend);
        }

        /// <summary>
        /// Where the camera sits and what it looks at, for the current zoom.
        ///
        /// The far end of the wheel is the board shot this framing chose. The near end, underground, is the
        /// LEADER'S EYE: at the head of the marching order, at the tallest figure's own height, looking the way
        /// they are marching -- so the party is behind the camera and the corridor is ahead. That is a real first
        /// person view rather than a very close third, and it is the one thing a zoom alone cannot give, because
        /// distance is not the difference between the two: WHICH WAY YOU FACE is. Everything in between is a
        /// straight blend of both, so one wheel runs the whole way from map to eye.
        ///
        /// In town the eye is skipped. The party there is not marching anywhere -- the game reports "North"
        /// regardless and the figures are turned to face the player -- so a first-person view would look at a
        /// wall chosen by accident. Coming in close on a town board is for reading Boltac's price tags, which
        /// the flattened seat already does.
        /// </summary>
        private void Compose(Vector3 focus, out Vector3 seat, out float blend)
        {
            var far = Zoomed(focus, Seat(focus));

            Vector3 eye;
            if (framing != Framing.FollowParty || !TryPartyEye(out eye))
            {
                seat = far;
                blend = 1f;   // no eye to blend towards: aim at the focus, as this always did
                return;
            }

            blend = Mathf.InverseLerp(zoomIn, 1f, _zoom);

            // Past the board shot the wheel is just backing off, which is what Zoomed already does.
            if (blend >= 1f)
            {
                seat = far;
                return;
            }

            seat = Approach(Seat(focus), eye, focus, blend);
        }

        /// <summary>
        /// The path from the board shot into the leader's eye: an S, straight along the facing at the end.
        ///
        /// A straight line between the two was the first attempt and it is confusing to sit through, because the
        /// two ends are not merely far apart -- they are ORIENTED differently. The board camera sits south of the
        /// party whatever they are doing, so a straight run to the eye of an east-facing leader arrives sideways
        /// through his ear. What reads as entering someone's view is coming up BEHIND them, and that is what the
        /// end of this curve does: the last <see cref="straightRun"/> is dead straight along the way they look.
        ///
        /// A cubic Bezier, whose two middle points are exactly those two intentions -- leave along the board's own
        /// line of sight, arrive along the party's facing.
        /// </summary>
        private Vector3 Approach(Vector3 boardSeat, Vector3 eye, Vector3 focus, float blend)
        {
            var look = table.PartyFacing;

            // Where the approach ends and the run begins: at the leader's eye height, two cells BEHIND him.
            var stage = eye - look * stageBehind;

            // The run. Forward along the facing and nothing else, so the last stretch is one clean movement into
            // the back of his head.
            if (blend <= straightFrom)
                return Vector3.Lerp(eye, stage, Mathf.InverseLerp(0f, straightFrom, blend));

            // And getting there: down from the board shot and round behind the party.
            //
            // The previous attempt aimed the whole curve at the eye, and the result was almost entirely VERTICAL
            // -- 14mm of swinging round against 130mm of dropping -- so it read as arriving above the right spot
            // and then descending onto it. Aiming at a point behind the party instead puts the going-round part
            // where it belongs: it happens during the descent, and is finished before the run starts.
            var u = Mathf.InverseLerp(straightFrom, 1f, blend);   // 0 at the stage, 1 at the board shot

            // Leaves the stage going straight backwards, which is the same line the run uses -- so coming the
            // other way, the curve is already travelling forward when the run takes over and there is no corner.
            var p1 = stage - look * (stageBehind * 0.9f);

            // Then most of the way to the board seat sideways before most of the way up: the swing round happens
            // high and early, not as a last-second drop.
            var p2 = new Vector3(Mathf.Lerp(stage.x, boardSeat.x, 0.65f),
                                 Mathf.Lerp(stage.y, boardSeat.y, 0.3f),
                                 Mathf.Lerp(stage.z, boardSeat.z, 0.65f));

            return Bezier(stage, p1, p2, boardSeat, u);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            var u = 1f - t;
            return u * u * u * a
                 + 3f * u * u * t * b
                 + 3f * u * t * t * c
                 + t * t * t * d;
        }

        /// <summary>
        /// Where to point, blended between looking AT the party and looking the WAY the party is looking.
        ///
        /// The two ends disagree about more than distance, which is the whole difficulty. The board shot always
        /// views from the player's side and therefore always looks north; a first-person view has to face
        /// whichever way the party is marching. So the ends are two different ROTATIONS, and the way between them
        /// is a turn.
        ///
        /// Slerped rather than interpolating a look-at POINT, which is what this did first and which breaks in
        /// the one case that matters most: with the party marching SOUTH, towards the viewer, a point blended
        /// from "down the corridor" to "the party" passes through the camera itself, and the view flips end over
        /// end as it crosses. Slerp takes the short way round two rotations and cannot pass through anything.
        /// </summary>
        private Quaternion Facing(Vector3 from, Vector3 focus, float blend)
        {
            var atParty = Aim(from, focus);
            if (blend >= 1f) return atParty;

            var look = table.PartyFacing;
            var downTheCorridor = look.sqrMagnitude < 1e-8f
                                ? atParty
                                : Quaternion.LookRotation(look, Vector3.up);

            // Done turning by the time the straight run starts, so the last stretch is a move and nothing else.
            // Turning and closing at once, all the way in, is what made the arrival feel like a swerve.
            var turn = Mathf.InverseLerp(straightFrom, 1f, blend);
            return Quaternion.Slerp(downTheCorridor, atParty, turn);
        }

        /// <summary>
        /// The leader's eye, pushed far enough forward that the party is behind it.
        ///
        /// Just ahead of the front rank rather than exactly on it: standing the camera inside the leader's own
        /// mini fills the frame with the back of his cloak, and a hair of clearance is the difference between
        /// looking down a corridor and looking at a shoulder.
        /// </summary>
        private bool TryPartyEye(out Vector3 eye)
        {
            eye = Vector3.zero;
            if (table.PartyLead == null || table.PartyEyeHeight <= 0f) return false;

            // A near-plane behind the nose. Measured from the camera's own near clip rather than guessed, so
            // this stays right if that is ever changed: his face lands exactly on the plane that clips it.
            var cam = GetComponentInChildren<Camera>();
            var near = cam != null ? cam.nearClipPlane : 0.01f;

            eye = table.PartyLead.Value
                + Vector3.up * table.PartyEyeHeight
                - table.PartyFacing * Mathf.Max(0f, near - noseAhead);

            return true;
        }

        /// <summary>
        /// Reads the wheel and settles the zoom towards where it asks.
        ///
        /// Multiplied rather than added, so a notch covers the same PROPORTION of the distance whether the seat
        /// is half a metre out or five centimetres in -- added metres would crawl when far and lurch when near.
        /// </summary>
        private void TakeWheel()
        {
            var notches = Input.mouseScrollDelta.y;
            if (Mathf.Abs(notches) > 0.01f)
                _zoomWanted = Mathf.Clamp(_zoomWanted * Mathf.Pow(1f - zoomPerNotch, notches), zoomIn, zoomOut);


            // Settled here rather than left to the position damping below, because the two are different
            // motions: the seat glides so the party can walk without the view snapping about, while the wheel
            // should answer the hand immediately.
            _zoom = zoomSeconds <= 0f
                  ? _zoomWanted
                  : Mathf.Lerp(_zoom, _zoomWanted, 1f - Mathf.Exp(-Time.unscaledDeltaTime / zoomSeconds));
        }

        /// <summary>
        /// The seat with the zoom applied: nearer, and flatter as it comes in.
        ///
        /// The pitch matters as much as the distance. Coming straight in along a 38-degree line ends up looking
        /// down on the miniatures from just above, which is a close-up of their hats; flattening towards eye
        /// level as the distance shrinks ends up looking across the table at them, which is what makes this
        /// stand in for a first-person view. The horizontal bearing is untouched -- the camera stays on the
        /// player's side of the table, for the reason <see cref="ViewFrom"/> gives.
        /// </summary>
        private Vector3 Zoomed(Vector3 focus, Vector3 seat)
        {
            var offset = seat - focus;
            var flat = new Vector3(offset.x, 0f, offset.z);
            if (flat.sqrMagnitude < 1e-8f) return focus + offset * _zoom;   // straight overhead: nothing to flatten

            var range = offset.magnitude * _zoom;
            var ownPitch = Mathf.Atan2(offset.y, flat.magnitude) * Mathf.Rad2Deg;

            // Fully in at the floor, the framing's own pitch at 1 and beyond.
            var howFarIn = Mathf.InverseLerp(zoomIn, 1f, _zoom);
            var pitch = Mathf.Lerp(eyePitchDegrees, ownPitch, howFarIn) * Mathf.Deg2Rad;

            var dir = flat.normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return focus + dir * range;
        }

        /// <summary>
        /// Pull the aim point back inside the level. The party starts against the west wall, and
        /// centring on them there spends half the frame on bare tabletop; this trades a perfectly
        /// centred party for a frame that is all dungeon. No-op once they walk inland.
        /// </summary>
        private Vector3 KeepOnLevel(Vector3 focus)
        {
            var bounds = table.LaidBounds;
            if (bounds == null) return focus;

            var cam = GetComponentInChildren<Camera>();
            var fov = cam != null ? cam.fieldOfView : 40f;
            var aspect = cam != null ? cam.aspect : 1.6f;

            // Scaled by the zoom, since that is what decides how much table is actually in frame: without it a
            // player zoomed right in is still being nudged as though the wide window had to fit on the level.
            var visibleHeight = 2f * distance * _zoom * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var visibleWidth = visibleHeight * aspect;

            focus.x = ClampSpan(focus.x, bounds.Value.min.x, bounds.Value.max.x, visibleWidth);
            focus.z = ClampSpan(focus.z, bounds.Value.min.z, bounds.Value.max.z, visibleHeight);
            return focus;
        }

        /// <summary>
        /// Keep a window of the given width inside [min,max], centring if it will not fit. The
        /// window is allowed to hang over the edge by <see cref="edgeOverhang"/> of a half-window:
        /// clamping it fully inside shoves a party standing at the west wall right to the side of
        /// the frame, which is worse than showing a strip of bare table.
        /// </summary>
        private float ClampSpan(float value, float min, float max, float window)
        {
            var half = window * 0.5f * (1f - edgeOverhang);
            if (max - min <= half * 2f) return (min + max) * 0.5f;
            return Mathf.Clamp(value, min + half, max - half);
        }

        private Vector3? LevelFocus()
        {
            var bounds = table.LaidBounds;
            return bounds == null ? (Vector3?)null : bounds.Value.center;
        }

        /// <summary>Camera position for a given aim point: back along the view axis and up by the pitch.</summary>
        private Vector3 FollowSeat(Vector3 focus)
        {
            var pitch = pitchDegrees * Mathf.Deg2Rad;
            var back = ViewFrom * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return focus + back * distance;
        }

        private Vector3 Seat(Vector3 focus)
        {
            if (framing == Framing.FollowParty) return FollowSeat(focus);
            if (table.IsTownBoard) return TownSeat(focus);

            // Back off until the level fits. Vertical FOV is the binding constraint on a wide
            // window; on a tall one it is horizontal, so check both and take the further seat.
            var bounds = table.LaidBounds;
            if (bounds == null) return FollowSeat(focus);

            var cam = GetComponentInChildren<Camera>();
            var fov = cam != null ? cam.fieldOfView : 40f;
            var aspect = cam != null ? cam.aspect : 1.6f;

            var size = bounds.Value.size;
            var extent = Mathf.Max(size.x, size.z) / Mathf.Max(0.01f, levelFill);

            var vertical = extent * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var horizontalFov = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect);
            var horizontal = extent * 0.5f / Mathf.Tan(horizontalFov * 0.5f);
            var back = Mathf.Max(vertical, horizontal);

            // Steeper than the seated view: a map wants to be looked down on, not across.
            return focus + ViewFrom * (back * 0.55f) + Vector3.up * (back * 0.85f);
        }

        /// <summary>
        /// The whole town, from the seat rather than from above.
        ///
        /// Two things separate this from the level shot. It backs off to fit the board's WIDTH, because the
        /// town is a wide shallow strip -- five pads in a row and a gate in front -- and the level shot fits
        /// a square of the larger dimension, which for a 14x6 board wastes most of the frame on empty table
        /// and leaves the town a band across the middle. And it keeps the seated pitch instead of tilting to
        /// 57 degrees: the buildings are roofless walls a centimetre high meant to be seen INTO from a low
        /// angle, and the figures inside them read side-on, not from overhead.
        /// </summary>
        private Vector3 TownSeat(Vector3 focus)
        {
            var bounds = table.LaidBounds;
            if (bounds == null) return FollowSeat(focus);

            var cam = GetComponentInChildren<Camera>();
            var fov = cam != null ? cam.fieldOfView : 40f;
            var aspect = cam != null ? cam.aspect : 1.6f;

            var halfVertical = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var halfHorizontal = halfVertical * aspect;

            var size = bounds.Value.size;
            var width = size.x / Mathf.Max(0.01f, townFill);

            // Depth costs less than width at this pitch -- a strip of table seen at 38 degrees is
            // foreshortened -- but check it anyway, so a squarer town or a taller window still fits.
            var depth = size.z * Mathf.Sin(pitchDegrees * Mathf.Deg2Rad) / Mathf.Max(0.01f, townFill);

            var back = Mathf.Max(width * 0.5f / halfHorizontal, depth * 0.5f / halfVertical);

            var pitch = pitchDegrees * Mathf.Deg2Rad;
            return focus + (ViewFrom * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * back;
        }

        private static Quaternion Aim(Vector3 from, Vector3 focus)
        {
            var forward = focus - from;
            return forward.sqrMagnitude < 1e-8f
                ? Quaternion.identity
                : Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
