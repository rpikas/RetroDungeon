namespace Adnd.Core.Characters;

/// <summary>A class level lost to energy drain, kept so Restoration can give it back.</summary>
public class LevelDrainRecord
{
    public CharacterClass Class { get; set; }
    public int LevelBefore { get; set; }
    public int ExperienceBefore { get; set; }
}
