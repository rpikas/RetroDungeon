using Adnd.Core.Characters;
using Adnd.Core.Config;
using Adnd.Core.Items;

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

    /// <summary>Wizardry items bound to an alignment: equipping one with the wrong alignment curses it.</summary>
    public static WizardryAlignment? ItemAlignment(string? itemName) => itemName switch
    {
        "Evil Sword +3" or "Evil S-Sword +3" or "Evil Chain +2" or "Evil Shield +3" or "Helm +2 (E)" or "Evil Plate +3" => WizardryAlignment.Evil,
        "Neutral Plate +2" => WizardryAlignment.Neutral,
        _ => null
    };

    /// <summary>True when a Wizardry character equips an alignment-bound item of another alignment (it becomes cursed).</summary>
    public static bool EquipCursesItem(Character c, Item item)
        => c.Ruleset == Ruleset.Wizardry && ItemAlignment(item.Name) is { } required && required != AlignmentOf(c);

    /// <summary>Weeks of aging: (52 * random 0..2) + 252.</summary>
    public static int RollAgingWeeks(Random random) => (WeeksPerStep * random.Next(0, 3)) + BaseWeeks;

    /// <summary>Classes the character could switch to with the given (racial base + bonus) stats.</summary>
    public static IEnumerable<WizardryClass> EligibleTargets(Character c, WizardryStats stats)
        => WizardryRules.EligibleClasses(stats, AlignmentOf(c), classChange: true).Where(k => k != c.WizardryClass);

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
            {
                // The new class has no slots for this spell class, but the spells stay known.
                c.Spellcasting.Add(new Adnd.Core.Spells.SpellcastingState
                {
                    SpellClass = old.SpellClass,
                    SlotsPerDay = old.SlotsPerDay.Select(_ => 0).ToList(),
                    SlotsUsed = old.SlotsPerDay.Select(_ => 0).ToList(),
                    KnownSpellIds = old.KnownSpellIds.ToList()
                });
                continue;
            }
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
