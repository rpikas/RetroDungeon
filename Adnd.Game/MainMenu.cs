using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Game.Viewer;
using Adnd.Core.Config;
using Adnd.Core.Spells;
using Adnd.Data.Characters;
using Adnd.Data.Party;
using Adnd.Data.Spells;

namespace Adnd.Game;

public class MainMenu
{
    private readonly CityMenu _cityMenu = new();
    private readonly PartyMenu _partyMenu = new();
    private readonly TempleMenu _templeMenu = new();
    private readonly ShopMenu _shopMenu = new();
    private readonly DungeonMenu _dungeonMenu = new();
    private readonly SettingsMenu _settingsMenu = new();
    private readonly PartyRepository _partyRepo = new("Data/Party");
    private readonly CharacterRepository _charRepo = new("Data/Characters");
    private readonly SpellRepository _spellRepo = new("Data/Spells");
    private readonly TabletopViewerBridge _viewer = new();

    /// <summary>
    /// The places the party can stand, in the order walking on takes them: out from the gate, along the
    /// row, and back to the gate. The ids are the shared vocabulary -- where each one SITS on the table is
    /// the viewer's business, but which ones exist is the game's.
    /// </summary>
    private static readonly string[] TownWalk = { "EdgeOfTown", "TrainingGrounds", "Tavern", "Temple", "Shop" };

    private static readonly Dictionary<string, string> PlaceNames = new()
    {
        ["EdgeOfTown"] = "the edge of town",
        ["TrainingGrounds"] = "the training ground",
        ["Tavern"] = "Gilgamash Tavern",
        ["Temple"] = "the Church of Chant",
        ["Shop"] = "Boltac's Trading Post",
    };

    /// <summary>The place's name for reading, falling back to its id rather than throwing: an unnamed
    /// place should look wrong on screen, not take the hub menu down with it.</summary>
    private static string PlaceName(string id) =>
        PlaceNames.TryGetValue(id, out var name) ? name : id;

    /// <summary>
    /// Where the party is standing on the town board.
    ///
    /// Remembered rather than reset to the gate on every pass, because it is now somewhere they WALK. Come
    /// out of the temple and they are still outside the temple; the old code republished the gate on every
    /// pass, which with figures that walk would march them back across the square each time a menu closed.
    /// </summary>
    private string _standing = TownWalk[0];

    public void Show()
    {
        while (true)
        {
            RestoreDailySpellPointsInTown();

            // Back at the hub: put the party on the town board, where they last stood. Publishing on every
            // pass through the loop keeps the table right after any location returns, with no per-menu hooks.
            PublishTown(_standing);

            Console.Clear();
            Console.WriteLine("=== WELCOME TO THE CITY OF MYTHGAR ===\n");
            Console.WriteLine($"The party is at {PlaceName(_standing)}.\n");
            Console.WriteLine("N)ext place  (or click Next on the table)");
            Console.WriteLine("T)raining Ground");
            Console.WriteLine("G)ilgamash Tavern");
            Console.WriteLine("C)hurch of Chant");
            Console.WriteLine("B)oltac's Trading Post");
            Console.WriteLine("M)aze");
            Console.WriteLine("S)ettings");
            Console.WriteLine("L<-eave");

            var (key, command) = AwaitChoice();

            // A click on the table and a key at the console arrive as the same intention, and land in the
            // same code. Anything else the viewer sends is dropped here exactly as the pump drops a word it
            // does not know.
            if (command != null)
            {
                if (command == "next") WalkOn();
                else if (command.StartsWith("go:", StringComparison.Ordinal)) GoInto(command.Substring(3));
                else if (command == "enter") EnterStanding();
                else if (command == "maze" && HasPartyMembers()) EnterMaze();
                continue;
            }

            switch (key)
            {
                case ConsoleKey.N:
                    WalkOn();
                    break;

                case ConsoleKey.T:
                    Enter("TrainingGrounds");
                    _cityMenu.Show();
                    break;

                case ConsoleKey.G:
                    Enter("Tavern");
                    _partyMenu.Show();
                    break;

                case ConsoleKey.C:
                    if (!HasPartyMembers())
                    {
                        ShowPartyRequiredMessage();
                        break;
                    }

                    Enter("Temple");
                    _templeMenu.Show();
                    break;

                case ConsoleKey.B:
                    if (!HasPartyMembers())
                    {
                        ShowPartyRequiredMessage();
                        break;
                    }

                    Enter("Shop");
                    _shopMenu.Show(0,10,true);
                    break;

                case ConsoleKey.M:
                    if (!HasPartyMembers())
                    {
                        ShowPartyRequiredMessage();
                        break;
                    }

                    EnterMaze();
                    break;

                case ConsoleKey.S:
                    _settingsMenu.Show();
                    break;

                case ConsoleKey.L:
                    return;
                case ConsoleKey.Enter:
                    return;
            }
        }
    }

