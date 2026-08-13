// What the game is waiting for, stated so the viewer can draw it.
//
// Until now the viewer sent commands blind and the game dropped whatever made no sense in context.
// That is enough for a keyboard, but not for a surface that has to SHOW the choice: which buttons
// belong on the table, whose turn it is, whether Fight is even allowed for this character. A headset
// especially cannot offer a pointer at a button that was never drawn.
//
// So the game states the open question with every snapshot, and the rule is the same one that holds
// everywhere else here: only LEGAL options are listed, and the game decides legality using the code
// that already owns those rules. Nothing recomputes them on the way out, and nothing recomputes them
// in the viewer. A viewer that renders exactly what it is given cannot offer an illegal move.

using System.Collections.Generic;

namespace Adnd.Game.Viewer;

/// <summary>One choice the game is waiting on, as the viewer should present it.</summary>
/// <param name="Kind">"maze" or "combat" -- lets the viewer lay the choice out differently.</param>
/// <param name="Text">Human-readable statement of the question, e.g. "Grond is choosing".</param>
/// <param name="For">Who the choice is for, as a party member id, or null when it is the party's.</param>
/// <param name="Options">The legal answers, in the order they should be offered.</param>
public sealed record ViewerPrompt(
    string Kind,
    string Text,
    string? For,
    IReadOnlyList<ViewerPromptOption> Options);

/// <summary>
/// One answer the player may give. <paramref name="Id"/> is the command id the viewer sends back --
/// the same vocabulary as <see cref="ViewerCommands"/>, so a rendered button and a pressed key are
/// indistinguishable by the time the game sees them.
/// </summary>
/// <param name="Target">
/// A figure or object already on the table that answers this option, e.g. <c>ViewerIds.Character(name)</c>.
/// When set, the viewer makes that object clickable instead of drawing a button -- see the note on
/// PromptOption.Target in the protocol. Optional, so every existing call site keeps working and a
/// button-only option stays a button.
/// </param>
public sealed record ViewerPromptOption(string Id, string Label, string? Target = null);
