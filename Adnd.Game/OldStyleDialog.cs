using System.Drawing;
using System.Windows.Forms;
using Adnd.Core.Config;

namespace Adnd.Game;

internal static class OldStyleDialog
{
    public static void Apply(Form form, Control content, TextBox? input = null, Action? onSelect = null, string selectLabel = "SELECT")
    {
        if (!GameRulesProvider.Current.UIOldStyle)
            return;

        form.FormBorderStyle = FormBorderStyle.None;
        form.ShowInTaskbar = false;
        form.BackColor = Color.Black;
        form.ForeColor = GameRulesProvider.Current.DefaultColor;
        form.KeyPreview = true;
        form.MinimumSize = Size.Empty;

        var baseHeight = input == null ? 500 : 540;
        var dialogHeight = baseHeight;
        if (content is ListBox listForSizing)
        {
            var extraRows = Math.Max(0, listForSizing.Items.Count - 10);
            dialogHeight = Math.Min(900, baseHeight + (extraRows * 18));
        }
        else if (content is TextBox textForSizing && textForSizing.Multiline)
        {
            var lineCount = textForSizing.Lines.Length;
            var extraRows = Math.Max(0, lineCount - 10);
            dialogHeight = Math.Min(900, baseHeight + (extraRows * 18));
        }

        form.ClientSize = new Size(820, dialogHeight);

        var frame = new OldStyleFramePanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Padding = new Padding(12)
        };
        form.Padding = new Padding(4);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Text = form.Text.ToUpperInvariant(),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = form.ForeColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Text = $"↵ {selectLabel}    ESC BACK",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = form.ForeColor,
            Font = new Font("Consolas", 18f, FontStyle.Bold)
        };

        content.Dock = DockStyle.Fill;
        content.BackColor = Color.Black;
        content.ForeColor = form.ForeColor;
        content.Font = hint.Font;
        if (content is ListBox list)
            list.HorizontalScrollbar = true;

        foreach (Control control in form.Controls)
            control.Visible = false;

        frame.Controls.Add(content);
        if (input != null)
        {
            input.Dock = DockStyle.Bottom;
            input.BackColor = Color.Black;
            input.ForeColor = form.ForeColor;
            input.Font = hint.Font;
            frame.Controls.Add(input);
        }
        content.Visible = true;
        if (input != null)
            input.Visible = true;
        frame.Controls.Add(hint);
        frame.Controls.Add(title);
        form.Controls.Add(frame);
        frame.BringToFront();

        // Route dialog keys to KeyDown instead of the hidden OK/Cancel buttons.
        static void PreviewSelectionKeys(object? sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                e.IsInputKey = true;
        }

        content.PreviewKeyDown += PreviewSelectionKeys;
        if (input != null)
            input.PreviewKeyDown += PreviewSelectionKeys;

        void Select()
        {
            if (onSelect != null)
            {
                onSelect();
                return;
            }

            form.DialogResult = DialogResult.OK;
            form.Close();
        }

        void Back()
        {
            form.DialogResult = DialogResult.Cancel;
            form.Close();
        }

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Escape)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Enter)
                Select();
            else
                Back();
        };

        // The "↵ SELECT" and "ESC BACK" texts answer to a click like their keys.
        hint.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            var charWidth = TextRenderer.MeasureText("MMMMMMMMMM", hint.Font, Size.Empty, TextFormatFlags.NoPadding).Width / 10f;
            var start = (hint.Width - (hint.Text.Length * charWidth)) / 2f;
            var column = (int)Math.Floor((e.X - start) / charWidth);
            var backStart = hint.Text.IndexOf("ESC", StringComparison.Ordinal);
            var selectEnd = backStart - 4;
            if (column >= 0 && column < selectEnd)
                Select();
            else if (column >= backStart && column < hint.Text.Length)
                Back();
        };
    }
}
