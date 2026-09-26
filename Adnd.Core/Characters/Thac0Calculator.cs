namespace Adnd.Core.Characters;

public static class Thac0Calculator
{
    // Return THAC0 (To Hit Armor Class 0) based on class and level per AD&D 1e rules.
    // THAC0 decreases as level increases (lower is better).
    public static int GetThac0(CharacterClass cls, int level)
    {
        var effectiveLevel = Math.Max(1, level);

        // AD&D 1e THAC0 progression by class group:
        // Fighters (Fighter, Paladin, Ranger): start at 20, improve by 1 every level
        // Clerics/Druids: start at 20, improve by 2 every 3 levels
        // Thieves/Assassins: start at 20, improve by 1 every 2 levels
        // Magic-Users/Illusionists: start at 20, improve by 1 every 3 levels
        // Monks: start at 20, improve by 1 every 2 levels (similar to thieves)
        // Bards: start at 20, improve by 1 every 2 levels (thief-like)
        // Progression caps per DMG p.74:
        // - Cleric/Druid/Monk stop improving after level 19
        // - Fighter/Paladin/Ranger/Bard stop improving after level 17
        // - Magic-User/Illusionist stop improving after level 21
        // - Thief/Assassin stop improving after level 21

        return cls switch
        {
            CharacterClass.Fighter or CharacterClass.Paladin or CharacterClass.Ranger
                => 20 - (Math.Min(17, effectiveLevel) - 1),
            CharacterClass.Bard
                => 20 - (Math.Min(17, effectiveLevel) - 1),
            CharacterClass.Cleric or CharacterClass.Druid
                => 20 - ((Math.Min(19, effectiveLevel) - 1) / 3) * 2,
            CharacterClass.Monk
                => 20 - (Math.Min(19, effectiveLevel) - 1) / 2,
            CharacterClass.Thief or CharacterClass.Assassin
                => 20 - (Math.Min(21, effectiveLevel) - 1) / 2,
            CharacterClass.MagicUser or CharacterClass.Illusionist
                => 20 - (Math.Min(21, effectiveLevel) - 1) / 3,
            _ => 20
        };
    }
}

