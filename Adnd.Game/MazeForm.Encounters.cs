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

// Random and fixed encounters, good/neutral encounter choices, defeat, searching and recovery.
public sealed partial class MazeForm
{
    private static bool PartyHasNoEvilCharacters(List<Character> party)
    {
        return party.All(c => c.Alignment != Alignment.LawfulEvil
                              && c.Alignment != Alignment.NeutralEvil
                              && c.Alignment != Alignment.ChaoticEvil);
    }

    private enum EncounterMoralAxis
    {
        Good,
        Neutral,
        Evil
    }

    private static EncounterMoralAxis? GetEncounterMoralAxis(Monster? monster)
    {
        var alignment = monster?.Alignment ?? string.Empty;
        if (string.IsNullOrWhiteSpace(alignment))
            return null;

        if (alignment.IndexOf("good", StringComparison.OrdinalIgnoreCase) >= 0)
            return EncounterMoralAxis.Good;

        if (alignment.IndexOf("evil", StringComparison.OrdinalIgnoreCase) >= 0)
            return EncounterMoralAxis.Evil;

        if (alignment.IndexOf("neutral", StringComparison.OrdinalIgnoreCase) >= 0)
            return EncounterMoralAxis.Neutral;

        return null;
    }

    private bool AreAllEncounterGroupsGood(IEnumerable<string> groupMonsterNames)
    {
        var allMonsters = _monsterRepository.GetAll().ToList();
        var foundAny = false;

        foreach (var name in groupMonsterNames)
        {
            var template = FindMonsterByName(allMonsters, name);
            if (template == null)
                return false;

            foundAny = true;
            if (string.IsNullOrWhiteSpace(template.Alignment)
                || template.Alignment.IndexOf("good", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return foundAny;
    }

    private bool ShouldOfferFriendlyEncounterChoice(List<Character> party, IEnumerable<string> groupMonsterNames)
    {
        return PartyHasNoEvilCharacters(party)
               && AreAllEncounterGroupsGood(groupMonsterNames);
    }

    private void ApplyGoodEncounterAttackConsequences(List<Character> party, string encounterName)
    {
        var goodMembers = party.Where(c => c.IsGoodAlignment()).ToList();
        if (goodMembers.Count == 0)
            return;

        var warningCandidates = goodMembers.Where(c => c.GoodEncounterAttackWarningCount < 2).ToList();
        if (warningCandidates.Count > 0)
        {
            foreach (var member in warningCandidates)
            {
                member.GoodEncounterAttackWarningCount += 1;
                _characterRepository.Save(member);
            }

            var warningNumber = warningCandidates.Max(c => c.GoodEncounterAttackWarningCount);
            var text = warningNumber == 1
                ? "1st warning"
                : "2nd and last warning";

            SayOnBoth("Alignment Warning", $"Attacking good {encounterName} gives good characters a {text}.");
            return;
        }

        var changed = new List<string>();
        foreach (var member in goodMembers)
        {
            var newAlignment = _random.Next(0, 2) == 0 ? Alignment.ChaoticNeutral : Alignment.NeutralEvil;
            member.Alignment = newAlignment;
            member.ApplyAlignmentBasedClassConsequences();
            member.GoodEncounterAttackWarningCount = 3;

            member.RefreshMoveFromArmorAndClass();
            member.RefreshMonkProgressionStats();

            _characterRepository.Save(member);
            changed.Add($"{member.Name} -> {newAlignment.ToAbbreviation()}");
        }

        if (changed.Count > 0)
            SayOnBoth("Alignment Shift", "Good characters attacked good encounters again and changed alignment: " + string.Join(", ", changed));
    }

    private bool AreAllEncounterGroupsNeutral(IEnumerable<string> groupMonsterNames)
    {
        var allMonsters = _monsterRepository.GetAll().ToList();
        var foundAny = false;

        foreach (var name in groupMonsterNames)
        {
            var template = FindMonsterByName(allMonsters, name);
            if (template == null)
                return false;

            foundAny = true;
            var alignment = template.Alignment ?? string.Empty;
            var hasNeutral = alignment.IndexOf("neutral", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasGood = alignment.IndexOf("good", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasEvil = alignment.IndexOf("evil", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasNeutral || hasGood || hasEvil)
                return false;
        }

        return foundAny;
    }

    private void TryRandomEncounter(double? chanceOverride = null, bool forceInLair = false)
    {
        var configuredChance = chanceOverride
            ?? GameRulesProvider.ClampChance(GameRulesProvider.Current.MonsterEncounterChance);
        var shouldEncounter = configuredChance > 0 && _random.NextDouble() < configuredChance;
        if (!shouldEncounter)
            return;

        _combatCoordinator.ForceMonstersInLair = forceInLair;
        try
        {
            RunRandomEncounter();
        }
        finally
        {
            _combatCoordinator.ForceMonstersInLair = false;
        }
    }

    // Dungeon squares with a fixed encounter: rolled once, the first time the party steps there after
    // entering the floor. Returns true when the square is such a location (and so was not visited before).
    private bool TryEncounterAtFixedLocation()
    {
        if (!_thinWalls)
            return false;

        var location = _encounterLocations.FirstOrDefault(l => l.X == _position.X && l.Y == _position.Y);
        if (location is null || !_visitedEncounterLocations.Add((location.X, location.Y)))
            return false;

        var chance = Math.Clamp(location.ChancePercent, 0, 100);
        var roll = _random.Next(1, 101);
        var encounter = roll <= chance;

        RuleApplicationInfo.Publish(
            "HomeBrew",
            "",
            $"Fixed encounter at ({location.X},{location.Y}) on level {_currentDungeonLevel}",
            "First visit to the square after entering the floor: roll 1d100, an encounter happens on a roll of the chance or less. The monsters are in their lair when they have one.",
            "1",
            "100",
            roll.ToString(),
            $"Chance {chance}% => {(encounter ? "encounter" : "no encounter")}.");

        if (encounter)
            TryRandomEncounter(chanceOverride: 1d, forceInLair: true);

        return true;
    }

    private void LoadEncounterLocationsForLevel(int level)
    {
        _visitedEncounterLocations.Clear();
        _encounterLocations = _thinWalls ? _dungeonLayouts.LoadEncounterLocations(level) : Array.Empty<DungeonEncounterLocation>();
        _darkAreas = _thinWalls ? _dungeonLayouts.LoadDarkAreas(level) : Array.Empty<DungeonDarkArea>();
    }

    private void RunRandomEncounter()
    {
        var firstGroupRoll = RollDungeonMonsterForLevelWithCount(_currentDungeonLevel);
        var monsterName = firstGroupRoll?.MonsterName;

        if (_currentDungeonLevel == 10)
        {
            for (var reroll = 0; reroll < 10 && string.Equals(monsterName, "No Encounter", StringComparison.OrdinalIgnoreCase); reroll++)
            {
                firstGroupRoll = RollDungeonMonsterForLevelWithCount(_currentDungeonLevel);
                monsterName = firstGroupRoll?.MonsterName;
            }
        }

        if (string.IsNullOrWhiteSpace(monsterName))
            monsterName = LevelOneMonsters[_random.Next(LevelOneMonsters.Length)];

        if (string.Equals(monsterName, "No Encounter", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(monsterName, "Adventurer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(monsterName, "Character", StringComparison.OrdinalIgnoreCase))
        {
            ResolveCharacterEncounter(_currentDungeonLevel, firstGroupRoll?.CountOverride);
            PublishToViewer();
            return;
        }

        var party = LoadEncounterParty();
        if (party.Count == 0)
        {
            SayOnBoth("Monsters", $"Encounter!\n\n{monsterName}\n\n(No active party members found)");
            return;
        }

        var groupRoll = _random.Next(1, 101);
        var numberOfGroups = groupRoll switch
        {
            1 => 4,
            <= 2 => 3,
            <= 4 => 2,
            _ => 1
        };

        RuleApplicationInfo.Publish(
            "HomeBrew",
            "",
            "Roll number of monster groups",
            "Chance for multiple groups uses 1d100:\n• 01 → 4 groups (1%)\n• 02-03 → 3 groups (4%)\n• 04-08 → 2 groups (14%)\n• 09-100 → 1 group (81%)",
            "1",
            "100",
            groupRoll.ToString(),
            $"Encounter has {numberOfGroups} monster group(s)."
        );

        CombatOutcome outcome;
        if (numberOfGroups > 1)
        {
            var allMonsters = _monsterRepository.GetAll().ToList();
            var primaryGroupAxis = GetEncounterMoralAxis(FindMonsterByName(allMonsters, monsterName));

            var groups = new List<(string name, int count)>
            {
                (monsterName, ResolveEncounterGroupCount(monsterName, firstGroupRoll?.CountOverride))
            };

            for (int i = 1; i < numberOfGroups; i++)
            {
                EncounterRoll? additionalRoll = null;
                string? additionalMonsterName = null;
                var foundMatchingAlignment = false;
                var filteredOutByAlignment = 0;
                var filteredAlignmentDetails = new List<string>();

                for (int reroll = 0; reroll < 30; reroll++)
                {
                    additionalRoll = RollDungeonMonsterForLevelWithCountExcludingCharacter(_currentDungeonLevel);
                    additionalMonsterName = additionalRoll?.MonsterName;
                    if (string.IsNullOrWhiteSpace(additionalMonsterName))
                        additionalMonsterName = LevelOneMonsters[_random.Next(LevelOneMonsters.Length)];

                    if (!primaryGroupAxis.HasValue)
                    {
                        foundMatchingAlignment = true;
                        break;
                    }

                    var candidateMonster = FindMonsterByName(allMonsters, additionalMonsterName);
                    var candidateAxis = GetEncounterMoralAxis(candidateMonster);
                    if (!candidateAxis.HasValue || candidateAxis.Value == primaryGroupAxis.Value)
                    {
                        foundMatchingAlignment = true;
                        break;
                    }

                    filteredOutByAlignment++;
                    var expectedAlignment = FormatEncounterMoralAxis(primaryGroupAxis.Value);
                    var actualAlignment = string.IsNullOrWhiteSpace(candidateMonster?.Alignment)
                        ? "Unknown"
                        : candidateMonster!.Alignment;
                    filteredAlignmentDetails.Add($"{additionalMonsterName}: expected {expectedAlignment}, actual {actualAlignment}");
                }

                if (filteredOutByAlignment > 0)
                {
                    var detailsText = filteredAlignmentDetails.Count == 0
                        ? string.Empty
                        : $" {string.Join("; ", filteredAlignmentDetails)}.";

                    RuleApplicationInfo.Publish(
                        "Adnd",
                        "Encounter groups",
                        "Filter additional monster groups by alignment",
                        "When multiple monster groups are rolled, extra groups (after the first) are rerolled until alignment matches the first group (or unknown alignment).",
                        "1",
                        "30",
                        filteredOutByAlignment.ToString(),
                        $"Filtered out {filteredOutByAlignment} additional group candidate(s) due to alignment mismatch.{detailsText}"
                    );
                }

                if (!foundMatchingAlignment || string.IsNullOrWhiteSpace(additionalMonsterName))
                    break;

                groups.Add((additionalMonsterName, ResolveEncounterGroupCount(additionalMonsterName, additionalRoll?.CountOverride)));
            }

            var askedLeaveOrAttack = false;

            if (AreAllEncounterGroupsNeutral(groups.Select(g => g.name)) && _random.Next(1, 101) <= 50)
            {
                var distinctNames = groups.Select(g => g.name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var neutralName = distinctNames.Count == 1
                    ? distinctNames[0]
                    : string.Join(", ", distinctNames);

                if (AskNeutralEncounterOnBoth(neutralName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
            }

            var isFriendlyGoodEncounter = ShouldOfferFriendlyEncounterChoice(party, groups.Select(g => g.name));
            if (isFriendlyGoodEncounter)
            {
                var distinctNames = groups.Select(g => g.name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var friendlyName = distinctNames.Count == 1
                    ? distinctNames[0]
                    : string.Join(", ", distinctNames);

                if (AskFriendlyEncounterOnBoth(friendlyName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
                ApplyGoodEncounterAttackConsequences(party, friendlyName);
            }

            foreach (var member in party.Where(c => !c.IsGoodAlignment() && c.GoodEncounterAttackWarningCount > 0))
            {
                member.GoodEncounterAttackWarningCount = 0;
                _characterRepository.Save(member);
            }

            outcome = _combatCoordinator.StartEncounterWithGroupCounts(
                this,
                groups,
                party,
                _characterRepository,
                _currentDungeonLevel,
                skipSurpriseRoll: askedLeaveOrAttack,
                dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        }
        else
        {
            var numberOfMonsters = ResolveEncounterGroupCount(monsterName, firstGroupRoll?.CountOverride);
            var encounterMonsterLevelForDragonProfile = firstGroupRoll?.EncounterMonsterLevel ?? _currentDungeonLevel;
            var dragonHitPointsPerDieProfile = ResolveDragonHitPointsPerDieProfile(
                monsterName,
                encounterMonsterLevelForDragonProfile,
                numberOfMonsters);
            var askedLeaveOrAttack = false;

            if (AreAllEncounterGroupsNeutral(new[] { monsterName }) && _random.Next(1, 101) <= 50)
            {
                if (AskNeutralEncounterOnBoth(monsterName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
            }

            var isFriendlyGoodEncounter = ShouldOfferFriendlyEncounterChoice(party, new[] { monsterName });
            if (isFriendlyGoodEncounter)
            {
                if (AskFriendlyEncounterOnBoth(monsterName) == DialogResult.No)
                {
                    PublishToViewer();
                    return;
                }

                askedLeaveOrAttack = true;
                ApplyGoodEncounterAttackConsequences(party, monsterName);
            }

            foreach (var member in party.Where(c => !c.IsGoodAlignment() && c.GoodEncounterAttackWarningCount > 0))
            {
                member.GoodEncounterAttackWarningCount = 0;
                _characterRepository.Save(member);
            }

            outcome = _combatCoordinator.StartEncounter(
                this,
                monsterName,
                numberOfMonsters,
                party,
                _characterRepository,
                encounterMonsterLevelForDragonProfile,
                skipSurpriseRoll: askedLeaveOrAttack,
                dragonHitPointsPerDieProfile: dragonHitPointsPerDieProfile,
                dungeonDay: _partyRepository.Load().DungeonDaysElapsed);
        }

        if (outcome == CombatOutcome.Defeat)
            HandlePartyDefeatAtCurrentCell();

        PublishToViewer();
    }

    private sealed record EncounterRoll(string MonsterName, int? CountOverride, int? EncounterMonsterLevel);

    private void HandlePartyDefeatAtCurrentCell()
    {
        var partyData = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var movedOut = new List<string>();

        foreach (var memberName in partyData.Members)
        {
            if (!roster.TryGetValue(memberName, out var character))
                continue;

            character.DungeonLevel = _currentDungeonLevel;
            character.DungeonCellX = _position.X;
            character.DungeonCellY = _position.Y;
            character.AddStatus(CharacterStatus.Out);
            movedOut.Add(character.Name);
            _characterRepository.Save(character);
        }

        if (movedOut.Count > 0)
        {
            RuleApplicationInfo.Publish(
                $"Party defeat: moved characters to dungeon position L{_currentDungeonLevel} ({_position.X},{_position.Y}) and marked OUT: {string.Join(", ", movedOut)}.");
        }

        partyData.Members.Clear();
        partyData.CurrentShopperIndex = -1;
        _partyRepository.Save(partyData);

        Close();
    }

    private List<Character> LoadEncounterParty()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<Character>();

        foreach (var memberName in party.Members)
        {
            if (roster.TryGetValue(memberName, out var c)
                && !c.HasStatus(CharacterStatus.Out))
                result.Add(c);
        }

        return result;
    }

    private readonly HashSet<(int Level, int X, int Y, WallSide Side)> _revealedSecretDoors = new();

    // S)earch: looks for secret doors on the square's edges and for lost characters lying here.
    // Each hidden door is found on a 1 in 6; elves and half-elves search at 2 in 6.
    private void SearchArea()
    {
        var foundSomething = false;

        if (_thinWalls && _walls is not null)
        {
            var chance = GetViewerParty().Any(c => c.Race is Race.Elf or Race.HalfElf && !c.HasStatus(CharacterStatus.Dead)) ? 2 : 1;
            foreach (var side in _walls.HiddenSecretDoorSides(_position.X, _position.Y).ToList())
            {
                var roll = _random.Next(1, 7);
                var found = roll <= chance;
                RuleApplicationInfo.Publish(
                    "AD&D 1e",
                    "PHB",
                    "Searching for secret doors",
                    "Roll 1d6 per hidden door on the square: found on a 1 (1-2 when an elf or half-elf searches).",
                    "1",
                    "6",
                    roll.ToString(),
                    found ? $"Secret door found to the {side}." : "Nothing found.");

                if (!found || !_walls.RevealSecretDoor(_position.X, _position.Y, side))
                    continue;

                _revealedSecretDoors.Add((_currentDungeonLevel, _position.X, _position.Y, side));
                foundSomething = true;
                SayOnBoth("Search", $"THE PARTY FINDS A SECRET DOOR TO THE {side.ToString().ToUpperInvariant()}!");
            }
        }

        if (_thinWalls && DungeonLookAroundItems.TryGetValue((_currentDungeonLevel, _position), out var hidden) && !PartyHasItem(hidden.Item))
        {
            FindLookAroundItem(hidden.Item);
            foundSomething = true;
        }

        if (HasLostCharacterAtCurrentPosition())
        {
            TryRecoverOutCharactersAtCurrentPosition();
            return;
        }

        if (!foundSomething)
            SayOnBoth("Search", "The party searches but finds nothing.");
    }

    private bool HasLostCharacterAtCurrentPosition()
    {
        var party = _partyRepository.Load();
        return _characterRepository.GetAll().Any(c => c.HasStatus(CharacterStatus.Out)
            && c.DungeonLevel == _currentDungeonLevel
            && c.DungeonCellX == _position.X
            && c.DungeonCellY == _position.Y
            && !party.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
    }

    private void TryRecoverOutCharactersAtCurrentPosition()
    {
        var party = _partyRepository.Load();
        if (party.Members.Count >= 6)
            return;

        var roster = _characterRepository.GetAll().ToList();
        var outAtCurrentPosition = roster
            .Where(c => c.HasStatus(CharacterStatus.Out))
            .Where(c => c.DungeonLevel == _currentDungeonLevel
                        && c.DungeonCellX == _position.X
                        && c.DungeonCellY == _position.Y)
            .Where(c => !party.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var recoverable = outAtCurrentPosition
            .Where(c => !c.HasStatus(CharacterStatus.Ashes))
            .ToList();

        if (recoverable.Count == 0)
        {
            if (outAtCurrentPosition.Count > 0)
            {
                var unavailable = string.Join(", ", outAtCurrentPosition.Select(c => $"{c.Name} ({c.Status})"));
                SayOnBoth("Inspect Area", $"Found OUT character(s) here, but unavailable for recovery: {unavailable}.");
                return;
            }

            SayOnBoth("Inspect Area", "No out character found here.");
            return;
        }

        var ask = AskOnBoth("Inspect Area", "Add found character to party (Y/N)?");
        if (ask != DialogResult.Yes)
            return;

        var freeSlots = Math.Max(0, 6 - party.Members.Count);
        if (freeSlots <= 0)
        {
            SayOnBoth("Inspect Area", "Party is full.");
            return;
        }

        var picks = new List<Character>();
        var remaining = recoverable.ToList();
        while (remaining.Count > 0 && picks.Count < freeSlots)
        {
            Character chosen;
            if (remaining.Count == 1)
            {
                chosen = remaining[0];
            }
            else
            {
                var prompt = new StringBuilder();
                prompt.AppendLine("Choose character to add:");
                prompt.AppendLine();
                for (int i = 0; i < remaining.Count; i++)
                    prompt.AppendLine($"{i + 1}. {remaining[i].Name}");

                var selected = PromptForNumber("Recover Character", prompt.ToString(), 1, remaining.Count);
                if (!selected.HasValue)
                    break;

                chosen = remaining[selected.Value - 1];
            }

            chosen.RemoveStatus(CharacterStatus.Out);
            chosen.RemoveStatus(CharacterStatus.Lost);
            if (chosen.CurrentHitPoints <= 0 && !chosen.HasStatus(CharacterStatus.Dead) && !chosen.HasStatus(CharacterStatus.Ashes) && !chosen.HasStatus(CharacterStatus.Lost))
                chosen.CurrentHitPoints = 1;

            _characterRepository.Save(chosen);

            if (!party.Members.Contains(chosen.Name, StringComparer.OrdinalIgnoreCase))
                party.Members.Add(chosen.Name);

            picks.Add(chosen);
            remaining.RemoveAll(c => string.Equals(c.Name, chosen.Name, StringComparison.OrdinalIgnoreCase));

            if (remaining.Count > 0 && picks.Count < freeSlots)
            {
                var addMore = AskOnBoth("Recover Character", "Add another out character from this location?");
                if (addMore != DialogResult.Yes)
                    break;
            }
        }

        if (picks.Count > 0)
        {
            _partyRepository.Save(party);
            RuleApplicationInfo.Publish(
                $"Recovered out character(s) at L{_currentDungeonLevel} ({_position.X},{_position.Y}): {string.Join(", ", picks.Select(c => c.Name))}. Status changed OUT->IN and added to party.");
            SayOnBoth("Inspect Area", $"Added to party: {string.Join(", ", picks.Select(c => c.Name))}.");
        }
    }
}
