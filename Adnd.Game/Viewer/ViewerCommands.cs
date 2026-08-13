// The vocabulary the viewer speaks, and the keys each word stands for.
//
// These ids are the contract. They name INTENTIONS, not keys, for two reasons: the viewer should
// not have to know that turning left happens to be "A" here, and a headset offering a pointer or a
// gesture has no keys to send. Adding a command means adding a line here and a button there --
// nothing in the game's own code changes.
//
// The viewer may send an id this build does not know; the pump ignores it. That is what lets the
// two halves ship at different times, which they will, since the viewer lives in Unity.

using System.Collections.Generic;
using System.Windows.Forms;

namespace Adnd.Game.Viewer;

public static class ViewerCommands
{
    /// <summary>What the maze accepts. Mirrors the switch in MazeForm's key handler.</summary>
    public static readonly IReadOnlyDictionary<string, Keys> Maze = new Dictionary<string, Keys>
    {
        ["forward"] = Keys.Up,
        ["turnLeft"] = Keys.Left,
        ["turnRight"] = Keys.Right,
        ["party"] = Keys.O,        // the party overlay
        ["camp"] = Keys.C,         // only while the overlay is up, exactly as on the keyboard
        ["status"] = Keys.S,
        ["inspect"] = Keys.I,
        ["back"] = Keys.Escape,
    };

    /// <summary>
    /// What a fight accepts. Mirrors the switch in EncounterForm's key handler, including the
    /// rank rules it enforces: Fight is refused for the back three, and the game says so in its own
    /// dialog. Those rules stay where they are -- the viewer offers the choice, the game judges it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Keys> Combat = new Dictionary<string, Keys>
    {
        ["fight"] = Keys.F,
        ["parry"] = Keys.P,
        ["spell"] = Keys.S,
        ["useItem"] = Keys.U,
        ["run"] = Keys.R,
        ["targetGroup"] = Keys.G,
        ["confirm"] = Keys.Enter,  // Fight for the front rank, Parry for the back
        ["undo"] = Keys.T,         // steps back to the previous character
        ["back"] = Keys.Escape,
    };

    /// <summary>
    /// What the town board accepts: walking from one place to the next.
    ///
    /// Unlike the maze and combat maps, nothing INJECTS these keys -- the hub is a console loop with no
    /// form to feed, so it applies the command itself, the way the tavern does. The key is here for the
    /// one thing this table is still good for: it is what the viewer prints on the button, so the table
    /// teaches the keyboard rather than hiding it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Keys> Town = new Dictionary<string, Keys>
    {
        ["next"] = Keys.N,
        ["maze"] = Keys.M,     // the same key the hub menu has always used

        // The hub's own letters, so the keys on the table are the keys the console prints.
        ["go:TrainingGrounds"] = Keys.T,
        ["go:Tavern"] = Keys.G,          // Gilgamash
        ["go:Temple"] = Keys.C,          // Church of Chant
        ["go:Shop"] = Keys.B,            // Boltac's
        ["go:EdgeOfTown"] = Keys.E,
        ["enter"] = Keys.Enter,
    };

    /// <summary>
    /// A numbered choice: which spell, which ally, which item, who pays. The old UI asks these by typing a
    /// number, so the table offers the same numbers.
    ///
    /// It also carries the NAMED answers of the screens that are menus rather than lists -- camp, a camped
    /// character's own screen, and a yes/no. Those are all published as "choice" because they are all a
    /// wrapped modal, so one map serves them; the ids are distinct per screen, and no two that share a key
    /// ever appear in the same prompt. Where the old screen has a letter of its own, that letter is used:
    /// camp answers to R, I and L in the game window, and the camped character's screen prints the same
    /// E/U/T/D/P/M/C/L the console does.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Keys> Choice = new Dictionary<string, Keys>
    {
        ["back"] = Keys.Escape,
        ["cancel"] = Keys.Escape,

        // A yes/no.
        ["yes"] = Keys.Y,
        ["no"] = Keys.N,

        // Camp, whose own form answers to exactly these three.
        ["reorder"] = Keys.R,
        ["inspect"] = Keys.I,
        ["leave"] = Keys.L,

        // One camped character's screen.
        ["equip"] = Keys.E,
        ["unequip"] = Keys.U,
        ["trade"] = Keys.T,
        ["drop"] = Keys.D,
        ["pool"] = Keys.P,
        ["memorize"] = Keys.M,
        ["cast"] = Keys.C,
        ["close"] = Keys.L,          // L<-eave, as the console prints it
    };

