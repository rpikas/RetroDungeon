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

// Stepping, stairs, light, magic seals, treasure finding and dungeon exit effects.
public sealed partial class MazeForm
{
    private void TryMoveForward()
    {
        var v = GetForwardVector(_direction);
        var candidate = WrapCell(new Point(_position.X + v.X, _position.Y + v.Y));
        if (!CanStepForward())
        {
            ShowCenteredMessage("YOU CAN'T WALK THROUGH WALLS!");
            return;
        }

        if (_thinWalls && DungeonRestrictedSquares.TryGetValue((_currentDungeonLevel, candidate), out var restriction)
            && !PartyHasItem(restriction.Item))
        {
            SayOnBoth("Message", restriction.Message);
            return;
        }

        if (_thinWalls && DungeonGatedDoors.TryGetValue((_currentDungeonLevel, _position, _direction), out var gate)
            && !PartyHasItem(gate.Item))
        {
            SayOnBoth("Sliding Wall", gate.Message);
            return;
        }

        _position = candidate;
        ApplyPoisonDamageForStep();

        // Draw the new square (walls, doors) before any sign, message or dialog appears.
        UpdateLighting();
        Refresh();
        PublishToViewer();

        if (IsElevatorSquare(_position))
        {
            if (_thinWalls && _currentDungeonLevel is 8 or 9 && !PartyHasItem("Blue Ribbon"))
                SayOnBoth("Elevator", "THE ELEVATOR WON'T MOVE WITHOUT THE BLUE RIBBON.");
            else
                ShowDungeonElevatorDialog();
        }

        if (_thinWalls && _currentDungeonLevel == 1 && _position.X == 12 && _position.Y == 9)
            SayOnBoth("Sign", "A LARGE SIGN ON THE WALL READ:" + Environment.NewLine + "** AREA OUT OF BOUNDS! **");

        ProcessTreasureFindingStep();
        ProcessLightStep();

        if (_thinWalls && DungeonWarps.TryGetValue((_currentDungeonLevel, _position), out var warpTo))
        {
            _position = warpTo;
            UpdateLighting();
            Invalidate();
            SayOnBoth("Teleport", "A strange force teleports the party to another place!");
        }

        if (ProcessLevelNineStep())
            return;

        if (ProcessWerdnaStep())
            return;

        var ambushed = ProcessLevelFourAmbushStep();

        ProcessPitStep();
        ProcessTurnTableStep();
        var alarm = ProcessSquareMessageStep();
        if (ProcessLevelOneScriptStep())
            return;
        if (_currentDungeonLevel == 1
            && _position.X == 0
            && _position.Y == 0
            && !HasRecoverableOutCharacterAtCurrentPosition())
        {
            var result = AskStairsOnBoth(up: true);

            if (result == DialogResult.Yes)
            {
                LeaveDungeonForTown();
                return;
            }
        }

        if (_thinWalls && StairsAt(_position) is var kind && kind != StairsKind.None
            && !(kind == StairsKind.Up && _currentDungeonLevel == 1))
        {
            var target = _currentDungeonLevel + (kind == StairsKind.Down ? 1 : -1);
            var answer = AskStairsOnBoth(kind == StairsKind.Up);
            if (answer == DialogResult.Yes)
            {
                TakeStairsToLevel(target);
                return;
            }
        }

        if (ambushed)
        {
            // The scripted ambush was this step's encounter.
        }
        else if (alarm)
        {
            TryRandomEncounter(chanceOverride: 1d, forceInLair: true);
        }
        else if (TryEncounterAtFixedLocation())
        {
            // The square's own encounter replaces the usual random check for this step.
        }
        else
        {
            TryRandomEncounter();
        }

        var delayMs = Math.Max(0, GameRulesProvider.Current.DelayInMsbetweenActions);
        if (delayMs > 0)
            Thread.Sleep(delayMs);
    }

    // Flashes a short message in the middle of the view, then clears it.
    private void ShowCenteredMessage(string text)
    {
        _centerMessage = text;
        Refresh();
        Thread.Sleep(600);
        _centerMessage = null;
        Invalidate();
    }

    private void LoadLightFromParty()
    {
        var party = _partyRepository.Load();
        _lightStepsRemaining = Math.Max(0, party.LightStepsRemaining);
        _continualLightActive = party.ContinualLightActive;
        UpdateLighting();
    }

