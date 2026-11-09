using System;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Wizardry;
using Xunit;

namespace Adnd.Tests;

// Each test guards a rule in docs\Wizardry-Handbook.md.
public class WizardryRulesTests
{
    [Theory]
    [InlineData(Race.Human, 8, 8, 5, 8, 8, 9)]
    [InlineData(Race.Elf, 7, 10, 10, 6, 9, 6)]
    [InlineData(Race.Dwarf, 10, 7, 10, 10, 5, 6)]
    [InlineData(Race.Gnome, 7, 7, 10, 8, 10, 7)]
    [InlineData(Race.Halfling, 5, 7, 7, 6, 10, 12)]
    public void Race_base_stats_match_handbook(Race race, int str, int iq, int pie, int vit, int agi, int luc)
    {
        Assert.Equal(new WizardryStats(str, iq, pie, vit, agi, luc), WizardryRules.RaceBaseStats(race));
    }

    [Fact]
    public void Dead_ashes_or_lost_character_has_no_other_status()
    {
        var c = new Character { Ruleset = Ruleset.Wizardry };
        c.AddStatus(CharacterStatus.Poisoned);
        c.ApplyParalysis(5);
        c.AddStatus(CharacterStatus.Dead);

        Assert.Equal(CharacterStatus.Dead, c.Status);
        Assert.Equal(0, c.ParalyzedRoundsRemaining);

        c.AddStatus(CharacterStatus.Poisoned);
        Assert.Equal(CharacterStatus.Dead, c.Status);
    }

    [Fact]
    public void Halfling_is_called_hobbit()
    {
        Assert.Equal("Hobbit", WizardryRules.RaceName(Race.Halfling));
        Assert.Equal("Hobbit", new Character { Ruleset = Ruleset.Wizardry, Race = Race.Halfling }.RaceDisplayName);
        Assert.NotEqual("Hobbit", new Character { Ruleset = Ruleset.ADND, Race = Race.Halfling }.RaceDisplayName);
    }

    [Fact]
    public void Elf_base_stats_qualify_for_no_class_until_bonus_points_are_spent()
    {
        var elf = WizardryRules.RaceBaseStats(Race.Elf);
        Assert.Empty(WizardryRules.EligibleClasses(elf, WizardryAlignment.Good));

        var spent = elf with { Iq = 12, Piety = 12 };
        var classes = WizardryRules.EligibleClasses(spent, WizardryAlignment.Good).ToList();

        Assert.Equal(new[] { WizardryClass.Mage, WizardryClass.Priest, WizardryClass.Bishop }.OrderBy(c => c), classes.OrderBy(c => c));
    }

    [Fact]
    public void Class_needs_its_minimum_stat()
    {
        var stats = new WizardryStats(10, 5, 5, 5, 5, 5);
        Assert.False(WizardryRules.MeetsRequirements(WizardryClass.Fighter, stats, WizardryAlignment.Neutral));
        Assert.True(WizardryRules.MeetsRequirements(WizardryClass.Fighter, stats with { Strength = 11 }, WizardryAlignment.Neutral));
    }

    [Theory]
    [InlineData(WizardryClass.Priest, WizardryAlignment.Neutral, false)]
    [InlineData(WizardryClass.Priest, WizardryAlignment.Good, true)]
    [InlineData(WizardryClass.Bishop, WizardryAlignment.Neutral, false)]
    [InlineData(WizardryClass.Lord, WizardryAlignment.Good, true)]
    [InlineData(WizardryClass.Lord, WizardryAlignment.Neutral, false)]
    [InlineData(WizardryClass.Ninja, WizardryAlignment.Evil, true)]
    [InlineData(WizardryClass.Ninja, WizardryAlignment.Good, false)]
    [InlineData(WizardryClass.Samurai, WizardryAlignment.Evil, false)]
    [InlineData(WizardryClass.Samurai, WizardryAlignment.Neutral, true)]
    [InlineData(WizardryClass.Thief, WizardryAlignment.Good, false)]
    [InlineData(WizardryClass.Thief, WizardryAlignment.Evil, true)]
    [InlineData(WizardryClass.Fighter, WizardryAlignment.Evil, true)]
    [InlineData(WizardryClass.Mage, WizardryAlignment.Good, true)]
    public void Alignment_restrictions(WizardryClass cls, WizardryAlignment alignment, bool allowed)
    {
        Assert.Equal(allowed, WizardryRules.AlignmentAllowed(cls, alignment));
    }

