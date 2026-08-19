using Adnd.Core.Characters;

namespace Adnd.Core.Combat.Sessions;

public sealed class CombatSession
{
    public CombatSession(List<Character> party, List<MonsterInstance> monsters)
    {
        Party = party;
        Monsters = monsters;
    }

    public List<Character> Party { get; }
    public List<MonsterInstance> Monsters { get; }
    public int RoundNumber { get; set; } = 1;
    public CombatOutcome Outcome { get; set; } = CombatOutcome.InProgress;

    // Temporary round-combat effects only (not persisted).
    public HashSet<string> BlessedPartyMembers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> InvisiblyBuffedPartyMembers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AsleepPartyRounds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Level1PriestSpellCastsUsed { get; set; }

    public bool IsBlessed(string characterName) => BlessedPartyMembers.Contains(characterName);

    public void SetPartyAsleep(string characterName, int rounds)
    {
        if (rounds <= 0)
        {
            AsleepPartyRounds.Remove(characterName);
            return;
        }

        AsleepPartyRounds[characterName] = rounds;
    }

    public int GetPartyAsleepRounds(string characterName)
    {
        return AsleepPartyRounds.TryGetValue(characterName, out var rounds) ? Math.Max(0, rounds) : 0;
    }

    public int TickPartyAsleep(string characterName)
    {
        if (!AsleepPartyRounds.TryGetValue(characterName, out var rounds) || rounds <= 0)
            return 0;

        rounds -= 1;
        if (rounds <= 0)
        {
            AsleepPartyRounds.Remove(characterName);
            return 0;
        }

        AsleepPartyRounds[characterName] = rounds;
        return rounds;
    }

    public IEnumerable<Character> AliveParty => Party.Where(p => p.CurrentHitPoints > 0 && !p.HasStatus(CharacterStatus.Dead));
    public IEnumerable<MonsterInstance> AliveMonsters => Monsters.Where(m => m.IsAlive);

    // Helper methods for group-based monster tracking
    public IEnumerable<string> GetDistinctGroupIds() => Monsters.Select(m => m.GroupId).Distinct();

    public IEnumerable<MonsterInstance> GetMonstersByGroup(string groupId) => Monsters.Where(m => m.GroupId == groupId);

    public IEnumerable<MonsterInstance> GetAliveMonstersByGroup(string groupId) => AliveMonsters.Where(m => m.GroupId == groupId);

    public int GetAliveCountByGroup(string groupId) => GetAliveMonstersByGroup(groupId).Count();

    /// <summary>
    /// Where the next spread attack should land. Lives on the session so a whole party asking to spread
    /// their blows takes one monster each in turn, rather than each attacker separately picking "the next
    /// one" and all landing on the same unlucky monster.
    /// </summary>
    public int SpreadCursor { get; set; }

    /// <summary>The monster matching "group#index", alive or not, or null if nothing answers to it.</summary>
    public MonsterInstance? FindMonster(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var cut = key.LastIndexOf('#');
        if (cut <= 0 || cut == key.Length - 1) return null;

        var groupId = key.Substring(0, cut);
        if (!int.TryParse(key.Substring(cut + 1), out var index)) return null;

        return Monsters.FirstOrDefault(m => m.Index == index && m.GroupId == groupId);
    }
}
