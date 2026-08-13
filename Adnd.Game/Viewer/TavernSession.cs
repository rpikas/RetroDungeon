// The tavern, as a tabletop rather than a menu.
//
// This DUPLICATES the party manipulation that PartyMenu does through the console, and does so on
// purpose. Nothing here is a game rule: who stands in which slot, who waits on the bench, what the
// roster contains -- it is inventory handling and housekeeping, and there is no rule to violate by
// giving it a direct path. Compare the maze and combat bridges, which never touch a rule and always
// answer with a command the game's own code judges: THAT restraint exists because those places do
// carry rules, and two implementations of a rule is one too many.
//
// The freedom this buys is the point. A console menu can offer Add, Remove and Reorder because it can
// only offer what fits on a numbered list. Here the natural moves are "pick the strongest six" and
// "clear the bench" -- shortcuts nobody would build behind three levels of prompt, and which cost
// almost nothing once the party is a list you can sort.

using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Data.Characters;
using Adnd.Data.Party;

namespace Adnd.Game.Viewer;

/// <summary>
/// Owns the tavern's view of the roster and the party, and applies the commands the viewer sends.
///
/// Not a form and not a loop: the caller drives it, so the same session works from the console menu
/// today and from anywhere else later. Every mutation saves, because the tabletop has no "confirm"
/// step and a player who drags a figurine and walks away has made a decision.
/// </summary>
public sealed class TavernSession
{
    /// <summary>How many stand in the party. The party's own rules cap it; this only lays them out.</summary>
    public const int PartySlots = 6;

    private readonly CharacterRepository _characters;
    private readonly PartyRepository _parties;
    private readonly Party _party;

    public TavernSession(CharacterRepository characters, PartyRepository parties, Party party)
    {
        _characters = characters;
        _parties = parties;
        _party = party;
    }

    /// <summary>Everyone who exists, party and bench alike, by name.</summary>
    private Dictionary<string, Character> Roster() =>
        _characters.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The party in marching order, dropping names that no longer resolve to a character.
    ///
    /// Stale names are filtered rather than repaired: a party file can outlive a deleted character, and
    /// PartyMenu already treats such an entry as absent. Two places disagreeing about who is in the
    /// party would show a figurine on the table that the game does not think is there.
    /// </summary>
    public List<Character> Party()
    {
        var roster = Roster();
        return _party.Members
            .Where(roster.ContainsKey)
            .Select(name => roster[name])
            .ToList();
    }

    /// <summary>Everyone not currently in the party: the bench.</summary>
    public List<Character> Bench()
    {
        var inParty = new HashSet<string>(_party.Members, StringComparer.OrdinalIgnoreCase);
        return _characters.GetAll()
            .Where(c => !inParty.Contains(c.Name))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Applies one viewer command. Returns true when something changed, so the caller knows to republish.
    ///
    /// An unknown command is ignored rather than treated as an error, for the same reason the pump ignores
    /// one: the viewer and the game ship separately, and a viewer offering a button this build has never
    /// heard of should do nothing rather than crash the tavern.
    /// </summary>
    public bool Apply(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;

        // "toggle:<name>" -- the figurine gesture. One command for both directions, because on a table
        // there is only one gesture: you pick the mini up and it goes to the other side. Making the
        // viewer decide between add and remove would mean it tracking who is already in the party, and
        // then being wrong about it for one frame after every change.
        if (command.StartsWith("toggle:", StringComparison.Ordinal))
            return Toggle(command.Substring("toggle:".Length));

        switch (command)
        {
            case "strongest": return PickStrongest();
            case "clearParty": return ClearParty();
            default: return false;
        }
    }

    private bool Toggle(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var existing = _party.Members
            .FirstOrDefault(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            _party.Members.Remove(existing);
            Save();
            return true;
        }

        if (!Roster().TryGetValue(name, out var character)) return false;
        if (Party().Count >= PartySlots) return false;   // full; the table shows six slots and no more

        // Appended, so the order figurines are picked up in is the order they march in.
        _party.Members.Add(character.Name);
        Save();
        return true;
    }

    /// <summary>
    /// Fills the party with the six strongest characters on the roster.
    ///
    /// "Strongest" is a convenience, not a ruling: level first, then hit points, then a nudge for the
    /// classes that survive the front rank. It exists because it is the thing a player actually wants
    /// after rolling twenty characters, and it is honest about being a rough sort rather than pretending
    /// to know the best party -- which depends on the dungeon, not on arithmetic.
    /// </summary>
    private bool PickStrongest()
    {
        var chosen = _characters.GetAll()
            .Where(c => !IsUnfitForDuty(c))
            .OrderByDescending(Strength)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PartySlots)
            .Select(c => c.Name)
            .ToList();

        if (chosen.Count == 0) return false;
        if (chosen.SequenceEqual(_party.Members, StringComparer.OrdinalIgnoreCase)) return false;

        _party.Members.Clear();
        _party.Members.AddRange(chosen);
        Save();
        return true;
    }