    /// <summary>The Church of Chant. Its own two services, plus the way out.</summary>
    public static readonly IReadOnlyDictionary<string, Keys> Temple = new Dictionary<string, Keys>
    {
        ["back"] = Keys.L,               // L<-eave, as the console prints it
        ["cancel"] = Keys.Escape,
    };

    /// <summary>
    /// Boltac's. The goods are pointed at rather than named, so only the standing choices are in here -- but
    /// P and L being spoken for is what the counter's own letters have to work around; see KeyLabels.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Keys> Shop = new Dictionary<string, Keys>
    {
        ["pool"] = Keys.P,
        ["back"] = Keys.L,
    };

    /// <summary>
    /// Gilgamash's, where the party is picked. The roster is pointed at rather than named, so these are the
    /// standing three; L<-eave is the console's own letter for the menu this replaces.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Keys> Tavern = new Dictionary<string, Keys>
    {
        ["strongest"] = Keys.S,
        ["clearParty"] = Keys.E,     // E<-mpty
        ["back"] = Keys.L,
    };

    /// <summary>
    /// Keys that answer on the TABLE only, and must never go in the maps above.
    ///
    /// The maps are not just labels: the pump answers a command it finds in one by INJECTING that key into the
    /// form, on the assumption that the form's own handler knows it. That holds for the maze and a fight, whose
    /// maps mirror real key handlers. "Fight, spread out" has no key in the fight's window -- it is a choice the
    /// table offers -- so listing it in <see cref="Combat"/> would have the pump press A at a form that ignores
    /// A, and the command would vanish on the way in. Here it is a label and a viewer accelerator instead: the
    /// viewer turns the key into the id and the id travels the route it already took.
    ///
    /// Ids in here have to be unambiguous across every kind, since there is no kind to tell them apart by.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Keys> TableOnly = new Dictionary<string, Keys>
    {
        ["fightSpread"] = Keys.A,    // A<-ll of them, and the digits next to it aim at one
    };

    /// <summary>The vocabulary a prompt kind draws on, so one place decides which map is in play.</summary>
    public static IReadOnlyDictionary<string, Keys>? MapFor(string? kind) => kind switch
    {
        "maze" => Maze,
        "combat" => Combat,
        "town" => Town,
        "choice" => Choice,
        "temple" => Temple,
        "shop" => Shop,
        "tavern" => Tavern,
        "message" => ViewerMessage.Vocabulary,   // one word: Continue
        _ => null,
    };

    /// <summary>
    /// How a key is WRITTEN on a button in the viewer.
    ///
    /// ASCII on purpose: the viewer's font atlas is generated from ASCII, so an arrow glyph would draw
    /// as a missing-character box on the table -- correct-looking in the editor with a fallback font,
    /// a row of empty squares in the build.
    /// </summary>
    public static string KeyLabel(Keys key) => key switch
    {
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.Escape => "Esc",
        Keys.Enter => "Enter",
        Keys.Space => "Space",
        _ => key.ToString(),       // letters and digits already read as themselves
    };

    /// <summary>
    /// The key hint for one option by NAME, or null when the vocabulary does not name it.
    ///
    /// Answers for the ids that are words -- Fight, Pool, Leave -- and for a numbered pick. It cannot answer for
    /// the ids that name a THING, since "attack the third slime" or "buy this axe" is a key by its POSITION in
    /// the question rather than by what it is called; that is <see cref="KeyLabels"/>'s job.
    /// </summary>
    public static string? KeyLabelFor(string? kind, string? optionId)
    {
        if (string.IsNullOrEmpty(optionId)) return null;

        // "pick:2" answers to 3, and so on: the lists these come from are printed 1-based in the console, and
        // typing that number is how the old UI has always answered them. Not in a dictionary because the ids are
        // built when the question is asked -- there is no fixed set of them to write down. The tenth answers to
        // 0, as the numbered ranges below do and as a ten-line list is typed.
        if (optionId!.StartsWith(PickPrefix, StringComparison.Ordinal)
            && int.TryParse(optionId.Substring(PickPrefix.Length), out var index)
            && index >= 0 && index < Digits.Length)
        {
            return Digits[index].ToString();
        }

        var map = MapFor(kind);
        if (map != null && map.TryGetValue(optionId, out var key)) return KeyLabel(key);

        return TableOnly.TryGetValue(optionId, out var tableKey) ? KeyLabel(tableKey) : null;
    }

