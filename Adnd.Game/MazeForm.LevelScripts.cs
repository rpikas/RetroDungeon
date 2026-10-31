using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Dungeon;
using Adnd.Core.Monsters;
using Adnd.Data.Characters;
using Adnd.Data.Dungeon;
using Adnd.Data.Encounters;
using Adnd.Data.Monsters;
using Adnd.Data.Party;
using Adnd.Game.Combat;
using Adnd.Game.Viewer;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using static Adnd.Core.Dices.DiceFormulas;

namespace Adnd.Game;

// Scripted squares: messages, pickups, Level 1, 9 and 10 scripts, Werdna, pits and turn tables.
public sealed partial class MazeForm
{
    // Square messages plus the level 4 scripted squares. Returns true when the square forces an encounter.
    private bool ProcessSquareMessageStep()
    {
        if (!_thinWalls)
            return false;

        if (DungeonMessages.TryGetValue((_currentDungeonLevel, _position), out var message))
            SayOnBoth("Message", message);

        if (DungeonLookAroundItems.TryGetValue((_currentDungeonLevel, _position), out var lookAround) && !PartyHasItem(lookAround.Item))
        {
            SayOnBoth("Message", lookAround.Message);
            if (AskOneLineOnBoth("Look around", "WILL YOU SEARCH (Y/N) ?", "Look around?") == DialogResult.Yes)
                FindLookAroundItem(lookAround.Item);
        }

        if (_currentDungeonLevel != 4)
            return false;

        if (_position == Level4RibbonRoom && !PartyHasItem(BlueRibbon))
        {
            SayOnBoth("Message", Lines("AS THE PARTY ENTERS THE ROOM, THE DOOR SLAMS SHUT, GLOWS ORANGE AND VANISHES!",
                "A DOOR APPEARS TO THE RIGHT, A VOICE, COMING FROM THE CEILING ADDRESSES YOU:",
                "\"CONGRATULATIONS, BRAVE ADVENTURERS!",
                "TODAY YOU HAVE PROVED YOURSELF TO ME AND ARE NOW READY TO START YOUR QUEST!",
                "YEARS AGO, MY AMULET WAS STOLEN BY THE EVIL WIZARD WERDNA.",
                "WERDNA HIDES IN THE MAZE SOMEWHERE BELOW US.",
                "FIND HIM, SLAY HIM AND RETURN TO ME THE AMULET!",
                "TO AID YOU IN YOUR QUEST, I WILL GIVE YOU A BLUE RIBBON,",
                "WHICH MAY BE USED TO ENTER THE ELEVATOR ON THIS FLOOR.",
                "GO NOW - AND MAY THE GODS GUIDE YOU!\""));
            var ribbonHolder = GivePartyItem(BlueRibbon, "Given by Trebor. Makes the elevator on this floor accessible.");
            if (ribbonHolder is not null)
                SayOnBoth("Blue Ribbon", $"{ribbonHolder.ToUpperInvariant()} RECEIVES THE BLUE RIBBON!");
        }
        else if (_position == Level4SleepRoom && !PartyHasItem(BlueRibbon))
        {
            SayOnBoth("Message", Lines("YOU ALL FALL ASLEEP! LATER, EVERYONE WAKES UP, FEELING SICK AND WEAK.",
                "YOU ARE BACK OUTSIDE THE DOOR YOU TRIED TO ENTER."));
            _position = Level4SleepRoomOutside;
            UpdateLighting();
            Invalidate();
        }

        return _position == new Point(15, 10);
    }

    private string? GivePartyItem(string name, string description)
    {
        var receiver = GetViewerParty().FirstOrDefault();
        if (receiver is null)
            return null;

        var item = new Adnd.Data.Items.ItemRepository("Data/Items").LoadAll()
            .FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? new Adnd.Core.Items.Item
            {
                Name = name,
                Type = Adnd.Core.Items.ItemType.Misc,
                Weight = 0,
                IsShopBuyable = false,
                Description = description
            };
        receiver.Inventory.Add(item);
        _characterRepository.Save(receiver);
        return receiver.Name;
    }

