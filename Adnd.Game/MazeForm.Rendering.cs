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

// First-person drawing: scene, walls, doors, darkness, party status.
public sealed partial class MazeForm
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(Color.Black);
        e.Graphics.SmoothingMode = SmoothingMode.None;

        if (_cityMode)
        {
            DrawCity(e.Graphics);
            return;
        }

        // A fight takes over the window; only the party table stays where it always is.
        if (_encounterForm != null)
        {
            DrawPartyTable(e.Graphics);
            return;
        }

        using var pen = new Pen(GameRulesProvider.Current.DefaultColor, 2f);
        var topHudSpace = _partyOverlayVisible ? 62f : 12f;
        var viewportBottom = _partyOverlayVisible
            ? ClientSize.Height - (EncounterForm.PartyFrameHeight + EncounterForm.FrameMargin + 6f)
            : ClientSize.Height - 68f;
        var viewport = new RectangleF(12f, topHudSpace, ClientSize.Width - 24f, viewportBottom - topHudSpace);
        e.Graphics.DrawRectangle(pen, viewport.X, viewport.Y, viewport.Width, viewport.Height);

        InitWallCoords(viewport);

        // Squares far to the side reach past the frame; nothing may be drawn outside it.
        var sceneState = e.Graphics.Save();
        e.Graphics.SetClip(RectangleF.Inflate(viewport, -1f, -1f), CombineMode.Intersect);
        DrawScene(e.Graphics, pen, viewport);
        e.Graphics.Restore(sceneState);
        e.Graphics.DrawRectangle(pen, viewport.X, viewport.Y, viewport.Width, viewport.Height);

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
            DrawPartyTable(e.Graphics);
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

    // In the Wizardry maps N grows with the X coordinate and E with the Y coordinate, so the
    // internal directions are rotated against the compass names shown to the player.
    private string CompassHeading() => _thinWalls
        ? _direction switch
        {
            Direction.North => "West",
            Direction.East => "North",
            Direction.South => "East",
            _ => "South"
        }
        : _direction switch
        {
            Direction.North => "North",
            Direction.East => "East",
            Direction.South => "South",
            _ => "West"
        };

    // The encounter form's fonts, so the maze and the fight read the same.
    private static readonly Font HudFont = new("Consolas", 14f, FontStyle.Bold);

    private void DrawStatus(Graphics graphics)
    {
        var heading = CompassHeading();

        var status = $"Level: {_currentDungeonLevel} ({DungeonName})   Pos: {PositionText}   Facing: {heading}   Esc: Party view";
        using var brush = new SolidBrush(GameRulesProvider.Current.DefaultColor);
        graphics.DrawString(status, HudFont, brush, new PointF(16f, ClientSize.Height - 54f));
    }

    private void DrawOverlayLegend(Graphics graphics)
    {
        var heading = CompassHeading();

        using var brush = new SolidBrush(GameRulesProvider.Current.DefaultColor);
        graphics.DrawString("A/D or ←/→: Turn   W or ↑: Move   C: Camp   K: Kick   Esc: Toggle full dungeon view", HudFont, brush, new PointF(16f, 6f));
        graphics.DrawString($"Level: {_currentDungeonLevel} ({DungeonName})   Pos: {PositionText}   Facing: {heading}", HudFont, brush, new PointF(16f, 30f));
    }

    /// <summary>
    /// The party table at the bottom of the window, laid out like the encounter form's party list (same frame,
    /// font and columns) so it looks the same before, during and after a fight.
    /// </summary>
    private void DrawPartyTable(Graphics graphics)
    {
        var color = GameRulesProvider.Current.DefaultColor;
        var frame = EncounterForm.PartyFrameBounds(ClientSize);
        OldStyleFrame.Draw(graphics, frame, color);

        var list = Rectangle.Inflate(frame, -12, -12);
        using var brush = new SolidBrush(color);
        using var gridPen = new Pen(Color.FromArgb(64, color));
        var widths = EncounterForm.PartyColumnWidths;
        var headings = new[] { "#", "Character Name", "Class", "AC", "Hits", "Status" };
        const float headerHeight = 28f;
        const float rowHeight = 25f;

        float ColumnX(int index)
        {
            var x = list.Left;
            for (var i = 0; i < index; i++)
                x += widths[i];
            return x;
        }

        for (var i = 0; i < headings.Length; i++)
            graphics.DrawString(headings[i], HudFont, brush, ColumnX(i) + 4f, list.Top + 2f);

        graphics.DrawLine(gridPen, list.Left, list.Top + headerHeight, list.Right, list.Top + headerHeight);

        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var y = list.Top + headerHeight + 2f;

        if (party.Members.Count == 0)
        {
            graphics.DrawString("No party members.", HudFont, brush, list.Left + 4f, y);
            return;
        }

        for (var i = 0; i < party.Members.Count; i++)
        {
            var memberName = party.Members[i];
            string[] cells;

            if (roster.TryGetValue(memberName, out var c))
            {
                cells = new[]
                {
                    (i + 1).ToString(),
                    c.Name,
                    EncounterForm.GetClassCode(c),
                    c.ArmorClass.ToString(),
                    $"{c.CurrentHitPoints}/{c.MaxHitPoints}",
                    GetMazeStatusDisplay(c)
                };
            }
            else
            {
                cells = new[] { (i + 1).ToString(), memberName, "(missing)", "", "", "" };
            }

            for (var col = 0; col < cells.Length; col++)
            {
                var cell = new RectangleF(ColumnX(col) + 4f, y, widths[col] - 6f, rowHeight);
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                graphics.DrawString(cells[col], HudFont, brush, cell, format);
            }

            y += rowHeight;
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
            var alignment = c.AlignmentAbbreviation;

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

            // One scale for both axes, so the frame corners lie on the line from the viewport corner to the
            // centre and the walls stay straight at any window shape.
            float scale = 0.8f * (1f - t);
            float halfWidth = viewport.Width / 2f * scale;
            float halfHeight = viewport.Height / 2f * scale;

            float left = cx - halfWidth;
            float right = cx + halfWidth;
            float top = cy - halfHeight;
            float bottom = cy + halfHeight;

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
}
