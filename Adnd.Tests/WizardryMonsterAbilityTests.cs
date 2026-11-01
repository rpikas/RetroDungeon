using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
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
