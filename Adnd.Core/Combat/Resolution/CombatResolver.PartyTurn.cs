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
    private bool ResolvePartyMemberTurn(RoundState state, Character member)
    {
        if (ResolvePartyMemberStartOfTurn(state, member))
            return false;

        return ResolvePartyMemberAction(state, member);
    }

    // Returns true when the member's turn is over before an action is chosen (dead, frightened, asleep, held ...).
    private bool ResolvePartyMemberStartOfTurn(RoundState state, Character member)
    {
        var session = state.Session;
        var events = state.Events;
        var partyActions = state.PartyActions;

        if (!IsAlive(member))
            return true;

        if (state.TimeStopExtraRound && !ReferenceEquals(member, state.TimeStopCaster))
            return true;

        TryApplyMaceOfDisruptionEvilBacklash(session, member, events);
        if (!IsAlive(member))
            return true;

        var lifeDrain = EquipmentManager.ApplyEquippedLifeDrain(member);
        if (lifeDrain > 0)
        {
            events.Add(new CombatEvent($"{member.Name}'s cursed ring drains {lifeDrain} HP. HP {member.CurrentHitPoints + lifeDrain}->{member.CurrentHitPoints}."));
            if (!IsAlive(member))
            {
                events.Add(new CombatEvent($"{member.Name} dies from the ring's drain."));
                return true;
            }
        }

        var thunderboltsRest = session.GetPartyThunderboltsRestRounds(member.Name);
        if (thunderboltsRest > 0)
        {
            events.Add(new CombatEvent($"{member.Name} is resting after repeated Hammer of Thunderbolts throws and cannot act ({thunderboltsRest} round(s) remaining)."));
            session.TickPartyThunderboltsRest(member.Name);
            return true;
        }

        if (session.RoundNumber == 1 && session.PartySurprisedRound1)
        {
            if (!partyActions.TryGetValue(member.Name, out var surpriseAction))
                surpriseAction = CombatAction.OfType(CombatActionType.Parry);

            if (!CanActDuringCompleteSurpriseWithCrossbowOfSpeed(member, surpriseAction))
                return true;

            events.Add(new CombatEvent($"{member.Name}'s Crossbow of Speed snaps to readiness and can fire once despite complete surprise (2nd segment effect)."));
        }

        var drainRemaining = session.GetPartyDrainBloodRemaining(member.Name);
        if (drainRemaining > 0)
        {
            var drainDieSides = session.GetPartyDrainBloodDieSides(member.Name);
            var drainEffectName = session.GetPartyDrainBloodEffectName(member.Name);
            var drainRoll = _dice.Roll(drainDieSides);
            var drainAmount = session.ConsumePartyDrainBlood(member.Name, drainRoll);
            if (drainAmount > 0)
            {
                var beforeHp = member.CurrentHitPoints;
                member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - drainAmount);
                var actualDrain = beforeHp - member.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(member, actualDrain, events);

                var remainingAfter = session.GetPartyDrainBloodRemaining(member.Name);
                events.Add(new CombatEvent($"{member.Name} suffers {drainEffectName} for {actualDrain} (rolled {drainRoll} on 1d{drainDieSides}). Remaining drain: {remainingAfter}. HP {beforeHp}->{member.CurrentHitPoints}."));

                if (member.CurrentHitPoints <= 0)
                {
                    member.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{member.Name} dies from blood loss!"));
                    return true;
                }
            }
        }

        if (TryApplyConstrictionDamage(session, member, events))
            return true;

        var veryHotFireExposureRounds = session.GetPartyVeryHotFireExposureRounds(member.Name);
        if (veryHotFireExposureRounds > 0)
        {
            if (TryExtinguishFireWithFrostBrand(member, session, events))
            {
                veryHotFireExposureRounds = session.GetPartyVeryHotFireExposureRounds(member.Name);
            }

            var veryHotFireDamage = session.GetPartyVeryHotFireExposureDamagePerRound(member.Name);
            var appliedFireDamage = ApplyFireProtectionDamageReduction(
                member,
                veryHotFireDamage,
                damageDiceCount: 0,
                isMagicalFire: false,
                isNormalFire: false);

            if (appliedFireDamage <= 0)
            {
                events.Add(new CombatEvent($"{member.Name} is protected from very hot fire exposure."));
            }
            else
            {
                var beforeFireHp = member.CurrentHitPoints;
                member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - appliedFireDamage);
                var actualFireDamage = beforeFireHp - member.CurrentHitPoints;
                WakeCharacterIfAsleepAfterDamage(member, actualFireDamage, events);

                events.Add(new CombatEvent($"{member.Name} is engulfed in very hot fire for {actualFireDamage} damage. HP {beforeFireHp}->{member.CurrentHitPoints}."));
                TryBurnAwayWebsFromFireDamage(member, "very hot fire", session, events);

                if (member.CurrentHitPoints <= 0)
                {
                    member.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{member.Name} is consumed by the conflagration!"));
                    return true;
                }
            }

            var exposureRemaining = session.TickPartyVeryHotFireExposure(member.Name);
            if (exposureRemaining > 0)
                events.Add(new CombatEvent($"{member.Name} remains within very hot fire ({exposureRemaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{member.Name} is no longer directly within very hot fire."));
        }

        if (_retrievingWeaponRounds.TryGetValue(member.Name, out var retrievingLeft))
        {
            if (retrievingLeft <= 0 || !IsAlive(member) || !_thrownWeapons.ContainsKey(member.Name))
            {
                _retrievingWeaponRounds.Remove(member.Name);
            }
            else
            {
                var remainingToFetch = retrievingLeft - 1;
                if (remainingToFetch <= 0)
                {
                    _retrievingWeaponRounds.Remove(member.Name);
                    var thrown = _thrownWeapons[member.Name].Weapon;
                    _thrownWeapons.Remove(member.Name);
                    member.Equipment[EquipmentSlot.MainHand] = thrown;
                    events.Add(new CombatEvent($"{member.Name} runs after the weapon, retrieves {thrown.Name} and re-equips it."));
                }
                else
                {
                    _retrievingWeaponRounds[member.Name] = remainingToFetch;
                    events.Add(new CombatEvent($"{member.Name} is running after the weapon thrown by telekinesis ({remainingToFetch} round(s) remaining)."));
                }

                return true;
            }
        }

        if (_frightenedRounds.TryGetValue(member.Name, out var frightenedLeft))
        {
            if (frightenedLeft <= 0 || !IsAlive(member))
            {
                _frightenedRounds.Remove(member.Name);
            }
            else
            {
                events.Add(new CombatEvent($"{member.Name} is gripped by fear and flees from the fight ({frightenedLeft} round(s) remaining)."));
                if (frightenedLeft - 1 <= 0)
                {
                    _frightenedRounds.Remove(member.Name);
                    events.Add(new CombatEvent($"{member.Name} regains courage."));
                }
                else
                {
                    _frightenedRounds[member.Name] = frightenedLeft - 1;
                }

                return true;
            }
        }

        if (member.HasStatus(CharacterStatus.Confused))
        {
            var confusedRounds = session.GetPartyConfusedRounds(member.Name);
            events.Add(new CombatEvent(confusedRounds > 0
                ? $"{member.Name} is confused and cannot act ({confusedRounds} round(s) remaining)."
                : $"{member.Name} is confused and cannot act."));

            var remainingConfused = session.TickPartyConfused(member.Name);
            if (remainingConfused <= 0)
            {
                member.RemoveStatus(CharacterStatus.Confused);
                events.Add(new CombatEvent($"{member.Name} regains clarity."));
            }

            return true;
        }

        if (member.HasStatus(CharacterStatus.Charmed))
        {
            ResolveCharmedCharacterTurn(session, member, events);
            return true;
        }

        var regeneration = GetEquippedRegeneration(member);
        if (regeneration > 0 && member.CurrentHitPoints < member.MaxHitPoints)
        {
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Min(member.MaxHitPoints, member.CurrentHitPoints + regeneration);
            var healed = member.CurrentHitPoints - before;
            if (healed > 0)
                events.Add(new CombatEvent($"{member.Name} regenerates {healed} HP. HP {before}->{member.CurrentHitPoints}."));
        }

        var mirrorRounds = session.GetMirrorImageRounds(member.Name);
        TickBoneDevilStrengthLoss(member, events);
        if (mirrorRounds > 0)
        {
            var imageCount = session.GetMirrorImageCount(member.Name);
            var remaining = session.TickMirrorImage(member.Name);
            if (remaining <= 0 && imageCount > 0)
                events.Add(new CombatEvent($"{member.Name}'s mirror image fades."));
        }

        if (session.IsBlinking(member.Name) && session.TickBlink(member.Name) <= 0)
            events.Add(new CombatEvent($"{member.Name} stops blinking and stays put."));

        var barkskinRounds = session.GetBarkskinRounds(member.Name);
        if (barkskinRounds > 0)
        {
            var remaining = session.TickBarkskin(member.Name);
            if (remaining <= 0)
            {
                var bonus = session.GetBarkskinBonus(member.Name);
                if (bonus > 0)
                {
                    member.ArmorClass += bonus;
                    session.ClearBarkskin(member.Name);
                    events.Add(new CombatEvent($"{member.Name}'s Barkskin fades (+{bonus} AC removed)."));
                }
            }
        }

        var hasteRounds = session.GetHasteRounds(member.Name);
        if (hasteRounds > 0)
        {
            var hasteRemaining = session.TickHaste(member.Name);
            if (hasteRemaining <= 0)
            {
                var originalMove = session.GetHasteOriginalMove(member.Name);
                if (originalMove > 0)
                    member.Move = originalMove;

                session.ClearHaste(member.Name);
                events.Add(new CombatEvent($"{member.Name}'s Haste fades."));
            }
        }

        var strengthRounds = session.GetStrengthBuffRounds(member.Name);
        if (strengthRounds > 0)
        {
            var remaining = session.TickStrengthBuff(member.Name);
            if (remaining <= 0)
            {
                var bonus = session.GetStrengthBuffBonus(member.Name);
                if (bonus > 0)
                {
                    member.Abilities.Strength = Math.Max(1, member.Abilities.Strength - bonus);
                    session.ClearStrengthBuff(member.Name);
                    member.TemporaryStrengthBonus = 0;
                    member.TemporaryStrengthRoundsRemaining = 0;
                    events.Add(new CombatEvent($"{member.Name}'s Strength spell fades (−{bonus} STR)."));
                }
            }
            else
            {
                member.TemporaryStrengthBonus = session.GetStrengthBuffBonus(member.Name);
                member.TemporaryStrengthRoundsRemaining = remaining;
            }
        }

        if (member.FireProtectionRoundsRemaining > 0)
        {
            member.FireProtectionRoundsRemaining = Math.Max(0, member.FireProtectionRoundsRemaining - 1);
            if (member.FireProtectionRoundsRemaining <= 0)
            {
                member.ClearProtectionFromFire();
                events.Add(new CombatEvent($"{member.Name}'s Protection From Fire expires."));
            }
        }

        if (member.LightningProtectionRoundsRemaining > 0)
        {
            member.LightningProtectionRoundsRemaining = Math.Max(0, member.LightningProtectionRoundsRemaining - 1);
            if (member.LightningProtectionRoundsRemaining <= 0)
            {
                member.ClearProtectionFromLightning();
                events.Add(new CombatEvent($"{member.Name}'s Protection From Lightning expires."));
            }
        }

        if (member.PotionFireResistanceRoundsRemaining > 0)
        {
            member.PotionFireResistanceRoundsRemaining = Math.Max(0, member.PotionFireResistanceRoundsRemaining - 1);
            if (member.PotionFireResistanceRoundsRemaining <= 0)
            {
                member.ClearPotionFireResistance();
                events.Add(new CombatEvent($"{member.Name}'s Potion of Fire Resistance effect expires."));
            }
        }

        if (member.PotionInvulnerabilityRoundsRemaining > 0)
        {
            member.PotionInvulnerabilityRoundsRemaining = Math.Max(0, member.PotionInvulnerabilityRoundsRemaining - 1);
            if (member.PotionInvulnerabilityRoundsRemaining <= 0)
            {
                member.ClearPotionInvulnerability();
                events.Add(new CombatEvent($"{member.Name}'s Potion of Invulnerability effect expires."));
            }
        }

        if (member.ProtectionFromMagicScrollRoundsRemaining > 0)
        {
            member.ProtectionFromMagicScrollRoundsRemaining = Math.Max(0, member.ProtectionFromMagicScrollRoundsRemaining - 1);
            if (member.ProtectionFromMagicScrollRoundsRemaining <= 0)
            {
                member.ClearProtectionFromMagicScroll();
                events.Add(new CombatEvent($"{member.Name}'s anti-magic protection expires."));
            }
        }

        if (member.ColdResistanceRoundsRemaining > 0)
        {
            member.ColdResistanceRoundsRemaining = Math.Max(0, member.ColdResistanceRoundsRemaining - 1);
            if (member.ColdResistanceRoundsRemaining <= 0)
            {
                member.ClearResistCold();
                events.Add(new CombatEvent($"{member.Name}'s Resist Cold expires."));
            }
        }

        var partySlowRounds = session.GetPartySlowRounds(member.Name);
        if (partySlowRounds > 0)
        {
            var slowRemaining = session.TickPartySlow(member.Name);
            if (slowRemaining <= 0)
            {
                var originalMove = session.GetPartySlowOriginalMove(member.Name);
                if (originalMove > 0)
                    member.Move = originalMove;

                session.ClearPartySlow(member.Name);
                member.RemoveStatus(CharacterStatus.Slowed);
                events.Add(new CombatEvent($"{member.Name} is no longer slowed."));
            }
            else
            {
                member.AddStatus(CharacterStatus.Slowed);
            }
        }
        else if (member.HasStatus(CharacterStatus.Slowed))
        {
            member.RemoveStatus(CharacterStatus.Slowed);
        }

        if (member.HasStatus(CharacterStatus.Invisible))
        {
            var improvedRounds = session.GetImprovedInvisibilityRounds(member.Name);
            if (improvedRounds > 0)
            {
                var remaining = session.TickImprovedInvisibility(member.Name);
                if (remaining <= 0)
                {
                    member.RemoveStatus(CharacterStatus.Invisible);
                    member.ArmorClass += 4;
                    events.Add(new CombatEvent($"{member.Name}'s improved invisibility fades."));
                }
            }
        }

        if (member.HasStatus(CharacterStatus.Asleep))
        {
            var roundsRemaining = session.GetPartyAsleepRounds(member.Name);
            events.Add(new CombatEvent(roundsRemaining > 0
                ? $"{member.Name} is asleep and cannot act ({roundsRemaining} round(s) remaining)."
                : $"{member.Name} is asleep and cannot act."));

            var afterTick = session.TickPartyAsleep(member.Name);
            if (afterTick <= 0)
            {
                member.RemoveStatus(CharacterStatus.Asleep);
                events.Add(new CombatEvent($"{member.Name} wakes up."));
            }

            return true;
        }

        if (IsHeldByMonster(session, member, events))
            return true;

        if (member.HasStatus(CharacterStatus.Webbed))
        {
            var roundsRemaining = session.GetPartyWebbedRounds(member.Name);
            events.Add(new CombatEvent(roundsRemaining > 0
                ? $"{member.Name} is webbed and cannot act ({roundsRemaining} round(s) remaining)."
                : $"{member.Name} is webbed and cannot act."));

            var afterTick = session.TickPartyWebbed(member.Name);
            if (afterTick <= 0)
            {
                member.RemoveStatus(CharacterStatus.Webbed);
                events.Add(new CombatEvent($"{member.Name} breaks free of the webs."));
            }

            return true;
        }

        if (member.HasStatus(CharacterStatus.Paralyzed))
        {
            var remainingParalysis = member.TickParalysisRound();
            events.Add(new CombatEvent(remainingParalysis > 0
                ? $"{member.Name} is paralyzed and cannot act ({remainingParalysis} round(s) remaining)."
                : $"{member.Name} is no longer paralyzed."));
            return true;
        }

        if (member.HasStatus(CharacterStatus.Petrified))
        {
            events.Add(new CombatEvent($"{member.Name} is petrified and cannot act."));
            return true;
        }

        if (member.RotGrubDeathRoundsRemaining > 0)
        {
            member.RotGrubDeathRoundsRemaining = Math.Max(0, member.RotGrubDeathRoundsRemaining - 1);
            if (member.RotGrubDeathRoundsRemaining <= 0)
            {
                member.CurrentHitPoints = 0;
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} dies as rot grubs reach the heart."));
                return true;
            }
        }

        return false;
    }

    // Returns true when the whole round is over (all monsters defeated).
    private bool ResolvePartyMemberAction(RoundState state, Character member)
    {
        var session = state.Session;
        var events = state.Events;
        var partyActions = state.PartyActions;
        var parrying = state.Parrying;

        if (!partyActions.TryGetValue(member.Name, out var action))
            action = CombatAction.OfType(CombatActionType.Parry);

        if (session.IsSwallowed(member.Name) && action.Type is not (CombatActionType.Fight or CombatActionType.Parry))
        {
            events.Add(new CombatEvent($"{member.Name} is swallowed and can only fight from inside or wait; outside actions and escape are unavailable."));
            return false;
        }
        if (action.Target?.CharacterName is string allyName && session.IsSwallowed(allyName))
        {
            events.Add(new CombatEvent($"{member.Name} cannot reach {allyName} inside the Purple Worm."));
            return false;
        }

        switch (action.Type)
        {
            case CombatActionType.Fight:
                InterruptChantForAction(state, member, clearRoundFlag: false);
                ResolvePartyAttack(session, member, action, events);
                break;
            case CombatActionType.Parry:
                parrying.Add(member.Name);
                events.Add(new CombatEvent($"{member.Name} parries."));
                break;
            case CombatActionType.UseItem:
                InterruptChantForAction(state, member);
                ResolvePartyUseItem(session, member, action, events);
                break;
            case CombatActionType.BardCharm:
                InterruptChantForAction(state, member);
                ResolveBardCharm(session, member, action, events);
                break;
            case CombatActionType.DispellUndead:
                if (member.HasStatus(CharacterStatus.LycanthropeTransformed))
                {
                    events.Add(new CombatEvent($"{member.Name} is transformed by lycanthropy and cannot use class abilities."));
                    break;
                }

                InterruptChantForAction(state, member);
                ResolveDispellUndead(session, member, action, events);
                break;
            case CombatActionType.LayOnHands:
                InterruptChantForAction(state, member);
                ResolveLayOnHands(session, member, action, events);
                break;
            case CombatActionType.MonkBodyHeal:
                InterruptChantForAction(state, member);
                ResolveMonkBodyHeal(session, member, events);
                break;
            case CombatActionType.Spell:
            case CombatActionType.CastSpell:
                InterruptChantForAction(state, member);
                ResolvePartySpell(session, member, action, events);
                break;
            case CombatActionType.Run:
                InterruptChantForAction(state, member);
                state.PartyAttemptedRun = true;
                events.Add(new CombatEvent($"{member.Name} tries to run!"));
                break;
        }

        ResolveSwallowedCharacters(session, events, advanceTime: false);
        if (!session.AliveMonsters.Any())
        {
            session.Outcome = CombatOutcome.Victory;
            events.Add(new CombatEvent("All monsters are defeated!"));
            return true;
        }

        return false;
    }

    private static void InterruptChantForAction(RoundState state, Character member, bool clearRoundFlag = true)
    {
        if (!state.ChantActiveAtRoundStart
            || !string.Equals(state.Session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
            return;

        state.Session.BreakChant();
        if (clearRoundFlag)
            state.ChantActiveAtRoundStart = false;
        state.Events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
    }
}
