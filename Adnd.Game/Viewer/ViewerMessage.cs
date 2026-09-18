// A message the player can dismiss from the TABLE as well as from the game's own window.
//
// The game says things by stopping and waiting: MessageBox.Show, then nothing happens until someone
// clicks OK. That is fine when the only way in is this keyboard, and it strands anyone playing from the
// viewer -- every round of a fight ended with a box that the table could neither read nor answer, so the
// table sat there looking broken while the game waited behind it.
//
// MessageBox itself cannot be rescued: it is a modal loop inside Windows with no control of its own to
// register, so ViewerControlPump has nothing to hand a key to. This is the same dialog built out of a
// Form instead, which the pump CAN target -- and the pump already solves the hard part. It keeps one
// poller with a stack of registrations and gives the command to the newest, so a message opening over a
// fight takes the table's Continue without stealing the fight's Fight and Parry.
//
// Both ends stay live, as everywhere else here: OK, Enter, Escape and the table's own button all do the
// same thing, and a viewer that is closed, crashed or never started leaves an ordinary dialog behind.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Adnd.Core.Config;

namespace Adnd.Game.Viewer;

/// <summary>
/// A modal message that the viewer can answer. Drop-in for <c>MessageBox.Show(owner, text, title, OK)</c>.
/// </summary>
public static class ViewerMessage
{
    /// <summary>
    /// What a message accepts. One word, because there is only one thing to say to a message.
    ///
    /// Enter rather than Space or O: the form's accept button already answers to Enter, so the injected
    /// key travels the same path a keyboard press would and there is nothing extra to keep in step.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Keys> Vocabulary = new Dictionary<string, Keys>
    {
        ["continue"] = Keys.Enter,
    };

    /// <summary>The prompt to publish alongside, so the table has a button rather than a mystery.</summary>
    public static ViewerPrompt Prompt(string text) => new(
        "message",
        text,
        null,
        new[] { new ViewerPromptOption("continue", "Continue") });

    /// <summary>
    /// Says something on both surfaces at once: publishes it to the table, then shows the box.
    ///
    /// The pair is always wanted together -- a box the table cannot see is exactly the dead stop being fixed --
    /// and there are twenty-odd of these across the maze and the camp, so it is one call rather than two lines
    /// repeated twenty times. Callers put their own question back up afterwards.
    /// </summary>
    public static void Say(IWin32Window? owner, string title, string text, Action<ViewerPrompt?>? publish)
    {
        publish?.Invoke(Prompt(text));
        Show(owner, title, text);
    }

    /// <summary>
    /// Shows <paramref name="text"/> and waits for OK, Enter, Escape, or "continue" from the viewer.
    ///
    /// Sized to the text rather than fixed: a round of a fight is a dozen lines and a result is two words,
    /// and a box with a scrollbar in it would be worse than the MessageBox this replaces.
    /// </summary>
    public static void Show(IWin32Window? owner, string title, string text)
    {
        var isCombatRewards = string.Equals(title?.Trim(), "Combat Rewards", StringComparison.OrdinalIgnoreCase);
        var isCombatRound = string.Equals(title?.Trim(), "Combat Round", StringComparison.OrdinalIgnoreCase);
        Control? initialFocusControl = null;

        static bool IsCloseKey(Keys key) => key == Keys.Enter || key == Keys.Escape;

        void HandleCloseKeys(object? _, KeyEventArgs e, Form target)
        {
            if (!IsCloseKey(e.KeyCode))
                return;

            e.SuppressKeyPress = true;
            target.DialogResult = DialogResult.OK;
            target.Close();
        }

        void AttachCloseKeys(Control control, Form target)
        {
            control.PreviewKeyDown += (_, e) =>
            {
                if (IsCloseKey(e.KeyCode))
                    e.IsInputKey = true;
            };
            control.KeyDown += (_, e) => HandleCloseKeys(_, e, target);

            foreach (Control child in control.Controls)
                AttachCloseKeys(child, target);
        }

        using var form = new Form
        {
            Text = title,
            FormBorderStyle = (isCombatRewards || isCombatRound) ? FormBorderStyle.Sizable : FormBorderStyle.None,
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = isCombatRewards || isCombatRound,
            ShowInTaskbar = false,
            AutoSize = false,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Padding = (isCombatRewards || isCombatRound) ? new Padding(16) : new Padding(0),
            BackColor = (isCombatRewards || isCombatRound) ? SystemColors.Control : Color.Black,
            ForeColor = (isCombatRewards || isCombatRound) ? SystemColors.ControlText : GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
        };

        if (isCombatRewards)
        {
            form.ClientSize = new Size(642, 560);
            form.MinimumSize = new Size(642, 360);
        }
        else if (isCombatRound)
        {
            form.ClientSize = new Size(920, 680);
            form.MinimumSize = new Size(700, 420);
        }

        var message = string.IsNullOrWhiteSpace(text) ? " " : text.TrimEnd();

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };

