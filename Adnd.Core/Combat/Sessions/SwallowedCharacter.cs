using Adnd.Core.Characters;

namespace Adnd.Core.Combat.Sessions;

public sealed record SwallowedCharacter(Character Victim, MonsterInstance Worm, int SwallowedRound)
{
    public int RoundsInside(int currentRound) => Math.Max(0, currentRound - SwallowedRound);
    public int RoundsUntilDeath(int currentRound) => Math.Clamp(7 - RoundsInside(currentRound), 0, 6);
}