    private void UpdateMagicSeal()
    {
        var sealedNow = _thinWalls
            && DungeonMagicSealedAreas.TryGetValue(_currentDungeonLevel, out var area)
            && _position.X >= area.X1 && _position.X <= area.X2
            && _position.Y >= area.Y1 && _position.Y <= area.Y2;

        if (sealedNow == Adnd.Core.Spells.Casting.SpellCastingService.MagicSealed)
            return;

        Adnd.Core.Spells.Casting.SpellCastingService.MagicSealed = sealedNow;
        if (!sealedNow)
            return;

        // Magic cast before entering is cancelled as well.
        var hadLight = _lightStepsRemaining > 0 || _continualLightActive;
        _lightStepsRemaining = 0;
        _continualLightActive = false;
        var party = _partyRepository.Load();
        party.LightStepsRemaining = 0;
        party.ContinualLightActive = false;
        _partyRepository.Save(party);

        SayOnBoth("Hall of Silence", "MAGIC IS SEALED IN THIS SPACE." + Environment.NewLine
            + (hadLight ? "ALL MAGIC CAST BEFORE ENTERING IS CANCELED." : "NO SPELL CAN BE CAST HERE."));
    }

    private void UpdateLighting()
    {
        UpdateMagicSeal();
        _inMagicalDarkness = _darkAreas.Any(a => a.Contains(_position.X, _position.Y));
        _depthLevels = _inMagicalDarkness ? DarkDepthLevels
            : _continualLightActive ? ContinualLightDepthLevels
            : _lightStepsRemaining > 0 ? LightDepthLevels
            : BaseDepthLevels;
        Invalidate();
    }

    // Each step uses up one step of Light. Magical darkness snuffs out Continual Light for good.
    private void ProcessLightStep()
    {
        var enteredDarkness = _continualLightActive && IsDark(_position);
        if (!enteredDarkness && _lightStepsRemaining <= 0)
        {
            UpdateLighting();
            return;
        }

        string? message = null;
        var party = _partyRepository.Load();
        var changed = false;

        if (enteredDarkness)
        {
            _continualLightActive = false;
            party.ContinualLightActive = false;
            changed = true;
            message = "The magical darkness snuffs out the Continual Light.";
        }

        if (_lightStepsRemaining > 0)
        {
            _lightStepsRemaining--;
            party.LightStepsRemaining = _lightStepsRemaining;
            changed = true;
            if (_lightStepsRemaining == 0 && message is null && !_continualLightActive)
                message = "The magical light fades.";
        }

        if (changed)
            _partyRepository.Save(party);

        UpdateLighting();

        if (message is not null)
            SayOnBoth("Light", message);
    }

    private void ProcessTreasureFindingStep()
    {
        var party = _partyRepository.Load();
        if (!party.TreasureFindingActive)
            return;

        if (party.TreasureFindingStepsRemaining <= 0)
        {
            party.TreasureFindingActive = false;
            party.TreasureFindingStepsRemaining = 0;
            _partyRepository.Save(party);
            SayOnBoth("Treasure Finding", "The potion's sensing magic fades.");
            return;
        }

        EnsureTreasureFindingTarget(party);

        if (party.TreasureFindingTargetLevel == _currentDungeonLevel
            && party.TreasureFindingTargetX == _position.X
            && party.TreasureFindingTargetY == _position.Y)
        {
            AwardTreasureFindingCacheToParty(party);
            return;
        }

        var direction = GetTreasureFindingDirectionHint(
            _position,
            _currentDungeonLevel,
            new Point(party.TreasureFindingTargetX, party.TreasureFindingTargetY),
            party.TreasureFindingTargetLevel);

        party.TreasureFindingStepsRemaining = Math.Max(0, party.TreasureFindingStepsRemaining - 1);
        var stepsLeft = party.TreasureFindingStepsRemaining;

        if (stepsLeft == 0)
        {
            party.TreasureFindingActive = false;
            _partyRepository.Save(party);
            SayOnBoth("Treasure Finding", "The magical trail fades before the treasure is found.");
            return;
        }

        _partyRepository.Save(party);
        SayOnBoth("Treasure Finding", $"The treasure lies to the {direction}. ({stepsLeft} step(s) remaining)");
    }

    private void EnsureTreasureFindingTarget(Party party)
    {
        if (party.TreasureFindingTargetLevel > 0
            && party.TreasureFindingTargetX >= 0
            && party.TreasureFindingTargetY >= 0)
            return;

        var candidateLevels = Enumerable
            .Range(MinDungeonLevel, MaxDungeonLevel - MinDungeonLevel + 1)
            .OrderBy(_ => _random.Next())
            .ToList();

        foreach (var level in candidateLevels)
        {
            var candidates = GetOpenTiles(level)
                .Where(p => level != _currentDungeonLevel || p.X != _position.X || p.Y != _position.Y)
                .ToList();

            if (candidates.Count == 0)
                candidates = GetOpenTiles(level);

            if (candidates.Count == 0)
                continue;

            var target = candidates[_random.Next(candidates.Count)];
            party.TreasureFindingTargetLevel = level;
            party.TreasureFindingTargetX = target.X;
            party.TreasureFindingTargetY = target.Y;

            RuleApplicationInfo.Publish($"Potion of Treasure Finding cache placed on cleared square at L{party.TreasureFindingTargetLevel} ({party.TreasureFindingTargetX},{party.TreasureFindingTargetY}).");
            return;
        }

        // Fallback: keep effect active but with no target if no valid tile was found.
        party.TreasureFindingTargetLevel = 0;
        party.TreasureFindingTargetX = -1;
        party.TreasureFindingTargetY = -1;
        RuleApplicationInfo.Publish("Potion of Treasure Finding could not place cache: no cleared square found.");
    }

