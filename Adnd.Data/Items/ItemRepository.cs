using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adnd.Core.Characters;
using Adnd.Core.Items;
using Adnd.Core.Spells;
using Adnd.Data.Spells;

namespace Adnd.Data.Items;

public class ItemRepository
{
    private readonly string _folder;

    public ItemRepository(string folder = "Data/Items")
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    public IEnumerable<Item> LoadAll()
    {
        var list = new List<Item>();

        EnsureScrollsJsonContainsAllSpellScrolls();

        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            var json = File.ReadAllText(file);

            var grouped = JsonSerializer.Deserialize<ItemCategoryJsonModel>(json);
            if (grouped?.Items != null && grouped.Items.Count > 0)
            {
                foreach (var model in grouped.Items)
                {
                    var item = ToItem(model);
                    if (item != null)
                        list.Add(item);
                }

                continue;
            }

            var single = JsonSerializer.Deserialize<ItemJsonModel>(json);
            if (single != null)
            {
                var item = ToItem(single);
                if (item != null)
                    list.Add(item);
            }
        }

        return list
            .Where(i => i.Status != ItemStatus.NotImplemented)
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public bool TryAdjustStock(string itemName, int delta)
    {
        var file = FindItemFile(itemName);
        if (file == null)
            return false;

        var json = File.ReadAllText(file);

        var grouped = JsonSerializer.Deserialize<ItemCategoryJsonModel>(json);
        if (grouped?.Items != null && grouped.Items.Count > 0)
        {
            var target = grouped.Items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
            if (target == null)
                return false;

            if (!target.IsShopBuyable)
                return false;

            if (target.StockQuantity.HasValue)
            {
                var next = target.StockQuantity.Value + delta;
                if (next < 0)
                    return false;

                target.StockQuantity = next;

                var updatedGrouped = JsonSerializer.Serialize(grouped, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(file, updatedGrouped);
                return true;
            }

            return true;
        }

        var single = JsonSerializer.Deserialize<ItemJsonModel>(json);
        if (single == null)
            return false;

        if (!single.IsShopBuyable)
            return false;

        if (single.StockQuantity.HasValue)
        {
            var next = single.StockQuantity.Value + delta;
            if (next < 0)
                return false;

            single.StockQuantity = next;

            var updated = JsonSerializer.Serialize(single, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, updated);
            return true;
        }

        return true;
    }

    private string? FindItemFile(string itemName)
    {
        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            var json = File.ReadAllText(file);

            var grouped = JsonSerializer.Deserialize<ItemCategoryJsonModel>(json);
            if (grouped?.Items != null && grouped.Items.Any(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase)))
                return file;

            var model = JsonSerializer.Deserialize<ItemJsonModel>(json);
            if (model != null && string.Equals(model.Name, itemName, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private Item? ToItem(ItemJsonModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Type))
            return null;

        return new Item
        {
            Name = model.Name,
            Status = ResolveStatus(model.Status),
            Type = Enum.Parse<ItemType>(model.Type),
            Slot = string.IsNullOrEmpty(model.Slot) ? null : Enum.Parse<EquipmentSlot>(model.Slot),
            Cost = model.Cost,
            CostSilverPieces = model.CostSilverPieces,
            Weight = model.Weight,
            ToHitBonus = model.ToHitBonus,
            IsShopBuyable = model.IsShopBuyable,
            StockQuantity = model.StockQuantity,
            ArmorClassBonus = model.ArmorClassBonus,
            Damage = model.Damage,
            DamageVsLarge = model.DamageVsLarge,
            DamageType = model.DamageType,
            SpeedFactor = model.SpeedFactor,
            WeaponLength = model.WeaponLength,
            IsTwoHanded = model.IsTwoHanded,
            Range = model.Range,
            FireRate = model.FireRate,
            RequiresAmmo = model.RequiresAmmo,
            AmmoType = model.AmmoType,
            Quantity = model.Quantity,
            MagicBonus = model.MagicBonus,
            SpecialAbilities = model.SpecialAbilities ?? new List<string>(),
            IsCursed = model.IsCursed,
            AllowedClasses = ResolveAllowedClasses(model.AllowedClasses),
            Rarity = ResolveRarity(model.Rarity),
            Description = model.Description,
            Source = model.Source,
            Version = model.Version
        };
    }

    private static ItemStatus ResolveStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return ItemStatus.Implemented;

        return status.Trim().Equals("NotImplemented", StringComparison.OrdinalIgnoreCase)
            ? ItemStatus.NotImplemented
            : ItemStatus.Implemented;
    }

