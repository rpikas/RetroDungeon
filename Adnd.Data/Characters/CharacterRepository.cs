using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Adnd.Data.Party;
using Adnd.Data.Items;
using Adnd.Core.Characters;
using Adnd.Core.Items;

namespace Adnd.Data.Characters;

public class CharacterRepository
{
    private readonly string _folder;

    public CharacterRepository(string folder = "Data/Characters")
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    public IEnumerable<Character> GetAll()
    {
        var list = new List<Character>();
        var itemLookup = new ItemRepository("Data/Items")
            .LoadAll()
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            var json = File.ReadAllText(file);
            var c = JsonSerializer.Deserialize<Character>(json);
            if (c != null)
            {
                c.EnsureClassProgressions();
                HydrateWeaponDamageVsLarge(c, itemLookup);
                list.Add(c);
            }
        }

        return list;
    }

    private static void HydrateWeaponDamageVsLarge(Character character, IReadOnlyDictionary<string, Item> itemLookup)
    {
        foreach (var item in character.Inventory)
            HydrateItem(item, itemLookup);

        foreach (var equipped in character.Equipment.Values)
            HydrateItem(equipped, itemLookup);
    }

    private static void HydrateItem(Item? item, IReadOnlyDictionary<string, Item> itemLookup)
    {
        if (item == null || item.Type != ItemType.Weapon)
            return;

        if (!string.IsNullOrWhiteSpace(item.DamageVsLarge))
            return;

        if (!itemLookup.TryGetValue(item.Name, out var template))
            return;

        if (string.IsNullOrWhiteSpace(template.DamageVsLarge))
            return;

        item.DamageVsLarge = template.DamageVsLarge;
    }

    public void Save(Character character)
    {
        string fileName = $"{SanitizeFileName(character.Name)}.json";
        string path = Path.Combine(_folder, fileName);

        var json = JsonSerializer.Serialize(character, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
    }

    public void Delete(string name)
    {
        string fileName = $"{SanitizeFileName(name)}.json";
        string path = Path.Combine(_folder, fileName);

        if (File.Exists(path))
            File.Delete(path);

        // If the character was a member of the current party, remove them as well.
        try
        {
            var partyRepo = new PartyRepository();
            var party = partyRepo.Load();

            // Normalize and remove any party entries that refer to this character.
            // Party members may be stored as plain names or older JSON object fragments
            // (converter fallback). Attempt to extract a name in either case before comparing.
            int removed = 0;
            for (int i = party.Members.Count - 1; i >= 0; i--)
            {
                var member = party.Members[i];
                string memberName = member ?? string.Empty;

                // If the stored member looks like JSON, try to parse and extract Name
                if (memberName.TrimStart().StartsWith('{'))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(memberName);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            memberName = nameProp.GetString() ?? memberName;
                        else if (root.TryGetProperty("name", out var nameProp2) && nameProp2.ValueKind == System.Text.Json.JsonValueKind.String)
                            memberName = nameProp2.GetString() ?? memberName;
                    }
                    catch
                    {
                        // ignore parse errors and fall back to raw member text
                    }
                }

                // Compare both the raw names and their sanitized file-name forms to handle
                // any historical differences in how members were stored.
                bool match = string.Equals(memberName, name, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(SanitizeFileName(memberName), SanitizeFileName(name), StringComparison.OrdinalIgnoreCase);

                if (match)
                {
                    party.Members.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
                partyRepo.Save(party);
        }
        catch
        {
            // Swallow errors to avoid interfering with roster deletion.
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