    [Fact]
    public void Bonus_points_fall_in_the_three_handbook_tiers()
    {
        var random = new Random(1234);
        var rolls = Enumerable.Range(0, 20000).Select(_ => WizardryRules.RollBonusPoints(random)).ToList();

        Assert.All(rolls, p => Assert.True(p is >= 5 and <= 9 or >= 15 and <= 19 or >= 25 and <= 29));
        Assert.InRange(rolls.Count(p => p <= 9), 18700, 19300);
        Assert.InRange(rolls.Count(p => p is >= 15 and <= 19), 800, 1100);
        Assert.Contains(rolls, p => p >= 25);
    }

    [Fact]
    public void Starting_gold_is_between_90_and_189()
    {
        var random = new Random(99);
        var gold = Enumerable.Range(0, 2000).Select(_ => WizardryRules.RollStartingGold(random)).ToList();

        Assert.Equal(90, gold.Min());
        Assert.Equal(189, gold.Max());
    }

    [Fact]
    public void Xp_tables_match_handbook()
    {
        Assert.Equal(1000, WizardryRules.XpToNextLevel(WizardryClass.Fighter, 1));
        Assert.Equal(1050, WizardryRules.XpToNextLevel(WizardryClass.Priest, 1));
        Assert.Equal(702236, WizardryRules.XpToNextLevel(WizardryClass.Ninja, 12));
        Assert.Equal(289709, WizardryRules.XpToNextLevel(WizardryClass.Fighter, 13));
        Assert.Equal(289709, WizardryRules.XpToNextLevel(WizardryClass.Fighter, 20));
    }

    [Fact]
    public void Level_for_xp_uses_cumulative_steps()
    {
        Assert.Equal(2724, WizardryRules.TotalXpForLevel(WizardryClass.Fighter, 3));
        Assert.Equal(1, WizardryRules.LevelForXp(WizardryClass.Fighter, 999));
        Assert.Equal(2, WizardryRules.LevelForXp(WizardryClass.Fighter, 1000));
        Assert.Equal(2, WizardryRules.LevelForXp(WizardryClass.Fighter, 2723));
        Assert.Equal(3, WizardryRules.LevelForXp(WizardryClass.Fighter, 2724));
    }

    [Theory]
    [InlineData(WizardryClass.Fighter, 10)]
    [InlineData(WizardryClass.Lord, 10)]
    [InlineData(WizardryClass.Priest, 8)]
    [InlineData(WizardryClass.Samurai, 8)]
    [InlineData(WizardryClass.Thief, 6)]
    [InlineData(WizardryClass.Bishop, 6)]
    [InlineData(WizardryClass.Ninja, 6)]
    [InlineData(WizardryClass.Mage, 4)]
    public void Hit_die_per_class(WizardryClass cls, int sides)
    {
        Assert.Equal(sides, WizardryRules.HitDieSides(cls));
    }

    [Fact]
    public void Samurai_roll_one_extra_hit_die()
    {
        Assert.Equal(6, WizardryRules.HitDiceAtLevel(WizardryClass.Samurai, 5));
        Assert.Equal(5, WizardryRules.HitDiceAtLevel(WizardryClass.Fighter, 5));
    }

