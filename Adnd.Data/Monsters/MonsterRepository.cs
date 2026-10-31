// Monsters/MonsterRepository.cs
//Läser alla JSON‑filer i en mapp och returnerar riktiga Core‑monster.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adnd.Core.Monsters;

namespace Adnd.Data.Monsters;

public class MonsterRepository
{
    public IEnumerable<Monster> GetAll()
    {
        var monsters = new List<Monster>();

        if (!Directory.Exists(MonsterDataPaths.MonsterJsonFolder))
            return monsters;

        var files = Directory.GetFiles(MonsterDataPaths.MonsterJsonFolder, "*.json");

        foreach (var file in files)
        {
            var jsonText = File.ReadAllText(file);

            var grouped = JsonSerializer.Deserialize<MonsterLevelJsonModel>(jsonText);
            if (grouped?.Monsters != null && grouped.Monsters.Count > 0)
            {
                foreach (var m in grouped.Monsters)
                {
                    if (m != null)
                    {
                        var monster = MonsterImporter.Convert(m);
                        monster.DungeonLevel = grouped.Level; // Set the dungeon level
                        monsters.Add(monster);
                    }
                }

                continue;
            }

            var jsonModel = JsonSerializer.Deserialize<MonsterJsonModel>(jsonText);
            if (jsonModel != null)
            {
                var monster = MonsterImporter.Convert(jsonModel);
                monster.DungeonLevel = 0; // Default to 0 for monsters without a level file
                monsters.Add(monster);
            }
        }

        // Monsters sharing a name (a Kobold in both books) are looked up by first match, so the configured
        // source goes first. The sort is stable, so everything else keeps its file order.
        return MonsterSourcePreference.Order(monsters, Adnd.Core.Config.GameRulesProvider.Current.EffectiveMonsterSourceOptions);
    }
}
