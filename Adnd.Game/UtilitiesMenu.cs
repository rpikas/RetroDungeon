using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Data.Characters;
using Adnd.Data.Party;

namespace Adnd.Game;

/// <summary>The town's U)tilities: rename a character, reorder the party, and restart a party left OUT in the maze.</summary>
public sealed class UtilitiesMenu
{
    private const int MaxNameLength = 15;
    private const int MaxPartySize = 6;

    private readonly PartyRepository _partyRepo = new("Data/Party");
    private readonly CharacterRepository _charRepo = new("Data/Characters");

    /// <summary>
    /// Shows the menu. Returns the position of an OUT party that should be restarted in the maze, or
    /// null when the player just leaves.
    /// </summary>
    public OutPartyStart? Show()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== UTILITIES ===\n");
            Console.WriteLine("C)hange name");
            Console.WriteLine("M)ove characters");
            Console.WriteLine("R)estart an \"OUT\" party");
            Console.WriteLine("L↵eave utilities");

            var key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.C:
                    ChangeName();
                    break;
                case ConsoleKey.M:
                    MoveCharacters();
                    break;
                case ConsoleKey.R:
                    var start = RestartOutParty();
                    if (start != null)
                        return start;
                    break;
                case ConsoleKey.L:
                case ConsoleKey.Enter:
                case ConsoleKey.Escape:
                    return null;
            }
        }
    }

    private static void Pause()
    {
        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private static int? ReadIndex(string prompt, int count)
    {
        Console.Write(prompt);
        Console.ClickableNumberDigits = 2;
        try
        {
            return InputHelper.ReadNumber(1, count, 2, echoTypedCharacters: true);
        }
        finally
        {
            Console.ClickableNumberDigits = 0;
        }
    }

    private void ChangeName()
    {
        var roster = _charRepo.GetAll().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Console.Clear();
        Console.WriteLine("=== CHANGE NAME ===\n");
        if (roster.Count == 0)
        {
            Console.WriteLine("There are no characters.");
            Pause();
            return;
        }

        for (var i = 0; i < roster.Count; i++)
            Console.WriteLine($"{i + 1,-3} {roster[i].Name}");

        var pick = ReadIndex("\nRename which character #: ", roster.Count);
        if (!pick.HasValue)
            return;

        var character = roster[pick.Value - 1];
        Console.Write($"\nNew name for {character.Name} (Enter cancels): ");
        var newName = (Console.ReadLine() ?? string.Empty).Trim();
        if (newName.Length == 0)
            return;

        if (newName.Length > MaxNameLength)
        {
            newName = newName.Substring(0, MaxNameLength);
            Console.WriteLine($"(Name truncated to: {newName})");
        }

        if (!string.Equals(newName, character.Name, StringComparison.OrdinalIgnoreCase)
            && roster.Any(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("A character with that name already exists.");
            Pause();
            return;
        }

        Rename(character, newName);
        Console.WriteLine($"Renamed to {newName}.");
        Pause();
    }

    private void Rename(Character character, string newName)
    {
        var oldName = character.Name;
        var party = _partyRepo.Load();
        var partyIndex = party.Members.FindIndex(m => string.Equals(m, oldName, StringComparison.OrdinalIgnoreCase));

        // Delete first: it also strips the old name from the party file, which is put back below.
        _charRepo.Delete(oldName);
        character.Name = newName;
        _charRepo.Save(character);

        party = _partyRepo.Load();
        if (partyIndex >= 0)
        {
            party.Members.Insert(Math.Min(partyIndex, party.Members.Count), newName);
            _partyRepo.Save(party);
        }
    }

    private void MoveCharacters()
    {
        while (true)
        {
            var party = _partyRepo.Load();
            var roster = _charRepo.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var members = party.Members.Where(roster.ContainsKey).ToList();

            Console.Clear();
            Console.WriteLine("=== MOVE CHARACTERS ===\n");
            if (members.Count < 2)
            {
                Console.WriteLine("You need at least two party members to reorder them.");
                Pause();
                return;
            }

            for (var i = 0; i < members.Count; i++)
                Console.WriteLine($"{i + 1}) {members[i]}");

            var from = ReadIndex("\nMove which character # (Enter to finish): ", members.Count);
            if (!from.HasValue)
                return;

            var to = ReadIndex($"\nMove {members[from.Value - 1]} to position # (Enter cancels): ", members.Count);
            if (!to.HasValue)
                continue;

            var moved = members[from.Value - 1];
            members.RemoveAt(from.Value - 1);
            members.Insert(to.Value - 1, moved);

            // Keep any stale entries (names without a roster file) after the reordered members.
            party.Members = members.Concat(party.Members.Where(m => !roster.ContainsKey(m))).ToList();
            _partyRepo.Save(party);
        }
    }

    private OutPartyStart? RestartOutParty()
    {
        Console.Clear();
        Console.WriteLine("=== RESTART AN \"OUT\" PARTY ===\n");

        var party = _partyRepo.Load();
        var roster = _charRepo.GetAll().ToList();
        if (party.Members.Any(m => roster.Any(c => string.Equals(c.Name, m, StringComparison.OrdinalIgnoreCase))))
        {
            Console.WriteLine("Disband the current party first (the OUT party can only be restarted with an empty party).");
            Pause();
            return null;
        }

        var groups = roster
            .Where(c => c.HasStatus(CharacterStatus.Out)
                        && !c.HasStatus(CharacterStatus.Dead)
                        && !c.HasStatus(CharacterStatus.Ashes)
                        && !c.HasStatus(CharacterStatus.Lost))
            .GroupBy(c => (c.DungeonLevel, c.DungeonCellX, c.DungeonCellY))
            .OrderBy(g => g.Key.DungeonLevel).ThenBy(g => g.Key.DungeonCellX).ThenBy(g => g.Key.DungeonCellY)
            .ToList();

        if (groups.Count == 0)
        {
            Console.WriteLine("There is no party left OUT.");
            Pause();
            return null;
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var names = string.Join(", ", g.Select(c => c.Name).Take(MaxPartySize));
            Console.WriteLine($"{i + 1}) Level {g.Key.DungeonLevel} at ({g.Key.DungeonCellX},{g.Key.DungeonCellY}): {names}");
        }

        var pick = ReadIndex("\nRestart which party #: ", groups.Count);
        if (!pick.HasValue)
            return null;

        var chosen = groups[pick.Value - 1];
        var restarted = chosen.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(MaxPartySize).ToList();
        foreach (var member in restarted)
        {
            member.RemoveStatus(CharacterStatus.Out);
            if (member.CurrentHitPoints <= 0)
                member.CurrentHitPoints = 1;
            _charRepo.Save(member);
        }

        party.Members = restarted.Select(c => c.Name).ToList();
        party.CurrentShopperIndex = -1;
        _partyRepo.Save(party);

        return new OutPartyStart(chosen.Key.DungeonLevel, chosen.Key.DungeonCellX, chosen.Key.DungeonCellY);
    }
}

/// <summary>Where a restarted OUT party stands in the dungeon.</summary>
public sealed record OutPartyStart(int Level, int X, int Y);
