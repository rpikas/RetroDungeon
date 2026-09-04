// What the Church of Chant charges, and what happens when it tries.
//
// Lifted out of TempleMenu so the console and the tabletop share ONE implementation. This is the line the
// tavern's own notes draw and it matters most here: laying figures out on a table is housekeeping and may be
// duplicated freely, but raising the dead is a rule -- a system shock roll against Constitution, a point of
// Constitution burned on success, Dead becoming Ashes and Ashes becoming Lost on failure. Two implementations
// of that would eventually disagree, and the disagreement would be about whether somebody's character is gone
// forever. So the table decides WHO and WHO PAYS, and this decides what happens.

using Adnd.Core.Characters;
using Adnd.Data.Characters;

namespace Adnd.Game;

public static class Temple
{
    /// <summary>Per character who needs it, paid out of that character's own purse.</summary>
    public const int HealCost = 10;
    public const int CurePoisonCost = 100;
    public const int CureParalysisCost = 200;

    public const int RaiseDeadCost = 100;
    public const int RaiseFromAshesCost = 500;

    public static bool NeedsHealing(Character c) =>
        c.CurrentHitPoints < c.MaxHitPoints
        || c.HasStatus(CharacterStatus.Poisoned)
        || c.HasStatus(CharacterStatus.Paralyzed)
        || c.HasStatus(CharacterStatus.Diseased);

    public static int CostToHeal(Character c)
    {
        var cost = 0;

        if (c.CurrentHitPoints < c.MaxHitPoints)
            cost += HealCost;

        if (c.HasStatus(CharacterStatus.Poisoned))
            cost += CurePoisonCost;

        if (c.HasStatus(CharacterStatus.Paralyzed))
            cost += CureParalysisCost;

        if (c.HasStatus(CharacterStatus.Diseased))
            cost += CurePoisonCost;

        return cost;
    }

    /// <summary>
    /// Whether the temple will attempt a raise. Ashes still count -- expensively, and at the risk of Lost --
    /// while Lost is beyond help, which is the whole point of Lost.
    /// </summary>
    public static bool CanBeRaised(Character c) =>
        (c.HasStatus(CharacterStatus.Dead) || c.HasStatus(CharacterStatus.Ashes) || c.CurrentHitPoints <= 0)
        && !c.HasStatus(CharacterStatus.Lost);

    public static int CostToRaise(Character c) =>
        c.HasStatus(CharacterStatus.Ashes) ? RaiseFromAshesCost : RaiseDeadCost;

    /// <summary>
    /// Heals one character out of their own gold. Returns false when they cannot pay, so the caller can say so
    /// rather than silently doing nothing.
    /// </summary>
    public static bool Heal(Character c, CharacterRepository repo)
    {
        var cost = CostToHeal(c);
        if (cost <= 0 || c.GoldPieces < cost)
            return false;

        c.GoldPieces -= cost;
        c.CurrentHitPoints = c.MaxHitPoints;
        c.RemoveStatus(CharacterStatus.Poisoned);
        c.RemoveStatus(CharacterStatus.Paralyzed);
        c.CureDiseaseAndRestoreConstitution();
        repo.Save(c);
        return true;
    }

    /// <summary>
    /// Attempts a raise, taking the fee from <paramref name="payer"/>, and returns what happened line by line
    /// so either surface can show it.
    ///
    /// The fee is charged whether or not it works, because that is what the console has always done and it is
    /// the harsher, more Wizardry answer: the temple prays either way.
    /// </summary>
    public static List<string> Raise(Character target, Character payer, CharacterRepository repo)
    {
        var events = new List<string>();
        var cost = CostToRaise(target);

        if (payer.GoldPieces < cost)
        {
            events.Add($"{payer.Name} does not have enough gold.");
            return events;
        }

        var fromAshes = target.HasStatus(CharacterStatus.Ashes);
        payer.GoldPieces -= cost;

        // Constitution 0 means there is nothing left to shock. Lost immediately, and the fee still paid.
        if (target.Abilities.Constitution <= 0)
        {
            target.RemoveStatus(CharacterStatus.Dead);
            target.RemoveStatus(CharacterStatus.Ashes);
            target.AddStatus(CharacterStatus.Lost);
            target.CurrentHitPoints = 0;

            events.Add($"{target.Name} has Constitution 0 and is automatically Lost.");
            events.Add($"{payer.Name} paid {cost} gp.");
            Save(target, payer, repo);
            return events;
        }

        var chance = SystemShockSurvivalChance(target.Abilities.Constitution);
        var roll = Random.Shared.Next(1, 101);
        events.Add($"System Shock roll for {target.Name}: {roll} (needs {chance} or less)");

        if (roll <= chance)
        {
            target.RemoveStatus(CharacterStatus.Dead);
            target.RemoveStatus(CharacterStatus.Ashes);
            target.RemoveStatus(CharacterStatus.Lost);

            if (target.CurrentHitPoints <= 0)
                target.CurrentHitPoints = 1;

            target.Abilities.Constitution = Math.Max(0, target.Abilities.Constitution - 1);

            events.Add($"{target.Name} has been raised.");
            events.Add($"{target.Name} loses 1 Constitution (now {target.Abilities.Constitution}).");
        }
        else if (fromAshes)
        {
            target.RemoveStatus(CharacterStatus.Dead);
            target.RemoveStatus(CharacterStatus.Ashes);
            target.AddStatus(CharacterStatus.Lost);
            target.CurrentHitPoints = 0;

            events.Add($"Revival failed. {target.Name} is now Lost and can never be revived again.");
        }
        else
        {
            target.RemoveStatus(CharacterStatus.Dead);
            target.AddStatus(CharacterStatus.Ashes);
            target.CurrentHitPoints = 0;

            events.Add($"Raise Dead failed. {target.Name} is now ashes.");
        }

        events.Add($"{payer.Name} paid {cost} gp.");
        Save(target, payer, repo);
        return events;
    }

    /// <summary>Saves both, and only once when the payer is paying for their own resurrection.</summary>
    private static void Save(Character target, Character payer, CharacterRepository repo)
    {
        repo.Save(target);
        if (!string.Equals(target.Name, payer.Name, StringComparison.OrdinalIgnoreCase))
            repo.Save(payer);
    }

    /// <summary>Not the 1e table; the game's own curve, kept exactly as it was when this moved here.</summary>
    public static int SystemShockSurvivalChance(int constitution)
    {
        return constitution switch
        {
            <= 1 => 30,
            <= 3 => 35,
            <= 5 => 40,
            <= 7 => 45,
            <= 9 => 50,
            <= 11 => 55,
            <= 13 => 60,
            <= 15 => 65,
            16 => 70,
            17 => 75,
            18 => 80,
            19 => 85,
            20 => 90,
            21 => 95,
            22 => 97,
            23 => 98,
            24 => 99,
            _ => 100
        };
    }
}
