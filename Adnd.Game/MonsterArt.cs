// Where the old UI's monster artwork lives, and how a monster's name finds it.
//
// Lifted out of EncounterForm so the tabletop viewer can be told about the same file. The search is fussy --
// three spellings of the name, six extensions, an optional _Wiz variant, a level folder first and then every
// level, in the deployed folder and in two source-tree folders -- and having that fussiness written twice
// would mean the fight dialog and the table eventually disagreeing about which picture is a goblin.
//
// Returns a PATH rather than an image, because the two callers want different things from it: the dialog
// loads a System.Drawing bitmap, and the viewer is another process that just needs to be told where to look.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adnd.Core.Config;
using Adnd.Core.Monsters;

namespace Adnd.Game;

public static class MonsterArt
{
    /// <summary>
    /// Whether to prefer the "_Wiz" variant: the monster comes from both books and the rules are set to
    /// Wizardry only. Mirrors the check EncounterForm has always made, in one place now.
    /// </summary>
    public static bool WizardryFirst(Monster? monster)
    {
        if (monster is null) return false;

        var sourceOption = GameRulesProvider.Current.MonsterSourceOptions;
        return monster.Source == Sources.WizardryAndAdnd && sourceOption == SourceOptions.OnlyWizardry;
    }

    /// <summary>The artwork file for a monster, or null when nothing on disk answers to that name.</summary>
    public static string? FindPath(string monsterName, int? dungeonLevel = null, bool useWizardrySuffix = false)
    {
        if (string.IsNullOrWhiteSpace(monsterName)) return null;

        var slug = monsterName.Trim().ToLowerInvariant().Replace(" ", "_");
        var camelCase = monsterName.Trim().Replace(" ", "");
        // PNG first, webp LAST. Both halves have to be able to open whatever is chosen, and the viewer reads
        // PNG and JPG only -- Unity's LoadImage has no webp decoder, while the game's ImageSharp does. With
        // webp preferred, a monster that had both formats came out as a blank card on the table while looking
        // fine in the fight dialog. Webp is still searched, so a monster that has nothing else is still found
        // by the game; it just cannot be printed on a standee.
        var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>();

        // Helper function to add candidates with optional _Wiz suffix
        void AddCandidates(string folder, string baseName, bool tryWizFirst)
        {
            foreach (var ext in exts)
            {
                // If Wizardry suffix should be used, try _Wiz version first
                if (tryWizFirst)
                {
                    candidates.Add(Path.Combine(folder, baseName + "_Wiz" + ext));
                }
                candidates.Add(Path.Combine(folder, baseName + ext));
            }
        }

        // If dungeonLevel is provided, search in level-specific folder first
        if (dungeonLevel.HasValue)
        {
            var levelFolder = $"Level{dungeonLevel.Value}";
            var baseFolder = Path.Combine(baseDir, "Assets", "Monsters", levelFolder);

            AddCandidates(baseFolder, slug, useWizardrySuffix);
            AddCandidates(baseFolder, camelCase, useWizardrySuffix);
            AddCandidates(baseFolder, monsterName, useWizardrySuffix);

            // Source paths
            var sourceFolder1 = Path.Combine("Adnd.Game", "Assets", "Monsters", levelFolder);
            var sourceFolder2 = Path.Combine("Assets", "Monsters", levelFolder);

            AddCandidates(sourceFolder1, slug, useWizardrySuffix);
            AddCandidates(sourceFolder1, camelCase, useWizardrySuffix);
            AddCandidates(sourceFolder2, slug, useWizardrySuffix);
            AddCandidates(sourceFolder2, camelCase, useWizardrySuffix);
        }

        // Also search in all level folders (Level1-Level10) if not found yet
        for (int level = 1; level <= 10; level++)
        {
            var levelFolder = $"Level{level}";
            var baseFolder = Path.Combine(baseDir, "Assets", "Monsters", levelFolder);

            AddCandidates(baseFolder, slug, useWizardrySuffix);
            AddCandidates(baseFolder, camelCase, useWizardrySuffix);
            AddCandidates(baseFolder, monsterName, useWizardrySuffix);

            // Source paths
            var sourceFolder1 = Path.Combine("Adnd.Game", "Assets", "Monsters", levelFolder);
            var sourceFolder2 = Path.Combine("Assets", "Monsters", levelFolder);

            AddCandidates(sourceFolder1, slug, useWizardrySuffix);
            AddCandidates(sourceFolder1, camelCase, useWizardrySuffix);
            AddCandidates(sourceFolder2, slug, useWizardrySuffix);
            AddCandidates(sourceFolder2, camelCase, useWizardrySuffix);
        }

        // Fallback: search in root Monsters folder
        var rootFolder = Path.Combine(baseDir, "Assets", "Monsters");
        AddCandidates(rootFolder, slug, useWizardrySuffix);
        AddCandidates(rootFolder, camelCase, useWizardrySuffix);
        AddCandidates(rootFolder, monsterName, useWizardrySuffix);

        // Source root paths
        AddCandidates(Path.Combine("Adnd.Game", "Assets", "Monsters"), slug, useWizardrySuffix);
        AddCandidates(Path.Combine("Adnd.Game", "Assets", "Monsters"), camelCase, useWizardrySuffix);
        AddCandidates(Path.Combine("Assets", "Monsters"), slug, useWizardrySuffix);
        AddCandidates(Path.Combine("Assets", "Monsters"), camelCase, useWizardrySuffix);
        return candidates.FirstOrDefault(File.Exists);
    }
}
