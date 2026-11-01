using Adnd.Core.Characters;
using Adnd.Core.Items;
using Xunit;

namespace Adnd.Tests;

public class PoisonSaveBonusTests
{
    [Fact]
    public void WieldedItemWithPoisonSaveBonus_LowersPoisonSaveTargetByThree()
    {
        var service = new CharacterSavingThrowService();
        var c = new Character { Name = "T", Classes = [CharacterClass.Fighter], Level = 1 };
        var before = service.GetSaveTarget(c, SaveThrowType.ParalyzationPoisonDeath);

        c.Equipment[EquipmentSlot.MainHand] = new Item
        {
            Name = "Mace of Poison",
            SpecialAbilities = ["+3 to saves vs. poison when wielded"]
        };

        Assert.Equal(before - 3, service.GetSaveTarget(c, SaveThrowType.ParalyzationPoisonDeath));
        Assert.Equal(service.GetSaveTarget(new Character { Name = "U", Classes = [CharacterClass.Fighter], Level = 1 }, SaveThrowType.Spell),
            service.GetSaveTarget(c, SaveThrowType.Spell));
    }
}
