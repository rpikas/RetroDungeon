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
    private bool TryResolveMonsterAssassination(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (session.RoundNumber != 1 || !session.PartySurprisedRound1)
            return false;

        var isAssassinByName = monster.Template.Name?.IndexOf("assassin", StringComparison.OrdinalIgnoreCase) >= 0;
        var isAssassinByAbility = HasAnySpecialAbility(monster, "Assassination", "Assassin", "Assassin Attack");
        if (!isAssassinByName && !isAssassinByAbility)
            return false;

        var target = SelectMonsterTarget(session);
        if (target == null)
            return false;

        var assassination = new AssassinationService("Data/Assassination");
        var assassinLevel = Math.Max(1, monster.Template.HitDice);
        var victimLevel = Math.Max(1, target.Level);
        var assassinationRoll = assassination.RollAssassination(assassinLevel, victimLevel, _rng);

        RuleApplicationInfo.Publish(
            "DMG",
            "75Assassin",
            $"{monster.DisplayName} assassination attempt vs {target.Name}",
            "Use assassin level vs victim level/hit dice table; roll 1d100 and succeed if roll is less than or equal to chance.",
            "1",
            "100",
            assassinationRoll.Roll.ToString(),
            $"Assassin level {assassinLevel}, target level {victimLevel}, chance {assassinationRoll.ChancePercent}% => {(assassinationRoll.Success ? "success" : "failure")}.");

        events.Add(new CombatEvent(
            $"{monster.DisplayName} attempts to assassinate {target.Name}: chance {assassinationRoll.ChancePercent}%, rolled {assassinationRoll.Roll} (1d100) => {(assassinationRoll.Success ? "success" : "failure")}."));

        if (!assassinationRoll.Success)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} attempts to assassinate {target.Name} but fails."));
            return true;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{monster.DisplayName} assassinates {target.Name} instantly!"));
        return true;
    }

    private void TryApplyMonsterParalyzation(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath);
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Monster Paralyzation",
            $"{monster.DisplayName} paralyzation attack on {target.Name}",
            "Target rolls saving throw vs Paralyzation/Poison/Death. On failed save, paralysis duration is 30-120 rounds (until dungeon exit if still active).",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save." : "Successful save.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists paralysis (save {saveRoll} vs {saveTarget})."));
            return;
        }

        if (target.HasStatus(CharacterStatus.Paralyzed))
        {
            events.Add(new CombatEvent($"{target.Name} is already paralyzed by {monster.DisplayName}."));
            return;
        }

        var rounds = _dice.Roll(91) + 29;
        target.ApplyParalysis(rounds);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Paralyzation Duration",
            $"{target.Name} paralysis duration",
            "Roll determines paralysis duration in rounds (30-120). Every combat round and each dungeon step counts as one round.",
            "30",
            "120",
            rounds.ToString(),
            "Character is paralyzed until duration expires or they leave the dungeon.");

        events.Add(new CombatEvent($"{target.Name} is paralyzed by {monster.DisplayName} for {rounds} round(s)! (save {saveRoll} vs {saveTarget})"));
    }

    private static int GetWebEscapeRoundsFromStrength(Character target)
    {
        var strength = Math.Clamp(target.Abilities.Strength, 1, 18);
        return Math.Clamp(19 - strength, 1, 18);
    }

    private static void TryBurnAwayWebsFromFireDamage(Character target, string source, CombatSession session, List<CombatEvent> events)
    {
        if (!target.HasStatus(CharacterStatus.Webbed))
            return;

        target.RemoveStatus(CharacterStatus.Webbed);
        session.SetPartyWebbed(target.Name, 0);
        events.Add(new CombatEvent($"{target.Name}'s webs burn away from {source}."));
    }

    private static void TryApplyWebsSpecialAttack(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Webs"))
            return;

        if (target.HasStatus(CharacterStatus.Webbed))
            return;

        var rounds = GetWebEscapeRoundsFromStrength(target);
        target.AddStatus(CharacterStatus.Webbed);
        session.SetPartyWebbed(target.Name, rounds);

        events.Add(new CombatEvent($"{monster.DisplayName} entangles {target.Name} in sticky webs. STR {Math.Clamp(target.Abilities.Strength, 1, 18)} breaks free in {rounds} round(s)."));
    }

    private void TryApplyPetrificationFromSpecialAttacks(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Petrifying Gaze", "Petrification Touch", "Stoning"))
            return;

        if (target.HasStatus(CharacterStatus.Petrified))
            return;

        var saveTarget = _savingThrowService.GetSaveTarget(target, SaveThrowType.PetrificationPolymorph);
        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        var attackName = HasAnySpecialAbility(monster, "Petrification Touch", "Stoning") ? "Petrification Touch" : "Petrifying Gaze";

        RuleApplicationInfo.Publish(
            "AD&D",
            "Petrification",
            $"{monster.DisplayName} {attackName} on {target.Name}",
            "Target rolls saving throw vs Petrification/Polymorph. On failed save, target turns to stone until cured.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save." : "Successful save.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists petrification (save {saveRoll} vs {saveTarget})."));
            return;
        }

        target.AddStatus(CharacterStatus.Petrified);
        events.Add(new CombatEvent($"{target.Name} is petrified by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})"));
    }

    private bool TryApplyPoisonSpit(MonsterInstance monster, Character target, List<CombatEvent> events, bool chantActiveAtRoundStart, bool prayerActiveAtRoundStart)
    {
        if (!HasSpecialAbility(monster, "Poison spit"))
            return false;

        if (!IsAlive(target))
            return false;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to poison spit."));
            return false;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
            saveTarget = Math.Max(1, saveTarget - 1);

        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        var saveMessage = $"{monster.DisplayName} poison spit vs {target.Name}: save {saveRoll} vs {saveTarget} => {(failedSave ? "failed" : "succeeded")}.";
        RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", saveMessage);

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s poison spit (save {saveRoll} vs {saveTarget})."));
            return false;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{target.Name} is killed by {monster.DisplayName}'s poison spit! (save {saveRoll} vs {saveTarget})"));
        return true;
    }

    private bool TryApplyConstrictionDamage(CombatSession session, Character member, List<CombatEvent> events)
    {
        var effects = session.GetConstrictionsForVictim(member.Name);
        if (effects.Count == 0)
            return false;

        var strongHelpers = session.ExposedParty
            .Where(IsAlive)
            .Where(c => !string.Equals(c.Name, member.Name, StringComparison.OrdinalIgnoreCase))
            .Count(c => c.Abilities.Strength >= 16);

        foreach (var effect in effects.ToList())
        {
            if (!effect.Constrictor.IsAlive || effect.Constrictor.HasFled)
            {
                session.RemoveConstriction(effect.Constrictor, effect.Victim);
                events.Add(new CombatEvent($"{member.Name} is freed as {effect.Constrictor.DisplayName} can no longer maintain constriction."));
                continue;
            }

            var rolledDamage = _dice.Roll(4) + _dice.Roll(4);
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - rolledDamage);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);
            events.Add(new CombatEvent($"{effect.Constrictor.DisplayName} constricts {member.Name} for {actual} damage (2d4). HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} is crushed to death by constriction!"));
                return true;
            }

            if (strongHelpers >= 4)
            {
                effect.UncoilRoundsRemaining ??= _dice.Roll(4) + 1;
                effect.UncoilRoundsRemaining = Math.Max(0, effect.UncoilRoundsRemaining.Value - 1);
                var remaining = effect.UncoilRoundsRemaining.Value;
                if (remaining <= 0)
                {
                    session.RemoveConstriction(effect.Constrictor, effect.Victim);
                    events.Add(new CombatEvent($"{member.Name} is uncoiled by strong allies after sustained effort."));
                }
                else
                {
                    events.Add(new CombatEvent($"Strong allies are uncoiling {effect.Constrictor.DisplayName}; {remaining} round(s) remain."));
                }
            }
        }

        return false;
    }

    private void TryApplyConstrictionOnHit(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Constriction"))
            return;

        if (!IsAlive(target))
            return;

        if (session.HasActiveConstriction(monster, target))
            return;

        session.StartConstriction(monster, target);
        events.Add(new CombatEvent($"{monster.DisplayName} wraps around {target.Name} and begins constriction (2-8 damage each melee round)."));
        RuleApplicationInfo.Publish(
            "AD&D",
            "Constriction",
            $"{monster.DisplayName} constricts {target.Name}",
            "Constriction inflicts 2-8 damage per melee round. If at least four strong creatures (e.g., Str 16+) grasp head and tail, they can uncoil it in 2-5 melee rounds.",
            "2",
            "8",
            null,
            $"{target.Name} is now constricted.");
    }

    private bool TryApplyPoisonSpores(MonsterInstance monster, Character target, List<CombatEvent> events, bool chantActiveAtRoundStart, bool prayerActiveAtRoundStart)
    {
        if (!HasSpecialAbility(monster, "Poison Spores"))
            return false;

        if (!IsAlive(target))
            return false;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to poison spores."));
            return false;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
            saveTarget = Math.Max(1, saveTarget - 1);

        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Poison Spores",
            $"{monster.DisplayName} poison spores vs {target.Name}",
            "Victim must save vs poison or die. Victims killed by poison spores require Cure Disease and Resurrection/Raise Dead within 24 hours.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save." : "Successful save.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s poison spores (save {saveRoll} vs {saveTarget})."));
            return false;
        }

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        target.ApplyPoisonSporesDeathCurse(daysUntilRaiseImpossible: 1);
        events.Add(new CombatEvent($"{target.Name} is killed by {monster.DisplayName}'s poison spores! Cure Disease and Resurrection/Raise Dead must both be completed within 24 hours. (save {saveRoll} vs {saveTarget})"));
        return true;
    }

    private void TryApplySalamanderHeat(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Heat"))
            return;

        var rolled = _dice.Roll(6);
        var damage = ApplyFireProtectionDamageReduction(target, rolled, 1, isMagicalFire: false, isNormalFire: true);
        RuleApplicationInfo.Publish(
            "AD&D",
            "Salamander Heat",
            $"{monster.DisplayName} heat on {target.Name}",
            "Each hit adds 1-6 points of heat damage. Fire resistant creatures suffer no heat damage.",
            "1", "6", rolled.ToString(),
            damage > 0 ? $"{damage} heat damage." : "Protected from heat.");

        if (damage <= 0)
        {
            events.Add(new CombatEvent($"{target.Name} resists {monster.DisplayName}'s heat."));
            return;
        }

        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        events.Add(new CombatEvent($"{monster.DisplayName}'s heat scorches {target.Name} for {before - target.CurrentHitPoints} additional damage. HP {before}->{target.CurrentHitPoints}."));
        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }
    }

    private bool TryApplyGiantSnakePoison(MonsterInstance monster, Character target, List<CombatEvent> events, bool chantActiveAtRoundStart, bool prayerActiveAtRoundStart)
    {
        if (!HasSpecialAbility(monster, "Giant Poisonous Snake Poison"))
            return false;

        if (target.IsMonkImmuneToPoison())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to poison."));
            return false;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
            saveTarget = Math.Max(1, saveTarget - 1);
        var saveRoll = _dice.Roll(20);
        var saved = saveRoll >= saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Giant Poisonous Snake Poison",
            $"{monster.DisplayName} bite on {target.Name}",
            "Victim must save vs. poison or die; even on a successful save the venom does 3-18 points of damage.",
            "1", "20", saveRoll.ToString(),
            $"Save target {saveTarget}. {(saved ? "Saved; takes 3d6 damage." : "Failed save; dies.")}");

        if (!saved)
        {
            target.CurrentHitPoints = 0;
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and dies from {monster.DisplayName}'s venom!"));
            return true;
        }

        var rolled = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - rolled);
        WakeCharacterIfAsleepAfterDamage(target, before - target.CurrentHitPoints, events);
        events.Add(new CombatEvent($"{target.Name} saves ({saveRoll} vs {saveTarget}) but takes {before - target.CurrentHitPoints} venom damage (3d6 = {rolled}). HP {before}->{target.CurrentHitPoints}."));
        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain!"));
        }

        return true;
    }

    private void TryApplyOtyughDisease(MonsterInstance monster, Character target, string attackName, List<CombatEvent> events)
    {
        var isNeo = string.Equals(monster.Template.Name, "Neo-Otyugh", StringComparison.OrdinalIgnoreCase);
        var isOtyugh = string.Equals(monster.Template.Name, "Otyugh", StringComparison.OrdinalIgnoreCase);
        if (!(isOtyugh && string.Equals(attackName, "Bite", StringComparison.OrdinalIgnoreCase))
            && !(isNeo && string.Equals(attackName, "Tentacle", StringComparison.OrdinalIgnoreCase)))
            return;

        var roll = _dice.Roll(100);
        RuleApplicationInfo.Publish(
            "AD&D",
            "Otyugh Disease",
            $"{monster.DisplayName} bite on {target.Name}",
            "A creature bitten by an otyugh has a 90% chance of being infected with disease (typhus).",
            "1",
            "100",
            roll.ToString(),
            roll <= 90 ? "Infected." : "No infection.");

        if (roll > 90)
            return;

        if (target.HasStatus(CharacterStatus.Diseased))
        {
            events.Add(new CombatEvent($"{target.Name} is already diseased (otyugh bite, roll {roll})."));
            return;
        }

        target.ApplyDisease();
        events.Add(new CombatEvent($"{target.Name} is infected with typhus by {monster.DisplayName}'s bite! (roll {roll}, needs 1-90)"));
    }

    private void TryApplyGiantRatDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        var isGiantRat = string.Equals(monster.Template.Name, "Giant Rat", StringComparison.OrdinalIgnoreCase);
        if (!isGiantRat && !HasAnySpecialAbility(monster, "Giant Rat Disease"))
            return;

        var diseaseRoll = _dice.Roll(100);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Giant Rat Disease",
            $"{monster.DisplayName} disease check after hit on {target.Name}",
            "Roll 1d100. Only 1-5 causes disease (5% chance).",
            "1",
            "100",
            diseaseRoll.ToString(),
            diseaseRoll <= 5
                ? "Result is within 1-5, disease is contracted."
                : "Result is outside 1-5, no disease.");

        if (diseaseRoll <= 5)
        {
            if (!target.HasStatus(CharacterStatus.Diseased))
            {
                target.ApplyDisease();
                events.Add(new CombatEvent($"{target.Name} is diseased by {monster.DisplayName}! (1d100 roll: {diseaseRoll}, needs 1-5)"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} carries Giant Rat Disease, but {target.Name} is already diseased. (1d100 roll: {diseaseRoll}, needs 1-5)"));
            }

            return;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} carries Giant Rat Disease. Infection roll: {diseaseRoll} on 1d100 (needs 1-5)."));
    }

    private static void TryApplyRotGrubExposure(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!string.Equals(monster.Template.Name, "Rot Grub", StringComparison.OrdinalIgnoreCase))
            return;

        if (target.RotGrubFlamePromptPending || target.RotGrubDeathRoundsRemaining > 0)
            return;

        target.MarkRotGrubExposurePendingFlame();
        events.Add(new CombatEvent($"ROT_GRUB_PROMPT::{target.Name}"));
    }

    private static void TryApplyEarSeekerDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!string.Equals(monster.Template.Name, "Ear Seeker", StringComparison.OrdinalIgnoreCase))
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to disease from {monster.DisplayName}."));
            return;
        }

        if (target.EarSeekerDeathOnNextDungeonEntry)
            return;

        target.ApplyEarSeekerDisease();
        events.Add(new CombatEvent($"{target.Name} contracts Ear Seeker disease from {monster.DisplayName}! They will die upon next dungeon entry unless cured."));
    }

    private static void TryApplyInfestation(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Infestation"))
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to infestation disease from {monster.DisplayName}."));
            return;
        }

        if (target.InfestationDeathDaysRemaining > 0)
            return;

        target.ApplyInfestationDisease();
        events.Add(new CombatEvent($"{target.Name} suffers infestation by {monster.DisplayName}: rhizomes penetrate flesh. Cure Disease is required within 24 hours or death will occur."));
    }

    private void TryApplyTickDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Tick Disease"))
            return;

        var diseaseRoll = _dice.Roll(100);
        RuleApplicationInfo.Publish(
            "AD&D",
            "Tick Disease",
            $"{monster.DisplayName} tick disease check after hit on {target.Name}",
            "Roll 1d100. Result 1-50 means fatal tick disease is contracted.",
            "1",
            "100",
            diseaseRoll.ToString(),
            diseaseRoll <= 50
                ? "Result is within 1-50, tick disease is contracted."
                : "Result is outside 1-50, no tick disease.");

        if (diseaseRoll > 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} carries Tick Disease. Infection roll: {diseaseRoll} on 1d100 (needs 1-50)."));
            return;
        }

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to disease from {monster.DisplayName}. (Tick Disease roll: {diseaseRoll} on 1d100, needs 1-50)"));
            return;
        }

        if (target.TickDiseaseDeathDaysRemaining > 0)
        {
            events.Add(new CombatEvent($"{target.Name} is hit by {monster.DisplayName}, but Tick Disease is already active ({target.TickDiseaseDeathDaysRemaining} day(s) until death unless cured)."));
            return;
        }

        var deathInDays = _dice.Roll(7) + 1;
        target.ApplyTickDisease(deathInDays);
        events.Add(new CombatEvent($"{target.Name} contracts Tick Disease from {monster.DisplayName}! Fatal disease will kill in {deathInDays} day(s) unless cured (infection roll: {diseaseRoll} on 1d100, needs 1-50)."));
    }

    private void TryApplyRottingDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Rotting Disease"))
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to disease from {monster.DisplayName}."));
            return;
        }

        if (target.RottingDiseaseMonthsRemaining > 0)
        {
            events.Add(new CombatEvent($"{target.Name} is hit by {monster.DisplayName}, but Rotting Disease is already active ({target.RottingDiseaseMonthsRemaining} month(s) until death unless cured)."));
            return;
        }

        var deathInMonths = _dice.Roll(6);
        target.ApplyRottingDisease(deathInMonths);
        RuleApplicationInfo.Publish(
            "AD&D",
            "Rotting Disease",
            $"{monster.DisplayName} rotting disease check after hit on {target.Name}",
            "On a successful hit, victim contracts rotting disease and dies in 1-6 months unless Cure Disease is applied.",
            "1",
            "6",
            deathInMonths.ToString(),
            $"Victim loses 2 CHA per month and dies in {deathInMonths} month(s) unless cured.");
        events.Add(new CombatEvent($"{target.Name} contracts Rotting Disease from {monster.DisplayName}: loses 2 CHA per month and dies in {deathInMonths} month(s) unless cured."));
    }

    private static void TryApplyMummyRotOnDeath(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!SpellDamageSaveHelper.IsMummy(monster))
            return;

        target.ApplyMummyRotDeathCurse(roundsUntilRaiseImpossible: 60);
        events.Add(new CombatEvent($"{target.Name}'s corpse begins to rot from mummy contagion. Cure Disease and Raise Dead must both be completed within 6 turns or raising becomes impossible."));
    }

    private static void ApplyMummyRotRaiseWindowRoundProgress(CombatSession session, List<CombatEvent> events)
    {
        foreach (var member in session.Party)
        {
            if (member.MummyRotRaiseWindowRoundsRemaining <= 0)
                continue;

            if (!member.HasStatus(CharacterStatus.Dead)
                && !member.HasStatus(CharacterStatus.Ashes)
                && member.CurrentHitPoints > 0)
            {
                continue;
            }

            var remaining = member.TickMummyRotRaiseWindowRound();
            if (remaining == 0)
            {
                events.Add(new CombatEvent($"{member.Name}'s mummy-rot revival window closes. Raise Dead can no longer restore this character."));
            }
        }
    }

    private void TryApplyLycanthropy(MonsterInstance monster, Character target, string? attackName, int biteDamage, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Lycanthropy"))
            return;

        if (string.IsNullOrWhiteSpace(attackName)
            || !attackName.Contains("bite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isHumanoidVictim = target.Race is Race.Human or Race.Elf or Race.Dwarf or Race.Gnome or Race.Halfling or Race.HalfElf or Race.HalfOrc;
        if (!isHumanoidVictim)
            return;

        if (target.IsMonkImmuneToDiseaseSlowHaste())
        {
            events.Add(new CombatEvent($"{target.Name} is immune to lycanthropy disease from {monster.DisplayName}."));
            return;
        }

        var hpThreshold = (int)Math.Ceiling(Math.Max(1, target.MaxHitPoints) * 0.5);
        if (biteDamage < hpThreshold)
            return;

        if (target.LycanthropyInfected)
        {
            events.Add(new CombatEvent($"{target.Name} is bitten by {monster.DisplayName}, but lycanthropy infection is already active."));
            return;
        }

        var onsetDays = _dice.Roll(8) + 6; // 7-14
        var sourceType = string.IsNullOrWhiteSpace(monster.Template.Name) ? "Lycanthrope" : monster.Template.Name;
        var sourceAlignment = ParseAlignmentOrFallback(monster.Template.Alignment, target.Alignment);
        target.InfectWithLycanthropy(sourceType, sourceAlignment, onsetDays);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Lycanthropy",
            $"{monster.DisplayName} lycanthropy check after bite on {target.Name}",
            "A humanoid bitten for at least 50% of max HP is infected. Cure Disease from cleric level 12+ must occur within 3 days. Otherwise infection becomes permanent, and onset occurs in 7-14 days.",
            hpThreshold.ToString(),
            target.MaxHitPoints.ToString(),
            biteDamage.ToString(),
            $"Bite damage {biteDamage} >= threshold {hpThreshold}. Infection applied. Onset in {onsetDays} day(s). Cure window: 3 day(s)."
        );

        events.Add(new CombatEvent($"{target.Name} is infected with lycanthropy by {monster.DisplayName}! Bite damage {biteDamage} (threshold {hpThreshold}). Cure Disease from cleric level 12+ is required within 3 days or it becomes permanent. Onset in {onsetDays} day(s)."));
    }

    private static Alignment ParseAlignmentOrFallback(string? text, Alignment fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        var normalized = text.Trim().ToLowerInvariant();
        return normalized switch
        {
            "lawful good" => Alignment.LawfulGood,
            "neutral good" => Alignment.NeutralGood,
            "chaotic good" => Alignment.ChaoticGood,
            "lawful neutral" => Alignment.LawfulNeutral,
            "true neutral" or "neutral" => Alignment.TrueNeutral,
            "chaotic neutral" => Alignment.ChaoticNeutral,
            "lawful evil" => Alignment.LawfulEvil,
            "neutral evil" => Alignment.NeutralEvil,
            "chaotic evil" => Alignment.ChaoticEvil,
            _ => fallback
        };
    }

    private void TryApplyEnergyDrain(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Energy Drain"))
            return;

        if (IsTemplate(monster, "Vampire"))
        {
            var drained = 0;
            for (var i = 0; i < 2 && TryDrainOneLevel(target, out _, out _); i++)
                drained++;
            events.Add(new CombatEvent(drained > 0
                ? $"{target.Name} loses {drained} level(s) to {monster.DisplayName}'s vampiric touch!"
                : $"{target.Name} cannot lose further levels."));
            return;
        }

        var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
            target,
            _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
        if (session.IsChantActive || session.IsPrayerActive)
            saveTarget = Math.Max(1, saveTarget - 1);

        var saveRoll = _dice.Roll(20);
        var failedSave = saveRoll < saveTarget;

        RuleApplicationInfo.Publish(
            "AD&D",
            "Energy Drain",
            $"{monster.DisplayName} energy drain attack on {target.Name}",
            "Target rolls saving throw vs Death. On failed save, target loses 1 level.",
            "1",
            "20",
            saveRoll.ToString(),
            $"Save target {saveTarget}. {(failedSave ? "Failed save: lose 1 level." : "Successful save: no level loss.")}");

        if (!failedSave)
        {
            events.Add(new CombatEvent($"{target.Name} resists energy drain (save {saveRoll} vs {saveTarget})."));
            return;
        }

        if (!TryDrainOneLevel(target, out var oldLevel, out var newLevel))
        {
            events.Add(new CombatEvent($"{target.Name} fails save vs energy drain ({saveRoll} vs {saveTarget}) but cannot lose levels below 1."));
            return;
        }

        events.Add(new CombatEvent($"{target.Name} fails save vs energy drain ({saveRoll} vs {saveTarget}) and loses 1 level ({oldLevel}->{newLevel})."));
    }

    private static bool TryDrainOneLevel(Character target, out int oldLevel, out int newLevel)
    {
        target.EnsureClassProgressions();

        var primaryClass = target.Classes.Count > 0 ? target.Classes[0] : target.Class;
        var progression = target.ClassProgressions.FirstOrDefault(p => p.Class == primaryClass);
        if (progression == null)
        {
            oldLevel = target.Level;
            newLevel = target.Level;
            return false;
        }

        oldLevel = Math.Max(1, progression.Level);
        if (oldLevel <= 1)
        {
            newLevel = oldLevel;
            return false;
        }

        newLevel = oldLevel - 1;
        progression.Level = newLevel;
        progression.Experience = Math.Min(
            progression.Experience,
            Math.Max(0, ExperienceTable.GetThresholdForLevel(primaryClass, newLevel)));

        if (target.Classes.Count <= 1)
        {
            target.Level = newLevel;
            target.Experience = progression.Experience;
        }
        else
        {
            target.Level = Math.Max(1, target.GetClassLevel(primaryClass));
            target.Experience = Math.Max(0, target.ClassProgressions.Sum(x => x.Experience));
        }

        ApplyBasicWarriorAttackProgressionAfterLevelDrain(target, newLevel);
        target.RefreshMoveFromArmorAndClass();
        target.RefreshMonkProgressionStats();
        if (!GameRulesProvider.Current.PlayerSelectsWeaponProficiencies)
            WeaponProficiencyRules.EnsureAutoProficiencies(target);

        return true;
    }

    private void TryApplyStrengthDrain(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Strength Drain"))
            return;

        if (!IsAlive(target) || target.CurrentHitPoints <= 0)
            return;

        var drainAmount = 1;
        var beforeStrength = Math.Max(0, target.Abilities.Strength);
        var actualDrain = target.ApplyStrengthDrainUntilDungeonExit(drainAmount);
        var afterStrength = Math.Max(0, target.Abilities.Strength);

        RuleApplicationInfo.Publish(
            "AD&D",
            "Strength Drain",
            $"{monster.DisplayName} strength drain on {target.Name}",
            "On hit, victim loses 1 Strength. If Strength reaches 0, victim dies. Original Strength returns on dungeon exit.",
            "1",
            "1",
            drainAmount.ToString(),
            $"Strength {beforeStrength}->{afterStrength} (drained {actualDrain}).");

        events.Add(new CombatEvent($"{target.Name} suffers Strength Drain from {monster.DisplayName}: STR {beforeStrength}->{afterStrength} (drained {actualDrain})."));

        if (target.Abilities.Strength > 0)
            return;

        target.CurrentHitPoints = 0;
        target.AddStatus(CharacterStatus.Dead);
        events.Add(new CombatEvent($"{target.Name} is reduced to 0 Strength and dies!"));
    }

    private void TryApplyDestroyMetalOnMonsterHit(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Destroy Metal"))
            return;

        var equipmentSlots = new[]
        {
            EquipmentSlot.MainHand,
            EquipmentSlot.OffHand,
            EquipmentSlot.Body,
            EquipmentSlot.Head,
            EquipmentSlot.Hands
        };

        var metalTargets = equipmentSlots
            .Select(slot => (slot, item: target.Equipment.TryGetValue(slot, out var i) ? i : null))
            .Where(x => x.item != null && IsMetalItem(x.item))
            .ToList();

        if (metalTargets.Count == 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} brushes {target.Name} with rusting antennae, but no exposed metal is affected."));
            return;
        }

        var selected = metalTargets[_dice.Roll(metalTargets.Count) - 1];
        var item = selected.item!;

        if (IsMagicalItem(item))
        {
            var saveRoll = _dice.Roll(20);
            const int saveTarget = 11;
            RuleApplicationInfo.Publish(
                "DMG",
                "Item Save",
                $"{item.Name} saving throw vs rust/corrosion",
                "Magical metal item touched by rust effect gets a saving throw; success resists corrosion.",
                "1",
                "20",
                saveRoll.ToString(),
                saveRoll >= saveTarget
                    ? $"Save succeeded ({saveRoll} vs {saveTarget}); item resists corrosion."
                    : $"Save failed ({saveRoll} vs {saveTarget}); item corrodes.");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name}'s {item.Name} resists corrosion (save {saveRoll} vs {saveTarget})."));
                return;
            }
        }

        DestroyEquippedItem(target, item, events, $"{monster.DisplayName} corrodes {target.Name}'s {item.Name}; it rusts away instantly!");
    }

    private void TryApplyDissolveWoodOnMonsterHit(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Dissolve Wood"))
            return;

        var woodenItems = new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Range }
            .Select(slot => target.Equipment.TryGetValue(slot, out var i) ? i : null)
            .Where(i => i != null && IsWoodenItem(i))
            .Distinct()
            .ToList();

        if (woodenItems.Count == 0)
            return;

        var item = woodenItems[_dice.Roll(woodenItems.Count) - 1]!;

        if (IsMagicalItem(item))
        {
            var saveRoll = _dice.Roll(20);
            const int saveTarget = 11;
            RuleApplicationInfo.Publish(
                "DMG",
                "Item Save",
                $"{item.Name} saving throw vs dissolving",
                "Magical wooden item touched by a wood-dissolving creature gets a saving throw; success resists.",
                "1",
                "20",
                saveRoll.ToString(),
                saveRoll >= saveTarget
                    ? $"Save succeeded ({saveRoll} vs {saveTarget}); item resists dissolving."
                    : $"Save failed ({saveRoll} vs {saveTarget}); item dissolves.");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{target.Name}'s {item.Name} resists dissolving (save {saveRoll} vs {saveTarget})."));
                return;
            }
        }

        DestroyEquippedItem(target, item, events, $"{monster.DisplayName} dissolves {target.Name}'s {item.Name}!");
    }

    private static readonly string[] WoodenItemKeywords =
    {
        "wood", "staff", "stick", "club", "spear", "javelin", "lance", "pike", "trident",
        "halberd", "bardiche", "bec de corbin", "guisarme", "fauchard", "fork", "glaive",
        "voulge", "partisan", "ranseur", "spetum", "lucern", "pole",
        "bow", "crossbow", "sling", "axe", "hammer", "flail", "morning star", "pick", "quarterstaff"
    };

    private static bool IsWoodenItem(Item? item)
    {
        if (item == null)
            return false;

        if (item.Type == ItemType.Shield)
            return $"{item.Name} {item.Description}".Contains("wood", StringComparison.OrdinalIgnoreCase);

        if (item.Type != ItemType.Weapon)
            return false;

        var name = item.Name.ToLowerInvariant();
        if (name.Contains("sword", StringComparison.Ordinal) || name.Contains("dagger", StringComparison.Ordinal))
            return false;

        if (item.Description.Contains("wood", StringComparison.OrdinalIgnoreCase))
            return true;

        return WoodenItemKeywords.Any(k => name.Contains(k, StringComparison.Ordinal));
    }

    private void TryApplyHitOnMonsterDestroysMetal(Character attacker, MonsterInstance target, Item? strikingWeapon, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(target, "Hit on Monster Destroys Metal"))
            return;

        if (strikingWeapon == null || !IsMetalItem(strikingWeapon))
            return;

        if (IsMagicalItem(strikingWeapon))
        {
            var saveRoll = _dice.Roll(20);
            const int saveTarget = 11;
            RuleApplicationInfo.Publish(
                "DMG",
                "Item Save",
                $"{strikingWeapon.Name} saving throw vs rusting hide",
                "Magical metal weapon that strikes a rusting monster gets a saving throw to avoid corrosion.",
                "1",
                "20",
                saveRoll.ToString(),
                saveRoll >= saveTarget
                    ? $"Save succeeded ({saveRoll} vs {saveTarget}); weapon resists corrosion."
                    : $"Save failed ({saveRoll} vs {saveTarget}); weapon corrodes.");

            if (saveRoll >= saveTarget)
            {
                events.Add(new CombatEvent($"{attacker.Name}'s {strikingWeapon.Name} resists corrosion (save {saveRoll} vs {saveTarget})."));
                return;
            }
        }

        DestroyEquippedItem(attacker, strikingWeapon, events,
            $"{attacker.Name}'s {strikingWeapon.Name} corrodes away after striking {target.DisplayName}!");
    }

    private static bool IsMagicalItem(Item item)
    {
        if (item == null)
            return false;

        if (item.MagicBonus > 0 || item.ToHitBonus > 0)
            return true;

        return item.SpecialAbilities != null && item.SpecialAbilities.Count > 0;
    }

    private static bool IsMetalItem(Item? item)
    {
        if (item == null)
            return false;

        var type = item.Type;
        if (type != ItemType.Weapon && type != ItemType.Armor && type != ItemType.Shield)
            return false;

        var text = $"{item.Name} {item.Description}".ToLowerInvariant();
        if (text.Contains("wood", StringComparison.Ordinal)
            || text.Contains("leather", StringComparison.Ordinal)
            || text.Contains("cloth", StringComparison.Ordinal)
            || text.Contains("bone", StringComparison.Ordinal)
            || text.Contains("stone", StringComparison.Ordinal)
            || text.Contains("hide", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static void DestroyEquippedItem(Character target, Item item, List<CombatEvent> events, string message)
    {
        foreach (var slot in target.Equipment
                     .Where(kv => kv.Value != null && ReferenceEquals(kv.Value, item))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            EquipmentManager.Unequip(target, slot);
        }

        target.Inventory.RemoveAll(i => ReferenceEquals(i, item));

        events.Add(new CombatEvent(message));
    }

    private static void ApplyBasicWarriorAttackProgressionAfterLevelDrain(Character target, int effectiveLevel)
    {
        if (target.Classes.Count == 0)
            return;

        var primary = target.Classes[0];
        if (primary is CharacterClass.Fighter or CharacterClass.Paladin or CharacterClass.Ranger)
        {
            if (effectiveLevel < 7)
                target.NumberOfAttacks = 1f;
            else if (effectiveLevel < 13)
                target.NumberOfAttacks = 1.5f;
            else
                target.NumberOfAttacks = 2f;

            return;
        }

        if (!target.IsMonk())
            target.NumberOfAttacks = 1f;
    }

    private static void TryApplyDrainBlood(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!(HasAnySpecialAbility(monster, "Stirge Blood drain") || HasAnySpecialAbility(monster, "Tick Blood Drain")))
            return;

        if (!IsAlive(target) || target.CurrentHitPoints <= 0)
            return;

        var remaining = session.GetPartyDrainBloodRemaining(target.Name);
        if (remaining <= 0)
        {
            var isTickDrain = HasAnySpecialAbility(monster, "Tick Blood Drain");
            var effectName = isTickDrain ? "Tick Blood Drain" : "Stirge Blood drain";
            var dieSides = isTickDrain ? 6 : 4;
            var totalToDrain = isTickDrain ? Math.Max(1, monster.MaxHitPoints) : 12;

            session.SetPartyDrainBlood(target.Name, totalToDrain, dieSides, effectName);
            events.Add(new CombatEvent($"{target.Name} is afflicted by {effectName} from {monster.DisplayName}! {totalToDrain} HP will be drained over subsequent rounds (1-{dieSides} per round)."));
            return;
        }

        var currentEffectName = session.GetPartyDrainBloodEffectName(target.Name);
        events.Add(new CombatEvent($"{target.Name} is hit again by {monster.DisplayName}, but {currentEffectName} is already active ({remaining} HP remaining to drain)."));
    }

    private static bool IsShrieker(MonsterInstance monster)
    {
        return string.Equals(monster.Template.Name, "Shrieker", StringComparison.OrdinalIgnoreCase)
               || HasSpecialAbility(monster, "Shriek");
    }

    private static bool IsPiercer(MonsterInstance monster)
    {
        return monster.Template.Name.StartsWith("Piercer", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveMonsterExplosionOnHit(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Explosion"))
            return false;

        if (!monster.IsAlive)
            return false;

        var isGasSpore = string.Equals(monster.Template.Name, "Gas Spore", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(monster.Template.TypeName, "Gas Spore", StringComparison.OrdinalIgnoreCase);

        var rolledDamage = 0;
        for (int i = 0; i < 6; i++)
            rolledDamage += _dice.Roll(6);

        monster.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{monster.DisplayName} explodes violently!"));

        foreach (var member in session.ExposedParty.ToList())
        {
            var saveThrowType = isGasSpore ? SaveThrowType.RodStaffWand : SaveThrowType.Spell;
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                member,
                _savingThrowService.GetSaveTarget(member, saveThrowType));
            var fireSaveBonus = GetFireResistanceSaveBonus(member);
            if (fireSaveBonus > 0)
                saveTarget = Math.Max(1, saveTarget - fireSaveBonus);
            if (session.IsChantActive || session.IsPrayerActive)
                saveTarget = Math.Max(1, saveTarget - 1);

            var saveRoll = _dice.Roll(20);
            var preSaveDamage = ApplyFireProtectionDamageReduction(member, rolledDamage, 6, isMagicalFire: true, isNormalFire: false);
            if (preSaveDamage <= 0)
            {
                events.Add(new CombatEvent($"{member.Name} is protected from the fiery explosion."));
                continue;
            }

            var applied = saveRoll >= saveTarget
                ? (isGasSpore ? (preSaveDamage / 2) : Math.Max(1, preSaveDamage / 2))
                : preSaveDamage;

            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - applied);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);

            events.Add(new CombatEvent(saveRoll >= saveTarget
                ? $"{member.Name} succeeds save ({saveRoll} vs {saveTarget}) and takes half explosion damage: {actual}. HP {before}->{member.CurrentHitPoints}."
                : $"{member.Name} fails save ({saveRoll} vs {saveTarget}) and takes {actual} explosion damage. HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} is slain by the explosion!"));
            }
        }

        return true;
    }
}
