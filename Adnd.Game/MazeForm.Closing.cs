using Adnd.Core.Characters;
using Adnd.Core.Config;
using System.Linq;
using System.Windows.Forms;

namespace Adnd.Game;

// Closing the dungeon window with the X: confirm it and apply PenaltyClosingDungeonView.
public sealed partial class MazeForm
{
    private bool _closeApproved;
    private bool _exitAfterBattle;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closeApproved && !e.Cancel && e.CloseReason == CloseReason.UserClosing)
        {
            if (!ConfirmClosingDungeonView())
            {
                e.Cancel = true;
                return;
            }

            _closeApproved = true;

            // A fight runs a nested loop inside this window; end it first and finish closing when it has returned.
            if (_encounterForm is { Visible: true } fight)
            {
                _exitAfterBattle = true;
                e.Cancel = true;
                fight.AbortEmbedded();
                return;
            }

            ApplyClosingPenalty();
        }

        base.OnFormClosing(e);
    }

    // At the dungeon entrance (level 1, 0/0) or with no party left, the party is effectively in town.
    private bool PartyIsInTown() =>
        (_currentDungeonLevel == 1 && _position.X == 0 && _position.Y == 0)
        || _partyRepository.Load().Members.Count == 0;

    private bool ConfirmClosingDungeonView()
    {
        if (PartyIsInTown())
            return true;

        var question = GameRulesProvider.Current.PenaltyClosingDungeonView switch
        {
            ClosingDungeonViewPenalty.AgeOneYear =>
                "Are you sure? Everyone in the party will be 1 year older if you close this window. The party returns to town.",
            ClosingDungeonViewPenalty.PartyLost =>
                "Are you sure? The party will be lost in the dungeon if you close this window. (Y/N)",
            _ => null
        };

        return question == null || AskOnBoth("Close", question) == DialogResult.Yes;
    }

    private void ApplyClosingPenalty()
    {
        RestoreStrengthDrainedOnDungeonExit();
        ClearProtectOnDungeonExit();
        RevertLycanthropeTransformationsOnDungeonExit();

        if (PartyIsInTown())
            return;

        switch (GameRulesProvider.Current.PenaltyClosingDungeonView)
        {
            case ClosingDungeonViewPenalty.AgeOneYear:
                foreach (var member in LoadClosingParty())
                {
                    member.AdvanceAgeByDays(365);
                    _characterRepository.Save(member);
                }

                break;

            case ClosingDungeonViewPenalty.PartyLost:
                foreach (var member in LoadClosingParty())
                {
                    member.DungeonLevel = _currentDungeonLevel;
                    member.DungeonCellX = _position.X;
                    member.DungeonCellY = _position.Y;
                    member.AddStatus(CharacterStatus.Out);
                    _characterRepository.Save(member);
                }

                var party = _partyRepository.Load();
                party.Members.Clear();
                party.CurrentShopperIndex = -1;
                _partyRepository.Save(party);
                break;
        }
    }

    private List<Character> LoadClosingParty()
    {
        var roster = _characterRepository.GetAll().ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        return _partyRepository.Load().Members
            .Select(n => roster.TryGetValue(n, out var c) ? c : null)
            .OfType<Character>()
            .ToList();
    }

    // Called once the fight that was running when the window was closed has returned.
    private void FinishClosingAfterBattle()
    {
        if (!_exitAfterBattle)
            return;

        _exitAfterBattle = false;
        ApplyClosingPenalty();
        Close();
    }
}
