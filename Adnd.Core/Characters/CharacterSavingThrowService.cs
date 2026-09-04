using System.Text.Json;

namespace Adnd.Core.Characters;

public enum SaveThrowType
{
    ParalyzationPoisonDeath,
    BreathWeapon,
    Spell
}

public sealed class CharacterSavingThrowService
{
    private readonly Lazy<Dictionary<CharacterClass, List<SaveThrowProgression>>> _table;

    public CharacterSavingThrowService()
    {
        _table = new Lazy<Dictionary<CharacterClass, List<SaveThrowProgression>>>(LoadTable);
    }

    public int GetSaveTarget(Character c, SaveThrowType type)
    {
        if (c.Classes == null || c.Classes.Count == 0)
            return 20;

        var targets = new List<int>();

        foreach (var cls in c.Classes)
        {
            var target = GetClassSaveTarget(cls, c.GetClassLevel(cls), type);
            targets.Add(target);
        }

        return targets.Count == 0 ? 20 : targets.Min();
    }

    private int GetClassSaveTarget(CharacterClass cls, int level, SaveThrowType type)
    {
        var mappedClass = MapClassForSavingThrows(cls);

        if (!_table.Value.TryGetValue(mappedClass, out var progressions) || progressions.Count == 0)
            return 20;

        var progression = progressions.FirstOrDefault(p => p.Contains(level))
                          ?? progressions.Last();

        return type switch
        {
            SaveThrowType.ParalyzationPoisonDeath => progression.ParalyzationPoisonDeath,
            SaveThrowType.BreathWeapon => progression.BreathWeapon,
            SaveThrowType.Spell => progression.Spell,
            _ => 20
        };
    }

    private static CharacterClass MapClassForSavingThrows(CharacterClass cls)
    {
        return cls switch
        {
            CharacterClass.Druid => CharacterClass.Cleric,
            CharacterClass.Paladin or CharacterClass.Ranger => CharacterClass.Fighter,
            CharacterClass.Monk or CharacterClass.Bard or CharacterClass.Assassin => CharacterClass.Thief,
            CharacterClass.Illusionist => CharacterClass.MagicUser,
            _ => cls
        };
    }

    private static Dictionary<CharacterClass, List<SaveThrowProgression>> LoadTable()
    {
        var result = new Dictionary<CharacterClass, List<SaveThrowProgression>>();

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "CharaterSavingThrows.json"),
            Path.Combine(AppContext.BaseDirectory, "CharaterSavingThrows.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Adnd.Data", "CharaterSavingThrows.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "CharaterSavingThrows.json")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null)
            return result;

        var json = File.ReadAllText(path);
        var model = JsonSerializer.Deserialize<SaveThrowRoot>(json);
        if (model?.SavingThrows == null)
            return result;

        foreach (var cls in model.SavingThrows)
        {
            if (!TryParseCharacterClass(cls.Class, out var characterClass))
                continue;

            var progression = new List<SaveThrowProgression>();
            foreach (var p in cls.Progression ?? new List<SaveThrowProgressionJson>())
            {
                if (!TryParseLevelRange(p.LevelRange, out var min, out var max))
                    continue;

                progression.Add(new SaveThrowProgression
                {
                    MinLevel = min,
                    MaxLevel = max,
                    ParalyzationPoisonDeath = p.ParalyzationPoisonDeath,
                    BreathWeapon = p.BreathWeapon,
                    Spell = p.Spell
                });
            }

            if (progression.Count > 0)
                result[characterClass] = progression;
        }

        return result;
    }

    private static bool TryParseCharacterClass(string value, out CharacterClass cls)
    {
        var normalized = (value ?? string.Empty)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalized, true, out cls);
    }

    private static bool TryParseLevelRange(string value, out int min, out int max)
    {
        min = 1;
        max = int.MaxValue;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var v = value.Trim();
        if (v.EndsWith("+", StringComparison.Ordinal))
        {
            if (int.TryParse(v.TrimEnd('+'), out var start))
            {
                min = Math.Max(1, start);
                max = int.MaxValue;
                return true;
            }

            return false;
        }

        var parts = v.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var s) && int.TryParse(parts[1], out var e))
        {
            min = Math.Max(1, Math.Min(s, e));
            max = Math.Max(s, e);
            return true;
        }

        if (int.TryParse(v, out var single))
        {
            min = max = Math.Max(1, single);
            return true;
        }

        return false;
    }

    private sealed class SaveThrowRoot
    {
        public List<SaveThrowClassJson> SavingThrows { get; set; } = new();
    }

    private sealed class SaveThrowClassJson
    {
        public string Class { get; set; } = string.Empty;
        public List<SaveThrowProgressionJson> Progression { get; set; } = new();
    }

    private sealed class SaveThrowProgressionJson
    {
        public string LevelRange { get; set; } = string.Empty;
        public int ParalyzationPoisonDeath { get; set; }
        public int RodStaffWand { get; set; }
        public int PetrificationPolymorph { get; set; }
        public int BreathWeapon { get; set; }
        public int Spell { get; set; }
    }

    private sealed class SaveThrowProgression
    {
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int ParalyzationPoisonDeath { get; set; }
        public int BreathWeapon { get; set; }
        public int Spell { get; set; }

        public bool Contains(int level) => level >= MinLevel && level <= MaxLevel;
    }
}
