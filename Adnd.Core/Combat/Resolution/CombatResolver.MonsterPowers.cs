using Adnd.Core.Assassination;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Config;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

public sealed partial class CombatResolver
{
    private bool ResolveHpDamageBreath(MonsterInstance monster, int maxDamage, CombatSession session, List<CombatEvent> events)
    {
        if (maxDamage <= 0)
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var rolledDamage = _dice.Roll(maxDamage);
        events.Add(new CombatEvent($"{monster.DisplayName} uses {maxDamage} HP damage breath (rolled {rolledDamage})!"));

        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var fireSaveBonus = GetFireResistanceSaveBonus(target);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(target, rolledDamage, 0, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s breath."));
                continue;
            }

            var damage = saveRoll >= saveTarget ? preSaveDamage / 2 : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            TryBurnAwayWebsFromFireDamage(target, "fire breath", session, events);

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds breath save ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails breath save ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private bool ResolveDragonBreathCurrentHp(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var breathDamage = Math.Max(0, monster.CurrentHitPoints);
        if (breathDamage <= 0)
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        events.Add(new CombatEvent($"{monster.DisplayName} uses Dragon breath for {breathDamage} damage!"));

        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var dragonName = monster.Template.Name?.Trim() ?? string.Empty;
            var isBlueDragonLightningBreath = string.Equals(dragonName, "Blue Dragon", StringComparison.OrdinalIgnoreCase);
            var isWhiteDragonColdBreath = string.Equals(dragonName, "White Dragon", StringComparison.OrdinalIgnoreCase);
            var isSilverDragonColdBreath = string.Equals(dragonName, "Silver Dragon", StringComparison.OrdinalIgnoreCase);
            var isRedDragonFireBreath = string.Equals(dragonName, "Red Dragon", StringComparison.OrdinalIgnoreCase);
            var isGoldDragonFireBreath = string.Equals(dragonName, "Gold Dragon", StringComparison.OrdinalIgnoreCase);
            var isBrassDragonFireBreath = string.Equals(dragonName, "Brass Dragon", StringComparison.OrdinalIgnoreCase);
            var isColdDragonBreath = isWhiteDragonColdBreath || isSilverDragonColdBreath;
            var isFireDragonBreath = isRedDragonFireBreath || isGoldDragonFireBreath || isBrassDragonFireBreath;
            if (isBlueDragonLightningBreath && target.LightningProtectionSaveBonusVsLightning > 0)
                saveTarget = Math.Max(1, saveTarget - target.LightningProtectionSaveBonusVsLightning);
            if (isColdDragonBreath && target.HasActiveResistCold && target.ColdResistanceSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - target.ColdResistanceSaveBonus);
            if (isFireDragonBreath)
            {
                var fireSaveBonus = GetFireResistanceSaveBonus(target);
                if (fireSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            }
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = breathDamage;
            if (isFireDragonBreath)
                preSaveDamage = ApplyFireProtectionDamageReduction(target, preSaveDamage, 0, isMagicalFire: true, isNormalFire: false);
            if (isBlueDragonLightningBreath)
                preSaveDamage = ApplyLightningProtectionDamageReduction(target, preSaveDamage, isMagicalLightning: true, isNormalLightning: false);
            if (isColdDragonBreath)
                preSaveDamage = ApplyColdResistanceDamageReduction(target, preSaveDamage, isMagicalCold: true, isNormalCold: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s dragon breath."));
                continue;
            }

            var applied = saveRoll >= saveTarget ? preSaveDamage / 2 : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds save vs Breath ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails save vs Breath ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by dragon breath!"));
            }
        }

        return true;
    }

