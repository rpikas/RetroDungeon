using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Xunit;

namespace Adnd.Tests;

public class SlayLivingHandlerTests
{
    private static MonsterInstance Monster(MonsterType type = MonsterType.Other, int deathResistancePercent = 0)
    {
        var template = new Monster { Name = "Test", Type = type, HitDice = 4, HitDiceType = 8 };
        if (deathResistancePercent > 0)
            template.SpecialAbilities.Add(new MonsterSpecialAbility { Name = "Death Resistance", Description = $"Death resistant ({deathResistancePercent}%)." });
        return new MonsterInstance(template, 0);
    }

    private static MonsterInstance Cast(MonsterInstance monster, int seed)
    {
        var request = new SpellCastRequest
        {
            Caster = new Character { Name = "Cleric" },
            SpellId = "slay_living",
            Context = SpellUseContext.Combat,
            Spell = new Spell { Id = "slay_living", Name = "Slay Living" },
            Targets = { SpellCastTarget.Enemy(monster.Index) },
            MonsterTargets = { monster },
            Rng = new Random(seed)
        };

        Assert.True(new SlayLivingHandler().Resolve(request).Success);
        return monster;
    }

    [Fact]
    public void Undead_AreNeverAffected()
    {
        for (var seed = 0; seed < 50; seed++)
            Assert.True(Cast(Monster(MonsterType.Undead), seed).IsAlive);
    }

    [Fact]
    public void FullDeathResistance_AlwaysSurvives()
    {
        for (var seed = 0; seed < 50; seed++)
            Assert.True(Cast(Monster(deathResistancePercent: 100), seed).IsAlive);
    }

    [Fact]
    public void NoResistance_CanBeSlain()
    {
        Assert.Contains(Enumerable.Range(0, 100), seed => !Cast(Monster(), seed).IsAlive);
    }
}
