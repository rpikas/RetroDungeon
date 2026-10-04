using System;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Diagnostics;
using Adnd.Data.Characters;
using Adnd.Data.Party;

namespace Adnd.Game;

public sealed class AdventuresInnMenu
{
    private sealed record RoomOption(char Key, string Name, int CostPerWeek, int HealPerDay);
    private enum PaymentMode
    {
        Individual,
        PooledPartyGold
    }

    private static readonly RoomOption[] Rooms =
    [
        new('A', "THE STABLES", 0, 1),
        new('B', "A COT", 10, 2),
        new('C', "ECONOMY ROOMS", 50, 6),
        new('D', "MERCHANT SUITES", 200, 20),
        new('E', "THE ROYAL SUITE", 500, 40)
    ];

    private readonly PartyRepository _partyRepository = new("Data/Party");
    private readonly CharacterRepository _characterRepository = new("Data/Characters");

    public void Show()
    {
        while (true)
        {
            var party = _partyRepository.Load();
            var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var members = party.Members
                .Where(name => roster.ContainsKey(name))
                .Select(name => roster[name])
                .ToList();

            Console.Clear();
            Console.WriteLine("=== ADVENTURES INN ===");

            if (members.Count == 0)
            {
                Console.WriteLine("No party members are available.");
                Console.WriteLine("Press any key...");
                Console.ReadKey(true);
                return;
            }

            Console.WriteLine("Who wants to rest?");
            for (int i = 0; i < members.Count; i++)
            {
                var c = members[i];
                Console.WriteLine($"{i + 1}) {c.Name,-15} HP {c.CurrentHitPoints}/{c.MaxHitPoints}, GP {c.GoldPieces}, Age {c.Age}y {c.AgeDays}d");
            }

            Console.WriteLine("A)ll");
            Console.WriteLine("L)eave the inn without resting");
            Console.Write("Choice: ");

            var whoKey = Console.ReadKey(true);
            var whoChar = char.ToUpperInvariant(whoKey.KeyChar);

            if (whoChar == 'L' || whoKey.Key == ConsoleKey.Escape || whoKey.Key == ConsoleKey.Enter)
                return;

            List<Character> selected;
            if (whoChar == 'A')
            {
                selected = members;
            }
            else if (char.IsDigit(whoChar))
            {
                var idx = (whoChar - '0') - 1;
                if (idx < 0 || idx >= members.Count)
                    continue;

                selected = [members[idx]];
            }
            else
            {
                continue;
            }

            var paymentMode = PaymentMode.Individual;
            var selectedIsSingleMember = selected.Count == 1;
            var selectedIsEntireParty = selected.Count == members.Count;

            if (selectedIsSingleMember)
            {
                var action = PromptAlternative(showPoolGold: true, showDistributeGold: false);
                if (action == 'L')
                    return;

                if (action == 'P')
                    paymentMode = PaymentMode.PooledPartyGold;
            }

            RoomOption? room;
            if (selectedIsEntireParty)
            {
                room = PromptRoomOrSplitForWholeParty(members);
                if (room == null)
                    return;
            }
            else
            {
                room = PromptRoom();
                if (room == null)
                    return;
            }

            ApplyRest(selected, members, room, paymentMode);

            Console.WriteLine();
            Console.WriteLine("Rest completed. Press any key...");
            Console.ReadKey(true);
        }
    }

    private static char PromptAlternative(bool showPoolGold, bool showDistributeGold)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== ADVENTURES INN ===");
            Console.WriteLine("Alternative options:");
            if (showPoolGold)
                Console.WriteLine("P)ool gold (selected character can pay from party gold)");
            if (showDistributeGold)
                Console.WriteLine("S)plit gold among party members");
            Console.WriteLine("R)oom selection");
            Console.WriteLine("L)eave the inn without resting");
            Console.Write("Choice: ");

            var key = Console.ReadKey(true);
            var ch = char.ToUpperInvariant(key.KeyChar);

