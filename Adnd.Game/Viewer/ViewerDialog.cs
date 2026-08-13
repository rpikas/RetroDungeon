// Makes an existing modal dialog answerable from the tabletop as well as the keyboard.
//
// Nearly every dialog in this game is an ordinary WinForms modal, and a player driving from the viewer hits
// each one as a dead stop: the table has nothing to show and nothing to click, and the game will not go on
// until someone types into a window they may not even be looking at. Rather than rewrite each screen as a
// table-native one, this wraps them -- the same form still runs, its question also goes to the table, and an
// answer from either end closes it. Both ends stay live, whichever answers first.

using System.Windows.Forms;

namespace Adnd.Game.Viewer;

public static class ViewerDialog
{
    /// <summary>Marks a numbered answer from the table, e.g. "pick:2".</summary>
    public const string Pick = "pick:";

    /// <summary>
    /// A pump needs a key map, and these dialogs have no keys of their own -- they answer through commands.
    /// Empty rather than null, because null means "do not start".
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Keys> NoKeys = new Dictionary<string, Keys>();

    /// <summary>
    /// What a wrapped dialog came back with: the form's own result, and the table's answer when the table is
    /// what answered. Callers read <see cref="Picked"/> first and fall back to their own controls.
    /// </summary>
    public readonly record struct Outcome(DialogResult Result, int? Picked);

    /// <summary>
    /// Runs a modal that offers a numbered list -- which spell, which item, which party member. Each label
    /// becomes an option on the table, and clicking one closes the form and comes back as a zero-based
    /// <see cref="Outcome.Picked"/>.
    /// </summary>
    /// <param name="forId">
    /// Who is being asked, when it is one person. The table then asks over that figure as a bubble instead of
    /// laying a panel across the board, which at a close camera is sheared and half off the screen.
    /// </param>
    public static Outcome RunPick(Form form, IWin32Window? owner, string title, string? forId,
                                  IReadOnlyList<string> labels, Action<ViewerPrompt?>? publish)
    {
        if (publish is null)
            return new Outcome(form.ShowDialog(owner), null);   // no viewer in play: exactly as it was

        var options = new List<ViewerPromptOption>(labels.Count + 1);
        for (var i = 0; i < labels.Count; i++)
            options.Add(new ViewerPromptOption(Pick + i, labels[i]));
        options.Add(new ViewerPromptOption("back", "Never mind"));

        int? picked = null;
        Listen(form, publish, new ViewerPrompt("choice", title, forId, options), command =>
        {
            if (command == "back")
                return DialogResult.Cancel;

            if (!command.StartsWith(Pick, StringComparison.Ordinal)) return null;
            if (!int.TryParse(command.Substring(Pick.Length), out var index)) return null;
            if (index < 0 || index >= labels.Count) return null;

            picked = index;
            return DialogResult.OK;
        });

        return new Outcome(form.ShowDialog(owner), picked);
    }

    /// <summary>
    /// The label for choice <paramref name="n"/>, lifted out of prompt text that already lists the choices
    /// numbered -- "3. L1 Sleep" becomes "L1 Sleep".
    ///
    /// Read back out of the text rather than passed in, because the callers all build their own numbered list
    /// as a blob of text and threading a structured list through every one of them would be a far bigger change
    /// than reading back the one thing they all agree on. Falls back to the bare number, so a caller that words
    /// its list differently still gets buttons that work.
    /// </summary>
    public static string LabelFor(string text, int n)
    {
        var head = n + ".";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(head, StringComparison.Ordinal))
                return line.Substring(head.Length).Trim();
        }

        return n.ToString();
    }

    /// <summary>
    /// Runs a modal whose answers are named rather than numbered -- a menu, or a yes/no. Each entry maps a
    /// command from the table to the result the form would have closed with had someone clicked the button.
    /// </summary>
    public static DialogResult RunModal(Form form, IWin32Window? owner, ViewerPrompt prompt,
                                        IReadOnlyDictionary<string, DialogResult> answers,
                                        Action<ViewerPrompt?>? publish)
    {
        if (publish is null)
            return form.ShowDialog(owner);

        Listen(form, publish, prompt,
            command => answers.TryGetValue(command, out var result) ? result : null);

        return form.ShowDialog(owner);
    }

    /// <summary>
    /// Runs a modal that genuinely cannot be answered from the table -- dragging a party into a new order,
    /// typing a name -- and says so on the table instead of leaving it looking hung.
    ///
    /// It still takes over the table's commands while it is up, and answers none of them. That is the point:
    /// without a pump of its own the commands fall through to the maze underneath, and the party would walk
    /// around while a modal dialog sat open on top of it. Published with no options, because offering a button
    /// that does nothing is worse than offering none.
    /// </summary>
    public static DialogResult RunBlocked(Form form, IWin32Window? owner, string what,
                                          Action<ViewerPrompt?>? publish)
    {
        if (publish is null)
            return form.ShowDialog(owner);

        var prompt = new ViewerPrompt("message", what + " -- do this in the game window.", null,
            Array.Empty<ViewerPromptOption>());

        Listen(form, publish, prompt, _ => null);
        return form.ShowDialog(owner);
    }

    /// <summary>
    /// Puts the question on the table and listens for an answer for as long as the form is up.
    ///
    /// The pump starts on Shown rather than now because it has to be the newest one to receive commands, and
    /// it is disposed when the form closes so that whatever is underneath -- the fight, the maze -- gets its
    /// own commands back rather than swallowing them into a dialog that has gone.
    /// </summary>
    private static void Listen(Form form, Action<ViewerPrompt?> publish, ViewerPrompt prompt,
                               Func<string, DialogResult?> answer)
    {
        publish(prompt);

        ViewerControlPump? pump = null;
        form.Shown += (_, _) => pump = ViewerControlPump.Start(form, NoKeys, _ => { }, command =>
        {
            var result = answer(command);
            if (result is null) return;

            form.DialogResult = result.Value;
            form.Close();
        });
        form.FormClosed += (_, _) => pump?.Dispose();
    }
}
