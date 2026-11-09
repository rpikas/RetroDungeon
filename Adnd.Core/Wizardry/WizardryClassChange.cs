using Adnd.Core.Characters;
using Adnd.Core.Config;

namespace Adnd.Core.Wizardry;

/// <summary>Changing class in Wizardry. See docs\Wizardry-Handbook.md.</summary>
public static class WizardryClassChange
{
    public const int BaseWeeks = 252;
    public const int WeeksPerStep = 52;

    public static WizardryAlignment AlignmentOf(Character c) => c.Alignment switch
    {
        Alignment.LawfulGood or Alignment.NeutralGood or Alignment.ChaoticGood => WizardryAlignment.Good,
        Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil => WizardryAlignment.Evil,
        _ => WizardryAlignment.Neutral
    };

    /// <summary>Weeks of aging: (52 * random 0..2) + 252.</summary>
    public static int RollAgingWeeks(Random random) => (WeeksPerStep * random.Next(0, 3)) + BaseWeeks;

    /// <summary>Classes the character could switch to with the given (racial base + bonus) stats.</summary>
    public static IEnumerable<WizardryClass> EligibleTargets(Character c, WizardryStats stats)
        => WizardryRules.EligibleClasses(stats, AlignmentOf(c)).Where(k => k != c.WizardryClass);

    /// <summary>
    /// Level 1 and 0 XP in the new class; hit points kept; known spells kept; stats set to <paramref name="newStats"/>;
    /// equipment the new class cannot use is taken off; the character ages. Returns the weeks aged.
    /// </summary>
    public static int Apply(Character c, WizardryClass newClass, WizardryStats newStats, Random random)
    {
        var previousSpells = c.Spellcasting.ToList();

        c.WizardryClass = newClass;
        c.Classes = new List<CharacterClass> { WizardryCharacterFactory.AdndEquivalent(newClass) };
        c.Level = 1;
        c.Experience = 0;
        c.ClassProgressions = new List<ClassProgression>();
        c.EnsureClassProgressions();

        c.Abilities.Strength = newStats.Strength;
        c.Abilities.Intelligence = newStats.Iq;
        c.Abilities.Wisdom = newStats.Piety;
        c.Abilities.Constitution = newStats.Vitality;
        c.Abilities.Dexterity = newStats.Agility;
        c.Abilities.Charisma = newStats.Luck;

        WizardrySpellLearning.Initialize(c, newClass);
        foreach (var old in previousSpells)
        {
            var state = c.Spellcasting.FirstOrDefault(s => s.SpellClass == old.SpellClass);
            if (state == null)
                continue;
            foreach (var id in old.KnownSpellIds.Where(id => !state.KnownSpellIds.Contains(id)))
                state.KnownSpellIds.Add(id);
        }

        foreach (var slot in c.Equipment.Where(e => e.Value != null).Select(e => e.Key).ToList())
        {
            var item = c.Equipment[slot];
            if (item != null && !EquipmentManager.MeetsClassRequirement(c, item))
                EquipmentManager.Unequip(c, slot);
        }

        var weeks = RollAgingWeeks(random);
        c.AdvanceAgeByDays(weeks * 7);
        return weeks;
    }
}
