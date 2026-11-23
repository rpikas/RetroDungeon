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
            var members = PartyCharacters(party);
            var isWiz = Adnd.Core.Config.GameRulesProvider.Current.Rules == Adnd.Core.Config.Ruleset.Wizardry;
            var listed = isWiz ? members.Where(m => Temple.NeedsHealing(m) || Temple.CanBeRaised(m)).ToList() : members;
            var nobodyNeedsHelp = isWiz && listed.Count == 0;

            Console.Clear();
            Console.WriteLine("=== Church of Chant ===");
            if (nobodyNeedsHelp)
            {
            }
            else if (isWiz)
            {
                Console.WriteLine("Service / Spell                Fee");
                Console.WriteLine("Cure paralysis                 100 gp x level");
                Console.WriteLine("Cure stoned                    200 gp x level");
                Console.WriteLine("Raise dead                     250 gp x level");
                Console.WriteLine("Raise from ashes               500 gp x level");
            }
            else
            {
            Console.WriteLine("Service / Spell                Fee");
            Console.WriteLine("Heal hit points                15 gp/hp");
            Console.WriteLine($"Neutralize poison              {Temple.CurePoisonCost} gp");
            Console.WriteLine($"Remove paralyzation            {Temple.CureParalysisCost} gp");
            Console.WriteLine($"Raise dead                     {Temple.RaiseDeadBaseCost} gp + {Temple.RaiseDeadCostPerLevel} gp/level of target");
            Console.WriteLine($"Resurrection                   twice the Raise dead fee");
            Console.WriteLine($"Restoration                    {RestorationRules.BaseCost} gp + {RestorationRules.CostPerRecipientLevel} gp/level of recipient");
            Console.WriteLine($"Cure feeblemind                {Temple.CureFeeblemindCost} gp");
            Console.WriteLine($"Cure disease                   {Temple.CureDiseaseCost} gp");
            Console.WriteLine($"Restore from petrification     {Temple.RestorePetrificationCost} gp");
            }

            if (!nobodyNeedsHelp)
            {
                Console.WriteLine();
                Console.WriteLine("Party:");
            }
            else
            {
                Console.WriteLine();
            }
            if (nobodyNeedsHelp)
                Console.WriteLine("Nobody needs healing.");
            for (var i = 0; i < listed.Count; i++)
            {
                var m = listed[i];
                var state = m.HasStatus(CharacterStatus.Ashes) ? "Ashes" : m.HasStatus(CharacterStatus.Dead) ? "Dead" : $"{m.Status}";
                Console.WriteLine($"{i + 1}. {m.Name,-14} HP {m.CurrentHitPoints}/{m.MaxHitPoints}  {state}  {m.GoldPieces} gp");
            }

            var menu = new List<string>();
            if (!nobodyNeedsHelp)
            {
                menu.Add(isWiz ? "C)ure Party (status ailments)" : "H)eal Party");
                menu.Add(isWiz ? "(or press 1-6 / click a member to cure only them)" : "(or press 1-6 / click a member to heal only them)");
                menu.Add("R)aise Dead");
            }
            if (!isWiz)
                menu.Add("E) Restoration (lost level, feeblemind)");
            menu.Add("L↵eave");
            if (_viewer.Enabled && !nobodyNeedsHelp)
                menu.Add("(or click whoever needs tending on the table)");

            // With a picture the options go under the fees, with the picture beside them.
            if (TermConsole.Host != null && TermConsole.CityPicture != null)
                TermConsole.SideLines = menu.ToArray();
            else
            {
                Console.WriteLine();
                foreach (var line in menu) Console.WriteLine(line);
            }

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

            if (key == ConsoleKey.H || (isWiz && key == ConsoleKey.C)) HealParty(party);
            else if (key >= ConsoleKey.D1 && key <= ConsoleKey.D6) HealMember(party, listed, key - ConsoleKey.D1);
            else if (key >= ConsoleKey.NumPad1 && key <= ConsoleKey.NumPad6) HealMember(party, listed, key - ConsoleKey.NumPad1);
            else if (key == ConsoleKey.R) RaiseDead(party);
            else if (key == ConsoleKey.E && !isWiz) Restoration(party);
            else if (key == ConsoleKey.L || key == ConsoleKey.Escape || key == ConsoleKey.Enter) break;
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

            var command = _viewer.TryTakeCommandAsync().GetAwaiter().GetResult();
            if (command != null) return (default, command);

            System.Threading.Thread.Sleep(60);
        }
    }

    private void HealMember(Party party, List<Character> partyCharacters, int index)
    {
        if (index < 0 || index >= partyCharacters.Count)
            return;

        var target = partyCharacters[index];
        if (!Temple.NeedsHealing(target))
        {
            Console.WriteLine($"{target.Name} does not need healing.");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine($"=== HEAL {target.Name.ToUpperInvariant()} ===");
        Console.WriteLine();
        if (Temple.HealsHitPoints(target))
            Console.WriteLine($"HP {target.CurrentHitPoints}/{target.MaxHitPoints}");
        else
            Console.WriteLine($"Status: {target.Status}");
        Console.WriteLine($"Cost to heal: {Temple.CostToHeal(target)} gp");
        Console.WriteLine();
        Console.WriteLine("Heal? (Y/N)");

        while (true)
        {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Y)
                break;
            if (key == ConsoleKey.N || key == ConsoleKey.Escape)
                return;
        }

        HealParty(party, target);
    }

    private void HealParty(Party party, Character? only = null)
    {
        var partyCharacters = PartyCharacters(party);

        if (partyCharacters.Count == 0)
        {
            Console.WriteLine("No party members found.");
            Console.ReadKey(true);
            return;
        }

        var needHealing = partyCharacters.Where(Temple.NeedsHealing).ToList();
        if (only != null)
            needHealing = needHealing.Where(c => c.Name == only.Name).ToList();

        if (needHealing.Count == 0)
        {
            Console.WriteLine("No one needs healing.");
            Console.ReadKey(true);
            return;
        }

        if (Adnd.Core.Config.GameRulesProvider.Current.Rules == Adnd.Core.Config.Ruleset.Wizardry)
            Console.WriteLine("Cure costs: 100 gp x level (paralysis), 200 gp x level (stoned).");
        else
            Console.WriteLine($"Healing costs: {Temple.HealCostPerHp} gp/HP, +{Temple.CurePoisonCost} gp (poison), +{Temple.CureParalysisCost} gp (paralysis), +{Temple.CureDiseaseCost} gp (disease), +{Temple.CureFeeblemindCost} gp (feeblemind), +{Temple.RestorePetrificationCost} gp (restore from petrification).");

        var healedCount = 0;
        var skipped = new List<string>();
        var payments = new List<string>();

        foreach (var c in needHealing)
        {
            var cost = Temple.CostToHeal(c);
            if (cost <= 0)
                continue;

            // Normal case: character pays own healing.
            if (c.GoldPieces >= cost)
            {
                if (Temple.Heal(c, c, _charRepo))
                {
                    healedCount++;
                    payments.Add($"{c.Name} paid {cost} gp for own healing.");
                }
                else
                {
                    skipped.Add(c.Name);
                }

                continue;
            }

            // New behavior: ask who will pay when the target cannot afford healing.
            Console.WriteLine();
            Console.WriteLine($"{c.Name} cannot afford healing ({c.GoldPieces}/{cost} gp).");

            Console.WriteLine($"Who will pay {cost} gp for {c.Name}?");
            for (int i = 0; i < partyCharacters.Count; i++)
            {
                var payerCandidate = partyCharacters[i];
                var canAfford = payerCandidate.GoldPieces >= cost ? "" : " (not enough)";
                Console.WriteLine($"{i + 1}. {payerCandidate.Name} ({payerCandidate.GoldPieces} gp){canAfford}");
            }
            Console.WriteLine("S)hare costs");

            Console.Write("Choose # or S: ");
            // A single key answers: a digit (typed or clicked) picks the payer, S shares; anything else skips.
            Console.ClickableNumberDigits = 1;
            var payerChoice = string.Empty;
            while (true)
            {
                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.S)
                    payerChoice = "S";
                else if (keyInfo.KeyChar >= '1' && keyInfo.KeyChar <= '9' && keyInfo.KeyChar - '0' <= partyCharacters.Count)
                    payerChoice = keyInfo.KeyChar.ToString();
                else if (keyInfo.Key is ConsoleKey.Enter or ConsoleKey.Escape)
                    payerChoice = string.Empty;
                else
                    continue;

                break;
            }
            Console.ClickableNumberDigits = 0;
            Console.WriteLine(payerChoice);
            if (string.Equals(payerChoice, "S", StringComparison.OrdinalIgnoreCase))
            {
                if (Temple.HealShared(c, partyCharacters, _charRepo, out var sharedPaymentLines))
                {
                    healedCount++;
                    payments.AddRange(sharedPaymentLines);
                }
                else
                {
                    Console.WriteLine($"The party does not have enough gold to cover {cost} gp for {c.Name}.");
                    skipped.Add(c.Name);
                }

                continue;
            }

            if (!int.TryParse(payerChoice, out var payerIndex)
                || payerIndex < 1
                || payerIndex > partyCharacters.Count)
            {
                skipped.Add(c.Name);
                continue;
            }

            var payer = partyCharacters[payerIndex - 1];
            if (Temple.Heal(c, payer, _charRepo))
            {
                healedCount++;
                payments.Add($"{payer.Name} paid {cost} gp to heal {c.Name}.");
            }
            else
            {
                Console.WriteLine($"{payer.Name} does not have enough gold to cover {c.Name}'s healing.");
                skipped.Add(c.Name);
            }
        }

        Console.WriteLine($"Healed {healedCount} character(s).");

        foreach (var payment in payments)
            Console.WriteLine(payment);

        if (skipped.Count > 0)
            Console.WriteLine($"Could not afford healing: {string.Join(", ", skipped)}");

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    /// <summary>The party as characters, dropping names the roster no longer knows.</summary>
    private List<Character> PartyCharacters(Party party)
    {
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        return party.Members.Where(roster.ContainsKey).Select(name => roster[name]).ToList();
    }

    private void Restoration(Party party)
    {
        var partyCharacters = PartyCharacters(party);
        var candidates = partyCharacters.Where(Temple.CanBeRestored).ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine("No one can be helped by Restoration (a level must have been drained within 15 days, or feeblemind present).");
            Console.ReadKey(true);
            return;
        }

        Console.Clear();
        Console.WriteLine("=== RESTORATION ===");
        Console.WriteLine("The temple cleric (level 15) restores one drained level and cures feeblemind. Recipient ages 2 years.");
        Console.WriteLine();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            Console.WriteLine($"{i + 1}. {c.Name} (level {c.Level}, Cost {Temple.CostToRestore(c)} gp)");
        }

        Console.Write("Choose #: ");
        var selection = InputHelper.ReadNumber(1, candidates.Count);
        if (!selection.HasValue)
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
            return;
        }

        var target = candidates[selection.Value - 1];
        var cost = Temple.CostToRestore(target);

        Console.WriteLine();
        Console.WriteLine($"Who will pay {cost} gp?");
        for (var i = 0; i < partyCharacters.Count; i++)
        {
            var payer = partyCharacters[i];
            var canAfford = payer.GoldPieces >= cost ? "" : " (not enough)";
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
        foreach (var line in Temple.Restore(target, partyCharacters[payerSelection.Value - 1], _charRepo))
            Console.WriteLine(line);

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
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
        Console.WriteLine("S)hare costs");

        Console.Write("Choose # or S: ");
        var payerChoice = Console.ReadLine()?.Trim();
        if (string.Equals(payerChoice, "S", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            foreach (var line in Temple.RaiseShared(target, partyCharacters, _charRepo))
                Console.WriteLine(line);

            Console.WriteLine();
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
            return;
        }

        if (!int.TryParse(payerChoice, out var payerIndex)
            || payerIndex < 1
            || payerIndex > partyCharacters.Count)
        {
            Console.WriteLine("Invalid selection.");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        foreach (var line in Temple.Raise(target, partyCharacters[payerIndex - 1], _charRepo))
            Console.WriteLine(line);

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }

}
