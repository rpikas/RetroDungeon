using Adnd.Core.Characters;

namespace Adnd.Core.Combat.Sessions;

public static class CombatFormation
{
    public static bool CanFight(Character member, CombatSession? session = null) =>
        member.CurrentHitPoints > 0
        && (member.Status & (CharacterStatus.Dead | CharacterStatus.Ashes | CharacterStatus.Lost
            | CharacterStatus.Out | CharacterStatus.Paralyzed | CharacterStatus.Petrified
            | CharacterStatus.Asleep | CharacterStatus.Confused)) == 0
        && (session == null || session.GetPartyThunderboltsRestRounds(member.Name) == 0);

    public static int GetFightingRank(IEnumerable<Character> party, Character member, CombatSession? session = null)
    {
        var rank = 0;
        foreach (var candidate in party)
        {
            if (!CanFight(candidate, session) || candidate.HasStatus(CharacterStatus.Swallowed))
                continue;

            rank++;
            if (ReferenceEquals(candidate, member))
                return rank;
        }
        return -1;
    }
}
