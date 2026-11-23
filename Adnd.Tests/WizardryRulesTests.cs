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
    [Fact]
    public void Dispelled_monster_is_recorded_so_it_gives_no_xp_in_wizardry_only()
    {
        Adnd.Core.Combat.Sessions.CombatSession Run(Ruleset ruleset)
        {
            var priest = new Character { Name = "Pri", Ruleset = ruleset, Level = 14, MaxHitPoints = 40, CurrentHitPoints = 40, ArmorClass = 10, Damage = "1d6" };
            priest.Classes.Add(CharacterClass.Cleric);
            var skeleton = new Adnd.Core.Combat.Sessions.MonsterInstance(
                new Adnd.Core.Monsters.Monster { Name = "Skeleton", HitDice = 1, ArmorClass = 10, Type = Adnd.Core.Monsters.MonsterType.Undead }, 0);
            var session = new Adnd.Core.Combat.Sessions.CombatSession([priest], [skeleton]);
            var actions = new System.Collections.Generic.Dictionary<string, Adnd.Core.Combat.Actions.CombatActionType>
            {
                [priest.Name] = Adnd.Core.Combat.Actions.CombatActionType.DispellUndead
            };
            new Adnd.Core.Combat.Resolution.CombatResolver(new MaxDice()).ResolveRound(session, actions);
            return session;
        }

        Assert.Single(Run(Ruleset.Wizardry).DispelledMonsters);
        Assert.Empty(Run(Ruleset.ADND).DispelledMonsters);
    }

    [Fact]
    public void Lord_dispells_undead_from_level_4_as_priest_three_levels_lower()
    {
        Assert.Equal(0, WizardryIdentification.LordDispellLevel(3));
        Assert.Equal(1, WizardryIdentification.LordDispellLevel(4));
        Assert.Equal(7, WizardryIdentification.LordDispellLevel(10));

        bool Dispels(int level)
        {
            var lord = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(15, 12, 12, 15, 14, 15), WizardryAlignment.Good, WizardryClass.Lord, new Random(1));
            lord.Level = level;
            lord.MaxHitPoints = lord.CurrentHitPoints = 40;
            var skeleton = new Adnd.Core.Combat.Sessions.MonsterInstance(
                new Adnd.Core.Monsters.Monster { Name = "Skeleton", HitDice = 1, ArmorClass = 10, Type = Adnd.Core.Monsters.MonsterType.Undead }, 0);
            var session = new Adnd.Core.Combat.Sessions.CombatSession([lord], [skeleton]);
            var actions = new System.Collections.Generic.Dictionary<string, Adnd.Core.Combat.Actions.CombatActionType>
            {
                [lord.Name] = Adnd.Core.Combat.Actions.CombatActionType.DispellUndead
            };
            new Adnd.Core.Combat.Resolution.CombatResolver(new MaxDice()).ResolveRound(session, actions);
            return session.DispelledMonsters.Count == 1;
        }

        Assert.False(Dispels(3));
        Assert.True(Dispels(4));
    }

    [Fact]
    public void Cursed_item_stays_equipped_until_uncursed_for_half_its_value()
    {
        var c = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(15, 12, 12, 15, 14, 15), WizardryAlignment.Good, WizardryClass.Fighter, new Random(1));
        var helmet = new Adnd.Core.Items.Item { Name = "Cursed Helmet -2", Type = Adnd.Core.Items.ItemType.Armor, Slot = Adnd.Core.Items.EquipmentSlot.Head, IsCursed = true, Cost = 300 };
        c.Equipment[Adnd.Core.Items.EquipmentSlot.Head] = helmet;

        Assert.False(EquipmentManager.Unequip(c, Adnd.Core.Items.EquipmentSlot.Head));
        Assert.Equal(150, WizardryIdentification.ShopUncurseCost(helmet));

        EquipmentManager.RemoveCurse(helmet);
        Assert.True(EquipmentManager.Unequip(c, Adnd.Core.Items.EquipmentSlot.Head));
    }

    [Fact]
    public void Inn_rooms_use_wizardry_hit_points_and_fees_per_week()
    {
        Assert.Equal([0, 1, 3, 7, 10], WizardryRules.InnRooms.Select(r => r.HitPointsPerWeek));
        Assert.Equal([0, 10, 50, 200, 500], WizardryRules.InnRooms.Select(r => r.FeePerWeek));
    }

    [Fact]
    public void Good_and_evil_cannot_party_together_but_neutral_can_join_anyone()
    {
        Character Make(WizardryAlignment a) => WizardryCharacterFactory.Create(Race.Human, new WizardryStats(12, 12, 12, 12, 12, 12), a, WizardryClass.Fighter, new Random(1));
        var good = Make(WizardryAlignment.Good);
        var evil = Make(WizardryAlignment.Evil);
        var neutral = Make(WizardryAlignment.Neutral);

        Assert.False(WizardryRules.CanJoinParty(evil, [good]));
        Assert.False(WizardryRules.CanJoinParty(good, [neutral, evil]));
        Assert.True(WizardryRules.CanJoinParty(neutral, [good, evil]));
        Assert.True(WizardryRules.CanJoinParty(good, [neutral, good]));
        Assert.True(WizardryRules.CanJoinParty(evil, []));
    }

    [Fact]
    public void Ninja_critical_chance_and_monster_avoidance()
    {
        Assert.Equal(2, WizardryRules.NinjaCriticalChancePercent(1));
        Assert.Equal(20, WizardryRules.NinjaCriticalChancePercent(10));
        Assert.Equal(50, WizardryRules.NinjaCriticalChancePercent(40));

        Assert.True(WizardryRules.MonsterAvoidsNinjaCritical(5, 15));
        Assert.False(WizardryRules.MonsterAvoidsNinjaCritical(5, 16));
        Assert.True(WizardryRules.MonsterAvoidsNinjaCritical(24, 34));
    }

    [Fact]
    public void Ninja_critical_kills_a_hit_monster_instantly()
    {
        var ninja = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(17, 17, 17, 17, 17, 17), WizardryAlignment.Evil, WizardryClass.Ninja, new Random(1));
        ninja.Level = 25;
        ninja.MaxHitPoints = ninja.CurrentHitPoints = 80;
        var ogre = new Adnd.Core.Combat.Sessions.MonsterInstance(
            new Adnd.Core.Monsters.Monster { Name = "Ogre", HitDice = 1, ArmorClass = 10 }, 0);
        ogre.CurrentHitPoints = 500;
        var session = new Adnd.Core.Combat.Sessions.CombatSession([ninja], [ogre]);
        var actions = new System.Collections.Generic.Dictionary<string, Adnd.Core.Combat.Actions.CombatActionType>
        {
            [ninja.Name] = Adnd.Core.Combat.Actions.CombatActionType.Fight
        };

        new Adnd.Core.Combat.Resolution.CombatResolver(new CritDice()).ResolveRound(session, actions);

        Assert.True(ogre.CurrentHitPoints <= 0);
    }

    [Fact]
    public void Fled_monster_gives_xp_in_adnd_but_not_in_wizardry()
    {
        bool GivesXp(Ruleset ruleset)
        {
            var hero = new Character { Name = "H", Ruleset = ruleset };
            var monster = new Adnd.Core.Combat.Sessions.MonsterInstance(
                new Adnd.Core.Monsters.Monster { Name = "Orc", HitDice = 1, ArmorClass = 10 }, 0);
            var session = new Adnd.Core.Combat.Sessions.CombatSession([hero], [monster]);
            monster.HasFled = true;
            monster.CurrentHitPoints = 0;
            return session.MonsterGivesXp(monster);
        }

        Assert.False(GivesXp(Ruleset.Wizardry));
        Assert.True(GivesXp(Ruleset.ADND));
    }

    [Fact]
    public void Only_run_ability_monsters_flee_and_only_when_demoralized()
    {
        (bool fled, bool demoralized) Run(bool hasRun, int partyLevel, int monsterHitDice)
        {
            var hero = new Character { Name = "H", Ruleset = Ruleset.Wizardry, Level = partyLevel, MaxHitPoints = 100, CurrentHitPoints = 100, ArmorClass = 10, Damage = "1d4" };
            var template = new Adnd.Core.Monsters.Monster { Name = "Kobold", HitDice = monsterHitDice, ArmorClass = 10 };
            if (hasRun)
                template.SpecialAbilities.Add(new Adnd.Core.Monsters.MonsterSpecialAbility { Name = "Run" });
            var monster = new Adnd.Core.Combat.Sessions.MonsterInstance(template, 0);
            var session = new Adnd.Core.Combat.Sessions.CombatSession([hero], [monster]);
            var actions = new System.Collections.Generic.Dictionary<string, Adnd.Core.Combat.Actions.CombatActionType>
            {
                [hero.Name] = Adnd.Core.Combat.Actions.CombatActionType.Parry
            };
            var demoralized = session.AreMonstersDemoralized();
            new Adnd.Core.Combat.Resolution.CombatResolver(new LowDice()).ResolveRound(session, actions);
            return (monster.HasFled, demoralized);
        }

        Assert.Equal((true, true), Run(hasRun: true, partyLevel: 10, monsterHitDice: 2));
        Assert.Equal((false, true), Run(hasRun: false, partyLevel: 10, monsterHitDice: 2));
        Assert.Equal((false, false), Run(hasRun: true, partyLevel: 2, monsterHitDice: 2));
    }

    [Fact]
    public void Monster_xp_depends_on_hit_points_in_adnd_but_not_in_wizardry()
    {
        int Xp(Adnd.Core.Config.Sources source)
        {
            var template = new Adnd.Core.Monsters.Monster { Name = "Orc", HitDice = 3, ArmorClass = 10, BaseXPValue = 100, Source = source };
            return Adnd.Core.Combat.Sessions.CombatSession.GetMonsterXp(new Adnd.Core.Combat.Sessions.MonsterInstance(template, 0), 5);
        }

        Assert.Equal(100, Xp(Adnd.Core.Config.Sources.Wizardry));
        Assert.True(Xp(Adnd.Core.Config.Sources.Adnd) > 100);
    }

    [Fact]
    public void Montino_from_a_monster_priest_silences_party_members_who_fail_their_save()
    {
        var hero = new Character { Name = "H", Ruleset = Ruleset.Wizardry, Level = 1, MaxHitPoints = 100, CurrentHitPoints = 100, ArmorClass = 10, Damage = "1d4" };
        var template = new Adnd.Core.Monsters.Monster { Name = "Priest", HitDice = 3, ArmorClass = 10, Source = Adnd.Core.Config.Sources.Wizardry };
        template.SpecialAbilities.Add(new Adnd.Core.Monsters.MonsterSpecialAbility { Name = "Level 2 Priest spells", Description = "Level 2 Priest spells" });
        var session = new Adnd.Core.Combat.Sessions.CombatSession([hero], [new Adnd.Core.Combat.Sessions.MonsterInstance(template, 0)]);
        var actions = new System.Collections.Generic.Dictionary<string, Adnd.Core.Combat.Actions.CombatActionType>
        {
            [hero.Name] = Adnd.Core.Combat.Actions.CombatActionType.Parry
        };

        // Constant 2: picks the last castable spell (Montino) and fails the save.
        new Adnd.Core.Combat.Resolution.CombatResolver(new FixedValueDice(2)).ResolveRound(session, actions);

        Assert.True(session.IsPartySilenced(hero.Name));
    }

    [Fact]
    public void Bishop_spell_points_follow_half_level_mage_circles_after_refresh()
    {
        var bishop = new Character { Name = "B", Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Bishop, Level = 5 };
        bishop.Classes.Add(CharacterClass.MagicUser);
        bishop.Spellcasting.Add(new Adnd.Core.Spells.SpellcastingState
        {
            SpellClass = Adnd.Core.Spells.SpellClass.MagicUser,
            SlotsPerDay = [2],
            SlotsUsed = [0]
        });

        bishop.RefreshRingWizardryEffects();

        var slots = bishop.Spellcasting.Single(s => s.SpellClass == Adnd.Core.Spells.SpellClass.MagicUser).SlotsPerDay;
        var highestCircle = slots.FindLastIndex(s => s > 0) + 1;
        Assert.Equal(Adnd.Core.Spells.SpellProgression.WizardryBishopCircles(5).Mage, highestCircle);
        Assert.True(highestCircle < 3);
    }

    private sealed class FixedValueDice(int value) : Adnd.Core.Dices.IDice
    {
        public int Roll(int sides) => Math.Min(value, sides);
        public int RollMany(int sides, int count) => Roll(sides) * count;
        public int GetNumberOfSuccesses(int numberOfTries, float probabilityEachTry) => 0;
    }

    [Fact]
    public void Silenced_party_member_cannot_cast_and_silence_wears_off()
    {
        var session = new Adnd.Core.Combat.Sessions.CombatSession([new Character { Name = "H" }], []);
        session.SetPartySilenced("H", 2);
        Assert.True(session.IsPartySilenced("H"));
        session.TickPartySilenced();
        Assert.True(session.IsPartySilenced("H"));
        session.TickPartySilenced();
        Assert.False(session.IsPartySilenced("H"));
    }

    private sealed class LowDice : Adnd.Core.Dices.IDice
    {
        public int Roll(int sides) => 1;
        public int RollMany(int sides, int count) => count;
        public int GetNumberOfSuccesses(int numberOfTries, float probabilityEachTry) => 0;
    }

    private sealed class MaxDice : Adnd.Core.Dices.IDice
    {
        public int Roll(int sides) => sides;
        public int RollMany(int sides, int count) => sides * count;
        public int GetNumberOfSuccesses(int numberOfTries, float probabilityEachTry) => numberOfTries;
    }

    // Maximum rolls, except d100 which rolls 1 so percentage chances always succeed.
    private sealed class CritDice : Adnd.Core.Dices.IDice
    {
        public int Roll(int sides) => sides == 100 ? 1 : sides;
        public int RollMany(int sides, int count) => sides * count;
        public int GetNumberOfSuccesses(int numberOfTries, float probabilityEachTry) => numberOfTries;
    }

    [Fact]
    public void Murphys_ghost_is_one_in_a_thousand_on_level_one()
    {
        var random = new Random(12345);
        var ghosts = Enumerable.Range(0, 100_000)
            .Count(_ => Adnd.Core.Monsters.WizardryEncounterTable.Pick(1, random) == "Murphy's Ghost");
        Assert.InRange(ghosts, 50, 160);
    }
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
    public void Priest_blaster_hits_priests_and_bishops_in_wizardry()
    {
        var priest = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Priest };
        var bishop = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Bishop };
        var mage = new Character { Ruleset = Ruleset.Wizardry, WizardryClass = WizardryClass.Mage };

        Assert.True(Adnd.Core.Treasure.ChestTrapTargets.IsHitByPriestBlaster(priest));
        Assert.True(Adnd.Core.Treasure.ChestTrapTargets.IsHitByPriestBlaster(bishop));
        Assert.False(Adnd.Core.Treasure.ChestTrapTargets.IsHitByPriestBlaster(mage));
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
    public void Ninja_is_only_available_through_class_change()
    {
        var stats = new WizardryStats(17, 17, 17, 17, 17, 17);
        Assert.DoesNotContain(WizardryClass.Ninja, WizardryRules.EligibleClasses(stats, WizardryAlignment.Evil));
        Assert.Contains(WizardryClass.Ninja, WizardryRules.EligibleClasses(stats, WizardryAlignment.Evil, classChange: true));
    }

    [Fact]
    public void Lord_is_only_available_through_class_change()
    {
        var stats = new WizardryStats(15, 12, 12, 15, 14, 15);
        Assert.DoesNotContain(WizardryClass.Lord, WizardryRules.EligibleClasses(stats, WizardryAlignment.Good));
        Assert.Contains(WizardryClass.Lord, WizardryRules.EligibleClasses(stats, WizardryAlignment.Good, classChange: true));
    }

    [Theory]
    [InlineData(3, -1)]
    [InlineData(4, -1)]
    [InlineData(5, 0)]
    [InlineData(14, 0)]
    [InlineData(15, 1)]
    [InlineData(17, 2)]
    [InlineData(18, 3)]
    public void Luck_bonus_to_saving_throws(int luck, int bonus)
        => Assert.Equal(bonus, WizardryRules.SaveLuckBonus(luck));

    [Fact]
    public void Luck_improves_every_save_for_wizardry_but_not_adnd()
    {
        var service = new Adnd.Core.Characters.CharacterSavingThrowService();
        Adnd.Core.Characters.Character Make(Adnd.Core.Config.Ruleset rs, int luck) => new()
        {
            Name = "T", Classes = [Adnd.Core.Characters.CharacterClass.Fighter], Level = 1, Ruleset = rs,
            Abilities = new Adnd.Core.Characters.AbilityScores { Charisma = luck }
        };

        foreach (var type in Enum.GetValues<Adnd.Core.Characters.SaveThrowType>())
        {
            var plain = service.GetSaveTarget(Make(Adnd.Core.Config.Ruleset.Wizardry, 10), type, false);
            Assert.Equal(plain - 2, service.GetSaveTarget(Make(Adnd.Core.Config.Ruleset.Wizardry, 17), type, false));
            Assert.Equal(plain, service.GetSaveTarget(Make(Adnd.Core.Config.Ruleset.ADND, 17), type, false));
        }
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

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 1, 1)]
    [InlineData(6, 2, 1)]
    public void Bishop_casts_as_half_level_mage_and_third_level_priest(int level, int mage, int priest)
    {
        Assert.Equal((mage, priest), Adnd.Core.Spells.SpellProgression.WizardryBishopCircles(level));
    }

    [Fact]
    public void Bishop_level_6_has_circle_one_priest_and_circle_two_mage_spells()
    {
        var tracks = Adnd.Core.Spells.SpellProgression.GetWizardryBishopTracks(6);
        Assert.Equal(2, tracks.Single(t => t.SpellClass == Adnd.Core.Spells.SpellClass.MagicUser).SlotsPerDay.Count(s => s > 0));
        Assert.Equal(1, tracks.Single(t => t.SpellClass == Adnd.Core.Spells.SpellClass.Cleric).SlotsPerDay.Count(s => s > 0));
    }

    [Fact]
    public void Bishop_level_1_has_no_priest_spells()
    {
        var tracks = Adnd.Core.Spells.SpellProgression.GetWizardryBishopTracks(1);
        Assert.DoesNotContain(tracks, t => t.SpellClass == Adnd.Core.Spells.SpellClass.Cleric);
        Assert.Single(tracks.Single(t => t.SpellClass == Adnd.Core.Spells.SpellClass.MagicUser).SlotsPerDay, s => s > 0);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(12, 3)]
    [InlineData(16, 4)]
    [InlineData(20, 5)]
    [InlineData(24, 6)]
    [InlineData(28, 7)]
    [InlineData(40, 7)]
    public void Samurai_learns_mage_circle_every_four_levels(int level, int circle)
    {
        var tracks = Adnd.Core.Spells.SpellProgression.GetWizardrySamuraiTracks(level);
        var highest = tracks.Count == 0 ? 0 : tracks.Single().SlotsPerDay.FindLastIndex(s => s > 0) + 1;
        Assert.Equal(circle, highest);
        Assert.All(tracks, t => Assert.Equal(Adnd.Core.Spells.SpellClass.MagicUser, t.SpellClass));
    }

    [Fact]
    public void Lord_learns_priest_circle_every_four_levels()
    {
        foreach (var (level, circle) in new[] { (3, 0), (4, 1), (8, 2), (12, 3), (16, 4), (20, 5), (24, 6), (28, 7), (40, 7) })
        {
            var tracks = Adnd.Core.Spells.SpellProgression.GetWizardryLordTracks(level);
            var highest = tracks.Count == 0 ? 0 : tracks.Single().SlotsPerDay.FindLastIndex(s => s > 0) + 1;
            Assert.Equal(circle, highest);
            Assert.All(tracks, t => Assert.Equal(Adnd.Core.Spells.SpellClass.Cleric, t.SpellClass));
        }
    }

    [Fact]
    public void Agility_does_not_change_wizardry_armor_class()
    {
        var c = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(10, 10, 10, 10, 18, 10), WizardryAlignment.Good, WizardryClass.Fighter, new Random(1));
        Assert.Equal(0, c.DexterityArmorClassModifier);
        c.RefreshMonkProgressionStats();
        Assert.Equal(10, c.ArmorClass);
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

    [Fact]
    public void Wizardry_characters_level_with_wizardry_tables_and_no_race_cap()
    {
        var dwarf = WizardryCharacterFactory.Create(Race.Dwarf, new WizardryStats(12, 10, 10, 12, 10, 10), WizardryAlignment.Good, WizardryClass.Fighter, new Random(1));
        var cls = dwarf.Classes[0];
        var level8 = (int)WizardryRules.TotalXpForLevel(WizardryClass.Fighter, 8);
        var level13 = (int)WizardryRules.TotalXpForLevel(WizardryClass.Fighter, 13);

        Assert.Equal(8, Adnd.Core.Characters.Progression.ExperienceTable.GetLevelForClass(dwarf, cls, level8));
        Assert.Equal(13, Adnd.Core.Characters.Progression.ExperienceTable.GetLevelForClass(dwarf, cls, level13));
        Assert.Equal(level13, Adnd.Core.Characters.Progression.ExperienceTable.GetThresholdForLevel(dwarf, cls, 13));
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
    public void Spell_items_may_break_after_use_in_wizardry_only()
    {
        Assert.Equal(5, WizardryRules.ItemBreakChancePercent("Ring of Shielding"));
        Assert.Equal(10, WizardryRules.ItemBreakChancePercent("Ring of Rigidity"));
        Assert.Equal(10, WizardryRules.ItemBreakChancePercent("Rod of Flame"));
        Assert.Equal(5, WizardryRules.ItemBreakChancePercent("Ring of Suffocation"));
        Assert.Equal(0, WizardryRules.ItemBreakChancePercent("Potion of Dios"));

        var c = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(12, 10, 10, 12, 10, 10), WizardryAlignment.Neutral, WizardryClass.Mage, new Random(1));
        var rod = new Adnd.Core.Items.Item { Name = "Rod of Flame", Type = Adnd.Core.Items.ItemType.MagicItem };
        c.Inventory.Add(rod);

        Assert.False(WizardryRules.TryBreakItemAfterUse(c, rod, _ => 11));
        Assert.Contains(rod, c.Inventory);
        Assert.True(WizardryRules.TryBreakItemAfterUse(c, rod, _ => 10));
        Assert.DoesNotContain(rod, c.Inventory);
    }

    [Fact]
    public void Equipping_an_item_of_the_wrong_alignment_curses_it()
    {
        var good = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(12, 10, 10, 12, 10, 10), WizardryAlignment.Good, WizardryClass.Fighter, new Random(1));
        var evil = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(12, 10, 10, 12, 10, 10), WizardryAlignment.Evil, WizardryClass.Fighter, new Random(1));
        Adnd.Core.Items.Item Sword() => new() { Name = "Evil Sword +3", Type = Adnd.Core.Items.ItemType.Weapon, Slot = Adnd.Core.Items.EquipmentSlot.MainHand };

        var wrong = Sword();
        good.Inventory.Add(wrong);
        Assert.True(EquipmentManager.Equip(good, wrong));
        Assert.True(wrong.IsCursed);
        Assert.False(EquipmentManager.Unequip(good, Adnd.Core.Items.EquipmentSlot.MainHand));

        var right = Sword();
        evil.Inventory.Add(right);
        Assert.True(EquipmentManager.Equip(evil, right));
        Assert.False(right.IsCursed);
        Assert.Equal(WizardryAlignment.Neutral, WizardryClassChange.ItemAlignment("Neutral Plate +2"));
    }

    [Fact]
    public void Class_change_from_bishop_to_mage_keeps_known_priest_spells()
    {
        var c = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(10, 14, 14, 10, 10, 10), WizardryAlignment.Good, WizardryClass.Bishop, new Random(1));
        c.Spellcasting.Add(new Adnd.Core.Spells.SpellcastingState
        {
            SpellClass = Adnd.Core.Spells.SpellClass.Cleric,
            SlotsPerDay = [1],
            SlotsUsed = [0],
            KnownSpellIds = ["wiz_priest_test"]
        });
        var priestSpells = c.Spellcasting.Where(s => s.SpellClass != Adnd.Core.Spells.SpellClass.MagicUser).SelectMany(s => s.KnownSpellIds).ToList();
        Assert.NotEmpty(priestSpells);

        WizardryClassChange.Apply(c, WizardryClass.Mage, new WizardryStats(10, 14, 10, 10, 10, 10), new Random(1));

        var kept = c.Spellcasting.Where(s => s.SpellClass != Adnd.Core.Spells.SpellClass.MagicUser).SelectMany(s => s.KnownSpellIds).ToList();
        Assert.All(priestSpells, id => Assert.Contains(id, kept));
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
    public void Class_change_targets_are_evaluated_on_current_stats_before_reset()
    {
        var strong = new WizardryStats(17, 17, 17, 17, 17, 17);
        var c = WizardryCharacterFactory.Create(Race.Human, strong, WizardryAlignment.Good, WizardryClass.Fighter, new Random(1));
        var current = new WizardryStats(c.Abilities.Strength, c.Abilities.Intelligence, c.Abilities.Wisdom, c.Abilities.Constitution, c.Abilities.Dexterity, c.Abilities.Charisma);

        var targets = WizardryClassChange.EligibleTargets(c, current).ToList();

        Assert.DoesNotContain(WizardryClass.Fighter, targets);
        Assert.Contains(WizardryClass.Lord, targets);
        Assert.Empty(WizardryClassChange.EligibleTargets(c, WizardryRules.RaceBaseStats(Race.Human)).Where(t => t == WizardryClass.Lord));
    }

    [Fact]
    public void Temple_heals_hit_points_only_in_adnd()
    {
        Assert.False(WizardryRules.TempleHealsHitPoints(new Character { Ruleset = Ruleset.Wizardry }));
        Assert.True(WizardryRules.TempleHealsHitPoints(new Character { Ruleset = Ruleset.ADND }));
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
    [InlineData(WizardryClass.Ninja, 12, null, 3)]
    [InlineData(WizardryClass.Samurai, 7, null, 1)]
    [InlineData(WizardryClass.Lord, 8, null, 2)]
    [InlineData(WizardryClass.Samurai, 28, null, 7)]
    [InlineData(WizardryClass.Fighter, 31, null, 8)]
    [InlineData(WizardryClass.Mage, 20, null, 1)]
    [InlineData(WizardryClass.Mage, 3, "Dagger of Speed", 7)]
    [InlineData(WizardryClass.Fighter, 20, "Long Sword +1", 6)]
    [InlineData(WizardryClass.Fighter, 4, "Blade Cusinart", 4)]
    public void Attacks_are_class_progression_with_weapon_swing_minimum(WizardryClass cls, int level, string weapon, int expected)
    {
        Assert.Equal(expected, WizardryAttacks.AttacksPerRound(cls, level, weapon));
    }
}
