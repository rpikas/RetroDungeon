using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class CharmPersonHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "charm_person_magic_user", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "charm_person_illusionist", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "charm_person", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Charm Person can only be cast in combat.");

        var rng = request.Rng ?? Random.Shared;
        var session = request.CombatSession;

        MonsterInstance? target = null;

        var firstTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (firstTarget?.MonsterIndex is int idx)
            target = request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);

        if (target == null && !string.IsNullOrWhiteSpace(firstTarget?.TargetGroupId) && session != null)
        {
            var groupTargets = session.GetAliveMonstersByGroup(firstTarget.TargetGroupId).ToList();
            if (groupTargets.Count > 0)
                target = groupTargets[rng.Next(groupTargets.Count)];
        }

        target ??= request.MonsterTargets.FirstOrDefault(m => m.IsAlive);

        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        if (target.Template.Type != MonsterType.Humanoid)
        {
            result.Events.Add($"{target.DisplayName} is not a humanoid. {spell.Name} has no effect.");
            return result;
        }

        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, spell.Id))
        {
            result.Events.Add($"{target.DisplayName} is immune to mind-affecting spells. {spell.Name} has no effect.");
            return result;
        }

        MonsterIntelligenceResolver.RegisterMonsterIntelligence(target.Template.Name, target.Template.Intelligence);
        var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(target.Template.Name);

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
        var saveRoll = rng.Next(1, 21);
        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} resists charm (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
        {
            result.Events.Add($"{target.DisplayName} negates charm with magic resistance.");
            return result;
        }

        target.SetStatus(MonsterStatus.Charmed, int.MaxValue);
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is charmed.");
        result.Events.Add($"Check interval by INT {intelligence}: {GetCharmCheckIntervalText(intelligence)} (day break = leaving the dungeon).");
        result.Events.Add($"{target.DisplayName} now regards {request.Caster.Name} as a trusted friend and will aid the caster's side in combat.");
        return result;
    }

    private static string GetCharmCheckIntervalText(int intelligence)
    {
        if (intelligence <= 3) return "3 months";
        if (intelligence <= 6) return "2 months";
        if (intelligence <= 9) return "1 month";
        if (intelligence <= 12) return "3 weeks";
        if (intelligence <= 14) return "2 weeks";
        if (intelligence <= 16) return "1 week";
        if (intelligence == 17) return "3 days";
        if (intelligence == 18) return "2 days";
        return "1 day";
    }
}