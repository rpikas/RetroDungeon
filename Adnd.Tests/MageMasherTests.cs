using System.Reflection;
using Adnd.Core.Combat.Resolution;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Xunit;

namespace Adnd.Tests;

public class MageMasherTests
{
    // Same ability text as Mage Masher in Adnd.Data\Items\Wizardry.json.
    private static Item MageMasher() => new()
    {
        Name = "Mage Masher",
        Type = ItemType.Weapon,
        MagicBonus = 1,
        ToHitBonus = 1,
        SpecialAbilities = ["+2 vs. magic-using and enchanted creatures"],
    };

    private static int SituationalBonus(Item weapon, MonsterInstance target)
    {
        var method = typeof(CombatResolver).GetMethod("GetSituationalSwordBonusAgainstTarget",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int)method.Invoke(null, [weapon, target])!;
    }

    [Fact]
    public void MageMasher_GetsExtraPlusOneAgainstMagicUsers()
    {
        var mage = new MonsterInstance(new Monster { Name = "Lvl 5 Mage", HitDice = 5 }, 0);

        Assert.Equal(1, SituationalBonus(MageMasher(), mage));
    }

    [Fact]
    public void WereSlayer_GetsExtraPlusTwoAgainstLycanthropesOnly()
    {
        var weapon = new Item
        {
            Name = "Were Slayer",
            Type = ItemType.Weapon,
            MagicBonus = 1,
            ToHitBonus = 1,
            SpecialAbilities = ["+3 vs. lycanthropes and shape changers"],
        };
        var werewolf = new MonsterInstance(new Monster { Name = "Lycanthrope Werewolf", HitDice = 4 }, 0);
        var kobold = new MonsterInstance(new Monster { Name = "Kobold", HitDice = 1 }, 0);

        Assert.Equal(2, SituationalBonus(weapon, werewolf));
        Assert.Equal(0, SituationalBonus(weapon, kobold));
    }

    [Fact]
    public void MageMasher_GetsNoExtraBonusAgainstOrdinaryMonsters()
    {
        var kobold = new MonsterInstance(new Monster { Name = "Kobold", HitDice = 1 }, 0);

        Assert.Equal(0, SituationalBonus(MageMasher(), kobold));
    }
}