    /// <summary>
    /// Waits for a key at the console or a command from the table, whichever comes first.
    ///
    /// Polled rather than blocked on <see cref="Console.ReadKey"/>, which is the whole reason the table can
    /// be used at all here: a blocking read means a click is not even looked at until someone touches the
    /// keyboard. The console keeps working exactly as before, so a viewer that is closed, crashed or never
    /// started cannot strand anyone in this menu.
    ///
    /// Safe to take commands here because no pump is running at the hub -- see the warning on
    /// <see cref="TabletopViewerBridge.TryTakeCommand"/>. The maze and a fight own the queue while they
    /// are up, and this loop is not running then.
    /// </summary>
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

    /// <summary>On to the next place, and let the table walk them there.</summary>
    private void WalkOn()
    {
        var at = Array.IndexOf(TownWalk, _standing);
        _standing = TownWalk[(at + 1) % TownWalk.Length];   // -1 wraps to 0, which is the gate
    }

    /// <summary>Go inside somewhere: they are standing there now, and stay there when it closes.</summary>
    private void Enter(string locationId)
    {
        _standing = locationId;
        PublishTown(_standing);
    }

    /// <summary>
    /// Clicking a building walks the party there AND takes them inside, in one go.
    ///
    /// It used to only walk them over, with a separate button for the door. That was one press too many for a
    /// question nobody was really being asked: there is no reason to walk to Boltac's except to go into Boltac's,
    /// and the pads are destinations rather than places to stand about on. The door button stays for the place
    /// they are ALREADY standing at, which is how they get back in after coming out.
    ///
    /// Only somewhere that exists: the viewer and the game ship separately, and a board offering a building this
    /// build has never heard of should do nothing rather than strand the party in a place with no menu behind it.
    /// </summary>
    private void GoInto(string locationId)
    {
        if (Array.IndexOf(TownWalk, locationId) < 0)
            return;

        _standing = locationId;
        EnterStanding();
    }

    /// <summary>
    /// Through the door of wherever they are standing -- the table's equivalent of the hub's letter keys.
    ///
    /// Deliberately the same calls the keys make, including the party check, so the table cannot get into a
    /// place the keyboard would have refused. The gate leads down, which is what the gate is for.
    /// </summary>
    private void EnterStanding()
    {
        switch (_standing)
        {
            case "TrainingGrounds":
                Enter("TrainingGrounds");
                _cityMenu.Show();
                break;

            case "Tavern":
                Enter("Tavern");
                _partyMenu.Show();
                break;

            case "Temple":
                if (!HasPartyMembers()) { ShowPartyRequiredMessage(); break; }
                Enter("Temple");
                _templeMenu.Show();
                break;

            case "Shop":
                if (!HasPartyMembers()) { ShowPartyRequiredMessage(); break; }
                Enter("Shop");

                // Without the console's "who is shopping?" question, which is a keyboard-only prompt and would
                // have stopped a table-driven entry dead before the counter was ever published. The table asks
                // it better anyway: every other member wears a ring at the counter, so handing the purse over is
                // a click on the person taking it.
                _shopMenu.Show(0, 10, selectShopper: false);
                break;

            case "EdgeOfTown":
                if (!HasPartyMembers()) { ShowPartyRequiredMessage(); break; }
                EnterMaze();
                break;
        }
    }

