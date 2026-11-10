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
            var tracks = WizardryIdentification.IsBishop(character)
                ? SpellProgression.GetWizardryBishopTracks(character.Level)
                : SpellProgression.GetSpellcastingTracks(heldClass, character.Level);
            foreach (var track in tracks)
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

    /// <summary>
    /// On each level gained every unknown spell of an available circle is learned with chance IQ/30
    /// (Piety/30 for priest spells); the first spell of each circle is always learned.
    /// </summary>
    public static List<string> LearnOnLevelUp(Character character, int levelsGained, Random? random = null)
    {
        var rng = random ?? new Random();
        var learned = new List<string>();

        learned.AddRange(EnsureFirstSpellOfEachCircle(character));

        foreach (var state in character.Spellcasting)
        {
            var stat = state.SpellClass is SpellClass.MagicUser or SpellClass.Illusionist
                ? character.Abilities.Intelligence
                : character.Abilities.Wisdom;
            var chance = stat / 30.0;

            for (var i = 0; i < levelsGained; i++)
            {
                foreach (var spell in WizardrySpells.All.Where(s =>
                             s.SpellClass == state.SpellClass
                             && s.Level >= 1 && s.Level <= state.SlotsPerDay.Count
                             && state.SlotsPerDay[s.Level - 1] > 0
                             && !state.KnownSpellIds.Contains(s.Id)).ToList())
                {
                    var roll = rng.NextDouble();
                    var success = roll < chance;
                    Diagnostics.RuleApplicationInfo.Publish(
                        $"Wizardry spell learning: {character.Name} tries {spell.Name} (L{spell.Level}); chance {stat}/30 = {chance:P0}, rolled {roll:P0} -> {(success ? "learned" : "not learned")}.");
                    if (success)
                    {
                        state.KnownSpellIds.Add(spell.Id);
                        learned.Add($"{spell.Name} (L{spell.Level})");
                    }
                }
            }
        }

        return learned;
    }

    /// <summary>The first spell of each circle is always learned once the character has slots for that circle.</summary>
    public static List<string> EnsureFirstSpellOfEachCircle(Character character)
    {
        var added = new List<string>();
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
                added.Add($"{circleSpells[0].Name} (L{circleSpells[0].Level})");
            }
        }

        return added;
    }
}
