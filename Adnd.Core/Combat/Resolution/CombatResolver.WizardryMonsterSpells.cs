using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Spells;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private static readonly WizardryEffectKind[] MonsterUsableWizardryEffects =
    [
        WizardryEffectKind.Damage,
        WizardryEffectKind.Heal,
        WizardryEffectKind.Sleep,
        WizardryEffectKind.Kill
    ];

    /// <summary>Wizardry monsters cast Wizardry spells of their listed circle and lower; AD&D monsters never reach this.</summary>
    private bool TryResolveWizardryMonsterSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var pool = new List<Spell>();
        foreach (var (school, tradition) in new[] { (SpellClass.MagicUser, MonsterSpellTradition.MagicUser), (SpellClass.Cleric, MonsterSpellTradition.Cleric) })
        {
            var maxCircle = 0;
            for (var level = 1; level <= 7; level++)
            {
                if (HasSpellTierAbility(monster, level, tradition))
                    maxCircle = level;
            }

            if (maxCircle > 0)
                pool.AddRange(WizardrySpells.ForSchool(school, maxCircle));
        }

        var needsHealing = GetMonstersNeedingHealing(session).Count > 0;
        var castable = pool
            .Where(s => WizardrySpells.GetEffect(s.Id) is { Kind: var kind, UndeadOnly: false }
                        && MonsterUsableWizardryEffects.Contains(kind)
                        && (kind != WizardryEffectKind.Heal || needsHealing))
            .ToList();
        if (castable.Count == 0)
            return false;

        var spell = castable[_dice.Roll(castable.Count) - 1];
        var effect = WizardrySpells.GetEffect(spell.Id);

        if (effect.Kind == WizardryEffectKind.Heal)
        {
            var injured = GetMonstersNeedingHealing(session);
            var ally = injured[_dice.Roll(injured.Count) - 1];
            var before = ally.CurrentHitPoints;
            ally.CurrentHitPoints = Math.Min(ally.MaxHitPoints, ally.CurrentHitPoints + RollWizardryDice(effect));
            ApplySwordOfWoundingHealingRestriction(session, ally);
            events.Add(new CombatEvent($"{monster.DisplayName} casts {spell.Name} on {ally.DisplayName}, healing {ally.CurrentHitPoints - before} HP."));
            return true;
        }

        var targets = GetSpellAffectablePartyTargets(monster, session, events, spell.Name);
        if (targets.Count == 0)
            return true;

        if (!effect.Group)
            targets = new List<Character> { targets[_dice.Roll(targets.Count) - 1] };

        events.Add(new CombatEvent($"{monster.DisplayName} casts {spell.Name}!"));
        foreach (var target in targets)
            ApplyWizardrySpellToCharacter(spell, effect, target, session, events);

        return true;
    }

    private void ApplyWizardrySpellToCharacter(Spell spell, WizardrySpellEffect effect, Character target, CombatSession session, List<CombatEvent> events)
    {
        var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell);
        var saveRoll = _dice.Roll(20);
        var saved = saveRoll >= saveTarget;
        var saveText = $"(save {saveRoll} vs {saveTarget})";

        switch (effect.Kind)
        {
            case WizardryEffectKind.Damage:
            {
                var damage = RollWizardryDice(effect);
                if (saved)
                    damage /= 2;

                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
                var actual = before - target.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(target, actual, events);
                events.Add(new CombatEvent($"{target.Name} takes {actual} damage {saveText}{(saved ? ", half for a successful save" : string.Empty)}."));
                if (target.CurrentHitPoints <= 0)
                {
                    target.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{target.Name} is slain!"));
                }

                break;
            }
            case WizardryEffectKind.Sleep:
                if (saved)
                {
                    events.Add(new CombatEvent($"{target.Name} resists {spell.Name} {saveText}."));
                    break;
                }

                var rounds = _dice.Roll(3) + 1;
                target.AddStatus(CharacterStatus.Asleep);
                session.SetPartyAsleep(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} falls asleep for {rounds} round(s) {saveText}."));
                break;
            case WizardryEffectKind.Kill:
                if (saved)
                {
                    events.Add(new CombatEvent($"{target.Name} resists {spell.Name} {saveText}."));
                    break;
                }

                target.CurrentHitPoints = 0;
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by {spell.Name}! {saveText}"));
                break;
        }
    }

    private int RollWizardryDice(WizardrySpellEffect effect)
    {
        var total = 0;
        for (var i = 0; i < effect.Dice; i++)
            total += _dice.Roll(effect.Sides);
        return total;
    }
}
