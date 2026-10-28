using Adnd.Core.Config;
using Adnd.Core.Monsters;
using Xunit;

namespace Adnd.Tests;

public class MonsterSourcePreferenceTests
{
    private static List<Monster> Kobolds() =>
    [
        new Monster { Name = "Kobold", Source = Sources.Adnd },
        new Monster { Name = "Kobold", Source = Sources.WizardryAndAdnd },
        new Monster { Name = "Kobold", Source = Sources.Wizardry }
    ];

    [Fact]
    public void OnlyWizardry_PutsWizardryEntryFirst()
    {
        var ordered = MonsterSourcePreference.Order(Kobolds(), SourceOptions.OnlyWizardry);

        Assert.Equal(Sources.Wizardry, ordered.First(m => m.Name == "Kobold").Source);
    }

    [Theory]
    [InlineData(SourceOptions.OnlyAdnd)]
    [InlineData(SourceOptions.AllButWizardry)]
    public void AdndOptions_PutAdndEntryFirst(SourceOptions option)
    {
        var ordered = MonsterSourcePreference.Order(Kobolds(), option);

        Assert.Equal(Sources.Adnd, ordered[0].Source);
        Assert.Equal(Sources.Wizardry, ordered[^1].Source);
    }

    [Fact]
    public void All_KeepsOriginalOrder()
    {
        var input = Kobolds();

        var ordered = MonsterSourcePreference.Order(input, SourceOptions.All);

        Assert.Equal(input, ordered);
    }
}
