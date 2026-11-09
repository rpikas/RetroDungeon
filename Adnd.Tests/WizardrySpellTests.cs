using System;
using System.Collections.Generic;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Core.Wizardry;
using Xunit;

namespace Adnd.Tests;

// Each test guards a rule in docs\Wizardry-Handbook.md, section 5.
public class WizardrySpellTests
{
    [Fact]
    public void Spell_lists_have_the_handbook_sizes_per_circle()
    {
        var mage = WizardrySpells.ForSchool(SpellClass.MagicUser).GroupBy(s => s.Level).OrderBy(g => g.Key).Select(g => g.Count());
        var priest = WizardrySpells.ForSchool(SpellClass.Cleric).GroupBy(s => s.Level).OrderBy(g => g.Key).Select(g => g.Count());

        Assert.Equal(new[] { 4, 3, 2, 3, 3, 4, 3 }, mage);
        Assert.Equal(new[] { 5, 4, 4, 4, 6, 4, 2 }, priest);
    }

    [Fact]
    public void Spells_are_tagged_wizardry_with_unique_ids()
    {
        Assert.All(WizardrySpells.All, s => Assert.Equal(Ruleset.Wizardry, s.Ruleset));
        Assert.Equal(WizardrySpells.All.Count, WizardrySpells.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Spell_names_sit_in_the_right_school_and_circle()
    {
        Assert.Equal((SpellClass.MagicUser, 7), Find("Tiltowait"));
        Assert.Equal((SpellClass.Cleric, 7), Find("Malikto"));
        Assert.Equal((SpellClass.Cleric, 5), Find("Badi"));
        Assert.Equal((SpellClass.MagicUser, 6), Find("Zilwan"));
    }

    [Fact]
    public void Damage_dice_match_the_handbook()
    {
        Assert.Equal(new WizardrySpellEffect(WizardryEffectKind.Damage, 4, 6, true), WizardrySpells.GetEffect(WizardrySpells.ToId("Mahalito")));
        Assert.Equal(new WizardrySpellEffect(WizardryEffectKind.Damage, 10, 10, true), WizardrySpells.GetEffect(WizardrySpells.ToId("Tiltowait")));
        Assert.Equal(new WizardrySpellEffect(WizardryEffectKind.Damage, 2, 8, false), WizardrySpells.GetEffect(WizardrySpells.ToId("Badial")));
    }

    [Theory]
    [InlineData(WizardryClass.Mage, "Halito", "Katino")]
    [InlineData(WizardryClass.Bishop, "Halito", "Katino")]
    [InlineData(WizardryClass.Priest, "Dios", "Badios")]
    public void New_casters_start_with_their_two_spells(WizardryClass cls, string first, string second)
    {
        var character = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(10, 12, 12, 10, 10, 10), WizardryAlignment.Good, cls, new Random(1));

        var known = character.Spellcasting.SelectMany(s => s.KnownSpellIds).ToList();
        Assert.Contains(WizardrySpells.ToId(first), known);
        Assert.Contains(WizardrySpells.ToId(second), known);
    }

    [Fact]
    public void Fighters_start_without_spells()
    {
        var fighter = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(12, 8, 8, 10, 10, 10), WizardryAlignment.Good, WizardryClass.Fighter, new Random(1));
        Assert.Empty(fighter.Spellcasting);
    }

    [Fact]
    public void First_spell_of_a_circle_is_learned_when_slots_open()
    {
        var mage = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(10, 12, 12, 10, 10, 10), WizardryAlignment.Good, WizardryClass.Mage, new Random(1));
        var state = mage.Spellcasting.Single();
        state.SlotsPerDay = new List<int> { 1, 1 };

        WizardrySpellLearning.EnsureFirstSpellOfEachCircle(mage);

        Assert.Contains(WizardrySpells.ToId("Dilto"), state.KnownSpellIds);
    }

    [Fact]
    public void Level_up_learns_unknown_spells_with_iq_over_30_chance()
    {
        var smart = WizardryCharacterFactory.Create(Race.Human, new WizardryStats(10, 30, 12, 10, 10, 10), WizardryAlignment.Good, WizardryClass.Mage, new Random(1));
        var state = smart.Spellcasting.Single();
        state.SlotsPerDay = new List<int> { 4 };
        smart.Abilities.Intelligence = 30;
        var circle1 = WizardrySpells.All.Count(s => s.SpellClass == state.SpellClass && s.Level == 1);

        WizardrySpellLearning.LearnOnLevelUp(smart, 1, new Random(1));
        Assert.Equal(circle1, state.KnownSpellIds.Count);

        smart.Abilities.Intelligence = 0;
        state.KnownSpellIds.Clear();
        WizardrySpellLearning.LearnOnLevelUp(smart, 1, new Random(1));
        Assert.Single(state.KnownSpellIds);
    }

    [Fact]
    public void Dios_heals_a_single_ally_and_madi_fully_heals_and_clears_statuses()
    {
        var caster = new Character { Name = "Priest", Ruleset = Ruleset.Wizardry, MaxHitPoints = 40, CurrentHitPoints = 10 };
        var handler = new WizardrySpellHandler();

        var dios = Cast(handler, caster, "Dios", new Random(3));
        Assert.True(dios.Success);
        Assert.InRange(caster.CurrentHitPoints, 11, 18);

        caster.AddStatus(CharacterStatus.Poisoned);
        var madi = Cast(handler, caster, "Madi", new Random(3));
        Assert.True(madi.Success);
        Assert.Equal(40, caster.CurrentHitPoints);
        Assert.Equal(CharacterStatus.None, caster.Status);
    }

    [Fact]
    public void Unimplemented_wizardry_spells_have_no_handler()
    {
        var handler = new WizardrySpellHandler();
        Assert.False(handler.CanHandle(WizardrySpells.ToId("Malor")));
        Assert.True(handler.CanHandle(WizardrySpells.ToId("Halito")));
        Assert.False(handler.CanHandle("magic_missile"));
    }

    private static (SpellClass School, int Circle) Find(string name)
    {
        var spell = WizardrySpells.All.Single(s => s.Name == name);
        return (spell.SpellClass, spell.Level);
    }

    private static SpellCastResult Cast(WizardrySpellHandler handler, Character caster, string spellName, Random rng)
    {
        var spell = WizardrySpells.All.Single(s => s.Name == spellName);
        return handler.Resolve(new SpellCastRequest
        {
            Caster = caster,
            SpellId = spell.Id,
            Spell = spell,
            Context = SpellUseContext.Exploration,
            Rng = rng,
            PartyTargets = new List<Character> { caster }
        });
    }
}

