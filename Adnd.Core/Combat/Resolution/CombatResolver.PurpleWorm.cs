using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private static bool IsPurpleWorm(MonsterInstance monster) =>
        string.Equals(monster.Template.Name, "Purple Worm", StringComparison.OrdinalIgnoreCase);

    private static void LogPurpleWormRule(List<CombatEvent> events, string message)
    {
        events.Add(new CombatEvent(message));
        RuleApplicationInfo.PublishLinked("MM", "PurpleWorm", message);
    }

    private static void ResolveSwallowedCharacters(CombatSession session, List<CombatEvent> events, bool advanceTime)
    {
        foreach (var swallowed in session.SwallowedCharacters.Values.ToList())
        {
            var victim = swallowed.Victim;
            if (swallowed.Worm.HasFled)
            {
                victim.CurrentHitPoints = 0;
                victim.AddStatus(CharacterStatus.Dead | CharacterStatus.Lost);
                victim.RemoveStatus(CharacterStatus.Swallowed);
                session.SwallowedCharacters.Remove(victim.Name);
                LogPurpleWormRule(events, $"{swallowed.Worm.DisplayName} fled with {victim.Name} still inside; {victim.Name} is permanently Lost and cannot be recovered or raised.");
                continue;
            }
            if (!swallowed.Worm.IsAlive)
            {
                victim.RemoveStatus(CharacterStatus.Swallowed);
                session.SwallowedCharacters.Remove(victim.Name);
                LogPurpleWormRule(events, $"{swallowed.Worm.DisplayName} is dead. {victim.Name} is freed from its stomach{(IsAlive(victim) ? " alive" : "; the body can still be recovered")}.");
                continue;
            }

            if (!advanceTime)
                continue;

            var elapsed = swallowed.RoundsInside(session.RoundNumber);
            if (elapsed >= 120)
            {
                victim.CurrentHitPoints = 0;
                victim.AddStatus(CharacterStatus.Dead | CharacterStatus.Lost);
                victim.RemoveStatus(CharacterStatus.Swallowed);
                session.SwallowedCharacters.Remove(victim.Name);
                LogPurpleWormRule(events, $"{victim.Name} has been digested by {swallowed.Worm.DisplayName} after 12 turns (120 melee rounds) and is Lost; the body cannot be raised.");
            }
            else if (elapsed >= 6 && IsAlive(victim))
            {
                victim.CurrentHitPoints = 0;
                victim.AddStatus(CharacterStatus.Dead);
                LogPurpleWormRule(events, $"{victim.Name} dies inside {swallowed.Worm.DisplayName} after six full melee rounds. The body remains recoverable until digestion at 12 turns.");
            }
            else if (IsAlive(victim) && elapsed > 0)
            {
                LogPurpleWormRule(events, $"{victim.Name} is inside {swallowed.Worm.DisplayName}: {6 - elapsed} round(s) left before death; cutting attacks face AC 9 and lose {elapsed} damage per attack this round.");
            }
        }
    }

    private static IEnumerable<Character> GetPurpleWormRearAttackers(CombatSession session, MonsterInstance worm) =>
        session.RearAttackers.TryGetValue(worm, out var attackers)
            ? attackers.Where(c => IsAlive(c) && !session.IsSwallowed(c.Name))
            : Enumerable.Empty<Character>();

    private static bool CanPurpleWormSting(CombatSession session, MonsterInstance worm) =>
        GetPurpleWormRearAttackers(session, worm).Any()
        || (session.IsSpaciousArea && session.ExposedParty.Count() >= 4);

    private void ApplyPurpleWormHit(CombatSession session, MonsterInstance worm, Character victim,
        MonsterAttack attack, int roll, int needed, List<CombatEvent> events)
    {
        if (!IsPurpleWorm(worm) || !IsAlive(victim))
            return;

        if (string.Equals(attack.Name, "Bite", StringComparison.OrdinalIgnoreCase)
            && (roll == 20 || roll >= needed + 4)
            && !session.IsSwallowed(victim.Name))
        {
            session.SwallowedCharacters[victim.Name] = new SwallowedCharacter(victim, worm, session.RoundNumber);
            victim.AddStatus(CharacterStatus.Swallowed);
            LogPurpleWormRule(events, $"{worm.DisplayName} swallows {victim.Name} whole! Bite roll {roll} vs {needed}: {(roll == 20 ? "natural 20" : "at least 4 over the required roll")}. Kill this worm within the next six melee rounds to rescue {victim.Name}. Cutting attacks from inside use AC 9, with cumulative -1 damage per round inside.");
            return;
        }

        if (!string.Equals(attack.Name, "Sting", StringComparison.OrdinalIgnoreCase))
            return;

        if (victim.IsMonkImmuneToPoison())
        {
            LogPurpleWormRule(events, $"{victim.Name} is immune to {worm.DisplayName}'s sting poison.");
            return;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(victim,
            _savingThrowService.GetSaveTarget(victim, SaveThrowType.ParalyzationPoisonDeath));
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var saved = saveRoll >= saveTarget;
        var message = $"{victim.Name} saves vs. {worm.DisplayName}'s lethal sting poison: 1d20={saveRoll} vs {saveTarget} => {(saved ? "survives" : "failed; killed by poison")}.";
        events.Add(new CombatEvent(message));
        RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", message);
        if (!saved)
        {
            victim.CurrentHitPoints = 0;
            victim.AddStatus(CharacterStatus.Dead);
        }
    }
}