    private static List<CharacterClass> ResolveAllowedClasses(List<string>? rawAllowedClasses)
    {
        if (rawAllowedClasses == null || rawAllowedClasses.Count == 0)
            return new List<CharacterClass>();

        var hasAll = rawAllowedClasses.Any(c => string.Equals(c?.Trim(), "All", StringComparison.OrdinalIgnoreCase));
        if (hasAll)
            return Enum.GetValues<CharacterClass>().ToList();

        var list = new List<CharacterClass>();
        foreach (var name in rawAllowedClasses)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (Enum.TryParse<CharacterClass>(name.Trim(), ignoreCase: true, out var parsed))
                list.Add(parsed);
        }

        return list;
    }

    private static RarityType ResolveRarity(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity))
            return RarityType.Common;

        if (Enum.TryParse<RarityType>(rarity.Trim(), ignoreCase: true, out var parsed))
            return parsed;

        return RarityType.Common;
    }

    private void EnsureScrollsJsonContainsAllSpellScrolls()
    {
        var scrollsPath = Path.Combine(_folder, "Scrolls.json");
        if (!File.Exists(scrollsPath))
            return;

        var scrollsJson = File.ReadAllText(scrollsPath);
        var grouped = JsonSerializer.Deserialize<ItemCategoryJsonModel>(scrollsJson);
        if (grouped == null)
            return;

        grouped.Category = string.IsNullOrWhiteSpace(grouped.Category) ? "Scroll" : grouped.Category;
        grouped.Items ??= new List<ItemJsonModel>();

        var beforeCleanupCount = grouped.Items.Count;
        grouped.Items = grouped.Items
            .Where(i => !(string.Equals(i.Type, "Scroll", StringComparison.OrdinalIgnoreCase)
                          && IsGenericScrollTemplateName(i.Name)))
            .ToList();
        var removedGenericTemplates = beforeCleanupCount - grouped.Items.Count;

        var existing = grouped.Items
            .Where(i => string.Equals(i.Type, "Scroll", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var spells = new SpellRepository("Data/Spells").LoadAll()
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var spellsByNormalizedName = spells
            .GroupBy(s => NormalizeSpellKey(s.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var addedAny = false;
        foreach (var spell in spells)
        {
            var scrollName = $"Scroll of {spell.Name}";
            if (existing.Contains(scrollName))
                continue;

            grouped.Items.Add(CreateGeneratedScrollModel(spell));
            existing.Add(scrollName);
            addedAny = true;
        }

        foreach (var requiredSpellName in GetRequiredSpellScrollNames())
        {
            var requiredScrollName = $"Scroll of {requiredSpellName}";
            if (existing.Contains(requiredScrollName))
                continue;

            if (!spellsByNormalizedName.TryGetValue(NormalizeSpellKey(requiredSpellName), out var spell))
                continue;

            var concreteScrollName = $"Scroll of {spell.Name}";
            if (existing.Contains(concreteScrollName))
                continue;

            grouped.Items.Add(CreateGeneratedScrollModel(spell));
            existing.Add(concreteScrollName);
            addedAny = true;
        }

        if (!addedAny && removedGenericTemplates == 0)
            return;

        grouped.Items = grouped.Items
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updated = JsonSerializer.Serialize(grouped, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(scrollsPath, updated);
    }

    private static bool IsGenericScrollTemplateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var n = name.Trim();
        if (!n.StartsWith("Scroll of ", StringComparison.OrdinalIgnoreCase))
            return false;

        // Examples: "Scroll of 1 Spell (Level 1-6)", "Scroll of 2 Spells (Levels 1-4)", etc.
        return n.Contains("Spell (", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Spells (", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Levels ", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Level ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSpellKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static IEnumerable<string> GetRequiredSpellScrollNames()
    {
        return new[]
        {
            "Bless", "Cure Light Wounds", "Chant", "Find Traps", "Hold Person", "Silence 15' Radius",
            "Resist Cold", "Spiritual Hammer", "Cure Disease", "Glyph of Warding", "Remove Paralysis", "Prayer",
            "Cure Serious Wounds", "Neutralize Poison", "Cure Critical Wounds", "Flame Strike", "Insect Plague",
            "Raise Dead", "Blade Barrier", "Heal", "Harm", "Earthquake", "Resurrection", "Unholy Word",
            "Cause Light Wounds", "Cause Serious Wounds", "Cause Critical Wounds",
            "Entangle", "Faerie Fire", "Barkskin", "Protection From Fire", "Protection From Lightning", "Charm Person or Mammal", "Call Lightning", "Snare",
            "Summon Insects", "Pyrotechnics", "Wall of Fire", "Feeblemind", "Wall of Thorns",
            "Finger of Death", "Fire Storm",
            "Chromatic Orb", "Color Spray", "Phantasmal Force", "Blur", "Improved Phantasmal Force",
            "Invisibility", "Mirror Image", "Fear", "Paralyzation", "Spectral Force", "Confusion",
            "Improved Invisibility", "Phantasmal Killer", "Chaos", "Maze", "Permanent Illusion",
            "Magic Missile", "Shield", "Shocking Grasp", "Sleep", "Charm Person", "Melf's Acid Arrow",
            "Strength", "Fireball", "Haste", "Lightning Bolt", "Slow", "Ice Storm", "Cloudkill",
            "Hold Monster", "Cone of Cold", "Death Fog", "Disintegrate", "Delayed Blast Fireball",
            "Mass Invisibility", "Incendiary Cloud", "Power Word Stun", "Meteor Swarm", "Power Word Kill"
        };
    }

    private static ItemJsonModel CreateGeneratedScrollModel(Spell spell)
    {
        var level = Math.Max(1, spell.Level);
        return new ItemJsonModel
        {
            Name = $"Scroll of {spell.Name}",
            Status = "Implemented",
            Type = "Scroll",
            Slot = string.Empty,
            Cost = 200 * level,
            CostSilverPieces = 0,
            Weight = 1,
            ToHitBonus = 0,
            IsShopBuyable = false,
            StockQuantity = 0,
            ArmorClassBonus = 0,
            Damage = string.Empty,
            DamageVsLarge = string.Empty,
            DamageType = string.Empty,
            SpeedFactor = 0,
            WeaponLength = string.Empty,
            IsTwoHanded = false,
            Range = string.Empty,
            FireRate = "1",
            RequiresAmmo = false,
            AmmoType = string.Empty,
            Quantity = 0,
            MagicBonus = 0,
            SpecialAbilities = new List<string> { $"Casts {spell.Name.ToLowerInvariant()}" },
            IsCursed = false,
            AllowedClasses = new List<string> { spell.SpellClass.ToString() },
            Rarity = MapSpellLevelToRarity(level).ToString(),
            Description = $"Generated scroll for spell {spell.Name}.",
            Source = "Generated from spell list",
            Version = "1e"
        };
    }

    private static void AppendGeneratedScrollsForMissingSpells(List<Item> items)
    {
        var existingScrolls = items
            .Where(i => i.Type == ItemType.Scroll)
            .Select(i => i.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var spells = new SpellRepository("Data/Spells").LoadAll();
        var groupedByName = spells
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedByName)
        {
            var scrollName = $"Scroll of {group.Key}";
            if (existingScrolls.Contains(scrollName))
                continue;

            var allowed = group
                .Select(s => MapSpellClassToCharacterClass(s.SpellClass))
                .Distinct()
                .ToList();

            var minLevel = group.Min(s => Math.Max(1, s.Level));

            items.Add(new Item
            {
                Name = scrollName,
                Type = ItemType.Scroll,
                Cost = 200 * minLevel,
                Weight = 1,
                StockQuantity = 0,
                IsShopBuyable = false,
                AllowedClasses = allowed,
                SpecialAbilities = new List<string> { $"Casts {group.Key.ToLowerInvariant()}" },
                Rarity = MapSpellLevelToRarity(minLevel),
                Source = "Generated from spell list",
                Version = "1e",
                Status = ItemStatus.Implemented
            });
        }
    }

    private static CharacterClass MapSpellClassToCharacterClass(SpellClass spellClass)
    {
        return spellClass switch
        {
            SpellClass.MagicUser => CharacterClass.MagicUser,
            SpellClass.Illusionist => CharacterClass.Illusionist,
            SpellClass.Cleric => CharacterClass.Cleric,
            SpellClass.Druid => CharacterClass.Druid,
            _ => CharacterClass.MagicUser
        };
    }

    private static RarityType MapSpellLevelToRarity(int level)
    {
        return level switch
        {
            <= 2 => RarityType.Common,
            <= 4 => RarityType.Uncommon,
            <= 6 => RarityType.Rare,
            <= 7 => RarityType.VeryRare,
            <= 8 => RarityType.Legendary,
            _ => RarityType.Unique
        };
    }
}
