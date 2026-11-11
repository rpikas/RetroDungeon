using Adnd.Core.Config;

namespace Adnd.Core.Spells;

public enum WizardryEffectKind
{
    /// <summary>Not implemented yet: the spell is learnable but casting reports that.</summary>
    None,
    Damage,
    Heal,
    FullHeal,
    Sleep,
    Paralyze,
    Silence,
    Fear,
    /// <summary>Kills every target whose current HP is below a threshold (Makanito).</summary>
    KillWeak,
    /// <summary>Kills the target outright (Badi, Lakanito, Zilwan for undead).</summary>
    Kill,
    /// <summary>Reduces the target to 1d8 HP (Mabadi).</summary>
    ReduceToD8,
    /// <summary>Cures paralysis on one party member (Dialko).</summary>
    CureParalysis,
    /// <summary>Cures poison on one party member (Latumofis).</summary>
    CurePoison,
    /// <summary>Lowers the whole party's AC for the battle; Dice holds the amount (Matu, Bamatu).</summary>
    PartyArmorClass,
    /// <summary>Lowers the whole party's AC by Dice until the party leaves the dungeon (Maporfic).</summary>
    ExpeditionArmorClass
}

/// <param name="Dice">Number of dice rolled for damage or healing.</param>
/// <param name="Sides">Sides per die.</param>
/// <param name="Group">True when the spell hits a whole enemy group.</param>
/// <param name="UndeadOnly">True when only undead are valid targets (Zilwan).</param>
public sealed record WizardrySpellEffect(WizardryEffectKind Kind, int Dice = 0, int Sides = 0, bool Group = false, bool UndeadOnly = false);

/// <summary>The Wizardry Mage and Priest spell lists, circles 1-7.</summary>
public static class WizardrySpells
{
    public const string IdPrefix = "wiz_";

