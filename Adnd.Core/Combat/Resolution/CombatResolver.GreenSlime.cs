using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private static bool IsGreenSlime(MonsterInstance monster) => IsTemplate(monster, "Green Slime");

    // Green slime has no attacks: it drops onto a victim, then turns the victim into slime in 1-4 rounds.
    private bool TryResolveGreenSlime(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!IsGreenSlime(monster))
            return false;

        var attached = session.GreenSlimeAttachments.FirstOrDefault(a => ReferenceEquals(a.Value.Monster, monster));
        if (attached.Value != null)
        {
            var victim = session.Party.FirstOrDefault(c => string.Equals(c.Name, attached.Key, StringComparison.OrdinalIgnoreCase));
            if (victim == null || !IsAlive(victim))
            {
                session.GreenSlimeAttachments.Remove(attached.Key);
                return true;
            }

            attached.Value.RoundsLeft--;
            if (attached.Value.RoundsLeft <= 0)
            {
                session.GreenSlimeAttachments.Remove(attached.Key);
                KillCharacter(victim);
                victim.AddStatus(CharacterStatus.Lost);
                events.Add(new CombatEvent($"{victim.Name} is turned completely into green slime! (No resurrection possible.)"));
            }
            else
            {
                events.Add(new CombatEvent($"The green slime keeps spreading over {victim.Name} ({attached.Value.RoundsLeft} round(s) until fully converted). Fire, cold or Cure Disease will stop it."));
            }

            return true;
        }

        if (_dice.Roll(6) > 3)
            return true;

        var candidates = session.ExposedParty
            .Where(c => IsAlive(c) && !session.GreenSlimeAttachments.ContainsKey(c.Name))
            .ToList();
        if (candidates.Count == 0)
            return true;

        var target = candidates[_dice.Roll(candidates.Count) - 1];
        var rounds = _dice.Roll(4);
        session.GreenSlimeAttachments[target.Name] = new GreenSlimeAttachment { Monster = monster, RoundsLeft = rounds };
        events.Add(new CombatEvent($"{monster.DisplayName} drops onto {target.Name} and attaches to the flesh! In {rounds} round(s) {target.Name} will become green slime unless it is burned, frozen or cured."));
        return true;
    }
}
