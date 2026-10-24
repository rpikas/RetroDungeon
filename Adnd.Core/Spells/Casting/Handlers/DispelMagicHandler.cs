using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class DispelMagicHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "dispel_magic", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var session = request.CombatSession;
        if (request.Context != SpellUseContext.Combat || session == null)
            return SpellCastResult.Failure("Dispel Magic can only be cast in combat.");

        var rng = request.Rng ?? Random.Shared;
        var caster = request.Caster;
        var casterLevel = request.EffectiveCasterLevel
            ?? (caster.Classes.Contains(CharacterClass.Cleric) ? caster.GetClassLevel(CharacterClass.Cleric) : caster.Level);
        casterLevel = Math.Max(1, casterLevel);

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{caster.Name} casts {spell.Name}!");

        var groupId = request.Targets.FirstOrDefault()?.TargetGroupId;
        var monsters = !string.IsNullOrWhiteSpace(groupId)
            ? session.GetAliveMonstersByGroup(groupId!).ToList()
            : request.MonsterTargets.Where(m => m.IsAlive).ToList();

        var dispelled = 0;

        foreach (var monster in monsters)
        {
            var hasMagic = monster.HasStatus(MonsterStatus.Invisible)
                || monster.HasStatus(MonsterStatus.PermanentIllusion)
                || session.GetMonsterMirrorImageCount(monster) > 0
                || session.MonsterBarkskinRounds.ContainsKey(MonsterKeyOf(monster));

            if (!hasMagic)
                continue;

            if (!Roll(casterLevel, monster.InstanceHitDice, rng, out var chance, out var roll))
            {
                result.Events.Add($"{monster.DisplayName}'s magic resists the dispel ({roll} vs {chance}%).");
                continue;
            }

            monster.SetStatus(MonsterStatus.Invisible, 0);
            monster.SetStatus(MonsterStatus.PermanentIllusion, 0);
            session.SetMonsterMirrorImage(monster, 0, 0);
            session.ClearMonsterBarkskin(monster);
            dispelled++;
            result.Events.Add($"{monster.DisplayName}'s magical protections are dispelled!");
        }

        foreach (var member in session.Party)
        {
            var hadEffect = false;
            var asleepRounds = session.GetPartyAsleepRounds(member.Name);

            if (member.HasStatus(CharacterStatus.Charmed)) { hadEffect = true; }
            else if (member.HasStatus(CharacterStatus.Confused) || session.GetPartyConfusedRounds(member.Name) > 0) { hadEffect = true; }
            else if (member.HasStatus(CharacterStatus.Slowed) || session.IsPartySlowed(member.Name)) { hadEffect = true; }
            else if (member.HasStatus(CharacterStatus.Webbed) || session.GetPartyWebbedRounds(member.Name) > 0) { hadEffect = true; }
            else if (asleepRounds > 0) { hadEffect = true; }

            if (!hadEffect)
                continue;

            if (!Roll(casterLevel, Math.Max(1, member.Level), rng, out var chance, out var roll))
            {
                result.Events.Add($"The magic afflicting {member.Name} resists the dispel ({roll} vs {chance}%).");
                continue;
            }

            member.RemoveStatus(CharacterStatus.Charmed);
            member.RemoveStatus(CharacterStatus.Confused);
            member.RemoveStatus(CharacterStatus.Webbed);
            member.RemoveStatus(CharacterStatus.Slowed);
            if (asleepRounds > 0)
            {
                member.RemoveStatus(CharacterStatus.Asleep);
                session.SetPartyAsleep(member.Name, 0);
            }

            session.SetPartyConfused(member.Name, 0);
            session.SetPartyWebbed(member.Name, 0);

            var originalMove = session.GetPartySlowOriginalMove(member.Name);
            if (originalMove > 0)
                member.Move = originalMove;
            session.ClearPartySlow(member.Name);

            dispelled++;
            result.Events.Add($"The harmful magic on {member.Name} is dispelled!");
        }

        if (dispelled == 0)
            result.Events.Add("There was no magic to dispel (or all of it held fast).");

        return result;
    }

    // base 50%, +5% per level above, -2% per level below
    private static bool Roll(int casterLevel, int targetLevel, Random rng, out int chance, out int roll)
    {
        var diff = casterLevel - targetLevel;
        chance = Math.Clamp(50 + (diff >= 0 ? diff * 5 : diff * 2), 0, 100);
        roll = rng.Next(1, 101);
        return roll <= chance;
    }

    private static string MonsterKeyOf(MonsterInstance monster) => $"{monster.GroupId}#{monster.Index}";
}
