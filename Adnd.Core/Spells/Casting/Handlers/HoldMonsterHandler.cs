using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HoldMonsterHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "hold_monster", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Hold Monster can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        var candidates = new List<MonsterInstance>();
        var firstTarget = request.Targets.FirstOrDefault();
        if (firstTarget?.TargetGroupId != null && session != null)
            candidates = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();

        if (candidates.Count == 0)
            candidates = request.MonsterTargets.Where(m => m.IsAlive).ToList();

        if (candidates.Count == 0)
            return SpellCastResult.Failure("No valid targets for Hold Monster.");

        // 1-4 creatures; fewer targets means a harder save
        var targets = candidates.OrderBy(_ => rng.Next()).Take(4).ToList();
        var saveMod = targets.Count switch { 1 => -3, 2 => -1, _ => 0 };

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name} on {targets.Count} creature(s)! (saves {(saveMod == 0 ? "normal" : saveMod.ToString())})");

        foreach (var target in targets)
        {
            if (SpellDamageSaveHelper.IsUndeadImmuneToSpell(target, spell.Id))
            {
                result.Events.Add($"{target.DisplayName} is undead and immune to {spell.Name}.");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, spell.Id))
            {
                result.Events.Add($"{target.DisplayName} is immune to mind-affecting spells. {spell.Name} has no effect.");
                continue;
            }

            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
            var saveRoll = rng.Next(1, 21);

            if (saveRoll + saveMod >= saveTarget)
            {
                result.Events.Add($"{target.DisplayName} resists the spell (save {saveRoll}{(saveMod != 0 ? saveMod.ToString() : "")} vs {saveTarget}).");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
            {
                result.Events.Add($"{target.DisplayName} negates Hold Monster with magic resistance.");
                continue;
            }

            var rounds = rng.Next(2, 7); // 2-6 rounds
            if (saveRoll >= saveTarget)
            {
                // saved on the raw die but failed due to the penalty: partial effect = slow
                target.SetStatus(MonsterStatus.Slowed, rounds);
                result.Events.Add($"{target.DisplayName} partially resists ({saveRoll}{saveMod} vs {saveTarget}) and is slowed for {rounds} round(s)!");
            }
            else
            {
                target.SetStatus(MonsterStatus.Paralyzed, rounds);
                result.Events.Add($"{target.DisplayName} fails save ({saveRoll}{(saveMod != 0 ? saveMod.ToString() : "")} vs {saveTarget}) and is paralyzed for {rounds} round(s)!");
            }
        }

        return result;
    }
    private static bool IsUndead(MonsterType monsterType)
    {
       // var humanoids = new[] { MonsterType.Human, MonsterType.Elf, MonsterType.Dwarf, MonsterType.Halfling, MonsterType.Gnome, MonsterType.Orc, MonsterType.Goblin, MonsterType.Hobgoblin, MonsterType.Kobold, MonsterType.Bugbear, MonsterType.Gnoll };
      //  return humanoids.Contains(monsterName);
      return monsterType == MonsterType.Undead;
    }
    
}
