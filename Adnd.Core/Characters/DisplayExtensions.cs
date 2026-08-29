namespace Adnd.Core.Characters;

public static class DisplayExtensions
{
    public static string ToDisplayString(this Race r)
    {
        return r switch
        {
            Race.HalfElf => "Half-Elf",
            Race.HalfOrc => "Half-Orc",
            _ => r.ToString()
        };
    }

    public static string ToDisplayString(this CharacterClass c)
    {
        return c switch
        {
            CharacterClass.MagicUser => "Magic-User",
            _ => c.ToString()
        };
    }
    
    public static string ToDisplayString(this Spells.SpellClass c)
    {
        return c switch
        {
            Spells.SpellClass.MagicUser => "Magic-User",
            _ => c.ToString()
        };
    }
    
    public static string ToDisplayString(this Alignment a)
    {
        return a switch
        {
            Alignment.LawfulGood => "Lawful Good",
            Alignment.NeutralGood => "Neutral Good",
            Alignment.ChaoticGood => "Chaotic Good",
            Alignment.LawfulNeutral => "Lawful Neutral",
            Alignment.TrueNeutral => "True Neutral",
            Alignment.ChaoticNeutral => "Chaotic Neutral",
            Alignment.LawfulEvil => "Lawful Evil",
            Alignment.NeutralEvil => "Neutral Evil",
            Alignment.ChaoticEvil => "Chaotic Evil",
            _ => a.ToString()
        };
    }
}