            if (ch == 'L' || key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                return 'L';

            if (ch == 'R')
                return 'R';

            if (showPoolGold && ch == 'P')
                return 'P';

            if (showDistributeGold && ch == 'S')
                return 'S';
        }
    }

    private RoomOption? PromptRoomOrSplitForWholeParty(List<Character> members)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== ADVENTURES INN ===");
            Console.WriteLine("Choose room for the whole party:");
            Console.WriteLine("A)  THE STABLES (FREE!) Heals 1 HP per day.");
            Console.WriteLine("B)  A COT.  10 GP/WEEK Heals 2 HP per day.");
            Console.WriteLine("C)  ECONOMY ROOMS.  50 GP/WEEK Heals 6 HP per day.");
            Console.WriteLine("D)  MERCHANT SUITES.  200 GP/WEEK Heals 20 HP per day.");
            Console.WriteLine("E)  THE ROYAL SUITE.  500 GP/WEEK Heals 40 HP per day.");
            Console.WriteLine("S)plit gold among party members");
            Console.WriteLine("L)eave the inn without resting.");
            Console.Write("Choice: ");

            var key = Console.ReadKey(true);
            var ch = char.ToUpperInvariant(key.KeyChar);
            if (ch == 'L' || key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                return null;

            if (ch == 'S')
            {
                DistributeGoldEvenly(members);
                Console.WriteLine("Press any key...");
                Console.ReadKey(true);
                continue;
            }

            var room = Rooms.FirstOrDefault(r => r.Key == ch);
            if (room != null)
                return room;
        }
    }

    private static RoomOption? PromptRoom()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== ADVENTURES INN ===");
            Console.WriteLine("Choose room:");
            Console.WriteLine("A)  THE STABLES (FREE!) Heals 1 HP per day.");
            Console.WriteLine("B)  A COT.  10 GP/WEEK Heals 2 HP per day.");
            Console.WriteLine("C)  ECONOMY ROOMS.  50 GP/WEEK Heals 6 HP per day.");
            Console.WriteLine("D)  MERCHANT SUITES.  200 GP/WEEK Heals 20 HP per day.");
            Console.WriteLine("E)  THE ROYAL SUITE.  500 GP/WEEK Heals 40 HP per day.");
            Console.WriteLine("L)eave the inn without resting.");
            Console.Write("Room: ");

            var key = Console.ReadKey(true);
            var ch = char.ToUpperInvariant(key.KeyChar);
            if (ch == 'L' || key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                return null;

            var room = Rooms.FirstOrDefault(r => r.Key == ch);
            if (room != null)
                return room;
        }
    }

    private void ApplyRest(List<Character> selectedMembers, List<Character> partyMembers, RoomOption room, PaymentMode paymentMode)
    {
        foreach (var c in selectedMembers)
        {
            var hpMissing = Math.Max(0, c.MaxHitPoints - c.CurrentHitPoints);
            var healPerWeek = Math.Max(1, room.HealPerDay * 7);
            var weeksNeeded = hpMissing <= 0 ? 0 : (int)Math.Ceiling(hpMissing / (double)healPerWeek);
            var totalCost = room.CostPerWeek * weeksNeeded;
            var usePooledPartyGold = paymentMode == PaymentMode.PooledPartyGold && selectedMembers.Count == 1;

            if (usePooledPartyGold)
            {
                var totalPartyGold = partyMembers.Sum(m => Math.Max(0, m.GoldPieces));
                if (totalCost > totalPartyGold)
                {
                    Console.WriteLine($"Party cannot afford {room.Name} for {c.Name}: needs {totalCost} gp, has {totalPartyGold} gp total.");
                    RuleApplicationInfo.Publish($"Adventures Inn: {c.Name} selected {room.Name} with pooled gold. Cost {totalCost} gp, but party could not afford it (has {totalPartyGold} gp total).");
                    continue;
                }

                var beforePartyGold = totalPartyGold;
                DeductGoldFromParty(partyMembers, totalCost);
                var afterPartyGold = partyMembers.Sum(m => Math.Max(0, m.GoldPieces));

                var beforeHp = c.CurrentHitPoints;
                var beforeAge = c.Age;
                var beforeAgeDays = c.AgeDays;

                c.CurrentHitPoints = Math.Min(c.MaxHitPoints, c.CurrentHitPoints + weeksNeeded * healPerWeek);

                var daysAgedPooled = weeksNeeded * 7;
                c.AdvanceAgeByDays(daysAgedPooled);

                _characterRepository.Save(c);

                Console.WriteLine($"{c.Name}: {room.Name}, {weeksNeeded} week(s), cost {totalCost} gp (pooled), HP {beforeHp}->{c.CurrentHitPoints}, age {beforeAge}y {beforeAgeDays}d -> {c.Age}y {c.AgeDays}d.");

                RuleApplicationInfo.Publish(
                    $"Adventures Inn: {c.Name} selected {room.Name}. Cost {totalCost} gp pooled from party ({room.CostPerWeek} gp/week x {weeksNeeded} week(s)). " +
                    $"Healing rate {room.HealPerDay} HP/day. HP {beforeHp}->{c.CurrentHitPoints}. Party GP {beforePartyGold}->{afterPartyGold}. " +
                    $"Age advanced by {daysAgedPooled} day(s): {beforeAge}y {beforeAgeDays}d -> {c.Age}y {c.AgeDays}d.");
                continue;
            }

            if (totalCost > c.GoldPieces)
            {
                Console.WriteLine($"{c.Name} cannot afford {room.Name}: needs {totalCost} gp, has {c.GoldPieces} gp.");
                RuleApplicationInfo.Publish($"Adventures Inn: {c.Name} selected {room.Name}. Cost {totalCost} gp, but could not afford it (has {c.GoldPieces} gp).");
                continue;
            }

            var beforeHpSelf = c.CurrentHitPoints;
            var beforeGoldSelf = c.GoldPieces;
            var beforeAgeSelf = c.Age;
            var beforeAgeDaysSelf = c.AgeDays;

            c.GoldPieces -= totalCost;
            c.CurrentHitPoints = Math.Min(c.MaxHitPoints, c.CurrentHitPoints + weeksNeeded * healPerWeek);

            var daysAged = weeksNeeded * 7;
            c.AdvanceAgeByDays(daysAged);

            _characterRepository.Save(c);

            Console.WriteLine($"{c.Name}: {room.Name}, {weeksNeeded} week(s), cost {totalCost} gp, HP {beforeHpSelf}->{c.CurrentHitPoints}, age {beforeAgeSelf}y {beforeAgeDaysSelf}d -> {c.Age}y {c.AgeDays}d.");

            RuleApplicationInfo.Publish(
                $"Adventures Inn: {c.Name} selected {room.Name}. Cost {totalCost} gp ({room.CostPerWeek} gp/week x {weeksNeeded} week(s)). " +
                $"Healing rate {room.HealPerDay} HP/day. HP {beforeHpSelf}->{c.CurrentHitPoints}. GP {beforeGoldSelf}->{c.GoldPieces}. " +
                $"Age advanced by {daysAged} day(s): {beforeAgeSelf}y {beforeAgeDaysSelf}d -> {c.Age}y {c.AgeDays}d.");
        }
    }

    private void DistributeGoldEvenly(List<Character> partyMembers)
    {
        if (partyMembers.Count == 0)
            return;

        var totalGold = partyMembers.Sum(m => Math.Max(0, m.GoldPieces));
        var each = totalGold / partyMembers.Count;
        var remainder = totalGold % partyMembers.Count;

        for (int i = 0; i < partyMembers.Count; i++)
        {
            partyMembers[i].GoldPieces = each + (i < remainder ? 1 : 0);
            _characterRepository.Save(partyMembers[i]);
        }

        Console.WriteLine($"Party gold distributed evenly: total {totalGold} gp, each gets {each} gp{(remainder > 0 ? " (+1 gp to first members for remainder)" : string.Empty)}.");
        RuleApplicationInfo.Publish($"Adventures Inn: distributed party gold evenly. Total {totalGold} gp across {partyMembers.Count} member(s) => {each} gp each with {remainder} gp remainder distributed.");
    }

    private void DeductGoldFromParty(List<Character> partyMembers, int totalCost)
    {
        var remaining = Math.Max(0, totalCost);
        foreach (var member in partyMembers.OrderByDescending(m => m.GoldPieces))
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(Math.Max(0, member.GoldPieces), remaining);
            member.GoldPieces -= take;
            remaining -= take;
            _characterRepository.Save(member);
        }
    }
}
