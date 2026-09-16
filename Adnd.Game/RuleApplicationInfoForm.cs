using Adnd.Core.Config;
using System.Drawing;
using System.Windows.Forms;

namespace Adnd.Game;

public sealed class RuleApplicationInfoForm : Form
{
    private readonly RichTextBox _logBox;
    private readonly List<RuleLinkSpan> _ruleLinks = new();


    public RuleApplicationInfoForm()
    {
        Text = "AD&D Rule and Dice Info";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(700, 420);
        ShowInTaskbar = true;
        TopMost = true;

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

            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            using (var src = Image.FromFile(path))
                picture.Image = new Bitmap(src);

            viewer.Controls.Add(picture);
            viewer.ShowDialog(this);
        }

        _logBox.MouseDown += (_, e) => tryOpenRuleImage(e);
        _logBox.MouseUp += (_, e) => tryOpenRuleImage(e);

        _logBox.MouseMove += (_, e) =>
        {
            var index = _logBox.GetCharIndexFromPosition(e.Location);
            var isOnLink = FindLinkAt(index) != null
                           || FindLinkAt(index - 1) != null
                           || FindLinkAt(index + 1) != null;
            _logBox.Cursor = isOnLink ? Cursors.Hand : Cursors.IBeam;
        };

        Controls.Add(_logBox);
    }

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
