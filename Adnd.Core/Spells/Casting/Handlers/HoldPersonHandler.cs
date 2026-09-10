using Adnd.Core.Combat.Sessions;
using Adnd.Core.Characters;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class HoldPersonHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId)
        => string.Equals(spellId, "hold_person", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spellId, "hold_person_magic_user", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        if (request.Context != SpellUseContext.Combat)
            return SpellCastResult.Failure("Hold Person can only be cast in combat.");

        var session = request.CombatSession;
        var rng = request.Rng ?? Random.Shared;

        string? targetGroupId = request.Targets
            .Select(t => t.TargetGroupId)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        var hasExplicitGroupTarget = !string.IsNullOrWhiteSpace(targetGroupId);

        var humanoidCandidates = session != null
            ? session.GetAliveMonstersByGroup(targetGroupId ?? "default")
                .Where(m => IsHumanoid(m.InstanceMonsterType))
                .ToList()
            : new List<MonsterInstance>();

        // If a specific group was selected, do not fall back to other groups.
        if (humanoidCandidates.Count == 0 && hasExplicitGroupTarget)
            return SpellCastResult.Failure("No valid humanoid targets in the selected group for Hold Person.");

        if (humanoidCandidates.Count == 0)
        {
            humanoidCandidates = request.MonsterTargets
                .Where(m => m.IsAlive && IsHumanoid(m.InstanceMonsterType))
                .ToList();
        }

        if (humanoidCandidates.Count == 0)
            return SpellCastResult.Failure("No valid humanoid targets for Hold Person.");

        var isMagicUserHoldPerson = string.Equals(spell.Id, "hold_person_magic_user", StringComparison.OrdinalIgnoreCase)
            || spell.SpellClass == SpellClass.MagicUser;

        var maxTargets = isMagicUserHoldPerson ? 4 : 3;
        var targets = humanoidCandidates
            .OrderBy(_ => rng.Next())
            .Take(maxTargets)
            .ToList();

        var casterLevel = ResolveCasterLevel(request, spell);
        var rounds = isMagicUserHoldPerson
            ? Math.Max(1, casterLevel * 2)
            : 4 + Math.Max(1, casterLevel);

        var savePenalty = humanoidCandidates.Count == 1
            ? (isMagicUserHoldPerson ? 3 : 2)
            : 0;

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}!");
        result.Events.Add($"Hold Person targets up to {maxTargets} humanoids in one group ({targets.Count} selected).");

        var heldCount = 0;

        foreach (var target in targets)
        {
            var baseSaveTarget = target.Template.SavingThrows?.Spell ?? 20;
            var saveTarget = Math.Min(20, baseSaveTarget + savePenalty);
            var saveRoll = rng.Next(1, 21);

            if (saveRoll >= saveTarget)
            {
                result.Events.Add($"{target.DisplayName} resists the spell (save {saveRoll} vs {saveTarget}).");
                continue;
            }

            if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, rng, spell.Name))
            {
                result.Events.Add($"{target.DisplayName} negates Hold Person with magic resistance.");
                continue;
            }

            if (SpellDamageSaveHelper.IsUndeadImmuneToSpell(target, spell.Id))
            {
                result.Events.Add($"{target.DisplayName} is undead and immune to {spell.Name}.");
                continue;
            }

            target.SetStatus(MonsterStatus.Paralyzed, rounds);
            heldCount++;
            result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and is paralyzed for {rounds} round(s)!");
        }

        result.Events.Add($"Hold Person held {heldCount} target(s).");

        return result;
    }

    private static bool IsHumanoid(MonsterType monsterType)
    {
     //   var humanoids = new[] { "human", "elf", "dwarf", "halfling", "gnome", "orc", "goblin", "hobgoblin", "kobold", "bugbear", "gnoll" };
       // var nameLower = monsterName.ToLowerInvariant();
       //return humanoids.Any(h => nameLower.Contains(h));
       return monsterType == MonsterType.Humanoid;
    }

    private static int ResolveCasterLevel(SpellCastRequest request, Spell spell)
    {
        var caster = request.Caster;
        var classLevel = spell.SpellClass switch
        {
            SpellClass.Cleric when caster.Classes.Contains(CharacterClass.Cleric) => caster.GetClassLevel(CharacterClass.Cleric),
            SpellClass.MagicUser when caster.Classes.Contains(CharacterClass.MagicUser) => caster.GetClassLevel(CharacterClass.MagicUser),
            _ => caster.Level
        };

        return Math.Max(1, classLevel);
    }


}