    private bool PartyHasItem(string itemName)
        => GetViewerParty().Any(c => c.Inventory.Any(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase)));

    private void FindLookAroundItem(string itemName)
    {
        var finder = GivePartyItem(itemName, $"Found on level {_currentDungeonLevel}.");
        SayOnBoth("Key", finder is null
            ? $"THE PARTY FINDS THE {itemName.ToUpperInvariant()}!"
            : $"{finder.ToUpperInvariant()} FINDS THE {itemName.ToUpperInvariant()}!");
    }

    // Level 1 scripted squares: the wizard who throws the party out, and Murphy's shrine with its ghosts.
    // Returns true when the party was sent back to town.
    private bool ProcessLevelOneScriptStep()
    {
        if (!_thinWalls || _currentDungeonLevel != 1)
            return false;

        if (_position == Level1WizardRoom)
        {
            LeaveDungeonForTown();
            return true;
        }

        if (_position == Level1MurphyShrine && LoadEncounterParty().Count > 0)
        {
            _combatCoordinator.ForceMonstersInLair = true;
            _combatCoordinator.CannotRun = true;
            try
            {
                var outcome = _combatCoordinator.StartEncounter(
                    this,
                    "Murphy's Ghost",
                    ResolveEncounterGroupCount("Murphy's Ghost", null),
                    LoadEncounterParty(),
                    _characterRepository,
                    _currentDungeonLevel,
                    dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
                if (outcome == CombatOutcome.Defeat)
                    HandlePartyDefeatAtCurrentCell();
            }
            finally
            {
                _combatCoordinator.ForceMonstersInLair = false;
                _combatCoordinator.CannotRun = false;
            }

            PublishToViewer();
        }

        return false;
    }

    private static readonly Point Level10WerdnaLair = new(3, 17);

    private bool _werdnaDefeated;

    private bool _werdnaRewardGiven;

    // Werdna waits behind the door at E17-N3 with one or two groups of 1-4 Vampires, Liches or Demon Princes.
    // The fight happens in the monsters' lair and can't be fled. Returns true when the party left the maze.
    private bool ProcessWerdnaStep()
    {
        if (!_thinWalls || _currentDungeonLevel != 10 || _position != Level10WerdnaLair
            || _werdnaDefeated || _random.Next(1, 101) > 90 || LoadEncounterParty().Count == 0)
            return false;

        string[] escorts = ["Vampire", "Lich", "Orcus", "Demogorgon", "Asmodeus"];
        var groups = new List<(string name, int count)> { ("WERDNA", 1) };
        var groupCount = _random.Next(1, 3);
        for (var i = 0; i < groupCount; i++)
            groups.Add((escorts[_random.Next(escorts.Length)], _random.Next(1, 5)));

        SayOnBoth("Werdna", "WERDNA, THE EVIL WIZARD, TURNS TO FACE YOU. HIS GUARDIANS STIR!");
        _combatCoordinator.ForceMonstersInLair = true;
        _combatCoordinator.CannotRun = true;
        CombatOutcome outcome;
        try
        {
            outcome = _combatCoordinator.StartEncounterWithGroupCounts(
                this,
                groups,
                LoadEncounterParty(),
                _characterRepository,
                _currentDungeonLevel,
                dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        }
        finally
        {
            _combatCoordinator.ForceMonstersInLair = false;
            _combatCoordinator.CannotRun = false;
        }

        PublishToViewer();
        if (outcome == CombatOutcome.Defeat)
        {
            HandlePartyDefeatAtCurrentCell();
            return false;
        }

        if (outcome != CombatOutcome.Victory)
            return false;

        _werdnaDefeated = true;
        var finder = GivePartyItem("Amulet of Werdna", "The Mad Overlord's amulet.");
        SayOnBoth("Amulet", (finder ?? "THE PARTY").ToUpperInvariant() + " TAKES THE AMULET OF WERDNA! IT IS CURSED - NEVER WEAR IT.");
        if (AskOnBoth("Amulet", "The amulet can cast MALOR. Use it to return to the castle now?") != DialogResult.Yes)
            return false;

        LeaveDungeonForTown();
        return true;
    }

    // Returning the amulet to the surface earns 50,000 XP each and 50,000 GP for the party.
    private void AwardWerdnaAmuletReward()
    {
        if (_werdnaRewardGiven || !PartyHasItem("Amulet of Werdna"))
            return;

        _werdnaRewardGiven = true;
        foreach (var holder in GetViewerParty())
        {
            if (holder.Inventory.RemoveAll(i => string.Equals(i.Name, "Amulet of Werdna", StringComparison.OrdinalIgnoreCase)) > 0)
                _characterRepository.Save(holder);
        }
        var levelUp = new Adnd.Core.Characters.Progression.LevelUpService();
        var spells = new Adnd.Data.Spells.SpellRepository("Data/Spells").LoadAll();
        var members = GetViewerParty();
        foreach (var member in members)
        {
            levelUp.ApplyExperienceAndAutoLevel(member, 50000, spells);
            _characterRepository.Save(member);
        }

        if (members.FirstOrDefault() is { } first)
        {
            first.GoldPieces += 50000;
            _characterRepository.Save(first);
        }

        SayOnBoth("Congratulations", Lines("* * CONGRATULATIONS * *", "YOU HAVE RECOVERED THE AMULET", "AND PASSED THE OVERLORD'S TEST!",
            "THE MAD OVERLORD HAS GRANTED YOU 50,000 GP AND EP!", "HE HAS ALSO INDUCTED YOU INTO HIS ELITE GUARD.",
            "WEAR THE CHEVRON OF RANK (>) WITH HONOR.", "THE AMULET IS RETURNED TO TREBOR."));
    }

    // Level 1's up stairs lead out to town; the others are on a floor that has a neighbour built.
    private StairsKind StairsAt(Point cell)
    {
        if (_currentDungeonLevel == 1 && cell.X == 0 && cell.Y == 0)
            return StairsKind.Up;

        if (!_thinWalls || !DungeonStairs.TryGetValue(_currentDungeonLevel, out var stairs))
            return StairsKind.None;

        // The Dance Hall arrival on Level 8 has no way back up.
        if (cell == stairs.Up)
            return _currentDungeonLevel == 8 ? StairsKind.None : StairsKind.Up;

        return stairs.Down is { } down && cell == down && DungeonStairs.ContainsKey(_currentDungeonLevel + 1)
            ? StairsKind.Down
            : StairsKind.None;
    }

    // Takes the stairs to the neighbouring floor and lands on the stairs there.
    private void TakeStairsToLevel(int level)
    {
        var arrivedGoingDown = level > _currentDungeonLevel;
        _currentDungeonLevel = level;
        BuildMazeForLevel(level);
        LoadEncounterLocationsForLevel(level);
        _position = arrivedGoingDown ? DungeonStairs[level].Up : DungeonStairs[level].Down!.Value;
        UpdateLighting();
        Invalidate();
        PublishToViewer();
    }

    // A turn table spins the party to face a random direction.
    private void ProcessTurnTableStep()
    {
        if (!_thinWalls || !DungeonTurnTables.TryGetValue(_currentDungeonLevel, out var tables) || !tables.Contains(_position))
            return;

        _direction = (Direction)_random.Next(Enum.GetValues<Direction>().Length);
        Invalidate();
        PublishToViewer();
        SayOnBoth("Turn table", "THE TURN TABLE SPINS THE PARTY AROUND!");
    }

    // Falling into a pit costs every party member 2d6 hit points (1d8 on level 2).
    // Level 9: warp zones send the party to Floor 1, and the chute drops it to Floor 10. Returns true if the party left the floor.
    private bool ProcessLevelNineStep()
    {
        if (_thinWalls && _currentDungeonLevel == 10 && _position == new Point(0, 1))
        {
            SayOnBoth("Teleport", "A strange force teleports the party back to the castle!");
            LeaveDungeonForTown();
            return true;
        }

        if (!_thinWalls || _currentDungeonLevel != 9)
            return false;

        if (Level9WarpZones[_position.X][_position.Y] == 'W')
        {
            SayOnBoth("Teleport", "A strange force teleports the party all the way back to Floor 1!");
            JumpToDungeonLevel(1, new Point(0, 0));
            return true;
        }

        if (_position == Level9Chute)
        {
            if (_dungeonLayouts.TryLoad(10) is null)
            {
                SayOnBoth("Chute", "YOU STEP ON A CHUTE, BUT THE WAY DOWN IS BLOCKED.");
                return false;
            }

            SayOnBoth("Chute", "YOU FALL DOWN A CHUTE TO THE FLOOR BELOW!");
            JumpToDungeonLevel(10, new Point(0, 0));
            return true;
        }

        return false;
    }

    private void JumpToDungeonLevel(int level, Point position)
    {
        _currentDungeonLevel = level;
        BuildMazeForLevel(level);
        LoadEncounterLocationsForLevel(level);
        _position = position;
        UpdateLighting();
        Invalidate();
        PublishToViewer();
    }

    private void ProcessPitStep()
    {
        if (!_thinWalls || !DungeonPits.TryGetValue(_currentDungeonLevel, out var pits) || !pits.Contains(_position))
            return;

        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAllCached().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string> { "YOU FALL INTO A PIT!" };

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c) || c.CurrentHitPoints <= 0 || c.HasStatus(CharacterStatus.Dead))
                continue;

            var damage = _currentDungeonLevel == 2 ? _random.Next(1, 9) : _random.Next(1, 7) + _random.Next(1, 7);
            var before = c.CurrentHitPoints;
            c.CurrentHitPoints = Math.Max(0, before - damage);
            messages.Add($"{c.Name} takes {before - c.CurrentHitPoints} damage. HP {before}->{c.CurrentHitPoints}.");

            if (c.CurrentHitPoints <= 0)
            {
                c.AddStatus(CharacterStatus.Dead);
                messages.Add($"{c.Name} dies.");
            }

            _characterRepository.Save(c);
        }

        SayOnBoth("Pit", string.Join(Environment.NewLine, messages));
    }
}
