using Adnd.Core.Characters;
using Xunit;

namespace Adnd.Tests;

public class RestorationRulesTests
{
    private static Character DrainedFighter(int daysAgo)
    {
        var c = new Character
        {
            Name = "Victim",
            Classes = [CharacterClass.Fighter],
            Level = 4,
            Experience = 4000,
            Age = 30
        };
        c.EnsureClassProgressions();
        c.LevelDrainHistory.Add(new LevelDrainRecord { Class = CharacterClass.Fighter, LevelBefore = 5, ExperienceBefore = 20000 });
        c.DaysSinceLevelDrain = daysAgo;
        c.ClassProgressions[0].Level = 4;
        return c;
    }

    [Fact]
    public void Cost_IsTenThousandPlusTenThousandPerRecipientLevel()
    {
        Assert.Equal(50000, RestorationRules.CostFor(new Character { Level = 4 }));
    }

    [Fact]
    public void Restoration_WithinClericLevelDays_RestoresLevelAndAgesBoth()
    {
        var recipient = DrainedFighter(daysAgo: 15);
        var caster = new Character { Name = "Cleric", Age = 40 };

        var events = RestorationRules.Apply(recipient, caster, clericLevel: 15);

        Assert.Equal(5, recipient.ClassProgressions[0].Level);
        Assert.Empty(recipient.LevelDrainHistory);
        Assert.Equal(32, recipient.Age);
        Assert.Equal(42, caster.Age);
        Assert.Contains(events, e => e.Contains("regains a lost level"));
    }

    [Fact]
    public void Restoration_TooLate_DoesNotRestoreLevel()
    {
        var recipient = DrainedFighter(daysAgo: 16);

        RestorationRules.Apply(recipient, null, clericLevel: 15);

        Assert.Equal(4, recipient.ClassProgressions[0].Level);
        Assert.Equal(30, recipient.Age);
    }

    [Fact]
    public void Restoration_CuresFeeblemind()
    {
        var recipient = new Character { Name = "Dim", Age = 20 };
        recipient.AddStatus(CharacterStatus.Feeblemind);

        RestorationRules.Apply(recipient, null, clericLevel: 15);

        Assert.False(recipient.HasStatus(CharacterStatus.Feeblemind));
    }
}
