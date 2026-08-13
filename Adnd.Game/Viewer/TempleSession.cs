// The Church of Chant, as a tabletop rather than a menu.
//
// The gesture the temple wants is obvious once the party is standing on the board: point at the one who is
// hurt. The console has to ask "H)eal party" and then heal everyone it can afford, because a numbered list is
// all it has; here each figure who needs something wears a ring with its own price, and clicking one treats
// that one. Nobody is healed by accident and nobody is skipped silently.
//
// Raising is TWO clicks, deliberately: the corpse, then the purse. It is the most expensive and most
// irreversible thing in the game -- a failed raise turns Dead into Ashes, and Ashes into Lost forever -- so it
// should take a moment and it should be clear whose gold went. That is also why nothing here decides what
// happens: <see cref="Temple"/> owns the system shock roll and the Ashes/Lost progression, and this only
// chooses who it is applied to.

using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Data.Characters;
using Adnd.Data.Party;

namespace Adnd.Game.Viewer;

public sealed class TempleSession
{
    private readonly CharacterRepository _characters;
    private readonly PartyRepository _parties;

    /// <summary>
    /// Who is waiting to be raised while the table asks who pays, or null when nothing is pending.
    ///
    /// A name rather than the character, because every command reloads the roster from disk: holding an object
    /// across two clicks would mean paying out of a purse whose balance was read before the last heal.
    /// </summary>
    private string? _raising;

    /// <summary>What just happened, for the caller to show. Cleared as soon as it is taken.</summary>
    private readonly List<string> _events = new();

    public TempleSession(CharacterRepository characters, PartyRepository parties)
    {
        _characters = characters;
        _parties = parties;
    }

    /// <summary>The party in marching order, dropping names the roster no longer knows.</summary>
    public List<Character> Party()
    {
        var roster = _characters.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        return _parties.Load().Members
            .Where(roster.ContainsKey)
            .Select(name => roster[name])
            .ToList();
    }

    /// <summary>Anything worth reporting since the last call, and forgotten once taken.</summary>
    public List<string> TakeEvents()
    {
        var taken = new List<string>(_events);
        _events.Clear();
        return taken;
    }

    /// <summary>
    /// What the temple offers, as rings on the people it concerns.
    ///
    /// Only what the game will actually do: a character who cannot afford their own healing gets no ring, and
    /// while a raise is pending the only rings are the purses that can cover it. A button that has to be
    /// refused after the fact is worse on a table than in a console -- the console can explain, a ring cannot.
    /// </summary>
    public ViewerPrompt Prompt()
    {
        var party = Party();
        var options = new List<ViewerPromptOption>();

        if (_raising != null)
        {
            var target = party.FirstOrDefault(c => Same(c.Name, _raising));
            if (target == null)
            {
                // The pending target left the party between clicks. Drop it rather than ask for a payer.
                _raising = null;
                return Prompt();
            }

            var cost = Temple.CostToRaise(target);
            foreach (var payer in party.Where(p => p.GoldPieces >= cost))
                options.Add(new ViewerPromptOption("pay:" + payer.Name, $"{payer.Name} pays ({payer.GoldPieces} gp)",
                                                   Target: ViewerIds.Character(payer.Name)));

            options.Add(new ViewerPromptOption("cancel", "Never mind"));

            var text = options.Count > 1
                ? $"Raising {target.Name} costs {cost} gp. Who pays?"
                : $"Raising {target.Name} costs {cost} gp, and nobody in the party has it.";

            return new ViewerPrompt("temple", text, null, options);
        }

        foreach (var c in party)
        {
            if (Temple.CanBeRaised(c))
            {
                var state = c.HasStatus(CharacterStatus.Ashes) ? "ashes" : "dead";
                options.Add(new ViewerPromptOption("raise:" + c.Name,
                    $"Raise {c.Name} ({state}, {Temple.CostToRaise(c)} gp)",
                    Target: ViewerIds.Character(c.Name)));
                continue;
            }

            if (Temple.NeedsHealing(c) && c.GoldPieces >= Temple.HealCost)
            {
                options.Add(new ViewerPromptOption("heal:" + c.Name,
                    $"Heal {c.Name} ({c.CurrentHitPoints}/{c.MaxHitPoints}, {Temple.HealCost} gp)",
                    Target: ViewerIds.Character(c.Name)));
            }
        }

        options.Add(new ViewerPromptOption("back", "Leave the temple"));

        var waiting = options.Count > 1
            ? "The priests are waiting. Click whoever needs tending."
            : "Nobody here needs the priests.";

        return new ViewerPrompt("temple", waiting, null, options);
    }

    /// <summary>
    /// Applies one command. Returns true when the table should be redrawn.
    ///
    /// An unknown command does nothing, for the reason the pump ignores one: the viewer and the game ship
    /// separately, and a board offering a button this build has never heard of should be inert, not fatal.
    /// </summary>
    public bool Apply(string command)
    {
        if (command == "cancel")
        {
            var was = _raising;
            _raising = null;
            return was != null;
        }

        if (command.StartsWith("heal:", StringComparison.Ordinal))
        {
            var c = Find(command.Substring(5));
            if (c == null) return false;

            if (!Temple.Heal(c, _characters))
            {
                // Two different refusals, and saying the wrong one is worse than saying nothing. Reachable from
                // a board that has gone stale -- clicking a ring for someone another click already healed.
                _events.Add(Temple.NeedsHealing(c)
                    ? $"{c.Name} cannot afford the {Temple.HealCost} gp."
                    : $"{c.Name} is already well.");
                return true;
            }

            _events.Add($"{c.Name} is healed to {c.MaxHitPoints} and pays {Temple.HealCost} gp.");
            return true;
        }

        if (command.StartsWith("raise:", StringComparison.Ordinal))
        {
            var c = Find(command.Substring(6));
            if (c == null || !Temple.CanBeRaised(c)) return false;

            _raising = c.Name;   // now the table asks who pays
            return true;
        }

        if (command.StartsWith("pay:", StringComparison.Ordinal))
        {
            if (_raising == null) return false;

            var target = Find(_raising);
            var payer = Find(command.Substring(4));
            _raising = null;

            if (target == null || payer == null) return true;

            _events.AddRange(Temple.Raise(target, payer, _characters));
            return true;
        }

        return false;
    }

    private Character? Find(string name) =>
        _characters.GetAll().FirstOrDefault(c => Same(c.Name, name));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