    /// <summary>
    /// The key for every option in one prompt, in step with <see cref="ViewerPrompt.Options"/>. Null in a slot
    /// means that option has no key and must be pointed at.
    ///
    /// Taken a whole prompt at a time because the interesting keys are POSITIONAL. Everything the player points
    /// at -- a monster to hit, a card on the counter, whose purse to open -- is named by an id built when the
    /// question was asked, so there is no fixed set of them to write down and no name to look up. What the table
    /// can do is number them in the order they are offered, which is also the order they are laid out, and let
    /// the viewer print that number on the ring or the card. Doing it here, once, is what keeps two options from
    /// claiming the same key: a per-option lookup cannot see its neighbours.
    ///
    /// Two ranges, split where the old UI splits them -- LETTERS for the shop's stock, DIGITS for what is
    /// yours:
    ///
    ///   Boltac's shelves run A, B, C, which is what the console's Buy screen does with the same list.
    ///
    ///   Everything of the party's own is numbered, which is what the console does with all of it: your goods
    ///   are numbered on its Sell screen, your party is numbered when it asks who shops, and every "choose #"
    ///   in the game numbers people. So the shopper's own cards, the other purses at the counter, the monsters
    ///   in a fight and the people in the temple and the tavern all count 1-9 and then 0, in the order the
    ///   question offers them -- which is the order they are laid out.
    ///
    /// The split is what makes the counter fit. Both sides of it lettered from opposite ends of the alphabet
    /// worked, but the shop is far the longer list: two dozen goods, five things in a pack, five other purses
    /// and two standing answers is more than twenty-six letters, and the five that went without were the last
    /// five weapons on the shelf. Lettering only what Boltac owns leaves him the whole alphabet.
    ///
    /// Letters the prompt's own words have already claimed are skipped -- P is Pool and L is Leave at the
    /// counter -- so no card can shadow a standing answer. Past the end of either range the rest have no key:
    /// they are still there and still clickable, which is the same position a headset with no keyboard is in
    /// for all of them.
    /// </summary>
    public static IReadOnlyList<string?> KeyLabels(ViewerPrompt prompt)
    {
        var options = prompt.Options;
        var labels = new string?[options.Count];

        // The words first, so the positional ranges below know which keys are already spoken for.
        var taken = new HashSet<char>();
        for (var i = 0; i < options.Count; i++)
        {
            var label = KeyLabelFor(prompt.Kind, options[i].Id);
            labels[i] = label;
            if (label != null && label.Length == 1) taken.Add(char.ToUpperInvariant(label[0]));
        }

        var letter = 'A';
        var digit = 0;

        for (var i = 0; i < options.Count; i++)
        {
            if (labels[i] != null) continue;

            // A button the vocabulary does not name has nothing to number: it is one of a kind, not one of a
            // list, so a key for it belongs in a map above rather than in a range here.
            var target = options[i].Target;
            if (string.IsNullOrEmpty(target)) continue;

            labels[i] = Is(target, ShelfCards)
                ? TakeLetter(ref letter, taken)
                : TakeDigit(ref digit, taken);
        }

        return labels;
    }

    /// <summary>The next letter, stepping over any the prompt's own words already own, or null past Z.</summary>
    private static string? TakeLetter(ref char at, HashSet<char> taken)
    {
        while (at <= 'Z')
        {
            var candidate = at;
            at = (char)(at + 1);
            if (taken.Add(candidate)) return candidate.ToString();
        }

        return null;
    }

    /// <summary>
    /// The next unclaimed number, or null past the tenth. Counts 1 to 9 and then 0, as the top row of a
    /// keyboard does and as a numbered list of ten would be typed.
    /// </summary>
    private static string? TakeDigit(ref int at, HashSet<char> taken)
    {
        while (at < Digits.Length)
        {
            var candidate = Digits[at];
            at++;
            if (taken.Add(candidate)) return candidate.ToString();
        }

        return null;
    }

    private const string Digits = "1234567890";

    private static bool Is(string? target, string prefix) =>
        !string.IsNullOrEmpty(target) && target!.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Mirrors ViewerDialog.Pick; the two must agree about what a numbered answer looks like.</summary>
    private const string PickPrefix = "pick:";

    private static readonly string ShelfCards = ViewerIds.WarePrefix(ViewerIds.ShopSide);
}
