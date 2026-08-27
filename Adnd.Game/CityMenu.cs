using System;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Spells;
using Adnd.Data.Characters;
using Adnd.Data.Spells;

namespace Adnd.Game;

public class CityMenu
{
    private readonly CharacterRepository _repo = new("Data/Characters");
    private readonly CharacterCreator _creator = new();
    private readonly SpellRepository _spellRepo = new("Data/Spells");

    public void Show()
    {
        while (true)
        {
            var all = _repo.GetAll().ToList();

            Console.Clear();
            Console.WriteLine("=== TRAINING GROUNDS ===");
            Console.WriteLine($"Characters: {all.Count} / 100\n");

            if (all.Count > 0)
            {
                // Column headers
                Console.WriteLine($"{"#",-3} {"Name",-15} {"Race",-10} {"Alignment",-15} {"Class",-18} {"Lvl",-7} {"HP",7} {"AC",3} {"Status",-20}");
                Console.WriteLine(new string('-', 104));

                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    var cls = c.Classes != null && c.Classes.Count > 0
                        ? string.Join("/", c.Classes.Select(cc => cc.ToDisplayString()))
                        : c.Class.ToDisplayString();
                    var alignment = c.Alignment.ToDisplayString();
                    var hpDisplay = $"{c.CurrentHitPoints}/{c.MaxHitPoints}";
                    var statusInfo = c.Status != CharacterStatus.None ? GetStatusDisplay(c) : "-";
                    var levelDisplay = GetLevelDisplay(c);

                    Console.WriteLine($"{i + 1,-3} {c.Name,-15} {c.Race.ToDisplayString(),-10} {alignment,-15} {cls,-18} {levelDisplay,-7} {hpDisplay,7} {c.ArmorClass,3} {statusInfo,-20}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("\nC)reate New Character");
            Console.WriteLine("I)nspect Character");
            Console.WriteLine("D)elete Character");
            Console.WriteLine("X) Delete All Characters");
            Console.WriteLine("L<-eave");

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.C) CreateCharacter();
            else if (key == ConsoleKey.I) InspectCharacter(all);
            else if (key == ConsoleKey.D) DeleteCharacter(all);
            else if (key == ConsoleKey.X) DeleteAllCharacters(all);
            else if (key == ConsoleKey.L || key == ConsoleKey.Enter) break;
        }
    }

    private void CreateCharacter()
    {
        Console.Clear();
        Console.WriteLine("=== CREATE CHARACTER ===\n");

        Console.Write("Name: ");
        string name = Console.ReadLine() ?? "Unknown";

        // Enforce max length of 15 characters
        if (name.Length > 15)
        {
            name = name.Substring(0, 15);
            Console.WriteLine($"(Name truncated to: {name})");
        }

        // Roll abilities before race selection so player can choose race with knowledge
        // of the raw rolled stats.
        var abilities = _creator.RollAbilities();
        Console.WriteLine("\nRolled Abilities (raw):");
        Console.WriteLine(abilities);

        Console.WriteLine("Choose Race:");
        var raceValues = Enum.GetValues<Race>();
        int raceCount = raceValues.Length;
        for (int i = 0; i < raceCount; i++)
        {
            var r = (Race)raceValues.GetValue(i)!;
            var label = (char)('A' + i);
            Console.WriteLine($"{label}) {r.ToDisplayString()}");
        }
        Console.Write("Race: ");
        var raceIdx = InputHelper.ReadLetterIndex(raceCount);
        Race race = Race.Human;
        if (raceIdx.HasValue)
            race = (Race)raceValues.GetValue(raceIdx.Value)!;

        // Confirm race selection and apply racial modifiers, then show modified stats
        Console.WriteLine($"Selected race: {race.ToDisplayString()}");
        Console.WriteLine();
        abilities = _creator.ApplyRaceModifiers(abilities, race);
        Console.WriteLine("\nRolled Abilities (after racial modifiers):");
        Console.WriteLine(abilities);

        Console.WriteLine("Choose Class:");
        // Determine allowed classes based on abilities after racial modifiers
        var allowed = ClassRestrictions.GetAllowedClasses(abilities, race);

        // Present single-class options first
        var singleOptions = new System.Collections.Generic.List<CharacterClass>(allowed);

        // Prepare multiclass pairs using canonical per-race options (filtered by ability-allowed classes)
        var multiclassPairs = ClassRestrictions.GetAllowedMulticlasses(race, allowed);

        var chosenClasses = new System.Collections.Generic.List<CharacterClass> { CharacterClass.Fighter };

        // Loop until a selection (single or multiclass) is confirmed
        while (true)
        {
            for (int i = 0; i < singleOptions.Count; i++)
            {
                var label = (char)('A' + i);
                Console.WriteLine($"{label}) {singleOptions[i].ToDisplayString()}");
            }

            bool hasMulticlass = multiclassPairs.Count > 0;
            int totalChoices = singleOptions.Count + (hasMulticlass ? 1 : 0);

            if (hasMulticlass)
            {
                var label = (char)('A' + singleOptions.Count);
                Console.WriteLine($"{label}) Multiclass options...");
            }

            Console.Write("Class: ");
            var classIdx = InputHelper.ReadLetterIndex(totalChoices);

            if (!classIdx.HasValue)
            {
                // No valid selection (Enter or invalid) -> default to first single option if any
                if (singleOptions.Count > 0)
                    chosenClasses = new System.Collections.Generic.List<CharacterClass> { singleOptions[0] };
                break;
            }

            if (classIdx.Value < singleOptions.Count)
            {
                chosenClasses = new System.Collections.Generic.List<CharacterClass> { singleOptions[classIdx.Value] };
                break;
            }

            // Multiclass submenu
            Console.Clear();
            Console.WriteLine("Choose Multiclass Option:\n");
            for (int i = 0; i < multiclassPairs.Count; i++)
            {
                var p = multiclassPairs[i];
                var label = (char)('A' + i);
                var display = string.Join("/", p.Select(cc => cc.ToDisplayString()));
                Console.WriteLine($"{label}) {display}");
            }
            // Add a back option to return to single-class menu
            var backLabel = (char)('A' + multiclassPairs.Count);
            Console.WriteLine($"{backLabel}) Back to single-class options");

            Console.Write("Choice: ");
            var mcIdx = InputHelper.ReadLetterIndex(multiclassPairs.Count + 1);
            if (!mcIdx.HasValue)
            {
                // treat as back
                Console.Clear();
                continue;
            }

            if (mcIdx.Value < multiclassPairs.Count)
            {
                chosenClasses = new System.Collections.Generic.List<CharacterClass>(multiclassPairs[mcIdx.Value]);
                break;
            }

            // mcIdx corresponds to Back -> clear and re-show single-class options
            Console.Clear();
            continue;
        }

        // Confirm class selection
        var clsDisplay = chosenClasses.Count == 1 ? chosenClasses[0].ToDisplayString() : string.Join("/", chosenClasses.Select(cc => cc.ToDisplayString()));
        Console.WriteLine($"Selected class: {clsDisplay}");
       
        int? exceptionalStrengthPercentile = null;
        if (chosenClasses.Count == 1
            && abilities.Strength == 18
            && (chosenClasses[0] == CharacterClass.Fighter
                || chosenClasses[0] == CharacterClass.Ranger
                || chosenClasses[0] == CharacterClass.Paladin))
        {
            exceptionalStrengthPercentile = DiceRoller.Roll(1, 100);
            var pctDisplay = exceptionalStrengthPercentile.Value == 100
                ? "00"
                : exceptionalStrengthPercentile.Value.ToString("00");
            Console.WriteLine($"Exceptional Strength: 18/{pctDisplay}");
        }

        // After class selection, prompt the user to choose alignment with class-based restrictions.
        var allowedAlignments = AlignmentRestrictions.GetAllowedAlignments(chosenClasses, race);

        Console.WriteLine("Choose Alignment:");
        var alignValues = allowedAlignments.ToArray();
        for (int i = 0; i < alignValues.Length; i++)
        {
            var label = (char)('A' + i);
            Console.WriteLine($"{label}) {alignValues[i].ToDisplayString()}");
        }
        Console.Write("Alignment: ");
        var alignIdx = InputHelper.ReadLetterIndex(alignValues.Length);
        Alignment alignment = alignValues.Length > 0 ? alignValues[Math.Max(0, alignIdx ?? 0)] : Alignment.TrueNeutral;

        // determine HP using the already-rolled constitution
        int hp = _creator.RollHitPoints(chosenClasses[0], abilities.Constitution);
        int armorClass = 10 + AbilitiesTables.DexterityACModifier(abilities.Dexterity);

        var minGold = GameRulesProvider.Current.CharacterCreationMinGold;
        var maxGold = GameRulesProvider.Current.CharacterCreationMaxGold;
        var startingGold = minGold == maxGold
            ? minGold
            : Random.Shared.Next(minGold, maxGold + 1);

        var character = new Character
        {
            Name = name,
            Race = race,
            Classes = new System.Collections.Generic.List<CharacterClass>(chosenClasses),
            Abilities = abilities,
            Level = 1,
            MaxHitPoints = hp,
            CurrentHitPoints = hp,
            Experience = 0,
            GoldPieces = startingGold,
            ArmorClass = armorClass,
            Alignment = alignment,
            ExceptionalStrengthPercentile = exceptionalStrengthPercentile,
            NumberOfAttacks = 1,  // Base 1 attack per round at level 1
            Damage = "1d2",  // Default unarmed or no-weapon damage; will be replaced when weapon equipped
            Age = Random.Shared.Next(17, 29)
        };

        character.EnsureClassProgressions();

        InitializeSpellcasting(character);

        Console.WriteLine($"HP: {character.CurrentHitPoints}/{character.MaxHitPoints}, GP: {character.GoldPieces}\n");

        Console.Write("Save character? (Y/N): ");
        var saveKey = Console.ReadKey(true).Key;
        if (saveKey == ConsoleKey.Y || (saveKey != ConsoleKey.N && saveKey != ConsoleKey.Y))
        {
            _repo.Save(character);
            Console.WriteLine("Character saved.");
        }
        else
        {
            Console.WriteLine("Character discarded.");
        }
    }

    private void InitializeSpellcasting(Character character)
    {
        character.Spellcasting.Clear();

        foreach (var cls in character.Classes)
        {
            var tracks = SpellProgression.GetSpellcastingTracks(cls, character.Level);
            foreach (var track in tracks)
            {
                var spellClass = track.SpellClass;
                var slots = track.SlotsPerDay;

                // Skip tracks with no spell access yet
                if (slots.All(s => s <= 0))
                    continue;

                var state = new SpellcastingState
                {
                    SpellClass = spellClass,
                    SlotsPerDay = slots,
                    SlotsUsed = Enumerable.Repeat(0, slots.Count).ToList()
                };

                var classSpells = _spellRepo.LoadByClass(spellClass);
                var maxLevel = GetMaxSpellLevelForCurrentSlots(slots);

                if (spellClass == SpellClass.Cleric || spellClass == SpellClass.Druid)
                {
                    state.KnownSpellIds = classSpells
                        .Where(s => s.Level <= maxLevel)
                        .Select(s => s.Id)
                        .Distinct()
                        .ToList();
                }
                else
                {
                    var starterKnown = Math.Max(1, slots.FirstOrDefault());
                    var levelOneIds = classSpells
                        .Where(s => s.Level == 1)
                        .Select(s => s.Id)
                        .ToList();

                    if (spellClass == SpellClass.MagicUser
                        && levelOneIds.Any(id => string.Equals(id, "sleep", StringComparison.OrdinalIgnoreCase)))
                    {
                        levelOneIds = levelOneIds
                            .OrderBy(id => string.Equals(id, "sleep", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    state.KnownSpellIds = levelOneIds
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(starterKnown)
                        .ToList();
                }

                character.Spellcasting.Add(state);
            }
        }
    }

    private static int GetMaxSpellLevelForCurrentSlots(List<int> slots)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i] > 0)
                return i + 1;
        }

        return 0;
    }

