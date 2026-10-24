using System.Text.Json;
using Adnd.Core.Items;
using Adnd.Core.Spells.Casting;
using Adnd.Data.Items;
using Adnd.Data.Spells;

namespace Adnd.Game;

/// <summary>Windows Forms questions asked while a Wish is being cast.</summary>
internal sealed class WishPrompter : IWishPrompter
{
    public static readonly WishPrompter Instance = new();

    private const int MaxSpellLevel = 8;

    public WishKind? ChooseKind()
    {
        var key = AskKey("Wish",
            "What does the caster wish for?\r\n\r\n" +
            "H) Heal/cure\r\n" +
            "I) Item\r\n" +
            "S) Spell (scroll, level 8 or lower)\r\n" +
            "C) Character improvement\r\n\r\n" +
            "Press H, I, S or C. Esc cancels.",
            "HISC");

        return key switch
        {
            'H' => WishKind.HealOrCure,
            'I' => WishKind.Item,
            'S' => WishKind.Spell,
            'C' => WishKind.CharacterImprovement,
            _ => null
        };
    }

    public Item? ChooseItem()
    {
        var items = new ItemRepository("Data/Items").LoadAll().ToList();
        var found = FindByName("Wish for an Item", "Type the name of the item (or part of it).", items.Select(i => i.Name).ToList());
        var item = found == null ? null : items.First(i => string.Equals(i.Name, found, StringComparison.OrdinalIgnoreCase));
        return item == null ? null : Clone(item);
    }

    public Item? ChooseSpellScroll()
    {
        var spells = new SpellRepository("Data/Spells").LoadAll()
            .Where(s => s.Level <= MaxSpellLevel)
            .Select(s => s.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var found = FindByName("Wish for a Spell", $"Type the name of the spell (level {MaxSpellLevel} or lower, or part of it).", spells);
        if (found == null)
            return null;

        var scrollName = $"Scroll of {found}";
        var scroll = new ItemRepository("Data/Items").LoadAll()
            .FirstOrDefault(i => string.Equals(i.Name, scrollName, StringComparison.OrdinalIgnoreCase));
        if (scroll == null)
        {
            MessageBox.Show($"No scroll exists for {found}.", "Wish", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return Clone(scroll);
    }

    public string? ChooseAbility()
    {
        var key = AskKey("Wish",
            "Which ability is improved by 1 point?\r\n\r\n" +
            "S) Strength\r\n" +
            "I) Intelligence\r\n" +
            "W) Wisdom\r\n" +
            "D) Dexterity\r\n" +
            "C) Constitution\r\n" +
            "H) Charisma\r\n\r\n" +
            "Esc cancels.",
            "SIWDCH");

        return key switch
        {
            'S' => "Strength",
            'I' => "Intelligence",
            'W' => "Wisdom",
            'D' => "Dexterity",
            'C' => "Constitution",
            'H' => "Charisma",
            _ => null
        };
    }

    private static Item Clone(Item source)
        => JsonSerializer.Deserialize<Item>(JsonSerializer.Serialize(source))!;

    private static string? FindByName(string title, string prompt, IReadOnlyList<string> names)
    {
        var extra = string.Empty;
        while (true)
        {
            var text = AskText(title, prompt + extra);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = text.Trim();
            var exact = names.FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var matches = names
                .Where(n => n.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matches.Count == 1)
                return matches[0];

            extra = matches.Count == 0
                ? $"\r\n\r\nNothing matches \"{text}\"."
                : $"\r\n\r\n{matches.Count} matches for \"{text}\":\r\n" + string.Join("\r\n", matches.Take(15)) + (matches.Count > 15 ? "\r\n..." : string.Empty);
        }
    }

    private static char? AskKey(string title, string prompt, string validKeys)
    {
        using var form = CreateForm(title, prompt, out var promptBox);
        char? chosen = null;
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            var ch = (char)e.KeyCode;
            if (validKeys.Contains(ch))
            {
                chosen = ch;
                form.DialogResult = DialogResult.OK;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.Cancel;
            }
        };

        OldStyleDialog.Apply(form, promptBox, selectLabel: "CLOSE");
        form.ShowDialog();
        return chosen;
    }

    private static string? AskText(string title, string prompt)
    {
        using var form = CreateForm(title, prompt, out var promptBox);
        var input = new TextBox { Left = 12, Top = form.ClientSize.Height - 38, Width = form.ClientSize.Width - 120, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        var ok = new Button { Text = "OK", Left = form.ClientSize.Width - 96, Top = form.ClientSize.Height - 40, Width = 80, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        promptBox.Height -= 40;
        form.Controls.Add(input);
        form.Controls.Add(ok);
        form.AcceptButton = ok;
        form.Shown += (_, _) => input.Focus();
        OldStyleDialog.Apply(form, promptBox, input, selectLabel: "OK");

        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }

    private static Form CreateForm(string title, string prompt, out TextBox promptBox)
    {
        var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(520, 360),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };

        promptBox = new TextBox
        {
            Left = 12,
            Top = 12,
            Width = 496,
            Height = 336,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            TabStop = false,
            Text = prompt,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        form.Controls.Add(promptBox);
        form.CancelButton = new Button { DialogResult = DialogResult.Cancel };
        return form;
    }
}
