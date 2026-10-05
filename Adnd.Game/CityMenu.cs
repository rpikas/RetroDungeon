using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Spells;
using Adnd.Data.Characters;
using Adnd.Data.Spells;

namespace Adnd.Game;

public class CityMenu
{
    private readonly CharacterRepository _repo = new("Data/Characters");
    private readonly CharacterCreator _creator = new();
    private readonly SpellRepository _spellRepo = new("Data/Spells");
    private readonly DualClassService _dualClassService = new();
    private readonly CharacterSavingThrowService _savingThrowService = new();

    public void Show()
    {
        while (true)
        {
            var all = _repo.GetAll().ToList();
            var dualClassAvailability = GetDualClassMenuAvailability(all);

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
                    var cls = c.GetClassesDisplayText("/");
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
            if (dualClassAvailability.Enabled)
                Console.WriteLine("U) Dual-class (human only)");
            else
                Console.WriteLine($"U) Dual-class (unavailable: {dualClassAvailability.Reason})");
            Console.WriteLine("D)elete Character");
            Console.WriteLine("X) Delete All Characters");
            Console.WriteLine("L<-eave");

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.C) CreateCharacter();
            else if (key == ConsoleKey.I) InspectCharacter(all);
            else if (key == ConsoleKey.U)
            {
                if (!dualClassAvailability.Enabled)
                {
                    Console.WriteLine($"Dual-class unavailable: {dualClassAvailability.Reason}");
                    Console.WriteLine("Press any key...");
                    Console.ReadKey(true);
                }
                else
                {
                    StartDualClass(all);
                }
            }
            else if (key == ConsoleKey.D) DeleteCharacter(all);
            else if (key == ConsoleKey.X) DeleteAllCharacters(all);
            else if (key == ConsoleKey.L || key == ConsoleKey.Escape || key == ConsoleKey.Enter) break;
        }
    }

    private (bool Enabled, string Reason) GetDualClassMenuAvailability(System.Collections.Generic.List<Character> all)
    {
        if (all == null || all.Count == 0)
            return (false, "no characters");

        var hasHuman = all.Any(c => c.Race == Race.Human);
        if (!hasHuman)
            return (false, "no human character");

        var hasHumanCandidate = all.Any(c => c.Race == Race.Human);
        if (!hasHumanCandidate)
            return (false, "no human candidate");

        foreach (var c in all.Where(ch => ch.Race == Race.Human))
        {
            if (GetEligibleDualClassTargets(c).Count > 0)
                return (true, string.Empty);
        }

        return (false, "no valid target class for any eligible human");
    }

    private void StartDualClass(System.Collections.Generic.List<Character> all)
    {
        if (all.Count == 0)
        {
            Console.WriteLine("No characters available.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        var eligibleCharacters = all
            .Where(c => c.Race == Race.Human)
            .Where(c => GetEligibleDualClassTargets(c).Count > 0)
            .ToList();

        if (eligibleCharacters.Count == 0)
        {
            Console.WriteLine("No characters currently meet dual-class requirements.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine("Characters that can dual-class:");
        for (int i = 0; i < eligibleCharacters.Count; i++)
        {
            var c = eligibleCharacters[i];
            var currentClass = c.Classes.Count > 0 ? c.Classes[0] : c.Class;
            Console.WriteLine($"{i + 1}) {c.Name} - {c.Race.ToDisplayString()} {c.Alignment.ToDisplayString()} {currentClass.ToDisplayString()} L{c.GetClassLevel(currentClass)}");
        }

        Console.Write("Character #: ");
        var sel = InputHelper.ReadNumber(1, eligibleCharacters.Count, 2, echoTypedCharacters: true);
        if (!sel.HasValue)
        {
            Console.WriteLine("Invalid selection.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        var character = eligibleCharacters[sel.Value - 1];
        var targets = GetEligibleDualClassTargets(character);

        if (targets.Count == 0)
        {
            Console.WriteLine("No valid target class meets dual-class requirements for this character.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine("Choose target class:");
        for (int i = 0; i < targets.Count; i++)
        {
            var label = (char)('A' + i);
            Console.WriteLine($"{label}) {targets[i].ToDisplayString()}");
        }

        Console.Write("Target class: ");
        var idx = InputHelper.ReadLetterIndex(targets.Count);
        if (!idx.HasValue)
        {
            Console.WriteLine("Invalid selection.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        var targetClass = targets[idx.Value];

        // Revalidate immediately before confirmation/apply for deterministic UX.
        if (!_dualClassService.CanStartDualClass(character, targetClass, out var revalidateReason))
        {
            Console.WriteLine(revalidateReason);
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Dual-class warning:");
        Console.WriteLine($"- {DualClassService.NoOldClassProgressionMessage}");
        Console.WriteLine($"- {DualClassService.OldClassFunctionBlocksXpMessage}");
        if (targetClass == CharacterClass.Bard)
        {
            Console.WriteLine("- Bard path: Fighter (5-7) -> Thief (5-8) -> Bard.");
        }
        Console.Write("Type Y to confirm: ");
        var confirm = Console.ReadKey(true).Key;
        Console.WriteLine();
        if (confirm != ConsoleKey.Y)
        {
            Console.WriteLine("Dual-class cancelled.");
            Console.WriteLine("Press any key...");
            Console.ReadKey(true);
            return;
        }

        var result = _dualClassService.TryStartDualClass(character, targetClass);
        Console.WriteLine(result.Message);

        if (result.Success)
            _repo.Save(character);

        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private List<CharacterClass> GetEligibleDualClassTargets(Character character)
    {
        if (character == null)
            return new List<CharacterClass>();

        character.EnsureClassProgressions();
        var currentClass = character.Classes.Count > 0 ? character.Classes[0] : character.Class;

        return Enum.GetValues<CharacterClass>()
            .Where(c => c != currentClass)
            .Where(c => _dualClassService.CanStartDualClass(character, c, out _))
            .OrderBy(c => c.ToDisplayString())
            .ToList();
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

        var gender = Gender.Male;

        // Roll abilities before race selection so player can choose race with knowledge
        // of the raw rolled stats. Allow rerolling here.
        var raceValues = Enum.GetValues<Race>();
        AbilityScores abilities;
        Race race;

        while (true)
        {
            gender = Random.Shared.Next(0, 2) == 0 ? Gender.Male : Gender.Female;
            abilities = _creator.RollAbilities();
            Console.WriteLine("\nRolled Abilities (raw):");
            Console.WriteLine($"Gender: {gender}");
            Console.WriteLine(abilities);

            var availableRaces = raceValues
                .Where(r => CanSelectRaceWithAdjustedAbilities(abilities, r, gender))
                .ToList();

            RuleApplicationInfo.PublishLinked(
                "PHB",
                "14PenaltiesAndBonusesForRace",
                $"Choose race after abilities. Available races: {string.Join(", ", availableRaces.Select(r => r.ToDisplayString()))}.");

            if (availableRaces.Count < raceValues.Length)
            {
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "15AbilityScoreMaxMinRaceGender",
                    "If not all races are selectable, it might be because the ability scores are not applicable for all races.");
            }

            Console.WriteLine("Choose Race:");
            for (int i = 0; i < availableRaces.Count; i++)
            {
                var r = availableRaces[i];
                var label = (char)('A' + i);
                Console.WriteLine($"{label}) {r.ToDisplayString()} ({GetRaceAbilityModifierSummary(r)})");
            }

            Console.WriteLine("R)eroll abilities");
            Console.Write("Race: ");
            var raceKey = Console.ReadKey(true);
            var raceChar = char.ToUpperInvariant(raceKey.KeyChar);

            if (raceChar == 'R')
            {
                Console.WriteLine("Rerolling abilities...\n");
                continue;
            }

            race = availableRaces.Count > 0 ? availableRaces[0] : Race.Human;
            var raceIdx = raceChar - 'A';
            if (raceIdx >= 0 && raceIdx < availableRaces.Count)
                race = availableRaces[raceIdx];

            break;
        }

        // Confirm race selection and apply racial modifiers, then show modified stats
        Console.WriteLine($"Selected race: {race.ToDisplayString()}");
        RuleApplicationInfo.PublishLinked("PHB", "14PenaltiesAndBonusesForRace", $"Race selected: {race.ToDisplayString()}.");
        Console.WriteLine();
        abilities = _creator.ApplyRaceModifiers(abilities, race);
        abilities = RaceAbilityScoreLimits.ClampToLimits(abilities, race, gender);
        Console.WriteLine("\nRolled Abilities (after racial modifiers):");
        Console.WriteLine($"Gender: {gender}");
        Console.WriteLine(abilities);
        PublishFinalAbilities(abilities);

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
                var candidateClasses = new System.Collections.Generic.List<CharacterClass> { singleOptions[classIdx.Value] };
                if (!ConfirmClassLevelLimitWarning(race, abilities, candidateClasses))
                {
                    Console.Clear();
                    Console.WriteLine("Choose Class:");
                    continue;
                }

                chosenClasses = candidateClasses;
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
                var candidateClasses = new System.Collections.Generic.List<CharacterClass>(multiclassPairs[mcIdx.Value]);
                if (!ConfirmClassLevelLimitWarning(race, abilities, candidateClasses))
                {
                    Console.Clear();
                    Console.WriteLine("Choose Class:");
                    continue;
                }

                chosenClasses = candidateClasses;
                break;
            }

            // mcIdx corresponds to Back -> clear and re-show single-class options
            Console.Clear();
            continue;
        }

        // Confirm class selection
        var clsDisplay = chosenClasses.Count == 1 ? chosenClasses[0].ToDisplayString() : string.Join("/", chosenClasses.Select(cc => cc.ToDisplayString()));
        Console.WriteLine($"Selected class: {clsDisplay}");
        RuleApplicationInfo.Publish($"Class selected: {clsDisplay}.");
       
        int? exceptionalStrengthPercentile = null;
        if (chosenClasses.Count == 1
            && abilities.Strength == 18
            && IsExceptionalStrengthClass(chosenClasses[0]))
        {
            exceptionalStrengthPercentile = DiceRoller.Roll(1, 100);
            var pctDisplay = exceptionalStrengthPercentile.Value == 100
                ? "00"
                : exceptionalStrengthPercentile.Value.ToString("00");
            Console.WriteLine($"Exceptional Strength: 18/{pctDisplay}");
            RuleApplicationInfo.Publish($"Exceptional Strength rolled at character creation: 18/{pctDisplay}.");
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
        Console.WriteLine($"Selected alignment: {alignment.ToDisplayString()}");

        // determine HP using the already-rolled constitution
        int hp = _creator.RollHitPoints(chosenClasses[0], abilities.Constitution);
        int armorClass = 10 + AbilitiesTables.DexterityACModifier(abilities.Dexterity);

        var startingGold = RollStartingGold(chosenClasses[0]);

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
            Gender = gender,
            ExceptionalStrengthPercentile = exceptionalStrengthPercentile,
            NumberOfAttacks = 1,  // Base 1 attack per round at level 1
            Damage = "1d2",  // Default unarmed or no-weapon damage; will be replaced when weapon equipped
            Age = RollStartingAgeYears(race, chosenClasses)
        };

        var additionalLanguageSlots = LanguageRules.GetAdditionalLanguageCount(character.Abilities.Intelligence);
        List<string> additionalLanguages;
        if (GameRulesProvider.Current.PlayerSelectsLanguagesOnCreation)
        {
            additionalLanguages = SelectAdditionalLanguages(character, additionalLanguageSlots);
        }
        else
        {
            additionalLanguages = LanguageRules.PickRandomAdditionalLanguages(character, additionalLanguageSlots);
        }
        character.KnownLanguages = LanguageRules.CombineKnownLanguages(character, additionalLanguages);

        var abilitiesBeforeAgeAdjustment = CloneAbilities(character.Abilities);
        var exceptionalBeforeAgeAdjustment = character.ExceptionalStrengthPercentile;
        var exceptionalRolledAfterAgeAdjustment = false;
        character.ApplyAgeCategoryAdjustmentsForCreation();

        if (chosenClasses.Count == 1
            && character.Abilities.Strength == 18
            && !character.ExceptionalStrengthPercentile.HasValue
            && IsExceptionalStrengthClass(chosenClasses[0]))
        {
            character.ExceptionalStrengthPercentile = DiceRoller.Roll(1, 100);
            exceptionalRolledAfterAgeAdjustment = true;
            var adjustedPctDisplay = character.ExceptionalStrengthPercentile.Value == 100
                ? "00"
                : character.ExceptionalStrengthPercentile.Value.ToString("00");
            Console.WriteLine($"Exceptional Strength (after age adjustment): 18/{adjustedPctDisplay}");
            RuleApplicationInfo.Publish($"Exceptional Strength rolled after age adjustment: 18/{adjustedPctDisplay}.");
        }

        var ageAdjustmentSummary = BuildAgeAdjustmentSummary(
            abilitiesBeforeAgeAdjustment,
            character.Abilities,
            exceptionalBeforeAgeAdjustment,
            character.ExceptionalStrengthPercentile);

        RuleApplicationInfo.Publish($"Creating character '{name}': race {race.ToDisplayString()}, class {clsDisplay}.");
        if (race == Race.Human)
        {
            RuleApplicationInfo.PublishLinked(
                "DMG",
                "12StartingAgeHuman",
                $"Human starting age table used for {chosenClasses[0].ToDisplayString()}.");
        }
        else
        {
            RuleApplicationInfo.PublishLinked(
                "DMG",
                "12StartingAgeNonHuman",
                $"Non-human starting age table used for {race.ToDisplayString()} ({chosenClasses[0].ToDisplayString()}).");
        }
        RuleApplicationInfo.Publish($"Starting age rolled: {character.Age} years ({race.ToDisplayString()} / {chosenClasses[0].ToDisplayString()}).");
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "35StartingMoney",
            $"Starting gold for {character.Name} ({chosenClasses[0].ToDisplayString()}): {startingGold} gp.");
        RuleApplicationInfo.PublishLinked(
            "DMG",
            "13AgeCategoriesWithModifiers",
            $"Age category: {character.GetAgeCategoryDisplay()}. Age adjustment applied: {ageAdjustmentSummary}.");
        RuleApplicationInfo.Publish($"Age category adjustment applied ({character.GetAgeCategoryDisplay()}): {ageAdjustmentSummary}.");
        RuleApplicationInfo.Publish($"Languages known: {character.GetKnownLanguagesDisplay()}.");

        character.EnsureClassProgressions();
        character.RefreshMoveFromArmorAndClass();
        character.RefreshMonkProgressionStats();
        if (GameRulesProvider.Current.PlayerSelectsWeaponProficiencies)
        {
            SelectInitialWeaponProficiencies(character, chosenClasses);
            RuleApplicationInfo.PublishLinked("PHB", "37WeaponProficiency", $"Weapon proficiencies (player-selected): {character.GetWeaponProficienciesDisplay()}.");
        }
        else
        {
            WeaponProficiencyRules.EnsureAutoProficiencies(character);
            RuleApplicationInfo.PublishLinked("PHB", "37WeaponProficiency", $"Weapon proficiencies (auto): {character.GetWeaponProficienciesDisplay()}.");
        }

        InitializeSpellcasting(character);

        var saveVsParalyzation = _savingThrowService.GetSaveTarget(character, SaveThrowType.ParalyzationPoisonDeath);
        var saveVsPetrification = _savingThrowService.GetSaveTarget(character, SaveThrowType.PetrificationPolymorph);
        var saveVsRodStaffWand = _savingThrowService.GetSaveTarget(character, SaveThrowType.RodStaffWand);
        var saveVsBreath = _savingThrowService.GetSaveTarget(character, SaveThrowType.BreathWeapon);
        var saveVsSpell = _savingThrowService.GetSaveTarget(character, SaveThrowType.Spell);

        RuleApplicationInfo.Publish($"Final hit points: {character.CurrentHitPoints}/{character.MaxHitPoints}.");
        RuleApplicationInfo.Publish($"Final saving throws: Paralyzation/Poison/Death {saveVsParalyzation}, Petrification/Polymorph {saveVsPetrification}, Rod/Staff/Wand {saveVsRodStaffWand}, Breath Weapon {saveVsBreath}, Spell {saveVsSpell}.");
        RuleApplicationInfo.Publish($"Final THAC0: {character.Thac0Display}.");
        RuleApplicationInfo.Publish($"Final AC: {character.ArmorClass}.");

        Console.WriteLine($"HP: {character.CurrentHitPoints}/{character.MaxHitPoints}, GP: {character.GoldPieces}, Age: {Math.Max(0, character.Age)}y {Math.Max(0, character.AgeDays)}d ({character.GetAgeCategoryDisplay()})\n");
        Console.WriteLine($"Languages: {character.GetKnownLanguagesDisplay()}");
        Console.WriteLine($"Age adjustment ({character.GetAgeCategoryDisplay()}): {ageAdjustmentSummary}");
        if (exceptionalRolledAfterAgeAdjustment && character.ExceptionalStrengthPercentile.HasValue)
        {
            var pctDisplay = character.ExceptionalStrengthPercentile.Value == 100
                ? "00"
                : character.ExceptionalStrengthPercentile.Value.ToString("00");
            Console.WriteLine($"Exceptional Strength result: 18/{pctDisplay}");
        }

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

    private static int RollStartingAgeYears(Race race, List<CharacterClass> chosenClasses)
    {
        var cls = chosenClasses.Count > 0 ? chosenClasses[0] : CharacterClass.Fighter;

        if (race == Race.Human)
            return RollHumanStartingAge(cls);

        return RollDemihumanStartingAge(race, cls);
    }

    private static int RollHumanStartingAge(CharacterClass cls)
    {
        return cls switch
        {
            CharacterClass.Cleric => 18 + RollDice(1, 4),
            CharacterClass.Druid => 18 + RollDice(1, 4),
            CharacterClass.Fighter => 15 + RollDice(1, 4),
            CharacterClass.Paladin => 17 + RollDice(1, 4),
            CharacterClass.Ranger => 20 + RollDice(1, 4),
            CharacterClass.MagicUser => 24 + RollDice(2, 8),
            CharacterClass.Illusionist => 30 + RollDice(1, 6),
            CharacterClass.Thief => 18 + RollDice(1, 4),
            CharacterClass.Assassin => 20 + RollDice(1, 4),
            CharacterClass.Monk => 21 + RollDice(1, 4),
            CharacterClass.Bard => 20 + RollDice(1, 4),
            _ => 18 + RollDice(1, 4)
        };
    }

    private static int RollDemihumanStartingAge(Race race, CharacterClass cls)
    {
        var archetype = GetDemihumanAgeArchetype(cls);

        return race switch
        {
            Race.Dwarf => archetype switch
            {
                DemihumanAgeArchetype.Cleric => 250 + RollDice(2, 20),
                DemihumanAgeArchetype.Fighter => 40 + RollDice(5, 4),
                DemihumanAgeArchetype.Thief => 75 + RollDice(3, 6),
                _ => 40 + RollDice(5, 4)
            },
            Race.Elf => archetype switch
            {
                DemihumanAgeArchetype.Cleric => 500 + RollDice(1, 10),
                DemihumanAgeArchetype.Fighter => 130 + RollDice(5, 6),
                DemihumanAgeArchetype.MagicUser => 150 + RollDice(5, 6),
                DemihumanAgeArchetype.Thief => 100 + RollDice(5, 6),
                _ => 130 + RollDice(5, 6)
            },
            Race.Gnome => archetype switch
            {
                DemihumanAgeArchetype.Cleric => 300 + RollDice(3, 12),
                DemihumanAgeArchetype.Fighter => 60 + RollDice(5, 4),
                DemihumanAgeArchetype.MagicUser => 100 + RollDice(2, 12),
                DemihumanAgeArchetype.Thief => 80 + RollDice(3, 6),
                _ => 60 + RollDice(5, 4)
            },
            Race.HalfElf => archetype switch
            {
                DemihumanAgeArchetype.Cleric => 40 + RollDice(2, 4),
                DemihumanAgeArchetype.Fighter => 22 + RollDice(3, 4),
                DemihumanAgeArchetype.MagicUser => 30 + RollDice(2, 8),
                DemihumanAgeArchetype.Thief => 22 + RollDice(3, 8),
                _ => 22 + RollDice(3, 4)
            },
            Race.Halfling => archetype switch
            {
                DemihumanAgeArchetype.Fighter => 20 + RollDice(3, 4),
                DemihumanAgeArchetype.Thief => 40 + RollDice(2, 4),
                _ => 20 + RollDice(3, 4)
            },
            Race.HalfOrc => archetype switch
            {
                DemihumanAgeArchetype.Cleric => 20 + RollDice(1, 4),
                DemihumanAgeArchetype.Fighter => 13 + RollDice(1, 4),
                DemihumanAgeArchetype.Thief => 20 + RollDice(2, 4),
                _ => 13 + RollDice(1, 4)
            },
            _ => RollHumanStartingAge(cls)
        };
    }

    private static DemihumanAgeArchetype GetDemihumanAgeArchetype(CharacterClass cls)
    {
        return cls switch
        {
            CharacterClass.Cleric or CharacterClass.Druid => DemihumanAgeArchetype.Cleric,
            CharacterClass.MagicUser or CharacterClass.Illusionist => DemihumanAgeArchetype.MagicUser,
            CharacterClass.Thief or CharacterClass.Assassin or CharacterClass.Bard => DemihumanAgeArchetype.Thief,
            _ => DemihumanAgeArchetype.Fighter
        };
    }

    private static int RollDice(int count, int sides)
    {
        var total = 0;
        for (int i = 0; i < count; i++)
            total += Random.Shared.Next(1, sides + 1);

        return total;
    }

    private enum DemihumanAgeArchetype
    {
        Cleric,
        Fighter,
        MagicUser,
        Thief
    }

    private static AbilityScores CloneAbilities(AbilityScores source)
    {
        return new AbilityScores
        {
            Strength = source.Strength,
            Intelligence = source.Intelligence,
            Wisdom = source.Wisdom,
            Dexterity = source.Dexterity,
            Constitution = source.Constitution,
            Charisma = source.Charisma
        };
    }

    private static string BuildAgeAdjustmentSummary(AbilityScores before, AbilityScores after, int? exceptionalBefore, int? exceptionalAfter)
    {
        var changes = new List<string>();

        AppendDelta(changes, "STR", before.Strength, after.Strength);
        AppendDelta(changes, "INT", before.Intelligence, after.Intelligence);
        AppendDelta(changes, "WIS", before.Wisdom, after.Wisdom);
        AppendDelta(changes, "DEX", before.Dexterity, after.Dexterity);
        AppendDelta(changes, "CON", before.Constitution, after.Constitution);
        AppendDelta(changes, "CHA", before.Charisma, after.Charisma);

        if (exceptionalBefore != exceptionalAfter)
        {
            var beforePct = exceptionalBefore.HasValue ? exceptionalBefore.Value.ToString("00") : "--";
            var afterPct = exceptionalAfter.HasValue ? exceptionalAfter.Value.ToString("00") : "--";
            changes.Add($"Exceptional STR {beforePct}->{afterPct}");
        }

        return changes.Count == 0 ? "No ability score changes" : string.Join(", ", changes);
    }

    private static void AppendDelta(List<string> output, string label, int before, int after)
    {
        if (before == after)
            return;

        var delta = after - before;
        var sign = delta > 0 ? "+" : string.Empty;
        output.Add($"{label} {sign}{delta} ({before}->{after})");
    }

    private static bool IsExceptionalStrengthClass(CharacterClass characterClass)
    {
        return characterClass == CharacterClass.Fighter
               || characterClass == CharacterClass.Ranger
               || characterClass == CharacterClass.Paladin;
    }

    private static void SelectInitialWeaponProficiencies(Character character, System.Collections.Generic.List<CharacterClass> chosenClasses)
    {
        if (character == null)
            return;

        var primaryClass = chosenClasses.Count > 0 ? chosenClasses[0] : character.Class;
        var slots = WeaponProficiencyRules.GetInitialProficiencySlots(primaryClass);
        if (slots <= 0)
            return;

        var available = WeaponProficiencyRules.GetAllSelectableWeaponsForClass(primaryClass).ToList();
        if (available.Count == 0)
            return;

        character.WeaponProficiencies ??= new System.Collections.Generic.List<string>();

        if (primaryClass == CharacterClass.Monk)
        {
            WeaponProficiencyRules.AddProficiency(character, "Fist or Open Hand");
            RuleApplicationInfo.PublishLinked(
                "PHB",
                "37WeaponProficiency",
                $"Weapon proficiency selection: {character.Name} gains mandatory monk proficiency Fist or Open Hand.");
        }

        Console.WriteLine();
        Console.WriteLine($"Select {slots} weapon proficienc{(slots == 1 ? "y" : "ies")} for {primaryClass.ToDisplayString()}:");

        for (int pick = 0; pick < slots; pick++)
        {
            var already = new HashSet<string>(character.WeaponProficiencies, StringComparer.OrdinalIgnoreCase);
            var options = available.Where(w => !already.Contains(w)).ToList();
            if (options.Count == 0)
                break;

            Console.WriteLine();
            Console.WriteLine($"Pick {pick + 1} of {slots}:");
            for (int i = 0; i < options.Count; i++)
            {
                var label = (char)('A' + i);
                Console.WriteLine($"{label}) {options[i]}");
            }

            Console.Write("Weapon: ");
            var selected = InputHelper.ReadLetterIndex(options.Count);
            var idx = selected.HasValue ? Math.Max(0, selected.Value) : 0;
            var chosenWeapon = options[idx];
            WeaponProficiencyRules.AddProficiency(character, chosenWeapon);
            Console.WriteLine($"Selected: {chosenWeapon}");
            RuleApplicationInfo.PublishLinked(
                "PHB",
                "37WeaponProficiency",
                $"Weapon proficiency selection: {character.Name} selected {chosenWeapon} ({pick + 1} of {slots}) at character creation.");
        }
    }

    private static void PublishFinalAbilities(AbilityScores abilities)
    {
        RuleApplicationInfo.Publish(
            $"Final abilities: STR {abilities.Strength}, INT {abilities.Intelligence}, WIS {abilities.Wisdom}, DEX {abilities.Dexterity}, CON {abilities.Constitution}, CHA {abilities.Charisma}.");
    }

    private static bool ConfirmClassLevelLimitWarning(Race race, AbilityScores abilities, System.Collections.Generic.IReadOnlyList<CharacterClass> classes)
    {
        var probe = new Character
        {
            Race = race,
            Abilities = abilities
        };

        var limits = classes
            .Select(cls => new
            {
                Class = cls,
                MaxLevel = RaceClassLevelLimits.GetMaxLevel(probe, cls)
            })
            .Where(x => x.MaxLevel != int.MaxValue)
            .ToList();

        if (limits.Count == 0)
            return true;

        Console.WriteLine();
        Console.WriteLine("WARNING: This race/class has level limits:");
        foreach (var limit in limits)
            Console.WriteLine($"- {race.ToDisplayString()} {limit.Class.ToDisplayString()} maximum level: {limit.MaxLevel}");

        Console.Write("Continue with this selection? (Y/N): ");
        var confirm = Console.ReadKey(true).Key;
        Console.WriteLine();

        return confirm == ConsoleKey.Y;
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

    private static List<string> SelectAdditionalLanguages(Character character, int slots)
    {
        var selected = new List<string>();
        if (slots <= 0)
            return selected;

        var available = LanguageRules.GetAdditionalLanguageChoices(character);
        for (int pick = 0; pick < slots; pick++)
        {
            var options = available
                .Where(l => !selected.Contains(l, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (options.Count == 0)
                break;

            Console.WriteLine();
            Console.WriteLine($"Select additional language {pick + 1} of {slots}:");
            for (int i = 0; i < options.Count; i++)
            {
                var label = (char)('A' + i);
                Console.WriteLine($"{label}) {options[i]}");
            }

            Console.Write("Language: ");
            var selectedIndex = InputHelper.ReadLetterIndex(options.Count);
            var idx = selectedIndex.HasValue ? Math.Max(0, selectedIndex.Value) : 0;
            selected.Add(options[idx]);
        }

        return selected;
    }

    private static int RollStartingGold(CharacterClass primaryClass)
    {
        return primaryClass switch
        {
            CharacterClass.Cleric or CharacterClass.Druid => DiceRoller.Roll(3, 6, 0, "Starting gold: Cleric 3d6 x 10") * 10,
            CharacterClass.Fighter or CharacterClass.Paladin or CharacterClass.Ranger => DiceRoller.Roll(5, 4, 0, "Starting gold: Fighter 5d4 x 10") * 10,
            CharacterClass.MagicUser or CharacterClass.Illusionist => DiceRoller.Roll(2, 4, 0, "Starting gold: Magic-User 2d4 x 10") * 10,
            CharacterClass.Thief or CharacterClass.Assassin => DiceRoller.Roll(2, 6, 0, "Starting gold: Thief 2d6 x 10") * 10,
            CharacterClass.Monk => DiceRoller.Roll(5, 4, 0, "Starting gold: Monk 5d4"),
            CharacterClass.Bard => DiceRoller.Roll(2, 6, 0, "Starting gold: Bard uses Thief 2d6 x 10") * 10,
            _ => DiceRoller.Roll(5, 4, 0, "Starting gold: default 5d4 x 10") * 10
        };
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
            Console.WriteLine($"Age: {Math.Max(0, c.Age)}y {Math.Max(0, c.AgeDays)}d ({c.GetAgeCategoryDisplay()})");
            Console.WriteLine($"Languages: {c.GetKnownLanguagesDisplay()}");
            RuleApplicationInfo.Publish($"Inspect character: {c.Name} languages known: {c.GetKnownLanguagesDisplay()}.");

            if (c.HasStatus(CharacterStatus.Out))
            {
                Console.WriteLine($"Out position: L{c.DungeonLevel} ({c.DungeonCellX},{c.DungeonCellY})");
                RuleApplicationInfo.Publish(
                    $"Inspect character: {c.Name} is OUT at dungeon position L{c.DungeonLevel} ({c.DungeonCellX},{c.DungeonCellY}) with status {c.Status}.");
            }

            if (c.IsBard())
            {
                var bardLevel = c.GetBardLevel();
                var bardProgress = BardRules.GetProgressForLevel(bardLevel);
                Console.WriteLine($"Bard college: {bardProgress.College}");
                Console.WriteLine($"Bard charm chance: {bardProgress.CharmPercentage}%");
                Console.WriteLine($"Legend/lore & item knowledge chance: {bardProgress.LegendLoreItemKnowledgePercentage}%");
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "118BardTabeII",
                    $"Inspect bard: {c.Name} L{bardLevel} => college {bardProgress.College}, charm {bardProgress.CharmPercentage}%, legend/lore {bardProgress.LegendLoreItemKnowledgePercentage}%, additional languages known {bardProgress.AdditionalLanguagesKnown}.");
            }

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

            InspectCharacterInventory(c);
        }
        else Console.WriteLine("Invalid selection.");

        Console.WriteLine("Press any key...");
        Console.ReadKey(true);
    }

    private void InspectCharacterInventory(Character character)
    {
        while (true)
        {
            Console.WriteLine("\n=== INVENTORY ===");

            if (character.Inventory == null || character.Inventory.Count == 0)
            {
                Console.WriteLine(" (empty)");
                return;
            }

            for (int i = 0; i < character.Inventory.Count; i++)
            {
                var item = character.Inventory[i];
                var quantityText = item.Quantity > 1 ? $" x{item.Quantity}" : string.Empty;
                Console.WriteLine($"{i + 1,2}. {item.Name}{quantityText}");
            }

            Console.WriteLine("\nD)rop item");
            Console.WriteLine("L<-eave inventory");
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.L || key == ConsoleKey.Enter || key == ConsoleKey.Escape)
                return;

            if (key != ConsoleKey.D)
                continue;

            Console.Write("Item #: ");
            var itemSelection = InputHelper.ReadNumber(1, character.Inventory.Count, 3, echoTypedCharacters: true);
            if (!itemSelection.HasValue)
            {
                Console.WriteLine("Invalid selection.");
                continue;
            }

            var idx = itemSelection.Value - 1;
            var dropped = character.Inventory[idx];
            character.Inventory.RemoveAt(idx);
            _repo.Save(character);

            Console.WriteLine($"Dropped: {dropped.Name}");
        }
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
        var sel = InputHelper.ReadNumber(1, all.Count, autoSubmitAfterCharacters: 2);
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

    private string GetRaceAbilityModifierSummary(Race race)
    {
        var baseline = new AbilityScores
        {
            Strength = 10,
            Intelligence = 10,
            Wisdom = 10,
            Dexterity = 10,
            Constitution = 10,
            Charisma = 10
        };

        var adjusted = _creator.ApplyRaceModifiers(baseline, race);

        var parts = new System.Collections.Generic.List<string>();
        AddModifierPart(parts, "STR", adjusted.Strength - baseline.Strength);
        AddModifierPart(parts, "INT", adjusted.Intelligence - baseline.Intelligence);
        AddModifierPart(parts, "WIS", adjusted.Wisdom - baseline.Wisdom);
        AddModifierPart(parts, "DEX", adjusted.Dexterity - baseline.Dexterity);
        AddModifierPart(parts, "CON", adjusted.Constitution - baseline.Constitution);
        AddModifierPart(parts, "CHA", adjusted.Charisma - baseline.Charisma);

        return parts.Count == 0 ? "no ability modifiers" : string.Join(", ", parts);
    }

    private static void AddModifierPart(System.Collections.Generic.List<string> parts, string ability, int delta)
    {
        if (delta == 0)
            return;

        parts.Add($"{ability} {(delta > 0 ? "+" : string.Empty)}{delta}");
    }

    private bool CanSelectRaceWithAdjustedAbilities(AbilityScores rolledAbilities, Race race, Gender gender)
    {
        var adjusted = _creator.ApplyRaceModifiers(rolledAbilities, race);
        return RaceAbilityScoreLimits.IsWithinLimits(adjusted, race, gender);
    }

    private string GetStatusDisplay(Character c)
    {
        var statuses = new System.Collections.Generic.List<string>();
        if (c.HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (c.HasStatus(CharacterStatus.Out)) statuses.Add("Out");
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
