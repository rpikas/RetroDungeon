using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using Xunit;

namespace Adnd.Tests;

public class CloudkillHandlerTests
{
    private static MonsterInstance Cast(int hitDice, int extraHp, int seed, string spellId = "cloudkill")
    {
        var monster = new MonsterInstance(new Monster
        {
            Name = "Test", HitDice = hitDice, ExtraHitPoints = extraHp, HitDiceType = 8,
            SavingThrows = new MonsterSavingThrows { ParalyzationPoisonDeath = 10 }
        }, 0);
        var session = new CombatSession(new List<Character> { new() { Name = "Caster" } }, new List<MonsterInstance> { monster });
        var request = new SpellCastRequest
        {
            Caster = new Character { Name = "Caster" },
            SpellId = spellId,
            Context = SpellUseContext.Combat,
            Spell = new Spell { Id = spellId, Name = spellId, EffectDescription = "" },
            Targets = { SpellCastTarget.Enemy(monster.Index) },
            MonsterTargets = { monster },
            CombatSession = session,
            Rng = new Random(seed)
        };

        Assert.True(new CloudkillHandler().Resolve(request).Success);
        return monster;
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    public void MonstersBelowFourPlusOneHd_DieWithoutSave(int hd, int extra)
    {
        for (var seed = 0; seed < 30; seed++)
            Assert.False(Cast(hd, extra, seed).IsAlive);
    }

    [Fact]
    public void FourPlusOneToSixHd_GetASave()
    {
        foreach (var (hd, extra) in new[] { (4, 1), (5, 1), (6, 0) })
        {
            var outcomes = Enumerable.Range(0, 200).Select(s => Cast(hd, extra, s).IsAlive).ToList();
            Assert.Contains(true, outcomes);
            Assert.Contains(false, outcomes);
        }
    }

    [Fact]
    public void AboveSixHd_AreUnaffected()
    {
        for (var seed = 0; seed < 30; seed++)
            Assert.True(Cast(7, 0, seed).IsAlive);
    }

    [Fact]
    public void Makanito_UsesTheSameHandler()
        => Assert.True(new CloudkillHandler().CanHandle("makanito"));
}
