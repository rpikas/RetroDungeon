namespace Adnd.Core.Combat.Sessions;

public sealed class GreenSlimeAttachment
{
    public MonsterInstance Monster { get; init; } = null!;
    public int RoundsLeft { get; set; }
}
