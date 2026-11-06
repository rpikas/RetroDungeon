using Adnd.Core.Characters;
using Adnd.Core.Spells;

namespace Adnd.Core.Wizardry;

/// <summary>Which Wizardry spells a character knows. Slots still come from the AD&D memorization system.</summary>
public static class WizardrySpellLearning
{
    public static IReadOnlyList<string> StartingSpellIds(WizardryClass cls) => cls switch
    {
        WizardryClass.Mage or WizardryClass.Bishop => new[] { WizardrySpells.ToId("Halito"), WizardrySpells.ToId("Katino") },
        WizardryClass.Priest => new[] { WizardrySpells.ToId("Dios"), WizardrySpells.ToId("Badios") },
        _ => Array.Empty<string>()
    };

    /// <summary>Builds the spellcasting states and starting spells for a new Wizardry character.</summary>
    public static void Initialize(Character character, WizardryClass cls)
    {
        character.Spellcasting.Clear();

        foreach (var heldClass in character.Classes)
        {
            foreach (var track in SpellProgression.GetSpellcastingTracks(heldClass, character.Level))
            {
                if (track.SlotsPerDay.All(s => s <= 0))
                    continue;

                character.Spellcasting.Add(new SpellcastingState
                {
                    SpellClass = track.SpellClass,
                    SlotsPerDay = track.SlotsPerDay,
                    SlotsUsed = Enumerable.Repeat(0, track.SlotsPerDay.Count).ToList(),
                    KnownSpellIds = StartingSpellIds(cls)
                        .Where(id => WizardrySpells.All.Any(s => s.Id == id && s.SpellClass == track.SpellClass))
                        .ToList()
                });
            }
        }

        EnsureFirstSpellOfEachCircle(character);
    }

    /// <summary>The first spell of each circle is always learned once the character has slots for that circle.</summary>
    public static void EnsureFirstSpellOfEachCircle(Character character)
    {
        foreach (var state in character.Spellcasting)
        {
            for (var circle = 1; circle <= state.SlotsPerDay.Count; circle++)
            {
                if (state.SlotsPerDay[circle - 1] <= 0)
                    continue;

                var circleSpells = WizardrySpells.All
                    .Where(s => s.SpellClass == state.SpellClass && s.Level == circle)
                    .ToList();
                if (circleSpells.Count == 0 || circleSpells.Any(s => state.KnownSpellIds.Contains(s.Id)))
                    continue;

                state.KnownSpellIds.Add(circleSpells[0].Id);
            }
        }
    }
}
