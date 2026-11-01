using Adnd.Core.Combat.Resolution;

namespace Adnd.Core.Characters;

/// <summary>
/// Level 7 cleric Restoration, shared by the spell and the Church of Chant.
/// Cures feeblemind and gives back one drained level, provided it is cast within 1 day per cleric level of the drain.
/// Caster and recipient both age two years.
/// </summary>
public static class RestorationRules
{
    public const int BaseCost = 10000;
    public const int CostPerRecipientLevel = 10000;
    public const int AgingYears = 2;

    /// <summary>Clerics in a temple are assumed to be this level.</summary>
    public const int TempleClericLevel = 15;

    public static int CostFor(Character recipient) => BaseCost + CostPerRecipientLevel * Math.Max(1, recipient.Level);

    public static bool HasDrainedLevelWithinWindow(Character recipient, int clericLevel) =>
        recipient.LevelDrainHistory.Count > 0 && recipient.DaysSinceLevelDrain <= Math.Max(1, clericLevel);

    public static bool CanBenefit(Character recipient, int clericLevel) =>
        recipient.HasStatus(CharacterStatus.Feeblemind) || HasDrainedLevelWithinWindow(recipient, clericLevel);

    /// <summary>Applies the spell. <paramref name="caster"/> is null for a temple cleric, who is not tracked.</summary>
    public static List<string> Apply(Character recipient, Character? caster, int clericLevel)
    {
        var events = new List<string>();
        clericLevel = Math.Max(1, clericLevel);

        if (!CanBenefit(recipient, clericLevel))
        {
            events.Add(recipient.LevelDrainHistory.Count > 0
                ? $"Restoration comes too late for {recipient.Name}: the level was drained {recipient.DaysSinceLevelDrain} day(s) ago (limit {clericLevel} day(s) at cleric level {clericLevel})."
                : $"{recipient.Name} has nothing for Restoration to repair.");
            return events;
        }

        if (recipient.HasStatus(CharacterStatus.Feeblemind))
        {
            recipient.RemoveStatus(CharacterStatus.Feeblemind);
            events.Add($"{recipient.Name} is cured of feeblemind.");
        }

        if (HasDrainedLevelWithinWindow(recipient, clericLevel)
            && CombatResolver.TryRestoreOneDrainedLevel(recipient, out var from, out var to))
        {
            events.Add($"{recipient.Name} regains a lost level ({from}->{to}).");
        }
        else if (recipient.LevelDrainHistory.Count > 0)
        {
            events.Add($"Restoration is too late to return {recipient.Name}'s drained level ({recipient.DaysSinceLevelDrain} day(s) ago, limit {clericLevel}).");
        }

        recipient.AdvanceAgeByDays(AgingYears * 365);
        events.Add($"{recipient.Name} ages {AgingYears} years.");
        if (caster != null && !ReferenceEquals(caster, recipient))
        {
            caster.AdvanceAgeByDays(AgingYears * 365);
            events.Add($"{caster.Name} ages {AgingYears} years.");
        }

        return events;
    }
}