    private bool ClearParty()
    {
        if (_party.Members.Count == 0) return false;

        _party.Members.Clear();
        Save();
        return true;
    }

    /// <summary>Dead or otherwise out of action: never auto-picked, though they may be added by hand.</summary>
    private static bool IsUnfitForDuty(Character c) =>
        c.Status != CharacterStatus.None && c.Status != CharacterStatus.Poisoned;

    private static readonly CharacterClass[] FrontRankClasses =
    {
        CharacterClass.Fighter, CharacterClass.Paladin, CharacterClass.Ranger, CharacterClass.Cleric,
    };

    private static int Strength(Character c)
    {
        // Level dominates, hit points break ties, and someone who can hold the front rank edges out an
        // equal who cannot. The weights are deliberately crude -- see the note on PickStrongest.
        var front = c.Classes.Any(FrontRankClasses.Contains) ? 5 : 0;
        return c.Level * 100 + c.MaxHitPoints + front;
    }

    private void Save() => _parties.Save(_party);

    /// <summary>
    /// The prompt the tavern offers: one option per figurine on the table, plus the shortcuts.
    ///
    /// Every character is a TARGET rather than a button, so the whole roster costs no dialog space and
    /// the gesture is the same for all of them. The shortcuts have no object to point at, so they stay
    /// buttons -- which is exactly the split the protocol was made for.
    /// </summary>
    public ViewerPrompt Prompt()
    {
        var options = new List<ViewerPromptOption>();

        var inParty = Party();
        var full = inParty.Count >= PartySlots;

        // Anyone in the party can always be taken out.
        foreach (var c in inParty)
            options.Add(new ViewerPromptOption("toggle:" + c.Name, c.Name, ViewerIds.Character(c.Name)));

        // The bench is only offered when there is room. A full party still SHOWS everyone -- they are
        // standing there either way -- but they get no marker, so the table offers no move it would then
        // refuse. A figurine you can click that does nothing reads as a broken table, and the rule
        // everywhere else here is that only legal options are ever drawn.
        if (!full)
        {
            foreach (var c in Bench())
                options.Add(new ViewerPromptOption("toggle:" + c.Name, c.Name, ViewerIds.Character(c.Name)));
        }

        options.Add(new ViewerPromptOption("strongest", "Pick the strongest six"));
        options.Add(new ViewerPromptOption("clearParty", "Empty the party"));
        options.Add(new ViewerPromptOption("back", "Leave"));

        var text = inParty.Count == 0
            ? "The tavern. Nobody is in the party -- pick a figurine."
            : full
                ? $"The tavern. Full at {PartySlots} -- take someone out to make room."
                : $"The tavern. {inParty.Count} of {PartySlots} in the party.";

        return new ViewerPrompt("tavern", text, null, options);
    }
}
