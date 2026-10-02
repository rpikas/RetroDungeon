using Adnd.Core.Spells;

namespace Adnd.Core.Characters.Progression;

public sealed class LevelUpResult
{
    public string CharacterName { get; set; } = string.Empty;
    public int OldLevel { get; set; }
    public int NewLevel { get; set; }
    public int ExperienceBefore { get; set; }
    public int ExperienceAfter { get; set; }
    public int HitPointsGained { get; set; }
    public List<(SpellClass SpellClass, List<int> OldSlots, List<int> NewSlots)> SpellSlotChanges { get; set; } = new();
    public List<string> SpellsLearned { get; set; } = new();
    public List<string> WeaponProficienciesGained { get; set; } = new();

    public bool LeveledUp => NewLevel > OldLevel;
    public int LevelsGained => Math.Max(0, NewLevel - OldLevel);
}
