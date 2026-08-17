using System.Text.Json;
using Adnd.Core.Spells;

namespace Adnd.Data.Spells;

public class SpellRepository
{
    private readonly string _folder;

    public SpellRepository(string folder = "Data/Spells")
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    public List<Spell> LoadAll()
    {
        var list = new List<Spell>();

        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            var json = File.ReadAllText(file);
            var model = JsonSerializer.Deserialize<SpellJsonModel>(json);
            if (model == null)
                continue;

            if (!Enum.TryParse<SpellClass>(model.Class, true, out var spellClass))
                continue;

            list.Add(new Spell
            {
                Id = model.Id,
                Name = model.Name,
                SpellClass = spellClass,
                Level = model.Level,
                Description = model.Description,
                RangeType = ParseOrDefault(model.RangeType, SpellRangeType.Enemy),
                Targeting = ParseOrDefault(model.Targeting, SpellTargeting.Single),
                TargetingScope = ParseOrDefault(model.TargetingScope, SpellTargetingScope.SingleTarget),
                CastContext = ParseOrDefault(model.CastContext, SpellCastContext.Both),
                EffectType = ParseOrDefault(model.EffectType, SpellEffectType.Damage),
                EffectDescription = model.EffectDescription
            });
        }

        return list;
    }

    public List<Spell> LoadByClass(SpellClass spellClass)
    {
        return LoadAll()
            .Where(s => s.SpellClass == spellClass)
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList();
    }

    private static T ParseOrDefault<T>(string? value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
    }
}
