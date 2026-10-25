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
    private bool ResolveLevel2MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var imageCount = _dice.Roll(4);
            var rounds = Math.Max(1, Math.Max(1, monster.Template.HitDice) * 2);
            session.SetMonsterMirrorImage(monster, imageCount, rounds);
            events.Add(new CombatEvent($"{monster.DisplayName} casts Mirror Image on itself. {imageCount} mirror image(s) appear for {rounds} round(s)."));
            return true;
        }

        var spellTargets = GetSpellAffectablePartyTargets(monster, session, events, "Melf's Acid Arrow");
        if (spellTargets.Count == 0)
            return true;

        var target = spellTargets[_dice.Roll(spellTargets.Count) - 1];
        var roundsAcid = _dice.Roll(3);
        session.SetPartyAcidArrow(target.Name, roundsAcid);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Melf's Acid Arrow! {target.Name} is hit by acid for {roundsAcid} round(s)."));
        return true;
    }

    private bool ResolveLevel3IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        // Requested behavior: 50% Improved Phantasmal Force, 50% Mirror Image.
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Improved Phantasmal Force!"));
            ResolveIllusionistPhantasmalForce(monster, session, events);
            return true;
        }

        var imageCount = _dice.Roll(4);
        var rounds = Math.Max(1, Math.Max(1, monster.Template.HitDice) * 2);
        session.SetMonsterMirrorImage(monster, imageCount, rounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Mirror Image on itself. {imageCount} mirror image(s) appear for {rounds} round(s)."));
        return true;
    }

    private bool ResolveLevel3ClericSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50 && damagedAllies.Count > 0)
        {
            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            ApplySwordOfWoundingHealingRestriction(session, target);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Serious Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Glyph of Warding");
        if (aliveParty.Count == 0)
            return true;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = _dice.Roll(4) + _dice.Roll(4); // same glyph damage model used elsewhere
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            partyTarget,
            _savingThrowService.GetSaveTarget(partyTarget, SaveThrowType.Spell));
            var fireSaveBonus = GetFireResistanceSaveBonus(partyTarget);
        if (fireSaveBonus > 0)
            saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var preSaveDamage = ApplyFireProtectionDamageReduction(partyTarget, rolledDamage, 6, isMagicalFire: true, isNormalFire: false);
        var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

        var beforeHp = partyTarget.CurrentHitPoints;
        partyTarget.CurrentHitPoints = Math.Max(0, partyTarget.CurrentHitPoints - applied);
        var actualDamage = beforeHp - partyTarget.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(partyTarget, actualDamage, events);
        TryBurnAwayWebsFromFireDamage(partyTarget, "flame strike", session, events);

        events.Add(new CombatEvent(saveRoll >= saveTarget
            ? $"{monster.DisplayName} casts Glyph of Warding! {partyTarget.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half damage: {actualDamage}. HP {beforeHp}->{partyTarget.CurrentHitPoints}."
            : $"{monster.DisplayName} casts Glyph of Warding! {partyTarget.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actualDamage} damage. HP {beforeHp}->{partyTarget.CurrentHitPoints}."));

        if (partyTarget.CurrentHitPoints <= 0)
        {
            partyTarget.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{partyTarget.Name} is slain by glyph of warding!"));
        }

        return true;
    }

    private bool ResolveLevel5ClericSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50 && damagedAllies.Count > 0)
        {
            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8) + 3;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            ApplySwordOfWoundingHealingRestriction(session, target);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Critical Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Flame Strike");
        if (aliveParty.Count == 0)
            return true;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = 0;
        for (int i = 0; i < 6; i++)
            rolledDamage += _dice.Roll(8);

        rolledDamage = ApplyBroochOfShieldingAgainstMagicMissile(partyTarget, rolledDamage, events);
        if (rolledDamage <= 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName}'s magic missiles are fully absorbed by {partyTarget.Name}'s Brooch of Shielding."));
            return true;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            partyTarget,
            _savingThrowService.GetSaveTarget(partyTarget, SaveThrowType.Spell));
        var fireSaveBonus = GetFireResistanceSaveBonus(partyTarget);
        if (fireSaveBonus > 0)
            saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var preSaveDamage = ApplyFireProtectionDamageReduction(partyTarget, rolledDamage, 6, isMagicalFire: true, isNormalFire: false);
        if (preSaveDamage <= 0)
        {
            events.Add(new CombatEvent($"{partyTarget.Name} is protected from flame strike damage."));
            return true;
        }

        var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

        var beforeHp = partyTarget.CurrentHitPoints;
        partyTarget.CurrentHitPoints = Math.Max(0, partyTarget.CurrentHitPoints - applied);
        var actualDamage = beforeHp - partyTarget.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(partyTarget, actualDamage, events);

        events.Add(new CombatEvent(saveRoll >= saveTarget
            ? $"{monster.DisplayName} casts Flame Strike! {partyTarget.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half damage: {actualDamage}. HP {beforeHp}->{partyTarget.CurrentHitPoints}."
            : $"{monster.DisplayName} casts Flame Strike! {partyTarget.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actualDamage} damage. HP {beforeHp}->{partyTarget.CurrentHitPoints}."));

        if (partyTarget.CurrentHitPoints <= 0)
        {
            partyTarget.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{partyTarget.Name} is slain by flame strike!"));
        }

        return true;
    }

    private bool ResolveLevel2DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);

        var canCastCureLightWounds = damagedAllies.Count > 0;
        var spellRoll = _dice.Roll(100);
        var castBarkskin = !canCastCureLightWounds || spellRoll <= 50;

        if (castBarkskin)
        {
            var barkskinCandidates = session.AliveMonsters
                .Where(m => session.GetMonsterBarkskinRounds(m) <= 0)
                .ToList();

            if (barkskinCandidates.Count == 0)
                barkskinCandidates = session.AliveMonsters.ToList();

            if (barkskinCandidates.Count == 0)
                return false;

            var target = barkskinCandidates[_dice.Roll(barkskinCandidates.Count) - 1];
            var bonus = 1;
            var rounds = 4 + Math.Max(1, monster.Template.HitDice);

            var existingBonus = session.GetMonsterBarkskinBonus(target);
            if (existingBonus > 0)
            {
                target.AdjustArmorClass(existingBonus);
                session.ClearMonsterBarkskin(target);
            }

            target.AdjustArmorClass(-bonus);
            session.SetMonsterBarkskin(target, bonus, rounds);
            events.Add(new CombatEvent($"{monster.DisplayName} casts Barkskin on {target.DisplayName}. AC improves by {bonus} for {rounds} round(s)."));
            return true;
        }

        var cureTarget = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
        var healRoll = _dice.Roll(8);
        var before = cureTarget.CurrentHitPoints;
        cureTarget.CurrentHitPoints = Math.Min(cureTarget.MaxHitPoints, cureTarget.CurrentHitPoints + healRoll);
        ApplySwordOfWoundingHealingRestriction(session, cureTarget);
        var actual = cureTarget.CurrentHitPoints - before;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {cureTarget.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{cureTarget.CurrentHitPoints}."));
        return true;
    }

    private bool ResolveLevel3DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Call Lightning / Summon Insects");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Call Lightning!"));
            var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var rolledDamage = _dice.RollMany(6, 8);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            if (target.LightningProtectionSaveBonusVsLightning > 0)
                saveTarget = Math.Max(1, saveTarget - target.LightningProtectionSaveBonusVsLightning);
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyLightningProtectionDamageReduction(target, rolledDamage, isMagicalLightning: true, isNormalLightning: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from call lightning damage."));
                return true;
            }

            var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);
            TryBurnAwayWebsFromFireDamage(target, "fireball", session, events);

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half lightning damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} lightning damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by call lightning!"));
            }

            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Summon Insects!"));
        var insectTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rounds = Math.Max(1, monster.Template.HitDice);
        insectTarget.AddStatus(CharacterStatus.Paralyzed);
        session.SetPartyAsleep(insectTarget.Name, 0);
        events.Add(new CombatEvent($"{insectTarget.Name} is swarmed by summoned insects and cannot act for {rounds} round(s)."));
        return true;
    }

    private bool ResolveLevel4DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var damagedAllies = GetMonstersNeedingHealing(session);
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50 && damagedAllies.Count > 0)
        {
            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            ApplySwordOfWoundingHealingRestriction(session, target);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Serious Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Cause Serious Wounds");
        if (aliveParty.Count == 0)
            return true;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = _dice.Roll(8) + _dice.Roll(8) + 1;
        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            partyTarget,
            _savingThrowService.GetSaveTarget(partyTarget, SaveThrowType.Spell));
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var applied = saveRoll >= saveTarget ? Math.Max(1, rolledDamage / 2) : rolledDamage;

        var beforeHp = partyTarget.CurrentHitPoints;
        partyTarget.CurrentHitPoints = Math.Max(0, partyTarget.CurrentHitPoints - applied);
        var actualDamage = beforeHp - partyTarget.CurrentHitPoints;
        WakeCharacterIfAsleepAfterDamage(partyTarget, actualDamage, events);

        events.Add(new CombatEvent(saveRoll >= saveTarget
            ? $"{monster.DisplayName} casts Cause Serious Wounds! {partyTarget.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half damage: {actualDamage}. HP {beforeHp}->{partyTarget.CurrentHitPoints}."
            : $"{monster.DisplayName} casts Cause Serious Wounds! {partyTarget.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actualDamage} damage. HP {beforeHp}->{partyTarget.CurrentHitPoints}."));

        if (partyTarget.CurrentHitPoints <= 0)
        {
            partyTarget.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{partyTarget.Name} is slain by cause serious wounds!"));
        }

        return true;
    }

    private bool ResolveLevel5DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Wall of Fire / Insect Plague");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            // Wall of Fire: direct very-hot-fire exposure on one party target.
            var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var rounds = Math.Max(1, monster.Template.HitDice);
            session.SetPartyVeryHotFireExposure(partyTarget.Name, rounds, damagePerRound: 10);
            events.Add(new CombatEvent($"{monster.DisplayName} casts Wall of Fire! {partyTarget.Name} is directly within very hot flames for {rounds} round(s)."));
            return true;
        }

        // Insect Plague: panic one party member for 2-6 rounds (save negates).
        var insectTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var panicRounds = _dice.Roll(5) + 1;
        var panicSaveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            insectTarget,
            _savingThrowService.GetSaveTarget(insectTarget, SaveThrowType.Spell));
        if (session.IsChantActive || session.IsPrayerActive)
            panicSaveTarget = Math.Max(1, panicSaveTarget - 1);

        var panicSaveRoll = _dice.Roll(20);
        if (panicSaveRoll >= panicSaveTarget)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Insect Plague, but {insectTarget.Name} resists (save {panicSaveRoll} vs {panicSaveTarget})."));
            return true;
        }

        insectTarget.AddStatus(CharacterStatus.Confused);
        session.SetPartyConfused(insectTarget.Name, panicRounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Insect Plague! {insectTarget.Name} fails save ({panicSaveRoll} vs {panicSaveTarget}), panics, and cannot attack or cast for {panicRounds} round(s)."));
        return true;
    }

    private bool ResolveLevel5MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Cone of Cold / Feeblemind");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Cone of Cold!"));
            var damagePerTarget = (_dice.Roll(4) + 1) * Math.Max(1, monster.Template.HitDice);

            foreach (var target in aliveParty)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (target.HasActiveResistCold && target.ColdResistanceSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - target.ColdResistanceSaveBonus);
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                var damageAfterResistance = target.HasActiveResistCold
                    ? Math.Max(1, damagePerTarget / 2)
                    : damagePerTarget;
                var applied = saveRoll >= saveTarget
                    ? Math.Max(1, damageAfterResistance / 2)
                    : damageAfterResistance;

                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
                var actual = before - target.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(target, actual, events);

                events.Add(new CombatEvent(saveRoll >= saveTarget
                    ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half cone of cold damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                    : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} cone of cold damage. HP {before}->{target.CurrentHitPoints}."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{target.Name} is frozen to death!"));
                }
            }

            return true;
        }

        var feeblemindTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var feeblemindSaveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            feeblemindTarget,
            _savingThrowService.GetSaveTarget(feeblemindTarget, SaveThrowType.Spell));
        if (session.IsChantActive || session.IsPrayerActive)
            feeblemindSaveTarget = Math.Max(1, feeblemindSaveTarget - 1);
        var feeblemindSaveRoll = _dice.Roll(20);

        if (feeblemindSaveRoll >= feeblemindSaveTarget)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Feeblemind, but {feeblemindTarget.Name} resists (save {feeblemindSaveRoll} vs {feeblemindSaveTarget})."));
            return true;
        }

        feeblemindTarget.AddStatus(CharacterStatus.Feeblemind);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Feeblemind! {feeblemindTarget.Name} fails save ({feeblemindSaveRoll} vs {feeblemindSaveTarget}) and is feebleminded."));
        return true;
    }

    private bool ResolveLevel4MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Ice Storm / Wall of Fire");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Ice Storm!"));

            foreach (var target in aliveParty)
            {
                var rolledDamage = _dice.RollMany(4, 6);
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (target.HasActiveResistCold && target.ColdResistanceSaveBonus > 0)
                    saveTarget = Math.Max(1, saveTarget - target.ColdResistanceSaveBonus);
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                var damageAfterResistance = ApplyColdResistanceDamageReduction(target, rolledDamage, isMagicalCold: true, isNormalCold: false);
                var applied = saveRoll >= saveTarget ? Math.Max(1, damageAfterResistance / 2) : damageAfterResistance;

                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
                var actual = before - target.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(target, actual, events);

                events.Add(new CombatEvent(saveRoll >= saveTarget
                    ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half ice storm damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                    : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} ice storm damage. HP {before}->{target.CurrentHitPoints}."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{target.Name} is slain by ice storm!"));
                }
            }

            return true;
        }

        var wallTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rounds = Math.Max(1, monster.Template.HitDice);
        session.SetPartyAcidArrow(wallTarget.Name, rounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Wall of Fire! {wallTarget.Name} is trapped in flames for {rounds} round(s)."));
        return true;
    }

    private bool ResolveLevel5IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Chaos / Maze");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Chaos!"));

            var casterLevel = Math.Max(1, monster.Template.HitDice);
            var highestPartyLevel = aliveParty.Max(p => Math.Max(1, p.Level));
            var baseCount = _dice.Roll(4) + _dice.Roll(4); // 2-8
            var bonusCount = Math.Max(0, casterLevel - highestPartyLevel);
            var desiredCount = baseCount + bonusCount;

            var candidates = aliveParty
                .Where(p => !p.HasStatus(CharacterStatus.Dead)
                            && !p.HasStatus(CharacterStatus.Ashes)
                            && !p.HasStatus(CharacterStatus.Lost)
                            && p.CurrentHitPoints > 0)
                .ToList();

            if (candidates.Count == 0)
                return true;

            var affectedCount = Math.Min(candidates.Count, Math.Max(0, desiredCount));
            if (affectedCount <= 0)
            {
                events.Add(new CombatEvent("Chaos affects no party members this cast."));
                return true;
            }

            var affected = candidates
                .OrderBy(_ => _dice.Roll(100))
                .Take(affectedCount)
                .ToList();

            var rounds = casterLevel; // 1 round / caster level
            events.Add(new CombatEvent($"Chaos attempts to affect {desiredCount} target(s); {affected.Count} affected for up to {rounds} round(s)."));

            foreach (var target in affected)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Chaos (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                target.AddStatus(CharacterStatus.Confused);
                session.SetPartyConfused(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is confused for {rounds} round(s)."));
            }

            return true;
        }

        var mazeTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var intelligence = Math.Max(1, mazeTarget.Abilities.Intelligence);
        var roundsInMaze = RollMazeDurationRoundsByIntelligenceForParty(intelligence);
        var displayRounds = intelligence <= 5 ? Math.Max(1, roundsInMaze / 10) : roundsInMaze;
        var unitText = intelligence <= 5 ? "turn(s)" : "round(s)";

        mazeTarget.AddStatus(CharacterStatus.Paralyzed);
        session.SetPartyAsleep(mazeTarget.Name, roundsInMaze);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Maze! {mazeTarget.Name} (INT {intelligence}) is trapped for {displayRounds} {unitText}."));
        return true;
    }

    private bool ResolveLevel4IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Confusion!"));
            var spellTargets = GetSpellAffectablePartyTargets(monster, session, events, "Confusion");
            if (spellTargets.Count == 0)
                return true;

            var casterLevel = Math.Max(1, monster.Template.HitDice);
            var affectedCount = Math.Min(spellTargets.Count, Math.Max(1, casterLevel));
            var rounds = casterLevel;

            var targets = spellTargets
                .OrderBy(_ => _dice.Roll(100))
                .Take(affectedCount)
                .ToList();

            foreach (var target in targets)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);

                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Confusion (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                target.AddStatus(CharacterStatus.Confused);
                session.SetPartyConfused(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is confused for {rounds} round(s)."));
            }

            return true;
        }

        var imageCount = _dice.Roll(4) + 1;
        var invisRounds = Math.Max(1, monster.Template.HitDice);
        session.SetMonsterMirrorImage(monster, imageCount, invisRounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Improved Invisibility on itself ({imageCount} mirror image proxy for {invisRounds} round(s))."));
        return true;
    }

    private int RollMazeDurationRoundsByIntelligenceForParty(int intelligence)
    {
        if (intelligence < 3)
        {
            var turns = _dice.Roll(7) + 1; // 2-8 turns
            return turns * 10;
        }

        if (intelligence <= 5)
        {
            var turns = _dice.Roll(4); // 1-4 turns
            return turns * 10;
        }

        if (intelligence <= 8)
            return _dice.Roll(16) + 4; // 5-20 rounds

        if (intelligence <= 11)
            return _dice.Roll(13) + 3; // 4-16 rounds

        if (intelligence <= 14)
            return _dice.Roll(10) + 2; // 3-12 rounds

        if (intelligence <= 17)
            return _dice.Roll(7) + 1; // 2-8 rounds

        return _dice.Roll(4); // 1-4 rounds
    }

    private bool ResolveLevel3MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Slow / Fireball");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var targetCount = Math.Min(Math.Max(1, monster.Template.HitDice), aliveParty.Count);
            var targets = aliveParty
                .OrderBy(_ => _dice.Roll(100))
                .Take(targetCount)
                .ToList();

            var rounds = 3 + Math.Max(1, monster.Template.HitDice);
            var affected = 0;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Slow!"));

            foreach (var target in targets)
            {
                if (target.IsMonkImmuneToDiseaseSlowHaste())
                {
                    events.Add(new CombatEvent($"{target.Name} is immune to Slow effects."));
                    continue;
                }

                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Slow (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                if (!session.IsPartySlowed(target.Name))
                {
                    session.SetPartySlow(target.Name, rounds, target.Move);
                    target.Move = Math.Max(1, target.Move / 2);
                }
                else
                {
                    session.SetPartySlow(target.Name, rounds, session.GetPartySlowOriginalMove(target.Name));
                }

                affected++;
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is slowed for {rounds} round(s)."));
            }

            events.Add(new CombatEvent($"Slow affects up to {Math.Max(1, monster.Template.HitDice)} target(s); {affected} affected."));
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Fireball!"));
        var fireballDamageDice = Math.Min(10, Math.Max(1, monster.Template.HitDice));
        foreach (var target in aliveParty)
        {
            var rolledDamage = _dice.RollMany(6, fireballDamageDice);
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var fireSaveBonus = GetFireResistanceSaveBonus(target);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(target, rolledDamage, fireballDamageDice, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{target.Name} is protected from fireball damage."));
                continue;
            }

            var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - applied);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"{target.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half fireball damage: {actual}. HP {before}->{target.CurrentHitPoints}."
                : $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} fireball damage. HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain by the fireball!"));
            }
        }

        return true;
    }

    private bool ResolveLevel2IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Improved Phantasmal Force!"));
            ResolveIllusionistPhantasmalForce(monster, session, events);
            return true;
        }

        var imageCount = _dice.Roll(4);
        var rounds = Math.Max(1, Math.Max(1, monster.Template.HitDice) * 2);
        session.SetMonsterMirrorImage(monster, imageCount, rounds);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Mirror Image on itself. {imageCount} mirror image(s) appear for {rounds} round(s)."));
        return true;
    }

    private void ResolveLevel1MageSpells(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Magic Missile / Sleep");
        if (aliveParty.Count == 0)
            return;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
            var damage = _dice.Roll(4) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent($"{monster.DisplayName} casts Magic Missile! {target.Name} takes {actual} damage (rolled {damage})."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }

            return;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Sleep!"));
        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);
            var saveRoll = _dice.Roll(20);

            if (saveRoll >= saveTarget)
            {
                var saveMessage = $"{target.Name} resists Sleep (save {saveRoll} vs {saveTarget}).";
                events.Add(new CombatEvent(saveMessage));
                RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", saveMessage);
                continue;
            }

            var rounds = _dice.Roll(3) + 1; // 2-4 rounds
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            var failedSaveMessage = $"{target.Name} fails save ({saveRoll} vs {saveTarget}) and falls asleep for {rounds} round(s).";
            events.Add(new CombatEvent(failedSaveMessage));
            RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", failedSaveMessage);
        }
    }

    private static List<MonsterInstance> GetMonstersNeedingHealing(CombatSession session)
    {
        return session.AliveMonsters
            .Where(m => m.CurrentHitPoints < Math.Max(0, m.MaxHitPoints - session.GetMonsterWoundingUnhealableDamage(m)))
            .ToList();
    }

    private bool ResolveLevel1PriestSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (session.Level1PriestSpellCastsUsed >= 3)
            return false;

        var damagedAllies = GetMonstersNeedingHealing(session);

        if (damagedAllies.Count == 0)
            return false;

        var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
        var healRoll = _dice.Roll(8);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
        ApplySwordOfWoundingHealingRestriction(session, target);
        var actual = target.CurrentHitPoints - before;

        session.Level1PriestSpellCastsUsed += 1;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
        return true;
    }

    private enum MonsterSpellTradition
    {
        MagicUser,
        Cleric,
        Druid,
        Illusionist
    }

    private sealed record MonsterLevelMagicUseChance(int ChancePercent, int SpellLevel, bool IsAgeRelatedDragonMagic);
    private sealed record MonsterLevelMagicUseDecision(bool ShouldUseMagic, int Roll, int ChancePercent, MonsterSpellTradition? SelectedTradition);

    private bool CanMonsterAttemptLevelSpell(
        MonsterInstance monster,
        int spellLevel,
        MonsterSpellTradition requestedTradition,
        List<CombatEvent> events,
        Dictionary<int, MonsterLevelMagicUseDecision> decisionCache,
        bool useDefaultFiftyPercentWhenNoConfiguredChance)
    {
        if (monster?.Template == null)
            return false;

        if (!MonsterHasSpellTraditionAtLevel(monster, spellLevel, requestedTradition))
            return false;

        if (decisionCache.TryGetValue(spellLevel, out var cachedDecision))
        {
            if (!cachedDecision.ShouldUseMagic)
                return false;

            return !cachedDecision.SelectedTradition.HasValue || cachedDecision.SelectedTradition.Value == requestedTradition;
        }

        var configuredChance = TryGetMonsterLevelMagicUseChance(monster, spellLevel);
        if (configuredChance == null && !useDefaultFiftyPercentWhenNoConfiguredChance)
            return true;

        var chancePercent = configuredChance?.ChancePercent ?? 50;
        var isAgeRelatedDragonMagic = configuredChance?.IsAgeRelatedDragonMagic == true;
        var roll = _dice.Roll(100);
        var shouldUseMagic = roll <= chancePercent;

        MonsterSpellTradition? selectedTradition = null;
        if (shouldUseMagic)
            selectedTradition = SelectSpellTraditionForLevel(monster, spellLevel);

        decisionCache[spellLevel] = new MonsterLevelMagicUseDecision(shouldUseMagic, roll, chancePercent, selectedTradition);

        var traditionText = selectedTradition.HasValue
            ? $" Will use {FormatTraditionName(selectedTradition.Value)} level {spellLevel} spells."
            : string.Empty;

        var sourceText = isAgeRelatedDragonMagic
            ? " Age-related dragon magic"
            : " Level magic use";

        events.Add(new CombatEvent(
            $"{monster.DisplayName}{sourceText}: chance {chancePercent}%, roll {roll}, outcome {(shouldUseMagic ? "use magic" : "no magic")}. Dragon can use level {spellLevel} magic.{traditionText}"));

        if (!shouldUseMagic)
            return false;

        return !selectedTradition.HasValue || selectedTradition.Value == requestedTradition;
    }

    private static bool MonsterHasSpellTraditionAtLevel(MonsterInstance monster, int spellLevel, MonsterSpellTradition tradition)
    {
        return HasSpellTierAbility(monster, spellLevel, tradition);
    }

    private MonsterSpellTradition? SelectSpellTraditionForLevel(MonsterInstance monster, int spellLevel)
    {
        var availableTraditions = new List<MonsterSpellTradition>();

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.MagicUser))
            availableTraditions.Add(MonsterSpellTradition.MagicUser);

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.Cleric))
            availableTraditions.Add(MonsterSpellTradition.Cleric);

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.Druid))
            availableTraditions.Add(MonsterSpellTradition.Druid);

        if (HasSpellTierAbility(monster, spellLevel, MonsterSpellTradition.Illusionist))
            availableTraditions.Add(MonsterSpellTradition.Illusionist);

        if (availableTraditions.Count == 0)
            return null;

        var picked = _dice.Roll(availableTraditions.Count) - 1;
        return availableTraditions[picked];
    }

    private static bool HasAnyMonsterSpellcastingAbility(MonsterInstance monster)
    {
        for (var level = 1; level <= 9; level++)
        {
            if (HasSpellTierAbility(monster, level, MonsterSpellTradition.MagicUser)
                || HasSpellTierAbility(monster, level, MonsterSpellTradition.Cleric)
                || HasSpellTierAbility(monster, level, MonsterSpellTradition.Druid)
                || HasSpellTierAbility(monster, level, MonsterSpellTradition.Illusionist))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSpellTierAbility(MonsterInstance monster, int spellLevel, MonsterSpellTradition tradition)
    {
        if (spellLevel <= 0)
            return false;

        return tradition switch
        {
            MonsterSpellTradition.MagicUser => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Mage spells",
                $"Level {spellLevel} Magic-User spells",
                $"Lvl {spellLevel} Mage spells",
                $"Lvl {spellLevel} Magic-User spells"),
            MonsterSpellTradition.Cleric => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Priest spells",
                $"Level {spellLevel} Cleric spells",
                $"Lvl {spellLevel} Priest spells",
                $"Lvl {spellLevel} Cleric spells"),
            MonsterSpellTradition.Druid => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Druid spells",
                $"Lvl {spellLevel} Druid spells"),
            MonsterSpellTradition.Illusionist => HasAnySpecialAbility(
                monster,
                $"Level {spellLevel} Illusionist spells",
                $"Lvl {spellLevel} Illusionist spells"),
            _ => false
        };
    }

    private static string FormatTraditionName(MonsterSpellTradition tradition)
    {
        return tradition switch
        {
            MonsterSpellTradition.MagicUser => "Magic-User",
            MonsterSpellTradition.Cleric => "Cleric",
            MonsterSpellTradition.Druid => "Druid",
            MonsterSpellTradition.Illusionist => "Illusionist",
            _ => "Unknown"
        };
    }

    private static MonsterLevelMagicUseChance? TryGetMonsterLevelMagicUseChance(MonsterInstance monster, int spellLevel)
    {
        if (monster?.Template?.SpecialAbilities == null || monster.Template.SpecialAbilities.Count == 0)
            return null;

        foreach (var ability in monster.Template.SpecialAbilities)
        {
            if (ability == null || string.IsNullOrWhiteSpace(ability.Name))
                continue;

            if (!TryParseMonsterLevelMagicUseAbility(monster, ability.Name, ability.Description, out var parsed))
                continue;

            if (parsed.SpellLevel == spellLevel)
                return parsed;
        }

        return null;
    }

    private static bool TryParseMonsterLevelMagicUseAbility(MonsterInstance monster, string abilityName, string? abilityDescription, out MonsterLevelMagicUseChance parsed)
    {
        var trimmed = abilityName?.Trim() ?? string.Empty;
        var match = Regex.Match(
            trimmed,
            @"^(?<chance>\d{1,3})\s*%\s*Level\s*(?<level>\d+)\s*Magic\s*Use$",
            RegexOptions.IgnoreCase);

        if (match.Success
            && int.TryParse(match.Groups["chance"].Value, out var chance)
            && int.TryParse(match.Groups["level"].Value, out var level)
            && level > 0)
        {
            var isAgeRelated = !string.IsNullOrWhiteSpace(abilityDescription)
                && abilityDescription.Contains("dragon age-based", StringComparison.OrdinalIgnoreCase);
            parsed = new MonsterLevelMagicUseChance(Math.Clamp(chance, 0, 100), level, isAgeRelated);
            return true;
        }

        var ageRelatedMatch = Regex.Match(
            trimmed,
            @"^(?<chance>\d{1,3})\s*%\s*Level\s*Age\s*related\s*Magic\s*Use\s*every\s*(?<step>\d+(?:[.,]\d+)?)\s*agelevels?$",
            RegexOptions.IgnoreCase);

        if (ageRelatedMatch.Success
            && int.TryParse(ageRelatedMatch.Groups["chance"].Value, out var ageChance)
            && TryParseAgeRelatedMagicUseStep(ageRelatedMatch.Groups["step"].Value, out var step))
        {
            var ageLevel = ResolveMonsterAgeLevel(monster);
            var spellLevel = Math.Max(1, (int)Math.Floor((ageLevel - 1d) / step) + 1);
            parsed = new MonsterLevelMagicUseChance(Math.Clamp(ageChance, 0, 100), spellLevel, true);
            return true;
        }

        parsed = null!;
        return false;
    }

    private static bool TryParseAgeRelatedMagicUseStep(string raw, out double step)
    {
        step = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out step))
            return false;

        return step > 0;
    }

    private static int ResolveMonsterAgeLevel(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return 1;

        var ageFromName = TryResolveAgeLevelFromText(monster.Template.Name);
        if (ageFromName.HasValue)
            return ageFromName.Value;

        if (monster.Template.Type == MonsterType.Dragon
            && monster.Template.HitDice > 0
            && monster.MaxHitPoints > 0)
        {
            var hpPerDie = monster.MaxHitPoints / (double)monster.Template.HitDice;
            return Math.Clamp((int)Math.Round(hpPerDie, MidpointRounding.AwayFromZero), 1, 8);
        }

        return 1;
    }

    private static int? TryResolveAgeLevelFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        if (normalized.Contains("Very young", StringComparison.OrdinalIgnoreCase)) return 1;
        if (normalized.Contains("Young adult", StringComparison.OrdinalIgnoreCase)) return 4;
        if (normalized.Contains("Sub-adult", StringComparison.OrdinalIgnoreCase)) return 3;
        if (normalized.Contains("Very old", StringComparison.OrdinalIgnoreCase)) return 7;
        if (normalized.Contains("Ancient", StringComparison.OrdinalIgnoreCase)) return 8;
        if (normalized.Contains("Young", StringComparison.OrdinalIgnoreCase)) return 2;
        if (normalized.Contains("Adult", StringComparison.OrdinalIgnoreCase)) return 5;
        if (normalized.Contains("Old", StringComparison.OrdinalIgnoreCase)) return 6;

        return null;
    }

    private bool ResolveLevel1DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Entangle / Faerie Fire");
        if (aliveParty.Count == 0)
            return true;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Entangle!"));
            foreach (var target in aliveParty)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
                if (session.IsChantActive || session.IsPrayerActive)
                    saveTarget = Math.Max(1, saveTarget - 1);
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Entangle (save {saveRoll} vs {saveTarget})."));
                    continue;
                }

                var rounds = 5;
                target.AddStatus(CharacterStatus.Paralyzed);
                session.SetPartyAsleep(target.Name, 0);
                events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is entangled for {rounds} round(s)."));
            }

            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Faerie Fire!"));
        var affected = 0;
        foreach (var target in aliveParty)
        {
            if (session.FaerieFiredPartyMembers.Contains(target.Name))
                continue;

            target.ArmorClass += 2;
            session.FaerieFiredPartyMembers.Add(target.Name);
            affected++;
            events.Add(new CombatEvent($"{target.Name} is outlined by faerie fire: AC worsens by 2."));
        }

        if (affected == 0)
            events.Add(new CombatEvent("No additional party members are affected by faerie fire."));

        return true;
    }

    private bool ResolveLevel1IllusionistSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Color Spray!"));
            ResolveIllusionistColorSpray(monster, session, events);
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} casts Phantasmal Force!"));
        ResolveIllusionistPhantasmalForce(monster, session, events);
        return true;
    }

    private void ResolveIllusionistColorSpray(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Color Spray");
        if (aliveParty.Count == 0)
            return;

        var maxAffected = Math.Min(aliveParty.Count, _dice.Roll(6));
        var targets = aliveParty.OrderBy(_ => _dice.Roll(100)).Take(maxAffected).ToList();
        var casterLevel = Math.Max(1, monster.Template.HitDice);

        foreach (var target in targets)
        {
            var hdDifference = Math.Max(1, target.Level) - casterLevel;
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);

            if (hdDifference <= 0)
            {
                var rounds = _dice.Roll(4) + _dice.Roll(4);
                target.AddStatus(CharacterStatus.Asleep);
                session.SetPartyAsleep(target.Name, rounds);
                events.Add(new CombatEvent($"{target.Name} is overwhelmed by colors and falls unconscious for {rounds} round(s)."));
            }
            else if (hdDifference <= 2)
            {
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Color Spray (save {saveRoll} vs {saveTarget})."));
                }
                else
                {
                    var rounds = _dice.Roll(4);
                    target.AddStatus(CharacterStatus.Asleep);
                    session.SetPartyAsleep(target.Name, rounds);
                    events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is unconscious for {rounds} round(s)."));
                }
            }
            else
            {
                if (saveRoll >= saveTarget)
                {
                    events.Add(new CombatEvent($"{target.Name} resists Color Spray (save {saveRoll} vs {saveTarget})."));
                }
                else
                {
                    if (target.IsMonkImmuneToDiseaseSlowHaste())
                    {
                        events.Add(new CombatEvent($"{target.Name} is immune to Slow effects."));
                        continue;
                    }

                    target.AddStatus(CharacterStatus.Slowed);
                    events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and is stunned by the spray."));
                }
            }
        }
    }

    private void ResolveIllusionistPhantasmalForce(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = GetSpellAffectablePartyTargets(monster, session, events, "Phantasmal Force");
        if (aliveParty.Count == 0)
            return;

        foreach (var target in aliveParty)
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                target,
                _savingThrowService.GetSaveTarget(target, SaveThrowType.Spell));
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name} disbelieves the phantasmal force (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rolledDamage = 0;
            for (int i = 0; i < 16; i++)
                rolledDamage += _dice.Roll(6);

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - rolledDamage);
            var actual = Math.Max(0, before - target.CurrentHitPoints);
            WakeCharacterIfAsleepAfterDamage(target, actual, events);

            events.Add(new CombatEvent($"{target.Name} believes the phantasmal force and takes {actual} damage (rolled {rolledDamage}). HP {before}->{target.CurrentHitPoints}."));

            if (target.CurrentHitPoints <= 0)
            {
                target.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{target.Name} dies from terror and shock."));
            }
        }
    }

    private static List<Character> GetSpellAffectablePartyTargets(
        MonsterInstance monster,
        CombatSession session,
        List<CombatEvent> events,
        string spellName)
    {
        var aliveParty = session.ExposedParty.ToList();
        if (aliveParty.Count == 0)
            return aliveParty;

        var blockedTargets = aliveParty
            .Where(member => member.HasActiveProtectionFromMagicScroll)
            .ToList();

        foreach (var blocked in blockedTargets)
            events.Add(new CombatEvent($"{blocked.Name} is inside an anti-magic globe; {spellName} cannot affect them."));

        var affectable = aliveParty
            .Where(member => !member.HasActiveProtectionFromMagicScroll)
            .ToList();

        if (affectable.Count == 0 && blockedTargets.Count > 0)
            events.Add(new CombatEvent($"{monster.DisplayName}'s {spellName} is blocked: anti-magic protection prevents magic from entering the sphere."));

        return affectable;
    }
}
