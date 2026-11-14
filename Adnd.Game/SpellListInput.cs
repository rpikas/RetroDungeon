using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Adnd.Game;

/// <summary>
/// Two extra ways to pick from a list of spells, next to arrow keys + Enter and clicking: every row is numbered
/// and a number can be typed, or the beginning of a spell name can be typed and confirmed with Enter.
/// </summary>
internal static class SpellListInput
{
    public static void Attach(Form form, ListBox list, Button ok, IReadOnlyList<string> names)
    {
        var count = list.Items.Count;
        for (var i = 0; i < count; i++)
            list.Items[i] = $"{i + 1}. {list.Items[i]}";

        var typed = new Label
        {
            Left = list.Left,
            Top = ok.Top + 4,
            Width = ok.Left - list.Left - 8,
            Height = 20,
            Text = Hint(string.Empty)
        };
        form.Controls.Add(typed);

        var buffer = string.Empty;

        void SetBuffer(string value)
        {
            buffer = value;
            typed.Text = Hint(buffer);
            var match = Find(buffer, names);
            if (match >= 0)
                list.SelectedIndex = match;
        }

        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End)
            {
                buffer = string.Empty;
                typed.Text = Hint(buffer);
            }
        };

        list.KeyPress += (_, e) =>
        {
            if (e.KeyChar == (char)Keys.Back)
            {
                if (buffer.Length > 0)
                    SetBuffer(buffer[..^1]);
            }
            else if (char.IsLetterOrDigit(e.KeyChar) || (e.KeyChar == ' ' && buffer.Length > 0) || e.KeyChar == '\'' || e.KeyChar == '-')
            {
                SetBuffer(buffer + e.KeyChar);

                // A number that cannot grow into another valid number answers at once.
                if (int.TryParse(buffer, out var n) && n >= 1 && n <= count && n * 10 > count)
                    ok.PerformClick();
            }
            else
            {
                return;
            }

            e.Handled = true;
        };

        // Button.OnClick has already set the form's DialogResult when this runs; resetting it keeps the dialog open.
        ok.Click += (_, _) =>
        {
            if (buffer.Length == 0)
                return;

            var match = Find(buffer, names);
            if (match < 0)
            {
                form.DialogResult = DialogResult.None;
                typed.Text = $"No match for \"{buffer}\"";
                return;
            }

            list.SelectedIndex = match;
        };
    }

    private static string Hint(string buffer)
        => buffer.Length == 0 ? "Type a number or a name, then Enter" : $"> {buffer}";

    // A typed number picks that row; text picks the first spell starting with it, else the first containing it.
    private static int Find(string buffer, IReadOnlyList<string> names)
    {
        if (buffer.Length == 0)
            return -1;

        if (int.TryParse(buffer, out var n))
            return n >= 1 && n <= names.Count ? n - 1 : -1;

        for (var i = 0; i < names.Count; i++)
            if (names[i].StartsWith(buffer, StringComparison.OrdinalIgnoreCase))
                return i;

        for (var i = 0; i < names.Count; i++)
            if (names[i].Contains(buffer, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }
}