    /// <summary>
    /// Down into the maze. Offered from anywhere in town, as the hub's M key always has been, but they leave
    /// from the gate -- that is where the stair is, and it is where they come back up to.
    /// </summary>
    private void EnterMaze()
    {
        Enter("EdgeOfTown");

        ApplyEarSeekerDungeonEntryDeaths();
        _dungeonMenu.Show();

        // A new day starts only after the party leaves the maze.
        var party = _partyRepo.Load();
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var member) || !member.IsPaladin())
                continue;

            if (!member.LayOnHandsUsedToday)
                continue;

            member.LayOnHandsUsedToday = false;
            _charRepo.Save(member);
        }
    }

    private void ApplyEarSeekerDungeonEntryDeaths()
    {
        var party = _partyRepo.Load();
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var member))
                continue;

            if (!member.EarSeekerDeathOnNextDungeonEntry
                || member.HasStatus(CharacterStatus.Dead)
                || member.HasStatus(CharacterStatus.Ashes)
                || member.HasStatus(CharacterStatus.Lost)
                || member.CurrentHitPoints <= 0)
            {
                continue;
            }

            member.CurrentHitPoints = 0;
            member.AddStatus(CharacterStatus.Dead);
            member.EarSeekerDeathOnNextDungeonEntry = false;
            _charRepo.Save(member);
            messages.Add($"{member.Name} dies from untreated Ear Seeker disease when entering the dungeon.");
        }

        if (messages.Count > 0)
        {
            ViewerMessage.Show(null, "Ear Seeker Disease", string.Join(Environment.NewLine, messages));
        }
    }

    /// <summary>
    /// Show the party at a place in town. Silent no-op when no viewer is listening, and a missing
    /// roster entry is skipped rather than guessed at.
    /// </summary>
    private void PublishTown(string locationId)
    {
        var party = _partyRepo.Load();
        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<Character>();
        foreach (var name in party.Members)
        {
            if (roster.TryGetValue(name, out var c))
                ordered.Add(c);
        }

        // Stocked from INSIDE the shop only. Putting the stock out from anywhere in town made the stall look
        // open from the square, but it meant the whole of Boltac's inventory was on the table wherever the
        // party happened to be standing -- and a stall's worth of goods is a lot of table for scenery. Bare
        // shelves seen from outside are the cheaper of the two prices.
        var wares = locationId == "Shop" ? _shopMenu.DisplayWares() : null;

        _viewer.PublishTown(locationId, ordered, TownPrompt(locationId, ordered.Count > 0), wares);
    }

    /// <summary>
    /// What the table offers while the party is standing in town: walking on, and going down.
    ///
    /// One walk button rather than one per place, because five destination buttons would crowd the board and
    /// the pads are already the map -- when a pad can be clicked directly this is what it replaces.
    ///
    /// The maze is only listed with somebody to send into it, which is the rule everywhere else here: the
    /// table draws only what the game will accept, so a button never has to be refused after the fact. The
    /// console says why it refuses; a button on a table has nowhere to say it.
    /// </summary>
    private static ViewerPrompt TownPrompt(string locationId, bool hasParty)
    {
        var options = new List<ViewerPromptOption>();

        // A ring over each building rather than a row of buttons, because the pads already ARE the map: a
        // colonnaded temple and a market stall say where you are far better than the words do, so going
        // somewhere is a matter of pointing at it. Where they already stand is left out -- clicking the place
        // you are is a button that does nothing.
        foreach (var id in TownWalk)
        {
            if (id == locationId) continue;

            // Named for what the click DOES, now that it walks them over and takes them in.
            options.Add(new ViewerPromptOption("go:" + id, "Go to " + PlaceName(id), Target: id));
        }

        // Going IN is separate from walking over, so a click on a building is not a one-way trip into a menu:
        // the party walks there first and the door is then offered by name.
        options.Add(new ViewerPromptOption("enter", "Go inside " + PlaceName(locationId)));

        if (hasParty) options.Add(new ViewerPromptOption("maze", "Into the maze"));

        return new ViewerPrompt("town", $"The party is at {PlaceName(locationId)}.", null, options);
    }

    private void RestoreDailySpellPointsInTown()
    {
        var roster = _charRepo.GetAll().ToList();

        foreach (var character in roster)
        {
            var changed = false;

            if (character.HasStatus(CharacterStatus.Invisible))
            {
                character.RemoveStatus(CharacterStatus.Invisible);
                character.ArmorClass += 4;
                changed = true;
            }

            if (character.TemporaryStrengthRoundsRemaining > 0)
            {
                var roundsToConsume = character.TemporaryStrengthRoundsRemaining;
                character.TemporaryStrengthRoundsRemaining = 0;

                if (character.TemporaryStrengthBonus > 0)
                {
                    character.Abilities.Strength = Math.Max(1, character.Abilities.Strength - character.TemporaryStrengthBonus);
                    character.TemporaryStrengthBonus = 0;
                }

                changed = true;
            }

            if (character.Spellcasting == null || character.Spellcasting.Count == 0)
            {
                if (changed)
                    _charRepo.Save(character);
                continue;
            }

            foreach (var state in character.Spellcasting)
            {
                if (state.SlotsPerDay.Count == 0)
                    continue;

                while (state.SlotsUsed.Count < state.SlotsPerDay.Count)
                {
                    state.SlotsUsed.Add(0);
                    changed = true;
                }

                for (int i = 0; i < state.SlotsPerDay.Count; i++)
                {
                    if (state.SlotsUsed[i] != 0)
                    {
                        state.SlotsUsed[i] = 0;
                        changed = true;
                    }
                }

                if (EnsureMagicUserHasSleep(state))
                    changed = true;

                if (SyncAutoKnownAndPrepared(character, state))
                    changed = true;
            }

            if (changed)
                _charRepo.Save(character);
        }
    }

    private static bool EnsureMagicUserHasSleep(SpellcastingState state)
    {
        if (state.SpellClass != SpellClass.MagicUser)
            return false;

        if (state.KnownSpellIds.Any(id => string.Equals(id, "sleep", StringComparison.OrdinalIgnoreCase)))
            return false;

        state.KnownSpellIds.Insert(0, "sleep");
        return true;
    }

    private bool SyncAutoKnownAndPrepared(Adnd.Core.Characters.Character character, SpellcastingState state)
    {
        if (!IsAutoMemorizedClass(state.SpellClass))
            return false;

        var classSpells = _spellRepo.LoadByClass(state.SpellClass);
        var maxUnlockedLevel = 0;
        for (int i = state.SlotsPerDay.Count - 1; i >= 0; i--)
        {
            if (state.SlotsPerDay[i] > 0)
            {
                maxUnlockedLevel = i + 1;
                break;
            }
        }

        var shouldKnow = classSpells
            .Where(s => s.Level <= maxUnlockedLevel)
            .Select(s => s.Id)
            .Distinct()
            .ToList();

        var shouldPrepared = shouldKnow
            .Select(id => new PreparedSpell { SpellId = id, Count = 1 })
            .ToList();

        var knownChanged = state.KnownSpellIds.Count != shouldKnow.Count || state.KnownSpellIds.Except(shouldKnow).Any();
        var preparedChanged = state.PreparedSpells.Count != shouldPrepared.Count
                              || state.PreparedSpells.Any(ps => !shouldPrepared.Any(sp => sp.SpellId == ps.SpellId && sp.Count == ps.Count));

        if (!knownChanged && !preparedChanged)
            return false;

        state.KnownSpellIds = shouldKnow;
        state.PreparedSpells = shouldPrepared;
        return true;
    }

    private static bool IsAutoMemorizedClass(SpellClass spellClass)
    {
        if (spellClass is SpellClass.Cleric or SpellClass.Druid)
            return true;

        return GameRulesProvider.Current.AutoMemorizeArcaneSpellsDaily
               && spellClass is SpellClass.MagicUser or SpellClass.Illusionist;
    }

    private bool HasPartyMembers()
    {
        var party = _partyRepo.Load();
        if (party.Members.Count == 0)
            return false;

        var roster = _charRepo.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        return party.Members.Any(name => roster.ContainsKey(name));
    }

    private static void ShowPartyRequiredMessage()
    {
        Console.WriteLine("You need at least one party member first.");
        Console.ReadKey(true);
    }
}
