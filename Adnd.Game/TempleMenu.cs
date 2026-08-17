using System;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Data.Party;
using Adnd.Data.Characters;

namespace Adnd.Game;

public class TempleMenu
{
    private readonly PartyRepository _partyRepo = new("Data/Party");
    private readonly CharacterRepository _charRepo = new("Data/Characters");

    private readonly Adnd.Game.Viewer.TabletopViewerBridge _viewer = new();

    public void Show()
    {
        var session = new Adnd.Game.Viewer.TempleSession(_charRepo, _partyRepo);

        while (true)
        {
            var party = _partyRepo.Load();

            Console.Clear();
            Console.WriteLine("=== Church of Chant ===");
            Console.WriteLine("H)eal Party");
            Console.WriteLine("R)aise Dead");
            Console.WriteLine("L<-eave");
            if (_viewer.Enabled)
                Console.WriteLine("(or click whoever needs tending on the table)");

            PublishTemple(session);

            // Both ends live at once, exactly as in the maze and in a fight: whichever answers first is acted
            // on. The console keeps its own way out, so a viewer that has been closed cannot trap the party in
            // a temple waiting for a click nobody can make.
            var (key, command) = AwaitChoice();

            if (command != null)
            {
                if (command == "back") break;

                if (session.Apply(command))
                {
                    PublishTemple(session);
                    Report(session);
                }

                continue;
            }

            if (key == ConsoleKey.H) HealParty(party);
            else if (key == ConsoleKey.R) RaiseDead(party);
            else if (key == ConsoleKey.L || key == ConsoleKey.Enter) break;
        }
    }

    /// <summary>
    /// Lay the party out in the temple with the priests' offer on top.
    ///
    /// Reuses the town publish rather than inventing a temple layout: the party is ALREADY standing on the
    /// temple pad, because that is where walking there put them, and the building is already drawn around them.
    /// </summary>
    private void PublishTemple(Adnd.Game.Viewer.TempleSession session)
    {
        if (!_viewer.Enabled)
            return;

        _viewer.PublishTown("Temple", session.Party(), session.Prompt());
    }

    /// <summary>
    /// Says what the temple just did, on both surfaces. The roll and its consequences are the whole drama of a
    /// raise, so they are not left to a figure quietly standing up.
    /// </summary>
    private void Report(Adnd.Game.Viewer.TempleSession session)
    {
        var events = session.TakeEvents();
        if (events.Count == 0)
            return;

        foreach (var line in events)
            Console.WriteLine(line);

        var text = string.Join(Environment.NewLine, events);
        _viewer.PublishTown("Temple", session.Party(), Adnd.Game.Viewer.ViewerMessage.Prompt(text));

        // Wait for the acknowledgement from either end, so a resurrection cannot flash past unread. Any key at
        // the console does it, as every other Console.ReadKey here does; from the table it takes Continue.
        while (true)
        {
            var (_, command) = AwaitChoice();
            if (command == null || command == "continue" || command == "back")
                return;
        }
    }

    /// <summary>A key at the console or a click on the table, whichever comes first.</summary>
    private (ConsoleKey Key, string? Command) AwaitChoice()
    {
        while (true)
        {
            if (Console.KeyAvailable) return (Console.ReadKey(true).Key, null);

            var command = _viewer.TryTakeCommand();
            if (command != null) return (default, command);

            System.Threading.Thread.Sleep(60);
        }
    }

    private void HealParty(Party party)
    {
        var partyCharacters = PartyCharacters(party);

        if (partyCharacters.Count == 0)
        {
            Console.WriteLine("No party members found.");
            Console.ReadKey(true);
            return;
        }

        var needHealing = partyCharacters.Where(Temple.NeedsHealing).ToList();

        if (needHealing.Count == 0)
        {
            Console.WriteLine("No one needs healing.");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine($"Healing costs {Temple.HealCost} gp per character who needs healing.");

        var healedCount = 0;
        var skipped = new List<string>();

        foreach (var c in needHealing)
        {
            if (Temple.Heal(c, _charRepo)) healedCount++;
            else skipped.Add(c.Name);
        }

        Console.WriteLine($"Healed {healedCount} character(s).");

        if (skipped.Count > 0)
            Console.WriteLine($"Could not afford healing: {string.Join(", ", skipped)}");

        Console.ReadKey(true);
    }

    /// <summary>The party as characters, dropping names the roster no longer knows.</summary>
    private List<Character> PartyCharacters(Party party)
    {
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        return party.Members.Where(roster.ContainsKey).Select(name => roster[name]).ToList();
    }

    private void RaiseDead(Party party)
    {
        var partyCharacters = PartyCharacters(party);

        if (partyCharacters.Count == 0)
        {
            Console.WriteLine("No party members found.");
            Console.ReadKey(true);
            return;
        }

        var revivableMembers = partyCharacters.Where(Temple.CanBeRaised).ToList();

        if (revivableMembers.Count == 0)
        {
            Console.WriteLine("No party members can be revived.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("=== RAISE DEAD ===");
        Console.WriteLine();
        Console.WriteLine("Who wants to be raised?");
        for (int i = 0; i < revivableMembers.Count; i++)
        {
            var c = revivableMembers[i];
            var state = c.HasStatus(CharacterStatus.Ashes) ? "Ashes" : "Dead";
            Console.WriteLine($"{i + 1}. {c.Name} ({state}, HP {c.CurrentHitPoints}/{c.MaxHitPoints}, Cost {Temple.CostToRaise(c)} gp)");
        }

        Console.Write("Choose #: ");
        var targetSelection = InputHelper.ReadNumber(1, revivableMembers.Count);
        if (!targetSelection.HasValue)
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
            return;
        }

        var target = revivableMembers[targetSelection.Value - 1];
        var revivalCost = Temple.CostToRaise(target);

        Console.WriteLine();
        Console.WriteLine($"Who will pay {revivalCost} gp?");
        for (int i = 0; i < partyCharacters.Count; i++)
        {
            var payer = partyCharacters[i];
            var canAfford = payer.GoldPieces >= revivalCost ? "" : " (not enough)";
            Console.WriteLine($"{i + 1}. {payer.Name} ({payer.GoldPieces} gp){canAfford}");
        }

        Console.Write("Choose #: ");
        var payerSelection = InputHelper.ReadNumber(1, partyCharacters.Count);
        if (!payerSelection.HasValue)
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        foreach (var line in Temple.Raise(target, partyCharacters[payerSelection.Value - 1], _charRepo))
            Console.WriteLine(line);

        Console.ReadKey(true);
    }

}
