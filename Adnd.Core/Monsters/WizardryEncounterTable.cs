using System;
using System.Collections.Generic;

namespace Adnd.Core.Monsters;

// Which monsters can be met on each dungeon level when playing Wizardry. Replaces the AD&D encounter tables.
public static class WizardryEncounterTable
{
    public const int MinLevel = 1;
    public const int MaxLevel = 10;

    private static readonly Dictionary<int, string[]> Levels = new()
    {
        [1] = new[] { "Bubbly Slime", "Bushwacker", "Kobold", "Murphy's Ghost", "Orc", "Rogue", "Undead Kobold", "Highwayman" },
        [2] = new[]
        {
            "Creeping Coin?", "Creeping Crud", "Gas Cloud", "Highwayman", "Lvl 1 Mage", "Lvl 1 Ninja", "Lvl 1 Priest",
            "Rogue", "Vorpal Bunny", "Zombie"
        },
        [3] = new[]
        {
            "Capybara", "Coyote", "Dragon Fly", "Giant Toad", "Lvl 1 Ninja", "Lvl 1 Priest", "Lvl 3 Ninja", "Lvl 3 Priest",
            "Lvl 3 Samurai", "Lvl 5 Mage", "Rogue", "Rotting Corpse", "Vorpal Bunny", "Were Bear"
        },
        [4] = new[]
        {
            "Attack Dog", "Boring Beetle", "Coyote", "Dragon Fly", "Dragon Puppy", "Gas Dragon", "Grave Mist", "High Ninja",
            "Huge Spider", "Lvl 6 Ninja", "Lvl 7 Fighter", "Lvl 7 Mage", "Ogre", "Priestess", "Rotting Corpse", "Shade",
            "Vorpal Bunny", "Were Bear", "Wererat"
        },
        [5] = new[]
        {
            "Attack Dog", "Bishop", "Champ Samurai", "Dragon Fly", "Dragon Puppy", "Gargoyle", "Giant Spider", "Grave Mist",
            "Huge Spider", "Killer Wolf", "Lvl 4 Thief", "Lvl 5 Priest", "Lvl 6 Ninja", "Lvl 7 Mage", "Medusalizard",
            "Minor Daimyo", "Ogre", "Rotting Corpse", "Shade", "Spirit", "Swordsman", "Wererat", "Werewolf"
        },
        [6] = new[]
        {
            "Arch Mage (Lesser)", "Chimera", "Earth Giant", "Gaze Hound", "Giant Spider", "High Priest(Lesser)", "Killer Wolf",
            "Lvl 4 Thief", "Lvl 5 Mage", "Lvl 5 Priest", "Lvl 6 Ninja", "Lvl 8 Ninja", "Lvl 8 Priest", "Lifestealer",
            "Master Thief(Lesser)", "Medusalizard", "Ogre Lord", "Spirit", "Troll", "Weretiger", "Werewolf"
        },
        [7] = new[]
        {
            "Arch Mage (Lesser)", "Champ Samurai", "Chimera", "Gorgon", "High Priest(Lesser)", "Lesser Demon", "Lvl 5 Priest",
            "Lvl 6 Ninja", "Lvl 7 Mage", "Lvl 8 Fighter", "Lvl 8 Priest", "Lifestealer", "Major Daimyo", "Master Thief(Lesser)",
            "Medusalizard", "Nightstalker", "Ogre Lord", "Troll", "Weretiger", "Werewolf", "Wyvern"
        },
        [8] = new[]
        {
            "Arch Mage (Lesser)", "Champ Samurai", "Chimera", "Earth Giant", "Fire Dragon", "Gaze Hound", "Hatamoto",
            "High Master", "High Priest(Lesser)", "Lesser Demon", "Lvl 5 Priest", "Lvl 6 Ninja", "Lvl 7 Mage", "Lvl 8 Ninja",
            "Lvl 8 Priest", "Lvl 10 Fighter", "Lifestealer", "Major Daimyo", "Master Thief(Lesser)", "Nightstalker",
            "Ogre Lord", "Troll", "Wyvern"
        },
        [9] = new[]
        {
            "Arch Mage (Lesser)", "Chimera", "Earth Giant", "Fire Dragon", "Fire Giant", "Frost Giant", "Gaze Hound",
            "High Master", "Lesser Demon", "Lvl 7 Mage", "Lvl 7 Thief", "Lvl 8 Fighter", "Lvl 8 Ninja", "Lvl 8 Priest",
            "Lvl 10 Fighter", "Lifestealer", "Maelific", "Master Thief(Lesser)", "Murphy's Ghost", "Nightstalker",
            "Ogre Lord", "Spirit", "Troll", "Wyvern"
        },
        [10] = new[]
        {
            "Arch Mage (Greater)", "Bleeb", "Chimera", "Dragon Zombie", "Fire Dragon", "Fire Giant", "Flack", "Frost Giant",
            "Gorgon", "Greater Demon", "High Priest(Greater)", "High Wizard", "Lvl 8 Bishop", "Lvl 8 Fighter", "Lvl 8 Ninja",
            "Lvl 10 Mage", "Maelific", "Master Ninja", "Master Thief(Greater)", "Murphy's Ghost", "Poison Giant", "Raver Lord",
            "Thief", "Vampire", "Vampire Lord", "W*E*R*D*N*A", "Will O' Wisp"
        }
    };

    public static IReadOnlyList<string> GetMonsters(int dungeonLevel)
        => Levels[Math.Clamp(dungeonLevel, MinLevel, MaxLevel)];

    public static string Pick(int dungeonLevel, Random random)
    {
        var list = GetMonsters(dungeonLevel);
        return list[random.Next(list.Count)];
    }
}
