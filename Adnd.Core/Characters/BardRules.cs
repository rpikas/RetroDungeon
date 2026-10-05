namespace Adnd.Core.Characters;

public static class BardRules
{
    public readonly record struct BardLevelProgress(string College, int AdditionalLanguagesKnown, int CharmPercentage, int LegendLoreItemKnowledgePercentage);

    private static readonly BardLevelProgress[] Table =
    {
        new("(Probationer)", 0, 15, 0),
        new("Fochlucan", 0, 20, 5),
        new("Fochlucan", 0, 22, 7),
        new("Fochlucan", 1, 24, 10),
        new("Mac-Fuirmidh", 0, 30, 13),
        new("Mac-Fuirmidh", 1, 32, 16),
        new("Mac-Fuirmidh", 1, 34, 20),
        new("Doss", 0, 42, 25),
        new("Doss", 1, 44, 30),
        new("Canaith", 0, 50, 40),
        new("Canaith", 1, 53, 45),
        new("Cli", 0, 56, 50),
        new("Cli", 1, 59, 55),
        new("Cli", 1, 63, 60),
        new("Cli", 1, 66, 65),
        new("Cli", 1, 69, 70),
        new("Anstruth", 1, 73, 75),
        new("Anstruth", 1, 76, 80),
        new("Anstruth", 1, 79, 85),
        new("Ollamh", 1, 84, 89),
        new("Ollamh", 1, 88, 94),
        new("Magna Alumnae", 1, 95, 99),
        new("Magna Alumnae", 1, 95, 99),
    };

    public static BardLevelProgress GetProgressForLevel(int bardLevel)
    {
        if (bardLevel <= 0)
            return new BardLevelProgress("-", 0, 0, 0);

        var index = Math.Min(Table.Length - 1, bardLevel - 1);
        return Table[index];
    }

    public static int GetAdditionalLanguagesKnown(int bardLevel) => GetProgressForLevel(bardLevel).AdditionalLanguagesKnown;

    public static int GetCharmPercentage(int bardLevel) => GetProgressForLevel(bardLevel).CharmPercentage;

    public static int GetLegendLoreItemKnowledgePercentage(int bardLevel) => GetProgressForLevel(bardLevel).LegendLoreItemKnowledgePercentage;
}
