using Adnd.Core.Characters;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

// Special abilities of Wizardry-source monsters: Run, Help, LvlDrain, Decapitate and "Breath Attack (X)".
public sealed partial class CombatResolver
{
    private const string WizardryHelpGroupPrefix = "help-";
    private const int WizardryDecapitateChancePercent = 5;

    private readonly HashSet<MonsterInstance> _wizardryHelpCalled = new(ReferenceEqualityComparer.Instance as IEqualityComparer<MonsterInstance>);

    // "May Run Away": a badly wounded monster (25% HP or less) has a 50% chance to flee each turn.
    private bool TryResolveWizardryRun(MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Run") || monster.MaxHitPoints <= 0)
            return false;

        if (monster.CurrentHitPoints * 4 > monster.MaxHitPoints)
            return false;

        var roll = _dice.Roll(100);
        if (roll > 50)
            return false;

        monster.HasFled = true;
        monster.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{monster.DisplayName} is badly hurt and runs away!"));
        return true;
    }

    // "Reinforcements": once per combat a monster has a 25% chance on its turn to call 1d4 of its own kind.
    // Reinforcements themselves cannot call for more help.
    private bool TryResolveWizardryHelp(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Help")
            || _wizardryHelpCalled.Contains(monster)
            || monster.GroupId.StartsWith(WizardryHelpGroupPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_dice.Roll(4) != 1)
            return false;

        _wizardryHelpCalled.Add(monster);
        var count = _dice.Roll(4);
        var groupId = $"{WizardryHelpGroupPrefix}{monster.GroupId}-{_dice.Roll(10000)}";
        for (var i = 1; i <= count; i++)
            session.Monsters.Add(new MonsterInstance(monster.Template, i, groupId));

        RuleApplicationInfo.Publish("Wizardry", "Reinforcements", $"{monster.DisplayName} calls for help",
            "A monster with Reinforcements has a 25% chance per turn (once per combat) to call 1d4 monsters of its own kind.",
            "1", "4", count.ToString(), $"{count} reinforcement(s) arrive.");
        events.Add(new CombatEvent($"{monster.DisplayName} calls for help! {count} {monster.Name}(s) join the fight."));
        return true;
    }

    private void TryApplyWizardryLevelDrain(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        var levels = GetWizardryLevelDrainAmount(monster);
        if (levels <= 0)
            return;

        var drained = 0;
        int oldLevel = target.Level, newLevel = target.Level;
        for (var i = 0; i < levels; i++)
        {
            if (!TryDrainOneLevel(target, out var from, out var to))
                break;

            if (drained == 0)
                oldLevel = from;
            newLevel = to;
            drained++;
        }

        events.Add(new CombatEvent(drained > 0
            ? $"{monster.DisplayName} drains {drained} level(s) from {target.Name} ({oldLevel}->{newLevel})!"
            : $"{target.Name} cannot lose further levels to {monster.DisplayName}."));
    }

    // "LvlDrain" description: "Drains N experience level(s)."
    private static int GetWizardryLevelDrainAmount(MonsterInstance monster)
    {
        foreach (var ability in monster.Template.SpecialAbilities)
        {
            if (!string.Equals(ability.Name?.Trim(), "LvlDrain", StringComparison.OrdinalIgnoreCase))
                continue;

            var m = Regex.Match(ability.Description ?? string.Empty, @"(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out var levels) ? Math.Max(1, levels) : 1;
        }

        return 0;
    }

    private void TryApplyWizardryDecapitate(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Decapitate") || !IsAlive(target))
            return;

        var roll = _dice.Roll(100);
        if (roll > WizardryDecapitateChancePercent)
            return;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        var saveRoll = _dice.Roll(20);
        var failed = saveRoll < saveTarget;

        RuleApplicationInfo.Publish("Wizardry", "Decapitate",
            $"{monster.DisplayName} attempts to decapitate {target.Name}",
            $"A hit has a {WizardryDecapitateChancePercent}% chance to be a decapitation attempt; failing a save vs Death kills instantly.",
            "1", "20", saveRoll.ToString(),
            $"Save target {saveTarget}. {(failed ? "Failed save: decapitated." : "Successful save.")}");

        if (!failed)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to decapitate {target.Name}, but {target.Name} ducks (save {saveRoll} vs {saveTarget})."));
            return;
        }

        var beforeHp = target.CurrentHitPoints;
        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{monster.DisplayName} decapitates {target.Name}! (save {saveRoll} vs {saveTarget}). HP {beforeHp}->0."));
    }

    // "Breath Attack (Fire|Cold|Poison|Petrify)": 33% chance per turn; deals half the monster's current HP to the whole
    // party (save vs Breath for half). Petrify breath turns characters to stone on a failed save vs Petrification.
    private bool TryResolveWizardryBreathAttack(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var element = GetWizardryBreathElement(monster);
        if (element == null || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var baseDamage = Math.Max(1, monster.CurrentHitPoints / 2);
        events.Add(new CombatEvent($"{monster.DisplayName} breathes {element.ToLowerInvariant()} ({baseDamage} damage)!"));

        foreach (var target in party)
        {
            if (element.Equals("Petrify", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWizardryPetrifyBreath(monster, target, events);
                continue;
            }

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var damage = baseDamage;
            if (element.Equals("Fire", StringComparison.OrdinalIgnoreCase))
            {
                var fireSaveBonus = GetFireResistanceSaveBonus(target);
                if (fireSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
                damage = ApplyFireProtectionDamageReduction(target, damage, 0, isMagicalFire: true, isNormalFire: false);
            }
            else if (element.Equals("Cold", StringComparison.OrdinalIgnoreCase))
            {
                damage = ApplyColdResistanceDamageReduction(target, damage, isMagicalCold: true, isNormalCold: false);
            }

            if (damage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s {element.ToLowerInvariant()} breath."));
                continue;
            }

            var saveRoll = _dice.Roll(20);
            var saved = saveRoll >= saveTarget;
            if (saved)
                damage /= 2;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, before - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            if (element.Equals("Fire", StringComparison.OrdinalIgnoreCase))
                TryBurnAwayWebsFromFireDamage(target, "fire breath", session, events);

            events.Add(new CombatEvent(saved
                ? $"{target.Name} succeeds breath save ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                : $"{target.Name} fails breath save ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private void ApplyWizardryPetrifyBreath(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (target.HasStatus(CharacterStatus.Petrified))
            return;

        var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.PetrificationPolymorph);
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists the petrifying breath (save {saveRoll} vs {saveTarget})."));
            return;
        }

        target.AddStatus(CharacterStatus.Petrified);
        events.Add(new CombatEvent($"{target.Name} is turned to stone by {monster.DisplayName}'s breath! (save {saveRoll} vs {saveTarget})"));
    }

    // "Breath Attack (Fire)" -> "Fire"
    private static string? GetWizardryBreathElement(MonsterInstance monster)
    {
        foreach (var name in GetMonsterAbilityNames(monster))
        {
            var m = Regex.Match(name, @"^Breath Attack\s*\((?<element>[^)]+)\)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Groups["element"].Value.Trim();
        }

        return null;
    }
}
