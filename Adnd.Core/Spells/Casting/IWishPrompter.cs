using Adnd.Core.Items;

namespace Adnd.Core.Spells.Casting;

public enum WishKind
{
    HealOrCure,
    Item,
    Spell,
    CharacterImprovement
}

/// <summary>Asks the player what to wish for. Implemented by the UI; null results mean the wish was abandoned.</summary>
public interface IWishPrompter
{
    WishKind? ChooseKind();

    /// <summary>Returns a fresh copy of the wished-for item.</summary>
    Item? ChooseItem();

    /// <summary>Returns a fresh scroll of the wished-for spell (level 8 or lower).</summary>
    Item? ChooseSpellScroll();

    /// <summary>Returns one of Strength, Intelligence, Wisdom, Dexterity, Constitution or Charisma.</summary>
    string? ChooseAbility();
}
