namespace Adnd.Core.Combat.Sessions;

public sealed class HornOfValhallaSummon
{
    public HornOfValhallaSummon(string summonerName, string hornTypeName, bool hostileToParty, int fighterLevel, int berserkerCount, int roundsRemaining)
    {
        SummonerName = summonerName;
        HornTypeName = hornTypeName;
        HostileToParty = hostileToParty;
        FighterLevel = Math.Max(1, fighterLevel);
        BerserkerCount = Math.Max(1, berserkerCount);
        RoundsRemaining = Math.Max(1, roundsRemaining);
    }

    public string SummonerName { get; }
    public string HornTypeName { get; }
    public bool HostileToParty { get; }
    public int FighterLevel { get; }
    public int BerserkerCount { get; }
    public int RoundsRemaining { get; set; }
}
