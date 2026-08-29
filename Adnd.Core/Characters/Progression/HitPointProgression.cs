namespace Adnd.Core.Characters.Progression;

public static class HitPointProgression
{
    private sealed record HitDieRule(int DieSize, int MaxHitDieLevel, int FixedAfterNameLevel);

    private static readonly Dictionary<CharacterClass, HitDieRule> Rules = new()
    {
        [CharacterClass.Fighter] = new(10, 9, 3),
        [CharacterClass.Paladin] = new(10, 9, 3),
        [CharacterClass.Ranger] = new(8, 10, 2),

        [CharacterClass.Cleric] = new(8, 9, 2),
        [CharacterClass.Druid] = new(8, 9, 2),

        [CharacterClass.Thief] = new(6, 10, 2),
        [CharacterClass.Assassin] = new(6, 10, 2),
        [CharacterClass.Bard] = new(6, 10, 2),
        [CharacterClass.Monk] = new(6, 10, 2),

        [CharacterClass.MagicUser] = new(4, 11, 1),
        [CharacterClass.Illusionist] = new(4, 10, 1)
    };
    /*
    public static int RollHitPointGain(Character character, int newLevel)
    {
        var primaryClass = character.Classes != null && character.Classes.Count > 0
            ? character.Classes[0]
            : character.Class;

        var rule = Rules.TryGetValue(primaryClass, out var r)
            ? r
            : new HitDieRule(6, 9, 1);

        int baseGain = newLevel <= rule.MaxHitDieLevel
            ? DiceRoller.Roll(1, rule.DieSize)
            : rule.FixedAfterNameLevel;

        int conMod = GetConstitutionHpAdjustment(character.Abilities.Constitution, primaryClass);
        int total = baseGain + conMod;

        return Math.Max(1, total);
    }
    */
    public static int RollHitPointGain(Character character, int newLevel)
    {
        var primaryClass = character.Classes != null && character.Classes.Count > 0
            ? character.Classes[0]
            : character.Class;

        int conMod = GetConstitutionHpAdjustment(character.Abilities.Constitution, primaryClass);

        // --- SPECIAL FIXED HP RULES ---

        // Fighter: Level 10+ → fixed 3 HP
        if (primaryClass == CharacterClass.Fighter && newLevel >= 10)
            return Math.Max(1, 3 + conMod);

        // Paladin: Level 10+ → fixed 3 HP
        if (primaryClass == CharacterClass.Paladin && newLevel >= 10)
            return Math.Max(1, 3 + conMod);

        // Ranger: Level 11+ → fixed 2 HP
        if (primaryClass == CharacterClass.Ranger && newLevel >= 11)
            return Math.Max(1, 2 + conMod);

        // Cleric: Level 10 → fixed 2 HP
        if (primaryClass == CharacterClass.Cleric && newLevel == 10)
            return Math.Max(1, 2 + conMod);

        // Magic-User: Level 11+ → fixed 1 HP
        if (primaryClass == CharacterClass.MagicUser && newLevel >= 11)
            return Math.Max(1, 1 + conMod);

        // Illusionist: Level 11+ → fixed 1 HP
        if (primaryClass == CharacterClass.Illusionist && newLevel >= 11)
            return Math.Max(1, 1 + conMod);

        // Thief: Level 10+ → fixed 2 HP
        if (primaryClass == CharacterClass.Thief && newLevel >= 10)
            return Math.Max(1, 2 + conMod);

        // --- DEFAULT RAW AD&D RULES ---

        var rule = Rules.TryGetValue(primaryClass, out var r)
            ? r
            : new HitDieRule(6, 9, 1);

        int baseGain = newLevel <= rule.MaxHitDieLevel
            ? DiceRoller.Roll(1, rule.DieSize)
            : rule.FixedAfterNameLevel;

        int total = baseGain + conMod;

        return Math.Max(1, total);
    }


    private static int GetConstitutionHpAdjustment(int con, CharacterClass cls)
    {
        bool warrior = cls is CharacterClass.Fighter or CharacterClass.Paladin or CharacterClass.Ranger;

        return con switch
        {
            <= 6 => -1,
            <= 14 => 0,
            15 => 1,
            16 => 2,
            17 => warrior ? 3 : 2,
            _ => warrior ? 4 : 2
        };
    }
}