    private List<Point> GetOpenTiles(int level)
    {
        var previousMaze = _maze;
        var previousWalls = _walls;
        BuildMazeForLevel(level);

        var open = new List<Point>();
        for (var y = 0; y < _maze.GetLength(1); y++)
        {
            for (var x = 0; x < _maze.GetLength(0); x++)
            {
                if (_maze[x, y] == CellType.Floor)
                    open.Add(new Point(x, y));
            }
        }

        _maze = previousMaze;
        _walls = previousWalls;
        return open;
    }

    private static string GetTreasureFindingDirectionHint(Point partyPos, int partyLevel, Point targetPos, int targetLevel)
    {
        var horizontal = "none";
        var vertical = "none";
        var level = "same level";

        var dx = targetPos.X - partyPos.X;
        var dy = targetPos.Y - partyPos.Y;

        if (dx > 0)
            horizontal = "east";
        else if (dx < 0)
            horizontal = "west";

        if (dy > 0)
            vertical = "south";
        else if (dy < 0)
            vertical = "north";

        if (targetLevel > partyLevel)
            level = "up";
        else if (targetLevel < partyLevel)
            level = "down";

        return $"horizontal {horizontal}, vertical {vertical}, level {level}";
    }

    private void AwardTreasureFindingCacheToParty(Party party)
    {
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var members = party.Members
            .Where(name => roster.ContainsKey(name))
            .Select(name => roster[name])
            .ToList();

        if (members.Count == 0)
        {
            party.TreasureFindingActive = false;
            party.TreasureFindingStepsRemaining = 0;
            _partyRepository.Save(party);
            SayOnBoth("Treasure Finding", "Treasure found, but no active party members could collect it.");
            return;
        }

        var receiver = members[_random.Next(members.Count)];
        receiver.CopperPieces = Math.Max(0, receiver.CopperPieces + Math.Max(0, party.TreasureFindingCopperPieces));
        receiver.Inventory.Add(new Adnd.Core.Items.Item
        {
            Name = $"Gem Cache x{Math.Max(0, party.TreasureFindingGemCount)}",
            Type = Adnd.Core.Items.ItemType.Misc,
            Weight = 0,
            IsShopBuyable = false,
            Description = "Gem cache found via Potion of Treasure Finding."
        });

        _characterRepository.Save(receiver);

        party.TreasureFindingActive = false;
        party.TreasureFindingStepsRemaining = 0;
        _partyRepository.Save(party);

        RuleApplicationInfo.Publish($"Potion of Treasure Finding cache recovered at L{_currentDungeonLevel} {PositionText}: {party.TreasureFindingCopperPieces} cp and {party.TreasureFindingGemCount} gems.");
        SayOnBoth("Treasure Finding", $"Treasure found! {receiver.Name} collects {party.TreasureFindingCopperPieces} cp and {party.TreasureFindingGemCount} gems.");
    }

    private bool HasRecoverableOutCharacterAtCurrentPosition()
    {
        var party = _partyRepository.Load();
        return _characterRepository.GetAll()
            .Where(c => c.HasStatus(CharacterStatus.Out))
            .Where(c => c.DungeonLevel == _currentDungeonLevel
                        && c.DungeonCellX == _position.X
                        && c.DungeonCellY == _position.Y)
            .Where(c => !party.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .Any(c => !c.HasStatus(CharacterStatus.Ashes));
    }

    private void RestoreStrengthDrainedOnDungeonExit()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var restored = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (!c.RestoreStrengthDrainedUntilDungeonExit())
                continue;

            _characterRepository.Save(c);
            restored.Add(c.Name);
        }

        if (restored.Count > 0)
            SayOnBoth("Strength", $"Strength restored after leaving dungeon: {string.Join(", ", restored)}.");
    }

    private void RevertLycanthropeTransformationsOnDungeonExit()
    {
        var party = _partyRepository.Load();
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var reverted = new List<string>();

        foreach (var name in party.Members)
        {
            if (!roster.TryGetValue(name, out var c))
                continue;

            if (!c.LycanthropyTriggeredByFullMoon)
                continue;

            if (!c.EndLycanthropeTransformation())
                continue;

            _characterRepository.Save(c);
            reverted.Add(c.Name);
        }

        if (reverted.Count > 0)
            SayOnBoth("Lycanthropy", $"Lycanthrope form fades after leaving dungeon: {string.Join(", ", reverted)}.");
    }
}