    private static readonly List<(string Name, SpellClass School, int Circle, string Description, SpellCastContext Context, WizardryEffectKind Kind, int Dice, int Sides, bool Group, bool UndeadOnly)> Table = new()
    {
        ("Dumapic", SpellClass.MagicUser, 1, "Gives the exact current coordinates of the caster, including depth.", SpellCastContext.Exploration, WizardryEffectKind.None, 0, 0, false, false),
        ("Halito", SpellClass.MagicUser, 1, "Fireball that deals 1d8 fire damage to a single target.", SpellCastContext.Combat, WizardryEffectKind.Damage, 1, 8, false, false),
        ("Katino", SpellClass.MagicUser, 1, "Puts one enemy group to sleep. Sleeping targets take double damage.", SpellCastContext.Combat, WizardryEffectKind.Sleep, 0, 0, true, false),
        ("Mogref", SpellClass.MagicUser, 1, "Lowers the caster's Armor Class by 2 for one battle.", SpellCastContext.Combat, WizardryEffectKind.None, 0, 0, false, false),
        ("Badios", SpellClass.Cleric, 1, "Inflicts 1d8 damage to a single target.", SpellCastContext.Combat, WizardryEffectKind.Damage, 1, 8, false, false),
        ("Dios", SpellClass.Cleric, 1, "Heals 1d8 HP of a single target.", SpellCastContext.Both, WizardryEffectKind.Heal, 1, 8, false, false),
        ("Kalki", SpellClass.Cleric, 1, "Lowers the party's Armor Class by 1 for one battle.", SpellCastContext.Combat, WizardryEffectKind.None, 0, 0, false, false),
        ("Milwa", SpellClass.Cleric, 1, "Lesser light: see farther and spot secret doors for a short time.", SpellCastContext.Exploration, WizardryEffectKind.None, 0, 0, false, false),
        ("Porfic", SpellClass.Cleric, 1, "Lowers the caster's Armor Class by 4 for one battle.", SpellCastContext.Combat, WizardryEffectKind.None, 0, 0, false, false),

        ("Dilto", SpellClass.MagicUser, 2, "Darkness raises an enemy group's Armor Class for one battle.", SpellCastContext.Combat, WizardryEffectKind.None, 0, 0, true, false),
        ("Melito", SpellClass.MagicUser, 2, "Sparks deal 1d8 damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 1, 8, true, false),
        ("Sopic", SpellClass.MagicUser, 2, "Invisibility lowers the caster's Armor Class by 4 for one battle.", SpellCastContext.Combat, WizardryEffectKind.None, 0, 0, false, false),
        ("Calfo", SpellClass.Cleric, 2, "Identifies the type of trap on a chest, not always accurately.", SpellCastContext.Exploration, WizardryEffectKind.None, 0, 0, false, false),
        ("Matu", SpellClass.Cleric, 2, "Lowers the party's Armor Class by 2 for one battle.", SpellCastContext.Combat, WizardryEffectKind.PartyArmorClass, 2, 0, false, false),
        ("Manifo", SpellClass.Cleric, 2, "Paralyzes an enemy group. Paralyzed targets take double damage.", SpellCastContext.Combat, WizardryEffectKind.Paralyze, 0, 0, true, false),
        ("Montino", SpellClass.Cleric, 2, "Silences an enemy group. Silenced targets cannot cast spells.", SpellCastContext.Combat, WizardryEffectKind.Silence, 0, 0, true, false),

        ("Mahalito", SpellClass.MagicUser, 3, "Firestorm deals 4d6 damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 4, 6, true, false),
        ("Molito", SpellClass.MagicUser, 3, "Energy blast deals 3d6 damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 3, 6, true, false),
        ("Bamatu", SpellClass.Cleric, 3, "Lowers the party's Armor Class by 4 for one battle.", SpellCastContext.Combat, WizardryEffectKind.PartyArmorClass, 4, 0, false, false),
        ("Dialko", SpellClass.Cleric, 3, "Cures paralysis and awakens sleepers.",         SpellCastContext.Both, WizardryEffectKind.CureParalysis, 0, 0, false, false),
        ("Latumapic", SpellClass.Cleric, 3, "Identifies all opponents for the rest of the stay in the dungeon.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Lomilwa", SpellClass.Cleric, 3, "Greater light lasting until you leave the dungeon or enter a dark zone.", SpellCastContext.Exploration, WizardryEffectKind.None, 0, 0, false, false),

        ("Dalto", SpellClass.MagicUser, 4, "Blizzard deals 6d6 freezing damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 6, 6, true, false),
        ("Lahalito", SpellClass.MagicUser, 4, "Inferno deals 6d6 fire damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 6, 6, true, false),
        ("Morlis", SpellClass.MagicUser, 4, "Lesser fear: a group may flee the battle.", SpellCastContext.Combat, WizardryEffectKind.Fear, 0, 0, true, false),
        ("Badial", SpellClass.Cleric, 4, "Inflicts 2d8 damage to a single target.", SpellCastContext.Combat, WizardryEffectKind.Damage, 2, 8, false, false),
        ("Latumofis", SpellClass.Cleric, 4, "Neutralizes poison.", SpellCastContext.Both, WizardryEffectKind.CurePoison, 0, 0, false, false),
        ("Dial", SpellClass.Cleric, 4, "Heals 2d8 HP of a single target.", SpellCastContext.Both, WizardryEffectKind.Heal, 2, 8, false, false),
        ("Maporfic", SpellClass.Cleric, 4, "Lowers the party's Armor Class by 2 for the stay in the dungeon.", SpellCastContext.Both, WizardryEffectKind.ExpeditionArmorClass, 2, 0, false, false),

        ("Madalto", SpellClass.MagicUser, 5, "Arctic frost deals 8d8 freezing damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 8, 8, true, false),
        ("Makanito", SpellClass.MagicUser, 5, "Suffocates all enemies with fewer than 35 HP, killing them instantly.", SpellCastContext.Combat, WizardryEffectKind.KillWeak, 0, 0, true, false),
        ("Mamorlis", SpellClass.MagicUser, 5, "Greater fear: all opponents may flee the battle.", SpellCastContext.Combat, WizardryEffectKind.Fear, 0, 0, true, false),
        ("Badi", SpellClass.Cleric, 5, "Death magic: kills a single target instantly.", SpellCastContext.Combat, WizardryEffectKind.Kill, 0, 0, false, false),
        ("Badialma", SpellClass.Cleric, 5, "Inflicts 3d8 damage to a single target.", SpellCastContext.Combat, WizardryEffectKind.Damage, 3, 8, false, false),
        ("Di", SpellClass.Cleric, 5, "Revives a fallen comrade with 1 HP; may fail and reduce them to ashes.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Dialma", SpellClass.Cleric, 5, "Heals 3d8 HP of a single target.", SpellCastContext.Both, WizardryEffectKind.Heal, 3, 8, false, false),
        ("Kandi", SpellClass.Cleric, 5, "Gives the location of missing or dead party members.", SpellCastContext.Exploration, WizardryEffectKind.None, 0, 0, false, false),
        ("Litokan", SpellClass.Cleric, 5, "Pillar of flame deals 3d8 fire damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 3, 8, true, false),

        ("Haman", SpellClass.MagicUser, 6, "Lesser divine intervention; the caster loses a level.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Lakanito", SpellClass.MagicUser, 6, "Suffocates all enemies in a group regardless of HP, killing them instantly.", SpellCastContext.Combat, WizardryEffectKind.Kill, 0, 0, true, false),
        ("Masopic", SpellClass.MagicUser, 6, "Lowers the party's Armor Class by 4 for one battle.", SpellCastContext.Combat, WizardryEffectKind.None, 0, 0, false, false),
        ("Zilwan", SpellClass.MagicUser, 6, "Instantly destroys one undead monster.", SpellCastContext.Combat, WizardryEffectKind.Kill, 0, 0, false, true),
        ("Loktofeit", SpellClass.Cleric, 6, "Teleports the party back to town; all items and most gold are forfeited.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Lorto", SpellClass.Cleric, 6, "Ripping blades deal 6d6 damage to each member of an enemy group.", SpellCastContext.Combat, WizardryEffectKind.Damage, 6, 6, true, false),
        ("Mabadi", SpellClass.Cleric, 6, "Reduces a single target to 1d8 HP.", SpellCastContext.Combat, WizardryEffectKind.ReduceToD8, 0, 0, false, false),
        ("Madi", SpellClass.Cleric, 6, "Fully restores a character's HP and removes negative statuses.", SpellCastContext.Both, WizardryEffectKind.FullHeal, 0, 0, false, false),

        ("Mahaman", SpellClass.MagicUser, 7, "Greater divine intervention; the caster loses a level and must relearn it.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Malor", SpellClass.MagicUser, 7, "Teleports the party to chosen coordinates and depth.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Tiltowait", SpellClass.MagicUser, 7, "Deals 10d10 damage to all enemies.", SpellCastContext.Combat, WizardryEffectKind.Damage, 10, 10, true, false),
        ("Kadorto", SpellClass.Cleric, 7, "Revives a fallen comrade with full HP, even from ashes; failure loses them forever.", SpellCastContext.Both, WizardryEffectKind.None, 0, 0, false, false),
        ("Malikto", SpellClass.Cleric, 7, "Deals 12d6 damage to all enemies.", SpellCastContext.Combat, WizardryEffectKind.Damage, 12, 6, true, false),
    };

    private static readonly Lazy<List<Spell>> AllLazy = new(BuildSpells);

    public static IReadOnlyList<Spell> All => AllLazy.Value;

    public static bool IsWizardrySpellId(string? spellId)
        => spellId != null && spellId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ToId(string name) => IdPrefix + name.ToLowerInvariant();

    /// <summary>Mage spells are cast by Mage-school characters, Priest spells by Cleric-school characters.</summary>
    public static IEnumerable<Spell> ForSchool(SpellClass school, int maxCircle = 7)
        => All.Where(s => s.SpellClass == school && s.Level <= maxCircle);

    public static WizardrySpellEffect GetEffect(string spellId)
    {
        foreach (var row in Table)
        {
            if (string.Equals(ToId(row.Name), spellId, StringComparison.OrdinalIgnoreCase))
                return new WizardrySpellEffect(row.Kind, row.Dice, row.Sides, row.Group, row.UndeadOnly);
        }

        return new WizardrySpellEffect(WizardryEffectKind.None);
    }

    private static List<Spell> BuildSpells()
    {
        return Table.Select(row =>
        {
            var effect = new WizardrySpellEffect(row.Kind, row.Dice, row.Sides, row.Group, row.UndeadOnly);
            var isHeal = row.Kind is WizardryEffectKind.Heal or             WizardryEffectKind.FullHeal or WizardryEffectKind.CureParalysis or WizardryEffectKind.CurePoison;
                        return new Spell
                        {
                Id = ToId(row.Name),
                Name = row.Name,
                SpellClass = row.School,
                Level = row.Circle,
                Description = row.Description,
                EffectDescription = row.Description,
                RangeType = isHeal ? SpellRangeType.Ally : row.Kind is WizardryEffectKind.PartyArmorClass or WizardryEffectKind.ExpeditionArmorClass || (row.Kind == WizardryEffectKind.None && row.Context != SpellCastContext.Combat) ? SpellRangeType.Self : SpellRangeType.Enemy,
                Targeting = effect.Group ? SpellTargeting.Multiple : SpellTargeting.Single,
                TargetingScope = row.Name is "Tiltowait" or "Malikto" ? SpellTargetingScope.AllGroups
                    : effect.Group ? SpellTargetingScope.SingleGroup : SpellTargetingScope.SingleTarget,
                CastContext = row.Context,
                EffectType = isHeal ? SpellEffectType.Heal : SpellEffectType.Damage,
                Ruleset = Ruleset.Wizardry
            };
        }).ToList();
    }
}
