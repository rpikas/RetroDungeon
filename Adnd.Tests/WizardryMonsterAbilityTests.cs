using System.Linq;
using System.Linq;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Adnd.Core.Wizardry;
using Adnd.Core.Wizardry;
using Xunit;

namespace Adnd.Tests;

public class WizardryMonsterAbilityTests
{
    private static MonsterInstance Monster(params MonsterSpecialAbility[] abilities)
    {
        var template = new Monster { Name = "Test", HitDice = 4, HitDiceType = 8 };
        template.SpecialAbilities.AddRange(abilities);
        return new MonsterInstance(template, 0);
    }

    private static int CastDamage(MonsterInstance monster, string spellName)
    {
        var spell = WizardrySpells.All.Single(s => s.Name == spellName);
        var before = monster.CurrentHitPoints;
        new WizardrySpellHandler().Resolve(new SpellCastRequest
        {
            Caster = new Character { Name = "Mage", Ruleset = Adnd.Core.Config.Ruleset.Wizardry },
            SpellId = spell.Id,
            Spell = spell,
            Context = SpellUseContext.Combat,
            Targets = { SpellCastTarget.Enemy(monster.Index) },
            MonsterTargets = { monster },
            Rng = new Random(7)
        });
        return before - monster.CurrentHitPoints;
    }

    [Theory]
    [InlineData("Halito", "Fire Resistance")]
    [InlineData("Mahalito", "Fire Resistance")]
    [InlineData("Lahalito", "Fire Resistance")]
    [InlineData("Litokan", "Fire Resistance")]
    [InlineData("Dalto", "Cold Resistance")]
    [InlineData("Madalto", "Cold Resistance")]
    public void FireAndColdResistance_HalveMatchingWizardrySpellDamage(string spell, string ability)
    {
        var plain = Monster();
        plain.CurrentHitPoints = 500;
        var resistant = Monster(new MonsterSpecialAbility { Name = ability, Description = "Resistant." });
        resistant.CurrentHitPoints = 500;

        Assert.Equal(CastDamage(plain, spell) / 2, CastDamage(resistant, spell));
    }

    [Theory]
    [InlineData("Makanito")]
    [InlineData("Lakanito")]
    public void DeathResistance_LetsMonsterSurviveKillSpells(string spellName)
    {
        var resistant = Monster(new MonsterSpecialAbility { Name = "Death Resistance", Description = "Death resistant (100%)." });
        resistant.CurrentHitPoints = 10;
        var plain = Monster();
        plain.CurrentHitPoints = 10;

        Assert.Equal(0, CastDamage(resistant, spellName));
        Assert.Equal(10, CastDamage(plain, spellName));
    }

    [Fact]
    public void FireResistance_DoesNotHalveColdSpell()
    {
        var plain = Monster();
        plain.CurrentHitPoints = 500;
        var resistant = Monster(new MonsterSpecialAbility { Name = "Fire Resistance", Description = "Resistant." });
        resistant.CurrentHitPoints = 500;

        Assert.Equal(CastDamage(plain, "Dalto"), CastDamage(resistant, "Dalto"));
    }

    [Fact]
    public void FingerOfDeath_FullDeathResistance_AlwaysSurvives()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var monster = Monster(new MonsterSpecialAbility { Name = "Death Resistance", Description = "Death resistant (100%)." });
            var request = new SpellCastRequest
            {
                Caster = new Character { Name = "Druid" },
                SpellId = "finger_of_death",
                Context = SpellUseContext.Combat,
                Spell = new Spell { Id = "finger_of_death", Name = "Finger of Death" },
                Targets = { SpellCastTarget.Enemy(monster.Index) },
                MonsterTargets = { monster },
                Rng = new Random(seed)
            };

            Assert.True(new FingerOfDeathHandler().Resolve(request).Success);
            Assert.True(monster.IsAlive);
        }
    }
}