    private static bool TryGetHpDamageBreathDamage(MonsterInstance monster, out int damage)
    {
        foreach (var ability in monster.Template.SpecialAbilities)
        {
            var name = ability.Name?.Trim() ?? string.Empty;
            var m = Regex.Match(name, @"^(?<damage>\d+)\s*hp damage breath$", RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;

            if (int.TryParse(m.Groups["damage"].Value, out damage) && damage > 0)
                return true;
        }

        damage = 0;
        return false;
    }

    private bool TryResolvePyroHydraBreath(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Pyro hydra breath") || _pyroBreathCount.GetValueOrDefault(monster) >= 2)
            return false;

        var targets = session.ExposedParty.ToList();
        if (targets.Count == 0)
            return false;

        // Hydra Hit Dice represent the number of heads, not the number of bite attacks.
        var headCount = monster.Template.HitDice;
        if (headCount <= 0)
            throw new InvalidOperationException($"{monster.DisplayName} requires a positive head count for Pyro hydra breath.");

        var breathDamage = 8 * headCount;
        _pyroBreathCount[monster] = _pyroBreathCount.GetValueOrDefault(monster) + 1;
        events.Add(new CombatEvent($"{monster.DisplayName} uses Pyro hydra breath: {headCount} heads deal {breathDamage} fire damage (8 per head; save vs. breath weapon for {4 * headCount}, 4 per head). Each head can breathe twice per battle ({_pyroBreathCount[monster]}/2 used)."));
        RuleApplicationInfo.PublishLinked("MM", "Hydra", $"{monster.DisplayName}: Pyro hydra breath deals 8 damage per head, or 4 per head on a successful save vs. breath weapon; twice per day per head.");
        ResolveFireBreathDamage(monster, targets, breathDamage, "Pyro hydra breath", events);
        return true;
    }

    private bool TryResolveHellHoundBreatheFire(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var isHellHound = string.Equals(monster.Template.Name?.Trim(), "Hell Hound", StringComparison.OrdinalIgnoreCase);
        if (!isHellHound)
            return false;

        if (!HasSpecialAbility(monster, "Hell Hound Breathe Fire")
            && !HasSpecialAbility(monster, "Breathe Fire"))
            return false;

        var breathDamage = Math.Max(0, monster.Template.HitDice);
        if (breathDamage <= 0)
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        events.Add(new CombatEvent($"{monster.DisplayName} uses Hell Hound Breathe Fire for {breathDamage} damage (1 HP per HD)!"));
        ResolveFireBreathDamage(monster, aliveParty, breathDamage, "Hell Hound fire breath", events);
        return true;
    }

    private void ResolveFireBreathDamage(MonsterInstance monster, IReadOnlyList<Character> targets, int breathDamage, string breathName, List<CombatEvent> events)
    {
        foreach (var target in targets)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
            var fireSaveBonus = GetFireResistanceSaveBonus(target);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);

            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(target, breathDamage, 0, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s fire breath."));
                continue;
            }

            var damage = saveRoll >= saveTarget ? preSaveDamage / 2 : preSaveDamage;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            if (saveRoll >= saveTarget)
                events.Add(new CombatEvent($"{target.Name} succeeds save vs. breath weapon ({saveRoll} vs {saveTarget}) and takes half damage: {actual}. HP {before}->{target.CurrentHitPoints}."));
            else
                events.Add(new CombatEvent($"{target.Name} fails save vs. breath weapon ({saveRoll} vs {saveTarget}) and takes {actual} damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by {breathName}!"));
            }
        }
    }

    private void TryApplyErinyesDagger(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Venomous Dagger") || !IsAlive(target) || target.HasStatus(CharacterStatus.Asleep))
            return;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to the dagger's venom."));
            return;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists the venomous dagger (save vs poison {saveRoll} vs {saveTarget})."));
            return;
        }

        var rounds = _dice.Roll(6);
        target.AddStatus(CharacterStatus.Asleep);
        session.SetPartyAsleep(target.Name, rounds);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Erinyes Venomous Dagger",
            $"{monster.DisplayName} wounds {target.Name}",
            "The venomous dagger causes painful wounds; victims must save vs. poison or faint for 1-6 rounds.",
            "1",
            "6",
            rounds.ToString(),
            $"Save {saveRoll} vs {saveTarget} failed; faints for {rounds} round(s).");

        events.Add(new CombatEvent($"{target.Name} fails the save vs poison ({saveRoll} vs {saveTarget}) and faints for {rounds} round(s) from the venomous dagger!"));

        if (HasSpecialAbility(monster, "Rope of Entanglement") && !target.HasStatus(CharacterStatus.Webbed))
        {
            var ropeRounds = GetWebEscapeRoundsFromStrength(target);
            target.AddStatus(CharacterStatus.Webbed);
            session.SetPartyWebbed(target.Name, ropeRounds);
            events.Add(new CombatEvent($"{monster.DisplayName} binds the fallen {target.Name} with a Rope of Entanglement (breaks free in {ropeRounds} round(s) once conscious)."));
        }
    }

    private void TryApplyBoneHook(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Bone Hook") || !IsAlive(target))
            return;

        if (_boneHooks.Contains((monster, target)))
            return;

        var roll = _dice.Roll(100);
        var stuck = roll <= 50;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Bone Hook",
            $"{monster.DisplayName} bane hook on {target.Name}",
            "Any creature caught (hit) by the hook has a 50% chance of being stuck fast.",
            "1",
            "100",
            roll.ToString(),
            stuck ? "Stuck fast." : "Not stuck.");

        if (!stuck)
            return;

        _boneHooks.Add((monster, target));
        events.Add(new CombatEvent($"{monster.DisplayName}'s bone hook snares {target.Name}, who is stuck fast!"));
    }

    private bool TryResolveBoneDevilTail(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Bone Hook"))
            return false;

        var victims = _boneHooks
            .Where(h => ReferenceEquals(h.Monster, monster) && IsAlive(h.Victim) && h.Victim.CurrentHitPoints > 0)
            .Select(h => h.Victim)
            .ToList();
        _boneHooks.RemoveWhere(h => ReferenceEquals(h.Monster, monster) && !victims.Contains(h.Victim));

        if (victims.Count == 0)
            return false;

        var target = victims[_dice.Roll(victims.Count) - 1];
        var damage = _dice.Roll(4) + _dice.Roll(4);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);
        events.Add(new CombatEvent($"{monster.DisplayName} strikes the hooked {target.Name} with its tail for {actual} damage (2d4). HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
            return true;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists the tail's strength drain (save vs poison {saveRoll} vs {saveTarget})."));
            return true;
        }

        var loss = _dice.Roll(4);
        var beforeStrength = Math.Max(0, target.Abilities.Strength);
        var actualLoss = Math.Min(loss, Math.Max(0, beforeStrength - 1));
        target.Abilities.Strength = beforeStrength - actualLoss;

        var existing = _boneDevilStrengthLoss.TryGetValue(target, out var current) ? current.Amount : 0;
        _boneDevilStrengthLoss[target] = (existing + actualLoss, 10);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Bone Devil Tail",
            $"{monster.DisplayName} tail strikes {target.Name}",
            "Tail does 2-8 damage and causes a loss of 1-4 Strength unless a save vs. poison is made; strength loss lasts 10 melee rounds.",
            "1",
            "4",
            loss.ToString(),
            $"Save {saveRoll} vs {saveTarget} failed; STR {beforeStrength}->{target.Abilities.Strength} for 10 rounds.");

        events.Add(new CombatEvent($"{target.Name} fails the save vs poison ({saveRoll} vs {saveTarget}) and loses {actualLoss} Strength for 10 rounds: STR {beforeStrength}->{target.Abilities.Strength}."));
        return true;
    }

    private void TickBoneDevilStrengthLoss(Character member, List<CombatEvent> events)
    {
        if (!_boneDevilStrengthLoss.TryGetValue(member, out var loss))
            return;

        var remaining = loss.RoundsRemaining - 1;
        if (remaining > 0)
        {
            _boneDevilStrengthLoss[member] = (loss.Amount, remaining);
            return;
        }

        member.Abilities.Strength += loss.Amount;
        _boneDevilStrengthLoss.Remove(member);
        events.Add(new CombatEvent($"{member.Name}'s Strength returns (+{loss.Amount})."));
    }

    private void ClearBoneDevilEffects()
    {
        foreach (var (character, loss) in _boneDevilStrengthLoss.ToList())
            character.Abilities.Strength += loss.Amount;

        _boneDevilStrengthLoss.Clear();
        _boneHooks.Clear();
        _confusingGazeResolved.Clear();
        _fearAuraResolved.Clear();
        _sleepGazeResolved.Clear();
        _manticoreVolleys.Clear();
        _deathGroanUsed.Clear();
        _nightHagMagicMissileUses.Clear();
        _ogreMageSpellsUsed.Clear();
        _phaseSpidersInPhase.Clear();
        _nightHagSleepUsed.Clear();
        _nightHagEnfeeblementUses.Clear();
        _nightHagGateAttempted.Clear();
        _frightenedRounds.Clear();
        _iceDevilSpears.Clear();
        _iceStormUsed.Clear();
        _hornedWeapons.Clear();
        _wallOfFireUsed.Clear();
        _whirlwindUsed.Clear();
        _rockHurlOpened.Clear();
        _whirlwindPhase.Clear();
        _petrifyingBreathUses.Clear();
        _golemBerserkChance.Clear();
        _golemBerserk.Clear();
        ResetLevel8State();
        _bleedingCharacters.Clear();
        foreach (var (owner, weapon) in _thrownWeapons.Values)
            owner.Inventory.Add(weapon);

        _thrownWeapons.Clear();
        _retrievingWeaponRounds.Clear();
        _dreamRidingVictims.Clear();
    }

    private void ApplyDreamRidingDrain(CombatSession session, List<CombatEvent> events)
    {
        // The hag rides the victims that resisted her sleep spell that night, so she must have survived the encounter.
        foreach (var (hag, victim) in _dreamRidingVictims.ToList())
        {
            if (!hag.IsAlive || !IsAlive(victim))
                continue;

            var before = victim.Abilities.Constitution;
            victim.Abilities.Constitution = Math.Max(0, before - 1);
            events.Add(new CombatEvent($"That night {hag.DisplayName} rides {victim.Name} in the dream realm: CON {before}->{victim.Abilities.Constitution} (permanent)."));

            if (victim.Abilities.Constitution <= 0)
            {
                victim.CurrentHitPoints = 0;
                victim.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{victim.Name} reaches 0 Constitution and dies; {hag.DisplayName} carries off the larval soul."));
            }
        }
    }

    private bool TryResolveNightHagGate(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Gate") || _nightHagGateAttempted.Contains(monster))
            return false;

        if (MonsterTemplateLookup == null || _dice.Roll(3) != 1)
            return false;

        _nightHagGateAttempted.Add(monster);

        var roll = _dice.Roll(100);
        var success = roll <= 50;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Gate",
            $"{monster.DisplayName} attempts to gate in an ally",
            "A night hag may attempt to gate in a Type I demon or a barbed devil (50% chance); one attempt per encounter.",
            "1",
            "100",
            roll.ToString(),
            success ? "Success." : "Failure.");

        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in an ally but fails (roll {roll} vs 50%)."));
            return true;
        }

        var templateName = _dice.Roll(2) == 1 ? "Demon, Type I" : "Devil, Barbed";
        var template = MonsterTemplateLookup(templateName);
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var groupId = $"gate-{monster.GroupId}-{_dice.Roll(10000)}";
        var summoned = new MonsterInstance(template, 1, groupId);
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
        return true;
    }

    private bool TryResolveTelekinesisThrow(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Telekinesis") || _dice.Roll(4) != 1)
            return false;

        var targets = session.ExposedParty
            .Where(c => IsAlive(c) && !_thrownWeapons.ContainsKey(c.Name)
                        && c.Equipment.TryGetValue(EquipmentSlot.MainHand, out var w) && w != null)
            .ToList();
        if (targets.Count == 0)
            return false;

        var target = targets[_dice.Roll(targets.Count) - 1];
        var weapon = target.Equipment[EquipmentSlot.MainHand]!;
        target.Equipment[EquipmentSlot.MainHand] = null;
        _thrownWeapons[target.Name] = (target, weapon);
        RuleApplicationInfo.Publish("AD&D", "Telekinesis", $"{monster.DisplayName} on {target.Name}",
            "Weapon is thrown 20 feet away; the victim may run to get it (2 rounds), equip another weapon, or fight bare-handed.",
            "1", "1", "1", $"{weapon.Name} is torn from {target.Name}'s grasp.");
        events.Add(new CombatEvent($"{monster.DisplayName} uses telekinesis: {target.Name}'s {weapon.Name} is hurled 20 feet away!"));
        events.Add(new CombatEvent(TelekinesisPromptPrefix + target.Name));
        return true;
    }

    public bool HasThrownWeapon(Character character) => _thrownWeapons.ContainsKey(character.Name);

    // choice: 'R' run for the weapon (2 rounds), 'E' equip replacementWeapon from inventory, 'F' fight bare-handed.
    public void ApplyTelekinesisChoice(Character character, char choice, Item? replacementWeapon, List<CombatEvent> events)
    {
        if (!_thrownWeapons.TryGetValue(character.Name, out var thrown))
            return;

        switch (char.ToUpperInvariant(choice))
        {
            case 'R':
                _retrievingWeaponRounds[character.Name] = 2;
                events.Add(new CombatEvent($"{character.Name} runs after {thrown.Weapon.Name} (2 rounds)."));
                break;
            case 'E' when replacementWeapon != null && character.Inventory.Remove(replacementWeapon):
                character.Equipment[EquipmentSlot.MainHand] = replacementWeapon;
                events.Add(new CombatEvent($"{character.Name} draws {replacementWeapon.Name} from inventory."));
                break;
            default:
                events.Add(new CombatEvent($"{character.Name} fights with bare hands."));
                break;
        }
    }

    private bool TryResolveCauseFear(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Cause Fear") || _dice.Roll(4) != 1)
            return false;

        var targets = session.ExposedParty.Where(c => IsAlive(c) && !_frightenedRounds.ContainsKey(c.Name)).ToList();
        if (targets.Count == 0)
            return false;

        events.Add(new CombatEvent($"{monster.DisplayName} causes fear (as a wand of fear)!"));
        foreach (var target in targets)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.RodStaffWand));
            var saveRoll = _dice.Roll(20);
            var saved = saveRoll >= saveTarget;
            RuleApplicationInfo.Publish("AD&D", "Cause Fear", $"{monster.DisplayName} on {target.Name}",
                "Fear as a wand of fear; save vs. wand negates, otherwise flee for 6 rounds.", "1", "20", saveRoll.ToString(),
                $"Save target {saveTarget}. {(saved ? "Saved." : "Failed; flees in terror.")}");
            if (saved)
            {
                events.Add(new CombatEvent($"{target.Name} resists the fear (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            _frightenedRounds[target.Name] = 6;
            events.Add(new CombatEvent($"{target.Name} is terrified and flees for 6 rounds (save {saveRoll} vs {saveTarget})."));
        }

        return true;
    }

    private void TryApplyBarbedFear(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Barbed Fear") || _frightenedRounds.ContainsKey(target.Name))
            return;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.RodStaffWand));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists the terror of {monster.DisplayName}'s strike (save {saveRoll} vs {saveTarget})."));
            return;
        }

        _frightenedRounds[target.Name] = 6;
        events.Add(new CombatEvent($"{target.Name} is struck with fear by {monster.DisplayName} and flees for 6 rounds (save {saveRoll} vs {saveTarget})."));
    }

    private bool TryResolveBarbedDevilPower(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var powers = new List<string>();
        foreach (var name in new[] { "Pyrotechnics", "Produce Flame", "Hold Person" })
        {
            if (HasSpecialAbility(monster, name))
                powers.Add(name);
        }

        if (powers.Count == 0 || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var power = powers[_dice.Roll(powers.Count) - 1];
        if (power == "Produce Flame")
        {
            var target = party[_dice.Roll(party.Count) - 1];
            var damage = _dice.Roll(4) + monster.Template.HitDice;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
            events.Add(new CombatEvent($"{monster.DisplayName} hurls produced flame at {target.Name} for {before - target.CurrentHitPoints} damage (1d4+{monster.Template.HitDice}). HP {before}->{target.CurrentHitPoints}."));
            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }

            return true;
        }

        var isHold = power == "Hold Person";
        var affected = isHold ? party.OrderBy(_ => _dice.Roll(1000)).Take(_dice.Roll(3)).ToList() : party;
        events.Add(new CombatEvent(isHold
            ? $"{monster.DisplayName} casts hold person!"
            : $"{monster.DisplayName} fills the air with dazzling pyrotechnics!"));
        foreach (var target in affected)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(4);
            if (isHold)
            {
                target.ApplyParalysis(rounds + 4);
                events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and is held for {rounds + 4} round(s)."));
            }
            else
            {
                target.AddStatus(CharacterStatus.Confused);
                session.SetPartyConfused(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and is dazzled for {rounds} round(s)."));
            }
        }

        return true;
    }

    private void TryApplyIceDevilSpear(MonsterInstance monster, Character target, string attackName, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Great Spear")
            || !string.Equals(attackName, "Claw", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_iceDevilSpears.TryGetValue(monster, out var carriesSpear))
        {
            carriesSpear = _dice.Roll(100) <= 25;
            _iceDevilSpears[monster] = carriesSpear;
        }

        if (!carriesSpear)
            return;

        var damage = _dice.Roll(6) + _dice.Roll(6);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        events.Add(new CombatEvent($"{monster.DisplayName}'s great spear also pierces {target.Name} for {before - target.CurrentHitPoints} damage (2d6). HP {before}->{target.CurrentHitPoints}."));
        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
            return;
        }

        if (target.HasStatus(CharacterStatus.Slowed) || target.IsMonkImmuneToDiseaseSlowHaste())
            return;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} shrugs off the numbing cold (save {saveRoll} vs {saveTarget})."));
            return;
        }

        target.AddStatus(CharacterStatus.Slowed);
        events.Add(new CombatEvent($"{target.Name} is numbed by the spear's cold and slowed (save {saveRoll} vs {saveTarget})!"));
    }

    private void TryApplyHornedDevilAttack(MonsterInstance monster, Character target, string attackName, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Horned Weapon") || !IsAlive(target))
            return;

        if (string.Equals(attackName, "Tail", StringComparison.OrdinalIgnoreCase))
        {
            if (_bleedingCharacters.Add(target.Name))
                events.Add(new CombatEvent($"{target.Name} is gashed by {monster.DisplayName}'s tail and begins to bleed (1 HP/round until combat ends and the wound is bound)."));
            return;
        }

        if (!string.Equals(attackName, "Claw", StringComparison.OrdinalIgnoreCase))
            return;

        // 0 = unarmed, 1 = two-tined fork (75% of armed), 2 = barbed whip (25% of armed). Armed is a coin flip.
        if (!_hornedWeapons.TryGetValue(monster, out var weapon))
        {
            weapon = _dice.Roll(2) == 1 ? 0 : _dice.Roll(100) <= 75 ? 1 : 2;
            _hornedWeapons[monster] = weapon;
        }

        if (weapon == 0)
            return;

        var damage = weapon == 1 ? _dice.Roll(6) + _dice.Roll(6) : _dice.Roll(4);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
        events.Add(new CombatEvent($"{monster.DisplayName}'s {(weapon == 1 ? "two-tined fork (2d6)" : "barbed whip (1d4)")} also strikes {target.Name} for {before - target.CurrentHitPoints}. HP {before}->{target.CurrentHitPoints}."));
        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
            return;
        }

        if (weapon != 2 || target.HasStatus(CharacterStatus.Paralyzed))
            return;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} shrugs off the whip's stunning effect (save {saveRoll} vs {saveTarget})."));
            return;
        }

        target.ApplyParalysis(damage);
        events.Add(new CombatEvent($"{target.Name} is stunned by the barbed whip for {damage} round(s) (save {saveRoll} vs {saveTarget})."));
    }

    private void ApplyBleedingDamage(CombatSession session, List<CombatEvent> events)
    {
        foreach (var name in _bleedingCharacters.ToList())
        {
            var member = session.Party.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (member == null || !IsAlive(member))
            {
                _bleedingCharacters.Remove(name);
                continue;
            }

            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, before - 1);
            events.Add(new CombatEvent($"{member.Name} bleeds for 1 damage. HP {before}->{member.CurrentHitPoints}."));
            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} bleeds to death!"));
                _bleedingCharacters.Remove(name);
            }
        }
    }

    private bool TryResolveHornedWallOfFire(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var normalWall = HasSpecialAbility(monster, "Wall of Fire (Normal)");
        if ((!normalWall && !HasSpecialAbility(monster, "Wall of Fire")) || _wallOfFireUsed.Contains(monster) || _dice.Roll(3) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var diceCount = normalWall ? 1 : 3;
        _wallOfFireUsed.Add(monster);
        events.Add(new CombatEvent(normalWall
            ? $"{monster.DisplayName} raises a wall of fire!"
            : $"{monster.DisplayName} raises a wall of fire of triple strength!"));
        foreach (var target in party)
        {
            var damage = _dice.RollMany(8, diceCount);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);
            var saved = saveRoll >= saveTarget;
            var applied = saved ? damage / 2 : damage;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, before - applied);
            WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
            events.Add(new CombatEvent($"{target.Name} is scorched for {before - target.CurrentHitPoints} damage ({diceCount}d8, {(saved ? "saved for half" : "no save")}: {saveRoll} vs {saveTarget}). HP {before}->{target.CurrentHitPoints}."));
            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private bool TryResolveHurlRocks(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var hurlAbility = monster.Template.SpecialAttacks
            .Select(a => a?.Name?.Trim() ?? string.Empty)
            .Select(n => Regex.Match(n, @"^Hurl Rocks(?:\s*\((\d+)d(\d+)\))?$", RegexOptions.IgnoreCase))
            .FirstOrDefault(m => m.Success);
        if (hurlAbility == null)
            return false;

        var diceCount = hurlAbility.Groups[1].Success ? int.Parse(hurlAbility.Groups[1].Value) : 2;
        var dieSides = hurlAbility.Groups[2].Success ? int.Parse(hurlAbility.Groups[2].Value) : 8;
        // A giant opens by hurling a rock as the party closes in; afterwards it only occasionally throws instead of striking.
        var opened = _rockHurlOpened.Contains(monster);
        if (opened && _dice.Roll(4) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        _rockHurlOpened.Add(monster);
        var target = party[_dice.Roll(party.Count) - 1];
        var needed = Math.Clamp(GetMonsterThac0(monster) - target.ArmorClass, 1, 20);
        var roll = _dice.Roll(20);
        if (roll < needed)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} hurls a rock at {target.Name} but misses (rolled {roll}, needed {needed})."));
            return true;
        }

        var damage = _dice.RollMany(dieSides, diceCount);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, before - damage);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);
        events.Add(new CombatEvent($"{monster.DisplayName} hurls a rock at {target.Name} for {actual} damage ({diceCount}d{dieSides}; rolled {roll}, needed {needed}). HP {before}->{target.CurrentHitPoints}."));
        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }

        return true;
    }

    private bool TryResolvePetrifyingBreath(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Petrifying Breath"))
            return false;

        _petrifyingBreathUses.TryGetValue(monster, out var used);
        if (used >= 4)
            return false;

        var party = session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Petrified)).ToList();
        if (party.Count == 0)
            return false;

        _petrifyingBreathUses[monster] = used + 1;
        var affected = party.OrderBy(_ => _dice.Roll(1000)).Take(Math.Min(party.Count, _dice.Roll(3))).ToList();
        events.Add(new CombatEvent($"{monster.DisplayName} exhales a cone of petrifying breath ({used + 1}/4 uses today)!"));
        RuleApplicationInfo.PublishLinked("MM", "Gorgon", $"{monster.DisplayName}: breath turns creatures to stone (save vs. petrification); 4 uses per day, hits 1d3 characters.");

        foreach (var target in affected)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.PetrificationPolymorph));
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists the petrifying breath (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            target.AddStatus(CharacterStatus.Petrified);
            events.Add(new CombatEvent($"{target.Name} is turned to stone by {monster.DisplayName}'s breath! (save {saveRoll} vs {saveTarget})"));
        }

        return true;
    }

    private bool TryResolveFleshGolemBerserk(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Berserk"))
            return false;

        if (!_golemBerserk.Contains(monster))
        {
            // 1% cumulative chance per melee round.
            var chance = _golemBerserkChance.GetValueOrDefault(monster) + 1;
            _golemBerserkChance[monster] = chance;
            if (_dice.Roll(100) > chance)
                return false;

            _golemBerserk.Add(monster);
            events.Add(new CombatEvent($"{monster.DisplayName} goes berserk and attacks anything in sight!"));
        }
        else if (!IsTemplate(monster, "Clay") && _dice.Roll(10) == 1)
        {
            _golemBerserk.Remove(monster);
            _golemBerserkChance[monster] = 0;
            events.Add(new CombatEvent($"{monster.DisplayName} regains control of itself."));
            return false;
        }

        var allies = session.Monsters.Where(m => m.IsAlive && !ReferenceEquals(m, monster)).ToList();
        var targetCount = allies.Count + session.ExposedParty.Count(IsAlive);
        if (allies.Count == 0 || _dice.Roll(targetCount) > allies.Count)
            return false;

        var victim = allies[_dice.Roll(allies.Count) - 1];
        var damage = IsTemplate(monster, "Clay") ? _dice.RollMany(10, 3) : _dice.RollMany(8, 2) + _dice.RollMany(8, 2);
        var before = victim.CurrentHitPoints;
        victim.CurrentHitPoints = Math.Max(0, before - damage);
        events.Add(new CombatEvent($"{monster.DisplayName}, berserk, pounds {victim.DisplayName} for {before - victim.CurrentHitPoints} damage. HP {before}->{victim.CurrentHitPoints}."));
        if (!victim.IsAlive)
            events.Add(new CombatEvent($"{victim.DisplayName} is destroyed by the raging golem."));
        return true;
    }

    private bool TryResolveAirElementalWhirlwind(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Whirlwind"))
            return false;

        _whirlwindPhase.TryGetValue(monster, out var phase);
        var party = session.ExposedParty.Where(IsAlive).ToList();

        if (phase == 0)
        {
            if (party.Count == 0 || _whirlwindUsed.Contains(monster) || _dice.Roll(3) != 1)
                return false;

            _whirlwindUsed.Add(monster);
            _whirlwindPhase[monster] = 1;
            events.Add(new CombatEvent($"{monster.DisplayName} spins up a whirlwind {monster.InstanceHitDice}\" high; it needs this whole turn to form."));
            return true;
        }

        if (phase == 2)
        {
            _whirlwindPhase.Remove(monster);
            events.Add(new CombatEvent($"{monster.DisplayName} spends its turn dissipating the whirlwind."));
            return true;
        }

        _whirlwindPhase[monster] = 2;
        events.Add(new CombatEvent($"The whirlwind of {monster.DisplayName} tears across the party!"));
        foreach (var target in party)
        {
            if (target.Level < 3)
            {
                target.CurrentHitPoints = 0;
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} (under 3 hit dice) is swept away and killed!"));
                continue;
            }

            var damage = _dice.RollMany(8, 2);
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, before - damage);
            WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
            events.Add(new CombatEvent($"{target.Name} is battered by the whirlwind for {before - target.CurrentHitPoints} damage (2d8). HP {before}->{target.CurrentHitPoints}."));
            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        return true;
    }

    private bool TryResolveEfreetiWish(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Grant Wishes") || _dice.Roll(4) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        var target = party[_dice.Roll(party.Count) - 1];
        events.Add(new CombatEvent($"{monster.DisplayName} offers to grant {target.Name} a wish -- and twists its meaning to the letter!"));

        var outcome = _dice.Roll(4);
        if (outcome == 2 && target.Inventory.Count == 0)
            outcome = 4;

        switch (outcome)
        {
            case 1:
            {
                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(1, before / 2);
                events.Add(new CombatEvent($"The wish for vigor drains {target.Name}'s life force. HP {before}->{target.CurrentHitPoints}."));
                break;
            }
            case 2:
            {
                var item = target.Inventory[_dice.Roll(target.Inventory.Count) - 1];
                target.Inventory.Remove(item);
                events.Add(new CombatEvent($"The wish for treasure makes {target.Name}'s {item.Name} vanish forever."));
                break;
            }
            case 3:
            {
                var scores = target.Abilities;
                switch (_dice.Roll(6))
                {
                    case 1: scores.Strength = Math.Max(3, scores.Strength - 1); break;
                    case 2: scores.Intelligence = Math.Max(3, scores.Intelligence - 1); break;
                    case 3: scores.Wisdom = Math.Max(3, scores.Wisdom - 1); break;
                    case 4: scores.Dexterity = Math.Max(3, scores.Dexterity - 1); break;
                    case 5: scores.Constitution = Math.Max(3, scores.Constitution - 1); break;
                    default: scores.Charisma = Math.Max(3, scores.Charisma - 1); break;
                }

                events.Add(new CombatEvent($"The wish for power curses {target.Name}: one ability score permanently drops by 1."));
                break;
            }
            default:
                target.AdvanceAgeByDays(10 * 365);
                events.Add(new CombatEvent($"The wish for long life ages {target.Name} by 10 years (now {target.Age})."));
                if (target.DiedOfOldAge)
                    events.Add(new CombatEvent($"{target.Name} dies of old age!"));
                break;
        }

        monster.HasFled = true;
        monster.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{monster.DisplayName} is free and vanishes in a puff of smoke."));
        return true;
    }

    private bool TryResolveIceDevilPower(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var canGate = HasSpecialAbility(monster, "Ice Devil Gate") && !_nightHagGateAttempted.Contains(monster) && MonsterTemplateLookup != null;
        var canStorm = HasSpecialAbility(monster, "Ice Storm") && !_iceStormUsed.Contains(monster);
        if ((!canGate && !canStorm) || _dice.Roll(3) != 1)
            return false;

        if (canStorm && (!canGate || _dice.Roll(2) == 1))
        {
            var party = session.ExposedParty.Where(IsAlive).ToList();
            if (party.Count == 0)
                return false;

            _iceStormUsed.Add(monster);
            events.Add(new CombatEvent($"{monster.DisplayName} calls down an ice storm!"));
            foreach (var target in party)
            {
                var damage = _dice.Roll(10) + _dice.Roll(10) + _dice.Roll(10);
                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
                WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
                events.Add(new CombatEvent($"{target.Name} is battered by hail for {before - target.CurrentHitPoints} damage (3d10). HP {before}->{target.CurrentHitPoints}."));
                if (target.CurrentHitPoints <= 0)
                {
                    target.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{target.Name} is slain!"));
                }
            }

            return true;
        }

        _nightHagGateAttempted.Add(monster);
        var roll = _dice.Roll(100);
        RuleApplicationInfo.Publish("AD&D", "Ice Devil Gate", $"{monster.DisplayName} attempts to gate in devils",
            "Gates in two bone devils (70%) or another ice devil (30%); 60% chance of success.",
            "1", "100", roll.ToString(), roll <= 60 ? "Gate opens." : "Gate fails.");
        if (roll > 60)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in devils but fails (roll {roll} vs 60%)."));
            return true;
        }

        var boneDevils = _dice.Roll(100) <= 70;
        var template = MonsterTemplateLookup!(boneDevils ? "Devil, Bone" : "Devil, Ice");
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var gateGroup = $"gate-{monster.GroupId}-{_dice.Roll(10000)}";
        var count = boneDevils ? 2 : 1;
        for (var i = 1; i <= count; i++)
        {
            var summoned = new MonsterInstance(template, i, gateGroup);
            session.Monsters.Add(summoned);
            events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
        }

        return true;
    }

    private bool TryResolveVrockGate(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var (ability, templateName, percent) = HasSpecialAbility(monster, "Vrock Gate")
            ? ("Vrock Gate", "Demon, Type I", 10)
            : HasSpecialAbility(monster, "Hezrou Gate")
                ? ("Hezrou Gate", "Demon, Type II ", 20)
                : (string.Empty, string.Empty, 0);
        if (ability.Length == 0 || _nightHagGateAttempted.Contains(monster)
            || MonsterTemplateLookup == null || _dice.Roll(3) != 1)
            return false;

        _nightHagGateAttempted.Add(monster);
        var roll = _dice.Roll(100);
        var success = roll <= percent;
        RuleApplicationInfo.Publish("AD&D", ability, $"{monster.DisplayName} attempts to gate in another demon of its kind",
            $"May gate in another demon of its type ({percent}% chance of success); one attempt per encounter.",
            "1", "100", roll.ToString(), success ? "Gate opens." : "Gate fails.");
        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in another demon but fails (roll {roll} vs {percent}%)."));
            return true;
        }

        var template = MonsterTemplateLookup(templateName) ?? MonsterTemplateLookup(templateName.Trim());
        if (template == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
            return true;
        }

        var summoned = new MonsterInstance(template, 1, $"gate-{monster.GroupId}-{_dice.Roll(10000)}");
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
        return true;
    }

    private bool TryResolveSuccubusPower(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var hasGate = HasSpecialAbility(monster, "Succubus Gate") && !_nightHagGateAttempted.Contains(monster);
        var hasCharm = HasSpecialAbility(monster, "Charm Person");
        if ((!hasGate && !hasCharm) || _dice.Roll(3) != 1)
            return false;

        if (hasGate && (!hasCharm || _dice.Roll(2) == 1))
        {
            if (MonsterTemplateLookup == null)
                return false;

            _nightHagGateAttempted.Add(monster);
            var opens = _dice.Roll(100);
            RuleApplicationInfo.Publish("AD&D", "Succubus Gate", $"{monster.DisplayName} attempts to gate in a demon",
                "A succubus may gate in a type IV (70%), type VI (25%), or a demon lord/prince (5%); only a 40% chance that the gate opens.",
                "1", "100", opens.ToString(), opens <= 40 ? "Gate opens." : "Gate fails.");
            if (opens > 40)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} tries to gate in a demon but fails (roll {opens} vs 40%)."));
                return true;
            }

            var kind = _dice.Roll(100);
            var templateName = kind <= 70 ? "Demon, Type IV"
                : kind <= 95 ? "Demon, Type VI"
                : _dice.Roll(2) == 1 ? "Orcus" : "Demogorgon";
            var template = MonsterTemplateLookup(templateName);
            if (template == null)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} opens a gate, but nothing answers."));
                return true;
            }

            var summoned = new MonsterInstance(template, 1, $"gate-{monster.GroupId}-{_dice.Roll(10000)}");
            session.Monsters.Add(summoned);
            events.Add(new CombatEvent($"{monster.DisplayName} gates in {summoned.DisplayName}!"));
            return true;
        }

        var targets = GetSpellAffectablePartyTargets(monster, session, events, "Charm Person")
            .Where(t => !t.HasStatus(CharacterStatus.Charmed))
            .ToList();
        if (targets.Count == 0)
            return false;

        var target = targets[_dice.Roll(targets.Count) - 1];
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(target, _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
        var saveRoll = _dice.Roll(20);
        var saved = saveRoll >= saveTarget;
        RuleApplicationInfo.Publish("AD&D", "Charm Person", $"{monster.DisplayName} on {target.Name}",
            "Charm person at will; save vs. spell negates.", "1", "20", saveRoll.ToString(),
            $"Save target {saveTarget}. {(saved ? "Saved." : "Failed; charmed.")}");
        if (saved)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s Charm Person (save {saveRoll} vs {saveTarget})."));
            return true;
        }

        target.AddStatus(CharacterStatus.Charmed);
        events.Add(new CombatEvent($"{target.Name} is charmed by {monster.DisplayName} and will attack party allies."));
        return true;
    }

    private bool TryResolveNightHagStrangulation(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Strangulation"))
            return false;

        var sleepers = session.ExposedParty
            .Where(c => IsAlive(c) && c.HasStatus(CharacterStatus.Asleep))
            .ToList();
        if (sleepers.Count == 0)
            return false;

        var target = sleepers[_dice.Roll(sleepers.Count) - 1];
        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Strangulation",
            $"{monster.DisplayName} on {target.Name}",
            "The night hag strangles a sleeping victim; no save is allowed.",
            "0",
            "0",
            "-",
            "Sleeping victim is strangled.");

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{monster.DisplayName} strangles the sleeping {target.Name}, who dies!"));
        return true;
    }

    private bool TryResolveNightHagEnfeeblement(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Ray of Enfeeblement"))
            return false;

        _nightHagEnfeeblementUses.TryGetValue(monster, out var used);
        if (used >= 3 || _dice.Roll(100) > 50)
            return false;

        var targets = GetSpellAffectablePartyTargets(monster, session, events, "Ray of Enfeeblement");
        if (targets.Count == 0)
            return false;

        _nightHagEnfeeblementUses[monster] = used + 1;
        var target = targets[_dice.Roll(targets.Count) - 1];

        var casterLevel = Math.Max(1, monster.Template.HitDice);
        var percent = 25 + 2 * casterLevel;

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Ray of Enfeeblement ({used + 1}/3) at {target.Name}, who resists (save {saveRoll} vs {saveTarget})."));
            return true;
        }

        var beforeStrength = Math.Max(0, target.Abilities.Strength);
        var loss = Math.Max(1, beforeStrength * percent / 100);
        var actualLoss = Math.Min(loss, Math.Max(0, beforeStrength - 1));
        target.Abilities.Strength = beforeStrength - actualLoss;

        var existing = _boneDevilStrengthLoss.TryGetValue(target, out var current) ? current.Amount : 0;
        _boneDevilStrengthLoss[target] = (existing + actualLoss, casterLevel);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Ray of Enfeeblement",
            $"{monster.DisplayName} on {target.Name}",
            "Ray of enfeeblement reduces Strength by 25% + 2% per caster level (hag counts as level 8 = 41%); save vs. spell negates. Lasts 1 round per level.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget} failed; {percent}% reduction, STR {beforeStrength}->{target.Abilities.Strength} for {casterLevel} rounds.");

        events.Add(new CombatEvent($"{monster.DisplayName} casts Ray of Enfeeblement ({used + 1}/3)! {target.Name} fails the save ({saveRoll} vs {saveTarget}) and Strength drops {percent}%: STR {beforeStrength}->{target.Abilities.Strength} for {casterLevel} round(s)."));
        return true;
    }

    private bool TryResolveOgreMageSpell(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        // Each of these may be used once per day; at most one per turn, 1 in 3 chance each turn.
        foreach (var spell in new[] { "Ogre Mage Sleep", "Ogre Mage Charm Person", "Ogre Mage Cold Ray" })
        {
            if (!HasSpecialAbility(monster, spell) || _ogreMageSpellsUsed.Contains((monster, spell)))
                continue;
            if (_dice.Roll(3) != 1)
                continue;

            var targets = GetSpellAffectablePartyTargets(monster, session, events, spell)
                .Where(t => !t.HasStatus(CharacterStatus.Asleep) && !t.HasStatus(CharacterStatus.Charmed))
                .ToList();
            if (targets.Count == 0)
                continue;

            _ogreMageSpellsUsed.Add((monster, spell));

            if (spell == "Ogre Mage Sleep")
            {
                var count = _dice.Roll(4) + _dice.Roll(4);
                var rounds = _dice.RollMany(4, 4);
                var affected = targets.Where(t => t.Level <= 4).OrderBy(_ => _dice.Roll(1000)).Take(count).ToList();
                events.Add(new CombatEvent($"{monster.DisplayName} casts Sleep (2d4 = {count} creatures, up to 4+1 HD)."));
                RuleApplicationInfo.Publish("AD&D", "Ogre Mage Sleep", monster.DisplayName,
                    "Sleep (once per day) affects 2d4 creatures of up to 4+1 hit dice or levels; no saving throw.",
                    "2", "4", count.ToString(), $"{affected.Count} eligible target(s).");
                foreach (var t in affected)
                {
                    t.AddStatus(CharacterStatus.Asleep);
                    session.SetPartyAsleep(t.Name, rounds);
                    events.Add(new CombatEvent($"{t.Name} falls asleep for {rounds} round(s)."));
                }

                if (affected.Count == 0)
                    events.Add(new CombatEvent("The spell has no effect on the party."));
                return true;
            }

            var target = targets[_dice.Roll(targets.Count) - 1];
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, spell == "Ogre Mage Charm Person" ? SaveThrowType.Spell : SaveThrowType.RodStaffWand));
            var saveRoll = _dice.Roll(20);
            var saved = saveRoll >= saveTarget;

            if (spell == "Ogre Mage Charm Person")
            {
                RuleApplicationInfo.Publish("AD&D", "Ogre Mage Charm Person", $"{monster.DisplayName} on {target.Name}",
                    "Charm person (once per day); save vs. spell negates.", "1", "20", saveRoll.ToString(),
                    $"Save target {saveTarget}. {(saved ? "Saved." : "Failed; charmed.")}");
                if (saved)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Charm Person (save {saveRoll} vs {saveTarget})."));
                    return true;
                }

                target.AddStatus(CharacterStatus.Charmed);
                events.Add(new CombatEvent($"{target.Name} is charmed by {monster.DisplayName} and will attack party allies."));
                return true;
            }

            var rolled = _dice.RollMany(8, 8);
            var damage = ApplyColdResistanceDamageReduction(target, saved ? rolled / 2 : rolled, isMagicalCold: true, isNormalCold: false);
            RuleApplicationInfo.Publish("AD&D", "Ogre Mage Cold Ray", $"{monster.DisplayName} on {target.Name}",
                "Ray of cold (once per day) like a wand of cold: 8d8 damage, half if save vs. wand is made.", "8", "8",
                rolled.ToString(), $"Save target {saveTarget}. {(saved ? "Saved; half damage." : "Failed; full damage.")}");
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            events.Add(new CombatEvent($"{monster.DisplayName} fires a ray of cold at {target.Name} for {actual} damage (8d8 = {rolled}{(saved ? ", saved for half" : string.Empty)}). HP {before}->{target.CurrentHitPoints}."));
            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }

            return true;
        }

        return false;
    }

    private bool TryResolveNightHagSleep(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Sleep Magic") || _nightHagSleepUsed.Contains(monster))
            return false;

        var targets = GetSpellAffectablePartyTargets(monster, session, events, "Sleep")
            .Where(t => !t.HasStatus(CharacterStatus.Asleep))
            .ToList();
        if (targets.Count == 0)
            return false;

        _nightHagSleepUsed.Add(monster);
        events.Add(new CombatEvent($"{monster.DisplayName} casts a powerful Sleep spell that affects even high-level characters!"));

        foreach (var target in targets)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Night Hag Sleep Magic",
                $"{monster.DisplayName} on {target.Name}",
                "Sleep affects even 12th-level humans (no hit-dice limit) unless a save vs. magic is made.",
                "1",
                "20",
                saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Failed; falls asleep.")}");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists Sleep (save {saveRoll} vs {saveTarget})."));
                if (HasSpecialAbility(monster, "Dream Riding") && _dreamRidingVictims.Add((monster, target)))
                    events.Add(new CombatEvent($"{monster.DisplayName} marks {target.Name} to be ridden in dreams."));
                continue;
            }

            var rounds = _dice.Roll(4) + _dice.Roll(4);
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and falls asleep for {rounds} round(s)."));
        }

        return true;
    }

    private bool TryResolveNightHagMagicMissile(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Night Hag Magic missile"))
            return false;

        _nightHagMagicMissileUses.TryGetValue(monster, out var used);
        if (used >= 3 || _dice.Roll(100) > 50)
            return false;

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Magic Missile");
        if (aliveParty.Count == 0)
            return false;

        _nightHagMagicMissileUses[monster] = used + 1;
        var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolled = _dice.Roll(8) + _dice.Roll(8);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Night Hag Magic Missile",
            $"{monster.DisplayName} on {target.Name}",
            "Magic missile, 3 times per day, 2-16 damage, never misses.",
            "2",
            "8",
            rolled.ToString(),
            $"Cast {used + 1}/3");

        var damage = ApplyBroochOfShieldingAgainstMagicMissile(target, rolled, events);
        if (damage <= 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName}'s magic missile is fully absorbed by {target.Name}'s Brooch of Shielding."));
            return true;
        }

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Magic Missile ({used + 1}/3)! {target.Name} takes {actual} damage (rolled {rolled}). HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }

        return true;
    }

    private void ApplySleepGaze(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Sleep Gaze"))
            return;

        // A hostile creature is not unsuspecting: only a party caught by surprise on round 1 is affected.
        if (session.RoundNumber != 1 || !session.PartySurprisedRound1)
            return;

        foreach (var target in session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Asleep)).ToList())
        {
            if (!_sleepGazeResolved.Add((monster, target)))
                continue;

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Sleep Gaze",
                $"{monster.DisplayName} gazes at {target.Name}",
                "The gaze has the effect of a sleep spell on any unsuspecting creature that fails a save vs. magic. A hostile creature is not unsuspecting.",
                "1",
                "20",
                saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Failed; falls asleep.")}");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s sleep gaze (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.RollMany(4, 4);
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and falls asleep from {monster.DisplayName}'s gaze for {rounds} round(s)."));
        }
    }

    private bool TryResolveBrownMoldFreezing(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Freezing Aura"))
            return false;

        foreach (var target in session.ExposedParty.Where(IsAlive).ToList())
        {
            // Body heat of about 98 degrees is 43 above 55, i.e. four 10-degree steps of 1d8 frost damage each.
            var rolled = 0;
            for (var i = 0; i < 4; i++)
                rolled += _dice.Roll(8);

            var damage = ApplyColdResistanceDamageReduction(target, rolled, isMagicalCold: false, isNormalCold: true);
            if (damage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from {monster.DisplayName}'s heat drain."));
                continue;
            }

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            events.Add(new CombatEvent($"{monster.DisplayName} drains {target.Name}'s body heat for {actual} frost damage (4d8 = {rolled}). HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} freezes to death!"));
            }
        }

        RuleApplicationInfo.Publish(
            "AD&D",
            "Brown Mold Freezing Aura",
            $"{monster.DisplayName} drains heat",
            "Each melee round within 5', the mold drains 1-8 hp frost damage per 10 degrees of body heat above 55 degrees (about 4d8 for a human).",
            "4",
            "8",
            "-",
            "Applies to every living party member each round.");

        return true;
    }

    private bool TryResolveManticoreTailSpikes(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Tail Spikes"))
            return false;

        _manticoreVolleys.TryGetValue(monster, out var volleys);
        if (volleys >= 4)
            return false;

        // A manticore opens with a volley; afterwards it mixes volleys with melee.
        if (volleys > 0 && _dice.Roll(2) != 1)
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        _manticoreVolleys[monster] = volleys + 1;
        events.Add(new CombatEvent($"{monster.DisplayName} looses a volley of 6 tail spikes ({volleys + 1}/4)!"));

        var thac0 = GetMonsterThac0(monster);
        for (var i = 0; i < 6; i++)
        {
            var target = party[_dice.Roll(party.Count) - 1];
            if (!IsAlive(target))
                continue;

            var needed = Math.Clamp(thac0 - target.ArmorClass, 1, 20);
            var roll = _dice.Roll(20);
            if (roll < needed)
            {
                events.Add(new CombatEvent($"A spike misses {target.Name} (rolled {roll}, needed {needed})."));
                continue;
            }

            var damage = _dice.Roll(6);
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            events.Add(new CombatEvent($"A spike hits {target.Name} for {actual} damage (rolled {roll}, needed {needed}). HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
        }

        RuleApplicationInfo.Publish(
            "AD&D",
            "Manticore Tail Spikes",
            $"{monster.DisplayName} volley {volleys + 1}/4",
            "A manticore looses a volley of 6 iron tail spikes (18' range, as a light crossbow), 1-6 damage per hit; up to four volleys.",
            "6",
            "6",
            "-",
            "Each spike makes its own attack roll against a random target.");

        return true;
    }

    private void ApplyMedusaGaze(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Medusa Gaze"))
            return;

        foreach (var target in session.ExposedParty.Where(c => IsAlive(c) && !c.HasStatus(CharacterStatus.Petrified)).ToList())
        {
            // Each round a character either meets the gaze or averts their eyes (and is then bitten by the asps instead).
            if (_dice.Roll(2) == 1)
            {
                events.Add(new CombatEvent($"{target.Name} averts their eyes from {monster.DisplayName}'s gaze."));
                continue;
            }

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.PetrificationPolymorph));
            var saveRoll = _dice.Roll(20);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Medusa Gaze",
                $"{monster.DisplayName} gazes at {target.Name}",
                "Creatures within 3\" who meet the gaze turn to stone unless they save vs. petrification.",
                "1",
                "20",
                saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Failed; turned to stone.")}");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} meets {monster.DisplayName}'s gaze but resists petrification (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            target.AddStatus(CharacterStatus.Petrified);
            events.Add(new CombatEvent($"{target.Name} meets {monster.DisplayName}'s gaze and is turned to stone! (save {saveRoll} vs {saveTarget})"));
        }
    }

    private void TryApplyMedusaAspBite(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Poisonous Hair Asps") || !IsAlive(target))
            return;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to the asps' poison."));
            return;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.Name} resists the asps' poison (save vs poison {saveRoll} vs {saveTarget})."));
            return;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{target.Name} is bitten by {monster.DisplayName}'s hair asps and dies from the poison! (save {saveRoll} vs {saveTarget})"));
    }

    private void ApplyFearAura(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var usesWandSave = HasAnySpecialAbility(monster, "Erinyes Fear", "Radiate Fear");
        if (!usesWandSave && !HasSpecialAbility(monster, "Fear Aura"))
            return;

        foreach (var target in session.ExposedParty.Where(IsAlive).ToList())
        {
            if (!_fearAuraResolved.Add((monster, target)))
                continue;

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, usesWandSave ? SaveThrowType.RodStaffWand : SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} sees {monster.DisplayName} but resists fear (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(4);
            target.AddStatus(CharacterStatus.Confused);
            session.SetPartyConfused(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and is paralyzed with fear at the sight of {monster.DisplayName} for {rounds} round(s)!"));
        }
    }

    private bool TryResolveDeathGroan(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Death Groan") || _deathGroanUsed.Contains(monster))
            return false;

        var party = session.ExposedParty.Where(IsAlive).ToList();
        if (party.Count == 0)
            return false;

        // Combat takes place underground, so it is always treated as darkness; usable once per day.
        _deathGroanUsed.Add(monster);
        events.Add(new CombatEvent($"{monster.DisplayName} lets out a terrible keening wail!"));

        foreach (var target in party)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Death Groan",
                $"{monster.DisplayName} wail on {target.Name}",
                "Hearers within 3\" must save vs. magic or die on the spot. Usable once per day, only during darkness.",
                "1",
                "20",
                saveRoll.ToString(),
                $"Save target {saveTarget}. {(saveRoll >= saveTarget ? "Saved." : "Failed; dies.")}");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} resists the wail (save vs magic {saveRoll} vs {saveTarget})."));
                continue;
            }

            target.CurrentHitPoints = 0;
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} fails the save ({saveRoll} vs {saveTarget}) and dies from the wail!"));
        }

        return true;
    }

    private void ApplyConfusingGaze(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Confusing Gaze"))
            return;

        foreach (var target in session.ExposedParty.Where(IsAlive).ToList())
        {
            if (!_confusingGazeResolved.Add((monster, target)))
                continue;

            if (target.HasStatus(CharacterStatus.Confused))
                continue;

            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} meets {monster.DisplayName}'s gaze but resists confusion (save vs magic {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(4) + _dice.Roll(4) + _dice.Roll(4);
            target.AddStatus(CharacterStatus.Confused);
            session.SetPartyConfused(target.Name, rounds);

            RuleApplicationInfo.Publish(
                "AD&D",
                "Confusing Gaze",
                $"{monster.DisplayName} confusing gaze on {target.Name}",
                "Any intelligent creature viewing the four eyes must save vs. magic or be confused for 3-12 melee rounds.",
                "3",
                "4",
                rounds.ToString(),
                $"Save {saveRoll} vs {saveTarget} failed; confused for {rounds} round(s).");

            events.Add(new CombatEvent($"{target.Name} fails the save vs magic ({saveRoll} vs {saveTarget}) and is confused by {monster.DisplayName}'s gaze for {rounds} round(s)!"));
        }
    }

    private bool TryResolveBoneDevilSummon(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var isErinyesSummon = HasSpecialAbility(monster, "Summon Erinyes");
        var isBarbedSummon = HasSpecialAbility(monster, "Summon Barbed Devil");
        var isHornedSummon = HasSpecialAbility(monster, "Summon Horned Devil");
        if (!isErinyesSummon && !isBarbedSummon && !isHornedSummon && !HasSpecialAbility(monster, "Summon Bone Devil"))
            return false;

        // One summoning attempt in roughly four rounds; total devils of this kind are capped to keep encounters bounded.
        if (_dice.Roll(4) != 1)
            return false;

        const int maxDevils = 4;
        if (session.AliveMonsters.Count(m => string.Equals(m.Template.Name, monster.Template.Name, StringComparison.OrdinalIgnoreCase)) >= maxDevils)
            return false;

        var successChance = isHornedSummon ? 50 : isBarbedSummon ? 30 : isErinyesSummon ? 25 : 40;
        var roll = _dice.Roll(100);
        var success = roll <= successChance;
        RuleApplicationInfo.Publish(
            "AD&D",
            "Devil Summoning",
            $"{monster.DisplayName} attempts to summon another {monster.Template.Name}",
            $"A devil of this kind can summon another of its kind ({successChance}% chance of success), once per round.",
            "1",
            "100",
            roll.ToString(),
            success ? "Success." : "Failure.");

        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to summon another {monster.Template.Name} but fails (roll {roll} vs {successChance}%)."));
            return true;
        }

        var nextIndex = session.Monsters
            .Where(m => string.Equals(m.GroupId, monster.GroupId, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Index)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var summoned = new MonsterInstance(monster.Template, nextIndex, monster.GroupId) { IsInLair = monster.IsInLair };
        session.Monsters.Add(summoned);
        events.Add(new CombatEvent($"{monster.DisplayName} summons another devil: {summoned.DisplayName} appears!"));
        return true;
    }

    private bool TryResolveMonsterAcidSpit(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Acid Spit"))
            return false;

        var aliveParty = session.ExposedParty.Where(IsAlive).ToList();
        if (aliveParty.Count == 0)
            return false;

        var isFirstSpit = !_acidSpitRangeInches.ContainsKey(monster);
        if (!isFirstSpit && _dice.Roll(3) != 1)
            return false;

        // Range is determined on the first spit and kept for later spits.
        if (isFirstSpit)
            _acidSpitRangeInches[monster] = _dice.Roll(8);
        var rangeInches = _acidSpitRangeInches[monster];

        var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];

        // Base 50% at 6"; +10% per inch closer, -10% per inch beyond 6". First spit is always inaccurate (10%).
        var hitChance = isFirstSpit ? 10 : Math.Clamp(50 + (6 - rangeInches) * 10, 5, 100);
        var hitRoll = _dice.Roll(100);
        if (hitRoll > hitChance)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} spits acid at {target.Name} from {rangeInches}\" but misses (roll {hitRoll} vs {hitChance}%{(isFirstSpit ? ", first spit" : string.Empty)})."));
            return true;
        }

        var damage = _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8);
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.BreathWeapon));
        var saveRoll = _dice.Roll(20);
        var saved = saveRoll >= saveTarget;
        var applied = saved ? damage / 2 : damage;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Acid Spit",
            $"{monster.DisplayName} spits acid at {target.Name}",
            "Acid spit deals 4d8 damage; save vs. breath weapon for half damage. The first spit has only a 10% chance to hit.",
            "4",
            "8",
            damage.ToString(),
            $"Save {saveRoll} vs {saveTarget} => {(saved ? "half damage" : "full damage")}: {applied}.");

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
        var actual = before - target.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(target, actual, events);

        events.Add(new CombatEvent(saved
            ? $"{monster.DisplayName} spits acid at {target.Name}; save {saveRoll} vs {saveTarget} succeeds, half damage: {actual}. HP {before}->{target.CurrentHitPoints}."
            : $"{monster.DisplayName} spits acid at {target.Name}; save {saveRoll} vs {saveTarget} fails, {actual} damage. HP {before}->{target.CurrentHitPoints}."));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }

        return true;
    }

    private bool TryResolveMonsterPickPockets(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Pick pockets"))
            return false;

        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var chancePercent = Math.Clamp(25 + Math.Max(0, monster.Template.HitDice) * 5, 0, 100);
        var roll = _dice.Roll(100);
        var success = roll <= chancePercent;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Pick Pockets",
            $"{monster.DisplayName} pick pockets attempt (Player's Handbook p.28)",
            "Roll 1d100. Chance = 25% + Hit Dice x 5% (probability progression reference: PHB page 28).",
            "1",
            "100",
            roll.ToString(),
            $"Chance {chancePercent}%. {(success ? "Success" : "Failure")}.");

        if (!success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} tries to pick pockets but fails (roll {roll} vs {chancePercent}%)."));
            return false;
        }

        var victim = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var coinsStolen = TryStealCoins(victim, out var coinSummary);
        var itemStolen = TryStealInventoryItem(victim, out var stolenItemName);

        if (!coinsStolen && !itemStolen)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} succeeds at pick pockets but {victim.Name} has nothing to steal."));
            return false;
        }

        var effects = new List<string>();
        if (coinsStolen && !string.IsNullOrWhiteSpace(coinSummary))
            effects.Add(coinSummary!);
        if (itemStolen && !string.IsNullOrWhiteSpace(stolenItemName))
            effects.Add(stolenItemName!);

        events.Add(new CombatEvent($"{monster.DisplayName} steals from {victim.Name}: {string.Join("; ", effects)}."));

        var fleeingGroupId = monster.GroupId;
        session.NoXpMonsterGroupIds.Add(fleeingGroupId);
        var removedCount = session.Monsters.RemoveAll(m => string.Equals(m.GroupId, fleeingGroupId, StringComparison.OrdinalIgnoreCase));

        RuleApplicationInfo.Publish(
            "AD&D",
            "Pick Pockets Outcome",
            $"{monster.DisplayName} theft result",
            "A successful theft causes the encounter group to run away; no XP is awarded for that group.",
            null,
            null,
            null,
            $"Group {fleeingGroupId} fled after stealing: {string.Join(", ", effects)}.");

        events.Add(new CombatEvent($"Group {fleeingGroupId} runs away after the theft and leaves the battle ({removedCount} monster(s) gone)."));
        return true;
    }

    private bool TryResolveMonsterColdRadiation(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Cold Radiation"))
            return false;

        if (!session.CanMonsterUseColdRadiation(monster, session.RoundNumber))
            return false;

        var targets = SelectRandomPartyMembersInOneInchRange(session);
        if (targets.Count == 0)
            return false;

        session.MarkMonsterColdRadiationUsed(monster, session.RoundNumber);
        events.Add(new CombatEvent($"{monster.DisplayName} releases Cold Radiation! All within 1\" are affected ({targets.Count} random party member(s))."));

        foreach (var target in targets)
        {
            var rolledDamage = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
            var appliedDamage = ApplyColdResistanceDamageReduction(target, rolledDamage, isMagicalCold: false, isNormalCold: true);
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - appliedDamage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent(
                appliedDamage <= 0
                    ? $"{target.Name} is unaffected by the cold radiation due to Resist Cold (rolled {rolledDamage})."
                    : $"{target.Name} suffers {actual} cold radiation damage (rolled {rolledDamage}). HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0 && !target.HasStatus(CharacterStatus.Dead))
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by {monster.DisplayName}'s cold radiation!"));
            }
        }

        RuleApplicationInfo.Publish(
            "AD&D",
            "Cold Radiation",
            $"{monster.DisplayName} cold radiation burst",
            "All within 1” suffer 3-18 damage; in this project 1” affects 1d3 random party members. The attack can be used once every other melee round.",
            "3",
            "18",
            null,
            $"Affected {targets.Count} target(s): {string.Join(", ", targets.Select(t => t.Name))}.");

        return true;
    }

    private bool TryStealCoins(Character victim, out string? summary)
    {
        summary = null;

        var currencyGetters = new List<(string Label, Func<Character, int> Get, Action<Character, int> Set)>
        {
            ("gp", c => c.GoldPieces, (c, v) => c.GoldPieces = v),
            ("pp", c => c.PlatinumPieces, (c, v) => c.PlatinumPieces = v),
            ("ep", c => c.ElectrumPieces, (c, v) => c.ElectrumPieces = v),
            ("sp", c => c.SilverPieces, (c, v) => c.SilverPieces = v),
            ("cp", c => c.CopperPieces, (c, v) => c.CopperPieces = v)
        };

        var available = currencyGetters.Where(x => x.Get(victim) > 0).ToList();
        if (available.Count == 0)
            return false;

        var picked = available[_dice.Roll(available.Count) - 1];
        var current = picked.Get(victim);
        var amount = Math.Max(1, _dice.Roll(Math.Max(1, current)));
        amount = Math.Min(amount, current);
        picked.Set(victim, current - amount);
        summary = $"{amount} {picked.Label}";
        return true;
    }

    private bool TryStealInventoryItem(Character victim, out string? itemName)
    {
        itemName = null;
        if (victim.Inventory == null || victim.Inventory.Count == 0)
            return false;

        var idx = _dice.Roll(victim.Inventory.Count) - 1;
        var item = victim.Inventory[idx];
        victim.Inventory.RemoveAt(idx);
        itemName = item.Name;
        return true;
    }

    private static bool TryResolveMonsterLayOnHands(CombatSession session, MonsterInstance caster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(caster, "Lay on Hands"))
            return false;

        if (session.HasMonsterUsedLayOnHands(caster))
            return false;

        var target = session.Monsters
            .Where(m => m.IsAlive && m.CurrentHitPoints < m.MaxHitPoints)
            .OrderBy(m => m.CurrentHitPoints)
            .ThenBy(m => m.MaxHitPoints)
            .FirstOrDefault();

        if (target == null)
            return false;

        var healAmount = Math.Max(0, caster.Template.HitDice) * 2;
        if (healAmount <= 0)
            return false;

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healAmount);
        var healed = target.CurrentHitPoints - before;
        if (healed <= 0)
            return false;

        session.MarkMonsterLayOnHandsUsed(caster);
        events.Add(new CombatEvent($"{caster.DisplayName} uses Lay on Hands on {target.DisplayName} and heals {healed} HP. HP {before}->{target.CurrentHitPoints}."));
        return true;
    }

    private bool TryResolveMonsterBodyHeal(CombatSession session, MonsterInstance monk, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monk, "BodyHeal"))
            return false;

        if (session.HasMonsterUsedBodyHeal(monk))
            return false;

        var monkLevel = Math.Max(0, monk.Template.HitDice);
        if (monkLevel < 7)
            return false;

        if (!monk.IsAlive || monk.CurrentHitPoints >= monk.MaxHitPoints)
            return false;

        var bonus = Math.Max(1, monkLevel - 6);
        var rolled = _dice.Roll(4);
        var healAmount = rolled + bonus;
        var before = monk.CurrentHitPoints;
        monk.CurrentHitPoints = Math.Min(monk.MaxHitPoints, monk.CurrentHitPoints + healAmount);
        var healed = monk.CurrentHitPoints - before;
        if (healed <= 0)
            return false;

        session.MarkMonsterBodyHealUsed(monk);
        events.Add(new CombatEvent(
            $"{monk.DisplayName} uses Body Heal and restores {healed} HP (1d4+{bonus}, rolled {rolled}). HP {before}->{monk.CurrentHitPoints}."));
        return true;
    }

    private bool CanMonsterLocateTarget(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (monster == null || target == null)
            return false;

        if (!target.HasStatus(CharacterStatus.Invisible))
            return true;

        if (!HasSpecialDefense(monster, "Exceptional Senses"))
            return true;

        var roll = _dice.Roll(100);
        var located = roll <= 50;
        if (located)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} uses Exceptional Senses and locates invisible {target.Name} (roll {roll} <= 50)."));
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} uses Exceptional Senses but fails to locate invisible {target.Name} (roll {roll} > 50)."));
        return false;
    }

    private bool TryResolveBlinkDefenseAgainstAttack(MonsterInstance target, List<CombatEvent> events)
    {
        if (!IsBlinkDog(target))
            return false;

        var blinkRoll = _dice.Roll(100);
        if (blinkRoll > 50)
            return false;

        events.Add(new CombatEvent($"{target.DisplayName} blinks aside; 50% displacement causes the hit to miss (roll {blinkRoll} <= 50)."));
        return true;
    }

    private bool TryResolveBlinkDogTeleportBehind(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!IsBlinkDog(monster))
            return false;

        var blinkTriggerRoll = _dice.Roll(12);
        if (blinkTriggerRoll < 7)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} fails blink reposition check (1d12={blinkTriggerRoll}, needs 7+)."));
            return false;
        }

        var locationRoll = _dice.Roll(12);
        var location = locationRoll switch
        {
            1 => "in front of",
            2 => "at the shielded (left) front flank of",
            3 => "at the unshielded (right) front flank of",
            _ => "behind"
        };

        var isBehind = locationRoll >= 4;
        events.Add(new CombatEvent($"{monster.DisplayName} blinks and reappears {location} {target.Name} (location roll {locationRoll})."));

        if (isBehind)
            events.Add(new CombatEvent($"{monster.DisplayName} gains rear-attack benefits this strike (75% behind chance represented by 4-12 on d12)."));

        return isBehind;
    }

    private bool TryResolveBlinkDogPackRetreat(CombatSession session, MonsterInstance actingMonster, List<CombatEvent> events)
    {
        if (!IsBlinkDog(actingMonster))
            return false;

        var allBlinkDogsInGroup = session.Monsters
            .Where(m => string.Equals(m.GroupId, actingMonster.GroupId, StringComparison.OrdinalIgnoreCase) && IsBlinkDog(m))
            .ToList();
        if (allBlinkDogsInGroup.Count == 0)
            return false;

        var aliveBlinkDogsInGroup = allBlinkDogsInGroup.Where(m => m.IsAlive).ToList();
        if (aliveBlinkDogsInGroup.Count == 0)
            return false;

        var losses = allBlinkDogsInGroup.Count(m => !m.IsAlive);
        var lossesPercent = (losses * 100.0) / allBlinkDogsInGroup.Count;
        var seriouslyThreatened = actingMonster.CurrentHitPoints <= Math.Max(1, actingMonster.MaxHitPoints / 2);
        var sustainedQuarterLosses = lossesPercent >= 25.0;

        if (!seriouslyThreatened && !sustainedQuarterLosses)
            return false;

        foreach (var dog in aliveBlinkDogsInGroup)
            dog.CurrentHitPoints = 0;

        var reason = seriouslyThreatened && sustainedQuarterLosses
            ? "serious threat and 25%+ losses"
            : seriouslyThreatened
                ? "serious threat"
                : "25%+ losses";

        events.Add(new CombatEvent($"{actingMonster.Name} pack blinks out due to {reason} and does not return."));
        return true;
    }

    private static bool IsBlinkDog(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        var name = monster.Template.Name?.Trim() ?? string.Empty;
        return string.Equals(name, "Blink Dog", StringComparison.OrdinalIgnoreCase);
    }

    private void TryApplyNauseaFromCarrionStench(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Nausea") || !session.NauseaExposedMonsterGroups.Add(monster.GroupId))
            return;

        var nauseated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        session.NauseatedPartyByMonsterGroup[monster.GroupId] = nauseated;
        foreach (var member in session.ExposedParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                member,
                _savingThrowService.GetSaveTarget(member, SaveThrowType.ParalyzationPoisonDeath));
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);

            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{member.Name} resists {monster.Name} group {monster.GroupId}'s carrion stench nausea (save vs. poison {saveRoll} vs {saveTarget}); no further saves against this group this battle."));
                continue;
            }

            nauseated.Add(member.Name);
            events.Add(new CombatEvent($"{member.Name} is nauseated by {monster.Name} group {monster.GroupId}'s carrion stench (save vs. poison {saveRoll} vs {saveTarget}) and suffers -2 to hit its nausea-causing monsters until battle ends."));
        }
    }
}
