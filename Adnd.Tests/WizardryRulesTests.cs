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
    public void Bonus_points_are_seven_to_ten_normally_and_never_exceed_thirty()
    {
        var random = new Random(1234);
        var rolls = Enumerable.Range(0, 5000).Select(_ => WizardryRules.RollBonusPoints(random)).ToList();

        Assert.All(rolls, p => Assert.InRange(p, 7, 30));
        Assert.Contains(rolls, p => p >= 17);
        Assert.Contains(rolls, p => p <= 10);
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
        Assert.InRange(character.Age, 18, 23);
        Assert.Equal("Fighter", character.GetClassesDisplayText());
    }

    [Fact]
    public void Characters_default_to_adnd()
    {
        Assert.Equal(Ruleset.ADND, new Character().Ruleset);
        Assert.Equal(Ruleset.ADND, new GameRules().Rules);
    }
}