        if (isCombatRewards || isCombatRound)
        {
            var body = new TextBox
            {
                Text = message,
                ReadOnly = true,
                Multiline = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Left,
            };

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Height = 42,
                Padding = new Padding(0),
                Margin = new Padding(0),
                WrapContents = false,
            };
            buttonPanel.Controls.Add(ok);

            form.Controls.Add(body);
            form.Controls.Add(buttonPanel);
        }
        else
        {
            var lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var lineCount = Math.Max(1, lines.Length);
            var maxLen = lines.Length == 0 ? 0 : lines.Max(l => l.Length);
            var width = Math.Clamp(360 + (maxLen * 4), 420, 760);
            var height = Math.Clamp(130 + (lineCount * 22), 120, 520);
            form.ClientSize = new Size(width, height);

            var framePanel = new Panel
            {
                Left = 4,
                Top = 4,
                Width = form.ClientSize.Width - 8,
                Height = form.ClientSize.Height - 8,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black,
                TabStop = true,
                TabIndex = 0
            };
            initialFocusControl = framePanel;

            var titleLabel = new Label
            {
                Left = 0,
                Top = 10,
                Width = framePanel.ClientSize.Width,
                Height = 36,
                Text = (title ?? string.Empty).ToUpperInvariant(),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 20f, FontStyle.Bold)
            };

            var body = new Label
            {
                Left = 18,
                Top = 54,
                Width = framePanel.ClientSize.Width - 36,
                Height = Math.Max(26, framePanel.ClientSize.Height - 98),
                AutoSize = false,
                Text = message,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 14f, FontStyle.Bold)
            };

            var hint = new Label
            {
                Left = 0,
                Top = framePanel.ClientSize.Height - 30,
                Width = framePanel.ClientSize.Width,
                Height = 20,
                AutoSize = false,
                Text = "↵ CONTINUE   ESC CLOSE",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = GameRulesProvider.Current.DefaultColor,
                Font = new Font("Consolas", 9f, FontStyle.Bold)
            };

            ok.Visible = false;
            ok.Size = new Size(1, 1);
            ok.Location = new Point(-100, -100);

            framePanel.Controls.Add(titleLabel);
            framePanel.Controls.Add(body);
            framePanel.Controls.Add(hint);
            form.Controls.Add(framePanel);
            form.Controls.Add(ok);
        }

        form.AcceptButton = ok;
        form.CancelButton = ok;   // Escape means the same as OK: there is nothing here to cancel

        AttachCloseKeys(form, form);

        // Registered only while this dialog is up, and unregistered by Dispose on the way out, so the
        // fight underneath goes back to receiving the table's commands the moment this closes.
        ViewerControlPump? pump = null;
        form.Shown += (_, _) =>
        {
            if (!isCombatRewards)
            {
                form.Activate();
                if (initialFocusControl != null)
                    form.ActiveControl = initialFocusControl;
                form.Focus();
            }

            pump = ViewerControlPump.Start(form, Vocabulary, key =>
            {
                if (key != Keys.Enter) return;
                form.DialogResult = DialogResult.OK;
                form.Close();
            });
        };
        form.FormClosed += (_, _) => pump?.Dispose();

        form.ShowDialog(owner);
    }
}
