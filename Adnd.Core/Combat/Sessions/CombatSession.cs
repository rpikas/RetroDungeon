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
    public Dictionary<string, int> ImprovedInvisibilityRounds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> StrengthBuffRounds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> StrengthBuffBonuses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> MirrorImageRounds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> MirrorImageCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
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

    public void SetImprovedInvisibility(string characterName, int rounds)
    {
        if (rounds <= 0)
        {
            ImprovedInvisibilityRounds.Remove(characterName);
            return;
        }

        ImprovedInvisibilityRounds[characterName] = rounds;
    }

    public int GetImprovedInvisibilityRounds(string characterName)
    {
        return ImprovedInvisibilityRounds.TryGetValue(characterName, out var rounds) ? Math.Max(0, rounds) : 0;
    }

    public int TickImprovedInvisibility(string characterName)
    {
        if (!ImprovedInvisibilityRounds.TryGetValue(characterName, out var rounds) || rounds <= 0)
            return 0;

        rounds -= 1;
        if (rounds <= 0)
        {
            ImprovedInvisibilityRounds.Remove(characterName);
            return 0;
        }

        ImprovedInvisibilityRounds[characterName] = rounds;
        return rounds;
    }

    public void SetStrengthBuff(string characterName, int bonus, int rounds)
    {
        if (rounds <= 0 || bonus <= 0)
        {
            StrengthBuffRounds.Remove(characterName);
            StrengthBuffBonuses.Remove(characterName);
            return;
        }

        StrengthBuffBonuses[characterName] = bonus;
        StrengthBuffRounds[characterName] = rounds;
    }

    public int GetStrengthBuffRounds(string characterName)
    {
        return StrengthBuffRounds.TryGetValue(characterName, out var rounds) ? Math.Max(0, rounds) : 0;
    }

    public int GetStrengthBuffBonus(string characterName)
    {
        return StrengthBuffBonuses.TryGetValue(characterName, out var bonus) ? Math.Max(0, bonus) : 0;
    }

    public int TickStrengthBuff(string characterName)
    {
        if (!StrengthBuffRounds.TryGetValue(characterName, out var rounds) || rounds <= 0)
            return 0;

        rounds -= 1;
        if (rounds <= 0)
        {
            StrengthBuffRounds.Remove(characterName);
            return 0;
        }

        StrengthBuffRounds[characterName] = rounds;
        return rounds;
    }

    public void ClearStrengthBuff(string characterName)
    {
        StrengthBuffRounds.Remove(characterName);
        StrengthBuffBonuses.Remove(characterName);
    }

    public void SetMirrorImage(string characterName, int imageCount, int rounds)
    {
        if (imageCount <= 0 || rounds <= 0)
        {
            MirrorImageCounts.Remove(characterName);
            MirrorImageRounds.Remove(characterName);
            return;
        }

        MirrorImageCounts[characterName] = imageCount;
        MirrorImageRounds[characterName] = rounds;
    }

    public int GetMirrorImageCount(string characterName)
    {
        return MirrorImageCounts.TryGetValue(characterName, out var count) ? Math.Max(0, count) : 0;
    }

    public int GetMirrorImageRounds(string characterName)
    {
        return MirrorImageRounds.TryGetValue(characterName, out var rounds) ? Math.Max(0, rounds) : 0;
    }

    public int TickMirrorImage(string characterName)
    {
        if (!MirrorImageRounds.TryGetValue(characterName, out var rounds) || rounds <= 0)
            return 0;

        rounds -= 1;
        if (rounds <= 0)
        {
            MirrorImageRounds.Remove(characterName);
            MirrorImageCounts.Remove(characterName);
            return 0;
        }

        MirrorImageRounds[characterName] = rounds;
        return rounds;
    }

    public int RemoveOneMirrorImage(string characterName)
    {
        if (!MirrorImageCounts.TryGetValue(characterName, out var count) || count <= 0)
            return 0;

        count -= 1;
        if (count <= 0)
        {
            MirrorImageCounts.Remove(characterName);
            MirrorImageRounds.Remove(characterName);
            return 0;
        }

        MirrorImageCounts[characterName] = count;
        return count;
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
