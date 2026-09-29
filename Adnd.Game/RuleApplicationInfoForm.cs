using Adnd.Core.Config;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace Adnd.Game;

public sealed class RuleApplicationInfoForm : Form
{
    private const string WindowStatePath = @"C:\dev\RetroDungeon\Logs\rule-application-info-window.json";

    private readonly RichTextBox _logBox;
    private readonly List<RuleLinkSpan> _ruleLinks = new();

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);


    public RuleApplicationInfoForm()
    {
        Text = "AD&D Rule and Dice Info";
        //StartPosition = FormStartPosition.CenterScreen;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(700, 920);
        ShowInTaskbar = true;
        TopMost = true;

        var savedPosition = LoadSavedWindowPosition();
        if (savedPosition.HasValue)
        {
            StartPosition = FormStartPosition.Manual;
            Location = savedPosition.Value;
        }

        var dragBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 20,
            BackColor = Color.FromArgb(24, 24, 24),
            Cursor = Cursors.SizeAll
        };

        var dragLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = " Drag window",
            ForeColor = GameRulesProvider.Current.DefaultColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.SizeAll
        };

        dragBar.Controls.Add(dragLabel);

        void beginDrag(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        bool isOverRuleLink(Point location)
        {
            var index = _logBox.GetCharIndexFromPosition(location);
            return FindLinkAt(index) != null
                   || FindLinkAt(index - 1) != null
                   || FindLinkAt(index + 1) != null
                   || FindFirstLinkOnLine(index) != null;
        }

        dragBar.MouseDown += (_, e) => beginDrag(e);
        dragLabel.MouseDown += (_, e) => beginDrag(e);
        MouseDown += (_, e) => beginDrag(e);

        _logBox = new RichTextBox
        {
            ReadOnly = true,
            DetectUrls = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 10f)
        };

        void tryOpenRuleImage(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var index = _logBox.GetCharIndexFromPosition(e.Location);
            var link = FindLinkAt(index)
                       ?? FindLinkAt(index - 1)
                       ?? FindLinkAt(index + 1)
                       ?? FindFirstLinkOnLine(index);
            if (link == null)
                return;

            var path = link.TargetPath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(this,
                    $"Rule image not found:\n{path}",
                    "Rule Image",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var viewer = new Form
            {
                Text = "Rule Image",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(900, 700),
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                TopMost = true
            };

            // --- ESC closes the viewer ---
            viewer.KeyPreview = true;
            viewer.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    viewer.DialogResult = DialogResult.OK;
                    viewer.Close();
                }
            };

            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            using (var src = Image.FromFile(path))
                picture.Image = new Bitmap(src);

            viewer.Controls.Add(picture);

            viewer.Location = new Point(
    Screen.PrimaryScreen.WorkingArea.Width - viewer.Width,
    0
);

            viewer.ShowDialog(this);
        }

        _logBox.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (!isOverRuleLink(e.Location))
                beginDrag(e);
        };

        _logBox.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (isOverRuleLink(e.Location))
                tryOpenRuleImage(e);
        };

        _logBox.MouseMove += (_, e) =>
        {
            var index = _logBox.GetCharIndexFromPosition(e.Location);
            var isOnLink = FindLinkAt(index) != null
                           || FindLinkAt(index - 1) != null
                           || FindLinkAt(index + 1) != null;
            _logBox.Cursor = isOnLink ? Cursors.Hand : Cursors.IBeam;
        };

        FormClosing += (_, _) => SaveWindowPosition();

        Controls.Add(_logBox);
        Controls.Add(dragBar);
    }

    private Point? LoadSavedWindowPosition()
    {
        try
        {
            if (!File.Exists(WindowStatePath))
                return null;

            var json = File.ReadAllText(WindowStatePath);
            var state = JsonSerializer.Deserialize<RuleInfoWindowState>(json);
            if (state == null)
                return null;

            var point = new Point(state.Left, state.Top);
            return IsPointVisibleOnAnyScreen(point) ? point : null;
        }
        catch
        {
            return null;
        }
    }

    private void SaveWindowPosition()
    {
        try
        {
            var folder = Path.GetDirectoryName(WindowStatePath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var location = WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location;
            var state = new RuleInfoWindowState(location.X, location.Y);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(WindowStatePath, json);
        }
        catch
        {
            // Best effort only; failing to persist position must not affect gameplay.
        }
    }

    private static bool IsPointVisibleOnAnyScreen(Point point)
    {
        return Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(point));
    }

    private sealed record RuleInfoWindowState(int Left, int Top);

    public void AppendInfo(string message)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendInfo), message);
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
            return;

        AppendMessageWithRuleLinks(message);
    }

    private void AppendMessageWithRuleLinks(string message)
    {
        const string startToken = "[[RULEIMG|";
        const string endToken = "]]";

        var index = 0;
        while (index < message.Length)
        {
            var markerStart = message.IndexOf(startToken, index, StringComparison.Ordinal);
            if (markerStart < 0)
            {
                _logBox.AppendText(message[index..]);
                break;
            }

            if (markerStart > index)
                _logBox.AppendText(message[index..markerStart]);

            var markerEnd = message.IndexOf(endToken, markerStart, StringComparison.Ordinal);
            if (markerEnd < 0)
            {
                _logBox.AppendText(message[markerStart..]);
                break;
            }

            var payload = message.Substring(markerStart + startToken.Length, markerEnd - markerStart - startToken.Length);
            var split = payload.Split('|');
            if (split.Length >= 2)
            {
                var label = split[0];
                var target = split[1];
                AppendHyperlink(label, target);
            }
            else
            {
                _logBox.AppendText(message.Substring(markerStart, markerEnd + endToken.Length - markerStart));
            }

            index = markerEnd + endToken.Length;
        }

        _logBox.AppendText(Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void AppendHyperlink(string label, string target)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(target))
        {
            _logBox.AppendText(label);
            return;
        }

        var start = _logBox.TextLength;
        _logBox.AppendText(label);
        _logBox.Select(start, label.Length);
        _logBox.SelectionColor = GameRulesProvider.Current.DefaultColor;
        _logBox.SelectionFont = new Font(_logBox.Font, FontStyle.Underline);
        _logBox.Select(_logBox.TextLength, 0);

        _ruleLinks.Add(new RuleLinkSpan(start, label.Length, target));
    }

    private sealed class RuleLinkSpan
    {
        public RuleLinkSpan(int start, int length, string targetPath)
        {
            Start = start;
            Length = length;
            TargetPath = targetPath;
        }

        public int Start { get; }
        public int Length { get; }
        public string TargetPath { get; }
    }

    private RuleLinkSpan? FindLinkAt(int index)
    {
        if (index < 0)
            return null;

        return _ruleLinks.FirstOrDefault(l => index >= l.Start && index < l.Start + l.Length);
    }

    private RuleLinkSpan? FindFirstLinkOnLine(int index)
    {
        if (index < 0 || _logBox.TextLength == 0)
            return null;

        var line = _logBox.GetLineFromCharIndex(index);
        var lineStart = _logBox.GetFirstCharIndexFromLine(line);
        if (lineStart < 0)
            return null;

        var nextLineStart = _logBox.GetFirstCharIndexFromLine(line + 1);
        var lineEndExclusive = nextLineStart >= 0 ? nextLineStart : _logBox.TextLength;

        return _ruleLinks.FirstOrDefault(l => l.Start < lineEndExclusive && (l.Start + l.Length) > lineStart);
    }

}
