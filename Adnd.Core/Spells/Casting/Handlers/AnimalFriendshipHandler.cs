using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class AnimalFriendshipHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "animal_friendship", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Animal Friendship can only be cast in combat.");

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
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}.");

        if (target.Template.Type != MonsterType.Animal)
        {
            result.Events.Add($"{target.DisplayName} is not an animal. {spell.Name} has no effect.");
            return result;
        }

        MonsterIntelligenceResolver.RegisterMonsterIntelligence(target.Template.Name, target.Template.Intelligence);
        var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(target.Template.Name);
        if (intelligence > 4)
        {
            result.Events.Add($"{target.DisplayName} is too intelligent (INT {intelligence}) to be befriended.");
            return result;
        }

        var casterLevel = request.EffectiveCasterLevel
            ?? (request.Caster.Classes.Contains(CharacterClass.Druid) ? request.Caster.GetClassLevel(CharacterClass.Druid) : request.Caster.Level);
        casterLevel = Math.Max(1, casterLevel);

        if (target.InstanceHitDice > casterLevel * 2)
        {
            result.Events.Add($"{target.DisplayName} is too powerful ({target.InstanceHitDice} HD; limit {casterLevel * 2} HD).");
            return result;
        }

        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, spell.Id))
        {
            result.Events.Add($"{target.DisplayName} is immune to mind-affecting spells. {spell.Name} has no effect.");
            return result;
        }

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
        var saveRoll = rng.Next(1, 21);
        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} resists (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
        {
            result.Events.Add($"{target.DisplayName} negates the spell with magic resistance.");
            return result;
        }

        target.SetStatus(MonsterStatus.Charmed, int.MaxValue);
        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and becomes {request.Caster.Name}'s friend, aiding the party in combat.");
        return result;
    }
}