    private void InspectCharacter(System.Collections.Generic.List<Character> all)
    {
        Console.Write("Character #: ");
        var sel = InputHelper.ReadNumber(1, all.Count, 2, echoTypedCharacters: true);
        if (sel.HasValue)
        {
            Console.Clear();
            var c = all[sel.Value - 1];
            Console.WriteLine(c);

            Console.WriteLine("\n=== SPELLCASTING ===");
            if (c.Spellcasting == null || c.Spellcasting.Count == 0)
            {
                Console.WriteLine(" (no spellcasting)");
            }
            else
            {
                foreach (var state in c.Spellcasting)
                {
                    var allSpells = _spellRepo.LoadByClass(state.SpellClass);
                    var known = allSpells.Where(s => state.KnownSpellIds.Contains(s.Id)).ToList();
                    Console.WriteLine($" - {state.SpellClass}: known {known.Count}, prepared {state.PreparedSpells.Sum(ps => ps.Count)}");

                    for (int lvl = 0; lvl < state.SlotsPerDay.Count; lvl++)
                    {
                        var max = state.SlotsPerDay[lvl];
                        if (max <= 0)
                            continue;
                        var used = lvl < state.SlotsUsed.Count ? state.SlotsUsed[lvl] : 0;
                        Console.WriteLine($"   L{lvl + 1} slots: {Math.Max(0, max - used)}/{max}");
                    }

                    if (known.Count > 0)
                    {
                        Console.WriteLine("   Known: " + string.Join(", ", known.Select(s => $"L{s.Level} {s.Name}")));
                    }
                }
            }
        }
        else Console.WriteLine("Invalid selection.");

        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private static string GetLevelDisplay(Character c)
    {
        c.EnsureClassProgressions();

        if (c.Classes == null || c.Classes.Count <= 1)
            return c.Level.ToString();

        return string.Join("/", c.Classes.Select(c.GetClassLevel));
    }

    private void DeleteCharacter(System.Collections.Generic.List<Character> all)
    {
        Console.Write("Character #: ");
        var sel = InputHelper.ReadNumber(1, all.Count);
        if (sel.HasValue)
        {
            var ch = all[sel.Value - 1];

            var requiresConfirmation = ch.Level > 1 || ch.GoldPieces > GameRulesProvider.Current.CharacterCreationMaxGold;
            if (requiresConfirmation)
            {
                Console.WriteLine($"Confirm delete of {ch.Name}? (Level {ch.Level}, GP {ch.GoldPieces})");
                Console.Write("Type Y to confirm: ");
                var confirm = Console.ReadKey(true).Key;
                Console.WriteLine();
                if (confirm != ConsoleKey.Y)
                {
                    Console.WriteLine("Delete cancelled.");
                    Console.WriteLine("Press any key...");
                    Console.ReadKey(true);
                    return;
                }
            }

            _repo.Delete(ch.Name);
            var cls = ch.Classes != null && ch.Classes.Count > 0 ? string.Join("/", ch.Classes) : ch.Class.ToString();
            Console.WriteLine($"Deleted: {ch.Name} - {ch.Race} {cls}");
        }
        else Console.WriteLine("Invalid selection.");

        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private void DeleteAllCharacters(System.Collections.Generic.List<Character> all)
    {
        if (all.Count == 0)
        {
            Console.WriteLine("No characters to delete.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine("WARNING: This will delete ALL characters in the roster.");
        Console.Write("Type DELETE ALL to confirm: ");
        var confirmation = Console.ReadLine();

        if (!string.Equals(confirmation?.Trim(), "DELETE ALL", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Delete all cancelled.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        foreach (var c in all)
            _repo.Delete(c.Name);

        Console.WriteLine($"Deleted {all.Count} character(s).");
        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private string GetStatusDisplay(Character c)
    {
        var statuses = new System.Collections.Generic.List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (c.HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (c.HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (c.HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (c.HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (c.HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (c.HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        return string.Join(", ", statuses);
    }
}
