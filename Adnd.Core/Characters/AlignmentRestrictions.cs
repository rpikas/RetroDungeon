using System.Collections.Generic;

namespace Adnd.Core.Characters;

public static class AlignmentRestrictions
{
    // Return allowed alignments for the chosen classes and race, applying AD&D 1e rules.
    public static List<Alignment> GetAllowedAlignments(List<CharacterClass> classes, Race race)
    {
        // Start with all alignments and intersect class-by-class.
        var allowed = new HashSet<Alignment>((Alignment[])System.Enum.GetValues(typeof(Alignment)));

        foreach (var cls in classes)
        {
            var classAllowed = GetClassAllowedAlignments(cls);
            allowed.IntersectWith(classAllowed);
        }

        // Race rule: Half-Orcs must be Chaotic, Neutral, or Lawful Evil per user rule.
        if (race == Race.HalfOrc)
        {
            var halfOrcAllowed = new[]
            {
                Alignment.ChaoticNeutral,
                Alignment.TrueNeutral,
                Alignment.LawfulEvil,
                Alignment.NeutralEvil,
                Alignment.ChaoticEvil
            };
            allowed.IntersectWith(halfOrcAllowed);
        }

        return allowed.ToList();
    }

    private static IEnumerable<Alignment> GetClassAllowedAlignments(CharacterClass cls)
    {
        var all = (Alignment[])System.Enum.GetValues(typeof(Alignment));

        return cls switch
        {
            // (A)* any except True Neutral (reserved for druid)
            CharacterClass.Cleric => all.Where(a => a != Alignment.TrueNeutral),

            // (N)
            CharacterClass.Druid => new[] { Alignment.TrueNeutral },

            // (LG)
            CharacterClass.Paladin => new[] { Alignment.LawfulGood },

            // (G)
            CharacterClass.Ranger => new[] { Alignment.LawfulGood, Alignment.NeutralGood, Alignment.ChaoticGood },

            // (N to E)
            CharacterClass.Thief => new[]
            {
                Alignment.LawfulNeutral,
                Alignment.TrueNeutral,
                Alignment.ChaoticNeutral,
                Alignment.LawfulEvil,
                Alignment.NeutralEvil,
                Alignment.ChaoticEvil
            },

            // (E)
            CharacterClass.Assassin => new[] { Alignment.LawfulEvil, Alignment.NeutralEvil, Alignment.ChaoticEvil },

            // (L)
            CharacterClass.Monk => new[] { Alignment.LawfulGood, Alignment.LawfulNeutral, Alignment.LawfulEvil },

            // Bard: Neutral Good, Lawful Neutral, True Neutral, Chaotic Neutral, Neutral Evil
            CharacterClass.Bard => new[]
            {
                Alignment.NeutralGood,
                Alignment.LawfulNeutral,
                Alignment.TrueNeutral,
                Alignment.ChaoticNeutral,
                Alignment.NeutralEvil
            },

            // (A)
            _ => all
        };
    }
}
