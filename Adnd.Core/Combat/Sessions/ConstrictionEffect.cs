using Adnd.Core.Characters;

namespace Adnd.Core.Combat.Sessions;

public sealed class ConstrictionEffect
{
    public ConstrictionEffect(Character victim, MonsterInstance constrictor)
    {
        Victim = victim;
        Constrictor = constrictor;
    }

    public Character Victim { get; }
    public MonsterInstance Constrictor { get; }
    public int? UncoilRoundsRemaining { get; set; }
}
