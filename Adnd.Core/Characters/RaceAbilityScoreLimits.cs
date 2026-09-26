namespace Adnd.Core.Characters;

public static class RaceAbilityScoreLimits
{
    public static bool IsWithinLimits(AbilityScores abilities, Race race, Gender gender)
    {
        var clamped = ClampToLimits(abilities, race, gender);
        return clamped.Strength == abilities.Strength
               && clamped.Intelligence == abilities.Intelligence
               && clamped.Wisdom == abilities.Wisdom
               && clamped.Dexterity == abilities.Dexterity
               && clamped.Constitution == abilities.Constitution
               && clamped.Charisma == abilities.Charisma;
    }

    public static AbilityScores ClampToLimits(AbilityScores abilities, Race race, Gender gender)
    {
        var limits = GetLimits(race, gender);

        return new AbilityScores
        {
            Strength = Math.Clamp(abilities.Strength, limits.StrengthMin, limits.StrengthMax),
            Intelligence = Math.Clamp(abilities.Intelligence, limits.IntelligenceMin, limits.IntelligenceMax),
            Wisdom = Math.Clamp(abilities.Wisdom, limits.WisdomMin, limits.WisdomMax),
            Dexterity = Math.Clamp(abilities.Dexterity, limits.DexterityMin, limits.DexterityMax),
            Constitution = Math.Clamp(abilities.Constitution, limits.ConstitutionMin, limits.ConstitutionMax),
            Charisma = Math.Clamp(abilities.Charisma, limits.CharismaMin, limits.CharismaMax)
        };
    }

    private static (int StrengthMin, int StrengthMax,
                    int IntelligenceMin, int IntelligenceMax,
                    int WisdomMin, int WisdomMax,
                    int DexterityMin, int DexterityMax,
                    int ConstitutionMin, int ConstitutionMax,
                    int CharismaMin, int CharismaMax)
        GetLimits(Race race, Gender gender)
    {
        var isMale = gender == Gender.Male;

        return race switch
        {
            Race.Dwarf => (8, isMale ? 18 : 17, 3, 18, 3, 18, 3, 17, 12, 19, 3, 16),
            Race.Elf => (3, isMale ? 18 : 16, 8, 18, 3, 18, 7, 19, 6, 18, 8, 18),
            Race.Gnome => (6, isMale ? 18 : 15, 7, 18, 3, 18, 3, 18, 8, 18, 3, 18),
            Race.HalfElf => (3, isMale ? 18 : 17, 4, 18, 3, 18, 6, 18, 6, 18, 3, 18),
            Race.Halfling => (6, isMale ? 17 : 14, 6, 18, 3, 17, 8, 18, 10, 19, 3, 18),
            Race.HalfOrc => (6, 18, 3, 17, 3, 14, 3, 17, 13, 19, 3, 12),
            _ => (3, 25, 3, 25, 3, 25, 3, 25, 3, 25, 3, 25)
        };
    }
}
