using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

internal static class SpellDamageSaveHelper
{
    internal readonly record struct Outcome(int SaveTarget, int SaveRoll, bool Saved, int AppliedDamage, int BeforeHp, int AfterHp, int ActualDamage);

    internal static Outcome ApplyToMonster(MonsterInstance monster, int rolledDamage, Random rng, string spellName)
    {
        var saveTarget = monster.Template.SavingThrows?.Spell ?? 0;
        var saveRoll = rng.Next(1, 21);
        var saved = saveTarget > 0 && saveRoll >= saveTarget;
        var appliedDamage = saved ? rolledDamage / 2 : rolledDamage;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - appliedDamage);
        var actual = Math.Max(0, before - monster.CurrentHitPoints);

        if (actual > 0)
        {
            if (monster.HasStatus(MonsterStatus.Asleep))
                monster.SetStatus(MonsterStatus.Asleep, 0);

            if (monster.HasStatus(MonsterStatus.Unconscious))
                monster.SetStatus(MonsterStatus.Unconscious, 0);
        }

        RuleApplicationInfo.Publish(
            "DMG",
            "Saving Throws",
            $"{monster.DisplayName} saving throw vs spell ({spellName})",
            $"Roll d20, need {saveTarget}+ to save. Success means half damage.",
            "1",
            "20",
            saveRoll.ToString(),
            saved
                ? $"Save made. Damage halved: {rolledDamage} -> {appliedDamage}."
                : $"Save failed. Full damage: {appliedDamage}.");

        return new Outcome(saveTarget, saveRoll, saved, appliedDamage, before, monster.CurrentHitPoints, actual);
    }
}
