using System.IO;
using System.Text.Json;

namespace Adnd.Core.Config;

public static class GameRulesProvider
{
    private const string DefaultRulesPath = "Data/Config/game-rules.json";
    private static GameRules _current = new();

    static GameRulesProvider()
    {
        Load(DefaultRulesPath);
    }

    public static GameRules Current
    {
        get => _current;
        set => _current = Normalize(value);
    }

    public static double ClampChance(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        if (value < 0)
            return 0;

        if (value > 1)
            return 1;

        return value;
    }

    public static void Load(string? path = null)
    {
        var resolved = ResolvePath(path);
        var options = CreateSerializerOptions();

        if (!File.Exists(resolved))
        {
            Current = new GameRules();
            Save(resolved);
            return;
        }

        try
        {
            var json = File.ReadAllText(resolved);
            var loaded = JsonSerializer.Deserialize<GameRules>(json, options);
            Current = loaded ?? new GameRules();
        }
        catch
        {
            Current = new GameRules();
        }
    }

    public static void Save(string? path = null)
    {
        var resolved = ResolvePath(path);
        var folder = Path.GetDirectoryName(resolved);
        var options = CreateSerializerOptions();

        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var json = JsonSerializer.Serialize(Current, options);

        File.WriteAllText(resolved, json);
    }

    private static string ResolvePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? DefaultRulesPath : path;
    }

    private static GameRules Normalize(GameRules? rules)
    {
        if (rules == null)
            return new GameRules();

        if (rules.ForegroundColor.IsEmpty)
            rules.ForegroundColor = System.Drawing.Color.Green;

        rules.TreasureFindChance = ClampChance(rules.TreasureFindChance);
        rules.MonsterEncounterChance = ClampChance(rules.MonsterEncounterChance);

        if (double.IsNaN(rules.XpMultiplier) || double.IsInfinity(rules.XpMultiplier) || rules.XpMultiplier < 0)
            rules.XpMultiplier = 0;

        if (rules.CharacterCreationMinGold < 0)
            rules.CharacterCreationMinGold = 0;

        if (rules.CharacterCreationMaxGold < 0)
            rules.CharacterCreationMaxGold = 0;

        if (rules.CharacterCreationMaxGold < rules.CharacterCreationMinGold)
        {
            var temp = rules.CharacterCreationMinGold;
            rules.CharacterCreationMinGold = rules.CharacterCreationMaxGold;
            rules.CharacterCreationMaxGold = temp;
        }

        return rules;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new ColorJsonConverter());
        return options;
    }
}
