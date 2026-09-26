using Adnd.Core.Spells;

namespace Adnd.Core.Characters.Progression;

public sealed class SpellProgressionService
{
    public List<(SpellClass SpellClass, List<int> OldSlots, List<int> NewSlots)> ApplyLevel(Character character, int oldLevel, int newLevel)
    {
        return RecalculateFromClassProgressions(character);
    }

    public List<(SpellClass SpellClass, List<int> OldSlots, List<int> NewSlots)> RecalculateFromClassProgressions(Character character)
    {
        var changes = new List<(SpellClass SpellClass, List<int> OldSlots, List<int> NewSlots)>();

        if (character.Spellcasting == null)
            character.Spellcasting = new List<SpellcastingState>();

        character.EnsureClassProgressions();

        var allTracks = new Dictionary<SpellClass, List<int>>();
        var classes = character.Classes != null && character.Classes.Count > 0
            ? character.Classes
            : new List<CharacterClass> { character.Class };

        foreach (var cls in classes)
        {
            var classLevel = character.GetClassLevel(cls);
            foreach (var track in SpellProgression.GetSpellcastingTracks(cls, classLevel))
            {
                if (!allTracks.TryGetValue(track.SpellClass, out var slots))
                {
                    allTracks[track.SpellClass] = new List<int>(track.SlotsPerDay);
                }
                else
                {
                    var maxLen = Math.Max(slots.Count, track.SlotsPerDay.Count);
                    while (slots.Count < maxLen) slots.Add(0);
                    for (int i = 0; i < track.SlotsPerDay.Count; i++)
                        slots[i] = Math.Max(slots[i], track.SlotsPerDay[i]);
                }
            }
        }

        var removedStates = character.Spellcasting
            .Where(s => !allTracks.ContainsKey(s.SpellClass))
            .ToList();

        foreach (var removed in removedStates)
        {
            character.Spellcasting.Remove(removed);
            changes.Add((removed.SpellClass, new List<int>(removed.SlotsPerDay), new List<int>()));
        }

        foreach (var kv in allTracks)
        {
            var spellClass = kv.Key;
            var newSlots = kv.Value;

            var state = character.Spellcasting.FirstOrDefault(s => s.SpellClass == spellClass);
            if (state == null)
            {
                state = new SpellcastingState
                {
                    SpellClass = spellClass,
                    SlotsPerDay = new List<int>(newSlots),
                    SlotsUsed = Enumerable.Repeat(0, newSlots.Count).ToList()
                };
                character.Spellcasting.Add(state);
                changes.Add((spellClass, new List<int>(), new List<int>(newSlots)));
                continue;
            }

            var oldSlots = new List<int>(state.SlotsPerDay);
            state.SlotsPerDay = new List<int>(newSlots);

            while (state.SlotsUsed.Count < state.SlotsPerDay.Count)
                state.SlotsUsed.Add(0);

            for (int i = 0; i < state.SlotsPerDay.Count; i++)
            {
                if (state.SlotsUsed[i] > state.SlotsPerDay[i])
                    state.SlotsUsed[i] = state.SlotsPerDay[i];
            }

            if (!oldSlots.SequenceEqual(state.SlotsPerDay))
                changes.Add((spellClass, oldSlots, new List<int>(state.SlotsPerDay)));
        }

        return changes;
    }
}