    [Theory]
    [InlineData(3, -2)]
    [InlineData(4, -1)]
    [InlineData(5, -1)]
    [InlineData(6, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(17, 2)]
    [InlineData(18, 3)]
    public void Vitality_hit_point_modifier(int vitality, int modifier)
    {
        Assert.Equal(modifier, WizardryRules.VitalityHitPointModifier(vitality));
    }

    [Theory]
    [InlineData(WizardryClass.Fighter)]
    [InlineData(WizardryClass.Mage)]
    [InlineData(WizardryClass.Ninja)]
    [InlineData(WizardryClass.Samurai)]
    public void New_wizardry_characters_start_with_8_hp_whatever_the_class(WizardryClass cls)
    {
        var stats = new WizardryStats(18, 18, 18, 18, 18, 18);
        var character = WizardryCharacterFactory.Create(Race.Human, stats, WizardryAlignment.Neutral, cls, new Random(5));

        Assert.Equal(8, character.MaxHitPoints);
        Assert.Equal(8, character.CurrentHitPoints);
        Assert.Equal(1, character.Level);
    }

    [Fact]
    public void New_wizardry_character_is_tagged_with_the_ruleset_and_keeps_stats()
    {
        var stats = new WizardryStats(12, 11, 10, 9, 8, 7);
        var character = WizardryCharacterFactory.Create(Race.Dwarf, stats, WizardryAlignment.Good, WizardryClass.Fighter, new Random(5));

        Assert.Equal(Ruleset.Wizardry, character.Ruleset);
        Assert.Equal(WizardryClass.Fighter, character.WizardryClass);
        Assert.Equal(stats, character.WizardryStats);
        Assert.InRange(character.GoldPieces, 90, 189);
        Assert.InRange(character.Age, 14, 17);
        Assert.Equal("Fighter", character.GetClassesDisplayText());
    }

    [Fact]
    public void Characters_default_to_adnd()
    {
        Assert.Equal(Ruleset.ADND, new Character().Ruleset);
    }

    [Fact]
    public void Wizardry_has_no_range_or_ammo_slots()
    {
        var saved = GameRulesProvider.Current;
        try
        {
            GameRulesProvider.Current = new GameRules { Rules = Ruleset.Wizardry };
            Assert.False(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Range));
            Assert.False(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Ammo));
            Assert.False(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Feet));
            Assert.False(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Belt));
            Assert.False(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Back));
            Assert.False(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Ring2));
            Assert.True(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Ring1));
            Assert.True(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.MainHand));

            GameRulesProvider.Current = new GameRules { Rules = Ruleset.ADND };
            Assert.True(EquipmentManager.IsSlotAvailable(Adnd.Core.Items.EquipmentSlot.Range));
        }
        finally
        {
            GameRulesProvider.Current = saved;
        }
    }

    [Fact]
    public void Menu_entries_follow_the_monster_setting()
    {
        var rules = new GameRules();
        rules.ApplySystemPreset(Ruleset.ADND);
        Assert.True(rules.MazeAvailable);
        Assert.False(rules.ProvingGroundsAvailable);

        rules.ApplySystemPreset(Ruleset.Wizardry);
        Assert.False(rules.MazeAvailable);
        Assert.True(rules.ProvingGroundsAvailable);
    }

    [Theory]
    [InlineData(Ruleset.ADND, SourceOptions.OnlyAdnd, BoltacItemSourceOptions.OnlyAdnd)]
    public void System_preset_switches_rules_items_and_monsters(Ruleset system, SourceOptions source, BoltacItemSourceOptions shop)
    {
        var rules = new GameRules();
        rules.ApplySystemPreset(system);
        Assert.Equal(system, rules.Rules);
        Assert.Equal(source, rules.ItemSourceOptions);
        Assert.Equal(source, rules.MonsterSourceOptions);
        Assert.Equal(shop, rules.BoltacItemSourceFilter);
    }

    [Fact]
    public void Shared_armour_is_adnd_only_and_has_wizardry_copies()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "Adnd.Data", "Items")))
            dir = System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        System.Collections.Generic.List<(string Name, string Source, int Ac)> Load(string file) =>
            System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir!, "Adnd.Data", "Items", file)))
                .RootElement.GetProperty("Items").EnumerateArray()
                .Select(e => (e.GetProperty("Name").GetString()!,
                    e.TryGetProperty("Source", out var s) ? s.GetString() ?? "" : "",
                    e.TryGetProperty("ArmorClassBonus", out var a) ? a.GetInt32() : 0))
                .ToList();

        var adnd = Load("Armor.json").Concat(Load("Shields.json")).ToList();
        var wiz = Load("Wizardry.json");
        Assert.DoesNotContain(adnd, i => i.Source == "WizardryAndAdnd");

        foreach (var (name, ac) in new[] { ("Leather Armor", 2), ("Chain Mail", 3), ("Plate Mail", 5), ("Small Shield", 2), ("Large Shield", 3),
                     ("Shield -1", -1), ("Shield +1", 4), ("Shield +2", 5), ("Shield +3", 6), ("Evil Shield +3", 5) })
            Assert.Contains(wiz, i => i.Name == name && i.Ac == ac && i.Source == "Wizardry");
    }

    [Fact]
    public void Item_class_access_follows_wizardry_items_json()
    {
        var wc = Adnd.Core.Wizardry.WizardryClass.Thief;
        Assert.False(Adnd.Core.Wizardry.WizardryItemClasses.Allows(wc, "Long Sword"));
        Assert.True(Adnd.Core.Wizardry.WizardryItemClasses.Allows(wc, "Short Sword"));
        Assert.True(Adnd.Core.Wizardry.WizardryItemClasses.Allows(Adnd.Core.Wizardry.WizardryClass.Samurai, "Long Sword"));
        Assert.False(Adnd.Core.Wizardry.WizardryItemClasses.Allows(Adnd.Core.Wizardry.WizardryClass.Bishop, "Plate Mail"));
        Assert.True(Adnd.Core.Wizardry.WizardryItemClasses.Allows(Adnd.Core.Wizardry.WizardryClass.Bishop, "Staff"));
        Assert.Null(Adnd.Core.Wizardry.WizardryItemClasses.Allows(wc, "Unlisted Thing"));
    }

    [Fact]
    public void Wizardry_characters_carry_at_most_eight_items_and_ignore_weight()
    {
        var c = new Adnd.Core.Characters.Character { Ruleset = Adnd.Core.Config.Ruleset.Wizardry };
        var heavy = new Adnd.Core.Items.Item { Name = "Anvil", Weight = 1000000 };
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.CanCarry(heavy));
            Assert.True(c.TryReceiveItem(heavy));
        }
        Assert.False(c.CanCarry(heavy));
    }

    [Fact]
    public void Identify_chances_follow_bishop_level()
    {
        Assert.Equal(15, WizardryIdentification.IdentifyChancePercent(1));
        Assert.Equal(60, WizardryIdentification.IdentifyChancePercent(10));
        Assert.Equal(100, WizardryIdentification.IdentifyChancePercent(18));
        Assert.Equal(32, WizardryIdentification.CursedEquipChancePercent(1));
        Assert.Equal(5, WizardryIdentification.CursedEquipChancePercent(10));
        Assert.Equal(0, WizardryIdentification.CursedEquipChancePercent(12));
    }

    [Fact]
    public void Found_items_are_unidentified_and_bishop_can_identify_them()
    {
        var sword = new Adnd.Core.Items.Item { Name = "Long Sword +1", Type = Adnd.Core.Items.ItemType.Weapon, Cost = 10000 };
        WizardryIdentification.MakeUnidentified(sword);
        Assert.False(sword.IsIdentified);
        Assert.Equal("?Sword", sword.DisplayName);
        Assert.Equal(5000, WizardryIdentification.ShopIdentifyCost(sword));

        var bishop = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Bishop, Level = 18 };
        bishop.Inventory.Add(sword);
        Assert.Equal(IdentifyOutcome.Identified, WizardryIdentification.TryBishopIdentify(bishop, sword, new Random(1)));
        Assert.Equal("Long Sword +1", sword.DisplayName);

        var mage = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Mage };
        Assert.Equal(IdentifyOutcome.NotBishop, WizardryIdentification.TryBishopIdentify(mage, sword, new Random(1)));
    }

    [Fact]
    public void Wizardry_levels_only_when_pending_levels_are_applied()
    {
        var svc = new Adnd.Core.Characters.Progression.LevelUpService();
        var fighter = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Fighter, Level = 1 };
        fighter.Classes.Add(CharacterClass.Fighter);

        svc.ApplyExperienceAndAutoLevel(fighter, 50000, null, awardLevels: false);
        Assert.Equal(1, fighter.Level);

        var result = svc.ApplyPendingLevels(fighter);
        Assert.True(fighter.Level > 1);
        Assert.True(result.NewLevel > result.OldLevel);
    }

    [Fact]
    public void Class_change_resets_level_keeps_hp_and_ages()
    {
        var c = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(12, 14, 14, 12, 10, 10), WizardryAlignment.Good, WizardryClass.Mage, new Random(1));
        c.MaxHitPoints = c.CurrentHitPoints = 33;
        c.Level = 5;
        var ageBefore = c.Age;

        var weeks = WizardryClassChange.Apply(c, WizardryClass.Priest, new WizardryStats(10, 10, 14, 10, 10, 10), new Random(1));

        Assert.Contains(weeks, new[] { 252, 304, 356 });
        Assert.Equal(WizardryClass.Priest, c.WizardryClass);
        Assert.Equal(1, c.Level);
        Assert.Equal(0, c.Experience);
        Assert.Equal(33, c.MaxHitPoints);
        Assert.InRange(c.Abilities.Wisdom, 13, 15);
        Assert.True(c.Age >= ageBefore + 4);
    }

    [Fact]
    public void Temple_fees_scale_with_level()
    {
        Assert.Equal(300, WizardryRules.TempleCost(CharacterStatus.Paralyzed, 3));
        Assert.Equal(600, WizardryRules.TempleCost(CharacterStatus.Petrified, 3));
        Assert.Equal(750, WizardryRules.TempleCost(CharacterStatus.Dead, 3));
        Assert.Equal(1500, WizardryRules.TempleCost(CharacterStatus.Ashes, 3));
    }

    [Fact]
    public void Level_up_attributes_young_gain_old_lose_and_low_vitality_kills()
    {
        var young = new Character { Ruleset = Ruleset.Wizardry, Age = 0, Abilities = new AbilityScores { Strength = 10, Intelligence = 10, Wisdom = 10, Constitution = 10, Dexterity = 10, Charisma = 10 } };
        var gains = Enumerable.Range(0, 50).SelectMany(s => WizardryAttributeChanges.Roll(young, 25, new Random(s))).ToList();
        Assert.All(gains, g => Assert.Equal(1, g.Delta));
        Assert.NotEmpty(gains);
        Assert.All(new[] { young.Abilities.Strength, young.Abilities.Charisma }, v => Assert.InRange(v, 10, 18));

        var old = new Character { Ruleset = Ruleset.Wizardry, Age = 100, Abilities = new AbilityScores { Strength = 10, Intelligence = 10, Wisdom = 10, Constitution = 3, Dexterity = 10, Charisma = 10 } };
        for (var s = 0; s < 20 && !old.HasStatus(CharacterStatus.Dead); s++)
            WizardryAttributeChanges.Roll(old, 25, new Random(s));
        Assert.True(old.HasStatus(CharacterStatus.Dead));
        Assert.True(old.Abilities.Constitution < 3);
    }

    [Fact]
    public void Level_up_hp_is_rerolled_total_or_plus_one()
    {
        // Vitality 18 (+3 per die): a level 6 fighter always rolls at least 6 * 4 = 24, never above 6 * 13 = 78.
        for (var s = 0; s < 100; s++)
        {
            var hp = WizardryRules.RollLevelUpMaxHitPoints(WizardryClass.Fighter, 6, 18, 45, new Random(s));
            Assert.True(hp == 46 || (hp > 45 && hp <= 78));
        }

        Assert.Equal(101, WizardryRules.RollLevelUpMaxHitPoints(WizardryClass.Fighter, 6, 18, 100, new Random(1)));
    }

    [Fact]
    public void Level_one_bishop_may_fail_to_identify()
    {
        var bishop = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Bishop, Level = 1 };
        var outcomes = Enumerable.Range(0, 200).Select(seed =>
        {
            var item = new Adnd.Core.Items.Item { Name = "Dagger", Type = Adnd.Core.Items.ItemType.Weapon };
            WizardryIdentification.MakeUnidentified(item);
            return WizardryIdentification.TryBishopIdentify(bishop, item, new Random(seed));
        }).ToList();
        Assert.Contains(IdentifyOutcome.Failed, outcomes);
        Assert.Contains(IdentifyOutcome.Identified, outcomes);
    }

    [Theory]
    [InlineData(WizardryClass.Fighter, 1, null, 1)]
    [InlineData(WizardryClass.Fighter, 4, null, 2)]
    [InlineData(WizardryClass.Ninja, 12, null, 4)]
    [InlineData(WizardryClass.Mage, 20, null, 1)]
    [InlineData(WizardryClass.Mage, 3, "Dagger of Speed", 7)]
    [InlineData(WizardryClass.Fighter, 20, "Long Sword +1", 6)]
    [InlineData(WizardryClass.Fighter, 4, "Blade Cusinart", 4)]
    public void Attacks_are_class_progression_with_weapon_swing_minimum(WizardryClass cls, int level, string weapon, int expected)
    {
        Assert.Equal(expected, WizardryAttacks.AttacksPerRound(cls, level, weapon));
    }
}
