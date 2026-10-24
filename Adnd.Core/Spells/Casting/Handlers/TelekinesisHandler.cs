using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class TelekinesisHandler : ISpellEffectHandler
{
    private static readonly string[] WeaponKeywords =
    {
        "weapon", "sword", "spear", "club", "mace", "axe", "dagger", "pike", "lance", "flail",
        "halberd", "glaive", "staff", "hammer", "scimitar", "trident", "javelin", "morning star", "rapier"
    };

    public bool CanHandle(string spellId) =>
        string.Equals(spellId, "telekinesis_magic_user", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Telekinesis requires combat session context.");

        var groupId = request.Targets.FirstOrDefault()?.TargetGroupId;
        var monsters = string.IsNullOrWhiteSpace(groupId) || string.Equals(groupId, "default", StringComparison.OrdinalIgnoreCase)
            ? session.Monsters.Where(m => m.IsAlive).ToList()
            : session.GetAliveMonstersByGroup(groupId).ToList();

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");

        var armed = monsters
            .Where(m => !m.HasStatus(MonsterStatus.Disarmed) && m.Template.Attacks.Any(IsWeaponAttack))
            .ToList();
        if (armed.Count == 0)
        {
            result.Events.Add("No weapon-wielding monster can be disarmed.");
            return result;
        }

        var rng = request.Rng ?? Random.Shared;
        var victim = armed[rng.Next(armed.Count)];
        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(victim, rng, spell.Name))
        {
            result.Events.Add($"{victim.DisplayName} negates telekinesis with magic resistance.");
            return result;
        }

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(victim, 0);
        var saveRoll = rng.Next(1, 21);
        var saved = saveTarget > 0 && saveRoll >= saveTarget;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Telekinesis",
            $"{victim.DisplayName} saving throw vs spell",
            "Telekinesis wrenches a weapon from one monster; save vs. spell negates. A disarmed monster spends 2 rounds retrieving it.",
            "1",
            "20",
            saveRoll.ToString(),
            saved ? "Save made. Weapon kept." : "Save failed. Weapon torn away.");

        if (saved)
        {
            result.Events.Add($"{victim.DisplayName} keeps hold of its weapon (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        victim.SetStatus(MonsterStatus.Disarmed, 2);
        result.Events.Add($"{victim.DisplayName} is disarmed and spends 2 rounds running after its weapon!");
        return result;
    }

    private static bool IsWeaponAttack(Adnd.Core.Monsters.MonsterAttack attack)
    {
        var name = attack.Name ?? string.Empty;
        return WeaponKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
