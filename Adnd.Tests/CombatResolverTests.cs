using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Resolution;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Dices;
using Adnd.Core.Monsters;
using Xunit;

namespace Adnd.Tests;

public class CombatResolverTests
{
    private sealed class FixedDice(bool max) : IDice
    {
        public int Roll(int sides) => max ? sides : 1;
        public int RollMany(int sides, int count) => Roll(sides) * count;
        public int GetNumberOfSuccesses(int numberOfTries, float probabilityEachTry) => max ? numberOfTries : 0;
    }

    private static Character Fighter() => new()
    {
        Name = "Hero",
        Level = 5,
        MaxHitPoints = 40,
        CurrentHitPoints = 40,
        ArmorClass = 10,
        Damage = "1d8",
    };

    private static MonsterInstance Brute(int hitDice = 2, string damage = "1d6", int armorClass = 10)
    {
        var template = new Monster
        {
            Name = "Brute",
            HitDice = hitDice,
            ArmorClass = armorClass,
            THAC0 = 20,
            Attacks = [new MonsterAttack { Name = "Claw", NumberOfAttacks = 1, Damage = damage }],
        };
        return new MonsterInstance(template, 0);
    }

    private static Dictionary<string, CombatActionType> Fight(Character c) =>
        new() { [c.Name] = CombatActionType.Fight };

    [Fact]
    public void ResolveRound_AdvancesRoundNumberAndReportsEvents()
    {
        var hero = Fighter();
        var session = new CombatSession([hero], [Brute()]);
        var resolver = new CombatResolver(new FixedDice(max: true));

        var events = resolver.ResolveRound(session, Fight(hero));

        Assert.NotEmpty(events);
        Assert.Equal(2, session.RoundNumber);
    }

    [Fact]
    public void ResolveRound_PartyWithMaxRolls_DamagesMonster()
    {
        var hero = Fighter();
        var brute = Brute(hitDice: 30);
        var session = new CombatSession([hero], [brute]);
        var resolver = new CombatResolver(new FixedDice(max: true));

        resolver.ResolveRound(session, Fight(hero));

        Assert.True(brute.CurrentHitPoints < brute.MaxHitPoints);
    }

    [Fact]
    public void ResolveRound_MonsterWithMaxRolls_DamagesPartyMember()
    {
        var hero = Fighter();
        var session = new CombatSession([hero], [Brute(hitDice: 30, damage: "1d6")]);
        var resolver = new CombatResolver(new FixedDice(max: true));

        resolver.ResolveRound(session, Fight(hero));

        Assert.True(hero.CurrentHitPoints < hero.MaxHitPoints);
    }

    [Fact]
    public void TimeStop_CasterActsAndMonstersDoNot()
    {
        var hero = Fighter();
        var brute = Brute(hitDice: 30);
        var session = new CombatSession([hero], [brute]);
        session.StartTimeStop(hero.Name, 2);
        var resolver = new CombatResolver(new FixedDice(max: true));

        resolver.ResolveRound(session, Fight(hero));

        Assert.Equal(hero.MaxHitPoints, hero.CurrentHitPoints);
    }
}
