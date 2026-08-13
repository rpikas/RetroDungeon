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
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
        };

        var body = new Label
        {
            Text = string.IsNullOrWhiteSpace(text) ? " " : text.TrimEnd(),
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 12),
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };
        layout.Controls.Add(body);
        layout.Controls.Add(ok);
        form.Controls.Add(layout);

        form.AcceptButton = ok;
        form.CancelButton = ok;   // Escape means the same as OK: there is nothing here to cancel

        // Registered only while this dialog is up, and unregistered by Dispose on the way out, so the
        // fight underneath goes back to receiving the table's commands the moment this closes.
        ViewerControlPump? pump = null;
        form.Shown += (_, _) => pump = ViewerControlPump.Start(form, Vocabulary, key =>
        {
            if (key != Keys.Enter) return;
            form.DialogResult = DialogResult.OK;
            form.Close();
        });
        form.FormClosed += (_, _) => pump?.Dispose();

        form.ShowDialog(owner);
    }
}
