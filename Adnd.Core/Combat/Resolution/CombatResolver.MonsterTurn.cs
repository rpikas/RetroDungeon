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
    private bool ResolveMonsterTurn(RoundState state, MonsterInstance monster)
    {
        if (!monster.IsAlive || !state.Session.Monsters.Contains(monster))
            return false;

        MonsterTurnFlow flow;

        flow = ResolveMonsterTurnStartAndSpells(state, monster);
        if (flow != MonsterTurnFlow.Proceed)
            return flow == MonsterTurnFlow.EndRound;

        flow = ResolveMonsterBreathGazesAndPowers(state, monster);
        if (flow != MonsterTurnFlow.Proceed)
            return flow == MonsterTurnFlow.EndRound;

        flow = ResolveMonsterStatusConditions(state, monster);
        if (flow != MonsterTurnFlow.Proceed)
            return flow == MonsterTurnFlow.EndRound;

        flow = ResolveMonsterLatePowers(state, monster);
        if (flow != MonsterTurnFlow.Proceed)
            return flow == MonsterTurnFlow.EndRound;

        return ResolveMonsterMeleeAttacks(state, monster);
    }

    private enum MonsterTurnFlow
    {
        Proceed,
        EndTurn,
        EndRound,
    }

    private MonsterTurnFlow ResolveMonsterTurnStartAndSpells(RoundState state, MonsterInstance monster)
    {
        var session = state.Session;
        var events = state.Events;

        var levelMagicUseDecisions = new Dictionary<int, MonsterLevelMagicUseDecision>();

        ApplySwordOfWoundingOngoingDamage(session, monster, events);
        if (!monster.IsAlive)
            return MonsterTurnFlow.EndTurn;

        ApplyMonsterRegeneration(session, monster, events);
        if (TryApplyMummyBurningOilSecondRoundDamage(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (session.RoundNumber == 1 && session.MonstersSurprisedRound1)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} is surprised and cannot act in round 1."));
            return MonsterTurnFlow.EndTurn;
        }

        if (session.RoundNumber == 1 && session.EncounterSleepingMonstersRound1.Contains(monster.DisplayName))
        {
            events.Add(new CombatEvent($"{monster.DisplayName} is asleep and cannot act in round 1."));
            return MonsterTurnFlow.EndTurn;
        }

        var isFeebleminded = monster.HasStatus(MonsterStatus.Feebleminded);
        var isSilenced = monster.HasStatus(MonsterStatus.Silenced);

        var monsterBarkskinRounds = session.GetMonsterBarkskinRounds(monster);
        if (monsterBarkskinRounds > 0)
        {
            var remaining = session.TickMonsterBarkskin(monster);
            if (remaining <= 0)
            {
                var bonus = session.GetMonsterBarkskinBonus(monster);
                if (bonus > 0)
                {
                    monster.AdjustArmorClass(bonus);
                    session.ClearMonsterBarkskin(monster);
                    events.Add(new CombatEvent($"{monster.DisplayName}'s Barkskin fades (+{bonus} AC removed)."));
                }
            }
        }

        if (isSilenced)
        {
            var remainingSilence = monster.TickStatus(MonsterStatus.Silenced);
            if (HasAnyMonsterSpellcastingAbility(monster))
            {
                events.Add(new CombatEvent(remainingSilence > 0
                    ? $"{monster.DisplayName} is silenced and cannot cast spells ({remainingSilence} round(s) remaining)."
                    : $"{monster.DisplayName} is no longer silenced."));
            }
        }

        if (!isSilenced && isFeebleminded
            && HasAnyMonsterSpellcastingAbility(monster))
        {
            events.Add(new CombatEvent($"{monster.DisplayName} is feebleminded and cannot cast spells."));
        }

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 5, MonsterSpellTradition.Illusionist)
            && ResolveLevel5IllusionistSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 5, MonsterSpellTradition.MagicUser)
            && ResolveLevel5MagicUserSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 5, MonsterSpellTradition.Druid)
            && ResolveLevel5DruidSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 5, MonsterSpellTradition.Cleric, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 5, MonsterSpellTradition.Cleric)
            && ResolveLevel5ClericSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 4, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 4, MonsterSpellTradition.Illusionist)
            && ResolveLevel4IllusionistSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 4, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 4, MonsterSpellTradition.MagicUser)
            && ResolveLevel4MagicUserSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
            && HasSpellTierAbility(monster, 3, MonsterSpellTradition.MagicUser)
            && ResolveLevel3MagicUserSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 3, MonsterSpellTradition.Illusionist)
            && ResolveLevel3IllusionistSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.Cleric, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 3, MonsterSpellTradition.Cleric)
            && ResolveLevel3ClericSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 2, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
            && HasSpellTierAbility(monster, 2, MonsterSpellTradition.Illusionist)
            && ResolveLevel2IllusionistSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 2, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
            && HasSpellTierAbility(monster, 2, MonsterSpellTradition.MagicUser)
            && ResolveLevel2MagicUserSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.MagicUser, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
            && HasSpellTierAbility(monster, 1, MonsterSpellTradition.MagicUser)
            )
        {
            ResolveLevel1MageSpells(monster, session, events);
            return MonsterTurnFlow.EndTurn;
        }

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.Cleric, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
            && HasSpellTierAbility(monster, 1, MonsterSpellTradition.Cleric)
            && ResolveLevel1PriestSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 4, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 4, MonsterSpellTradition.Druid)
            && ResolveLevel4DruidSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 3, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 3, MonsterSpellTradition.Druid)
            && ResolveLevel3DruidSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 2, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 2, MonsterSpellTradition.Druid)
            && ResolveLevel2DruidSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.Druid, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: false)
            && HasSpellTierAbility(monster, 1, MonsterSpellTradition.Druid)
            && ResolveLevel1DruidSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (!isSilenced
            && !isFeebleminded
            && CanMonsterAttemptLevelSpell(monster, 1, MonsterSpellTradition.Illusionist, events, levelMagicUseDecisions, useDefaultFiftyPercentWhenNoConfiguredChance: true)
            && HasSpellTierAbility(monster, 1, MonsterSpellTradition.Illusionist)
            && ResolveLevel1IllusionistSpell(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        return MonsterTurnFlow.Proceed;
    }

    private MonsterTurnFlow ResolveMonsterBreathGazesAndPowers(RoundState state, MonsterInstance monster)
    {
        var session = state.Session;
        var events = state.Events;

        if (HasSpecialAbility(monster, "Dragon breath")
            && ResolveDragonBreathCurrentHp(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveHellHoundBreatheFire(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (TryGetHpDamageBreathDamage(monster, out var breathDamage)
            && ResolveHpDamageBreath(monster, breathDamage, session, events))
            return MonsterTurnFlow.EndTurn;

        // A phase spider shifts into phase to bite, so the first attack against it next round can land.
        if (HasSpecialDefense(monster, "Phasing"))
            _phaseSpidersInPhase.Add(monster);

        ApplyConfusingGaze(session, monster, events);
        ApplyFearAura(session, monster, events);
        ApplySleepGaze(session, monster, events);
        ApplyMedusaGaze(session, monster, events);

        if (TryResolveBrownMoldFreezing(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveManticoreTailSpikes(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveDeathGroan(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveOgreMageSpell(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveNightHagGate(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveTelekinesisThrow(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveCauseFear(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveIceDevilPower(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveBarbedDevilPower(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveHornedWallOfFire(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveEfreetiWish(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveAirElementalWhirlwind(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveHurlRocks(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolvePetrifyingBreath(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveFleshGolemBerserk(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveLevel8Powers(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveGreenSlime(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveLevel9Powers(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveLevel10Powers(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveVrockGate(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveSuccubusPower(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveNightHagStrangulation(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveNightHagSleep(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveNightHagMagicMissile(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveNightHagEnfeeblement(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterAcidSpit(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveBoneDevilTail(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveBoneDevilSummon(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterPickPockets(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterBardCharm(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        return MonsterTurnFlow.Proceed;
    }

    private MonsterTurnFlow ResolveMonsterStatusConditions(RoundState state, MonsterInstance monster)
    {
        var session = state.Session;
        var events = state.Events;

        if (monster.HasStatus(MonsterStatus.IncendiaryCloud))
        {
            var rolledDamage = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
            var beforeHp = monster.CurrentHitPoints;
            var appliedDamage = HasSpecialDefense(monster, "Immunity to fire and heat") ? 0 : rolledDamage;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - appliedDamage);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
            var remaining = monster.TickStatus(MonsterStatus.IncendiaryCloud);

            if (appliedDamage <= 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is immune to fire and heat and ignores incendiary cloud damage (rolled {rolledDamage})."));
            else
                events.Add(new CombatEvent($"Incendiary cloud burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} remains inside the incendiary cloud ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"The incendiary cloud around {monster.DisplayName} dissipates."));

            if (!monster.IsAlive)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is consumed by flames."));
                return MonsterTurnFlow.EndTurn;
            }
        }

        var monsterMirrorCount = session.GetMonsterMirrorImageCount(monster);
        if (monsterMirrorCount > 0)
        {
            var remaining = session.TickMonsterMirrorImage(monster);
            if (remaining <= 0)
                events.Add(new CombatEvent($"{monster.DisplayName}'s mirror image fades."));
        }

        if (monster.HasStatus(MonsterStatus.SummonInsects))
        {
            var beforeHp = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - 2);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
            var remaining = monster.TickStatus(MonsterStatus.SummonInsects);

            events.Add(new CombatEvent($"Insects bite {monster.DisplayName} for {actualDamage}. HP {beforeHp}->{monster.CurrentHitPoints}."));
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} remains swarmed by insects ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"The insect swarm around {monster.DisplayName} disperses."));

            if (!monster.IsAlive)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is torn down by the insect swarm."));
                return MonsterTurnFlow.EndTurn;
            }
        }

        if (monster.HasStatus(MonsterStatus.DeathFog))
        {
            var rolledDamage = _dice.Roll(10);
            var beforeHp = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
            var remaining = monster.TickStatus(MonsterStatus.DeathFog);

            events.Add(new CombatEvent($"Death fog engulfs {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} remains in the death fog ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"The death fog around {monster.DisplayName} dissipates."));

            if (!monster.IsAlive)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} dies in the death fog."));
                return MonsterTurnFlow.EndTurn;
            }
        }

        if (monster.HasStatus(MonsterStatus.WallOfFire))
        {
            var resistedWallOfFire = SpellDamageSaveHelper.IsNegatedByMagicResistance(monster, _rng, "Wall of Fire");
            var remaining = monster.TickStatus(MonsterStatus.WallOfFire);

            if (resistedWallOfFire)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} negates wall of fire damage with magic resistance."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains within wall of fire ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The wall of fire around {monster.DisplayName} is no longer affecting it."));

                return MonsterTurnFlow.EndTurn;
            }

            var beforeHp = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - 10);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);

            events.Add(new CombatEvent($"Wall of fire burns {monster.DisplayName} for {actualDamage}. HP {beforeHp}->{monster.CurrentHitPoints}."));
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} remains within wall of fire ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"The wall of fire around {monster.DisplayName} is no longer affecting it."));

            if (!monster.IsAlive)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is consumed by the wall of fire."));
                return MonsterTurnFlow.EndTurn;
            }
        }

        if (monster.HasStatus(MonsterStatus.AcidArrow))
        {
            var rolledDamage = _dice.Roll(4) + _dice.Roll(4);
            var beforeHp = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
            var remaining = monster.TickStatus(MonsterStatus.AcidArrow);

            events.Add(new CombatEvent($"Acid burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is still corroding ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"The acid on {monster.DisplayName} dissipates."));

            if (!monster.IsAlive)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is destroyed."));
                return MonsterTurnFlow.EndTurn;
            }
        }

        if (monster.HasStatus(MonsterStatus.PermanentIllusion))
        {
            var rolledDamage = 0;
            for (int i = 0; i < 16; i++)
                rolledDamage += _dice.Roll(6);

            var beforeHp = monster.CurrentHitPoints;
            monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
            var actualDamage = beforeHp - monster.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);

            events.Add(new CombatEvent($"{monster.DisplayName} is tormented by permanent illusion for {actualDamage} damage (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));

            if (!monster.IsAlive)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} dies from terror and shock."));
                return MonsterTurnFlow.EndTurn;
            }
        }

        if (monster.HasStatus(MonsterStatus.Asleep))
        {
            var remaining = monster.TickStatus(MonsterStatus.Asleep);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is asleep ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} wakes up."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Webbed))
        {
            var remaining = monster.TickStatus(MonsterStatus.Webbed);
            var trappedRounds = session.IncrementMonsterWebTrappedRounds(monster);
            var fullTurnsTrapped = trappedRounds / 10;

            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is held in webs ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} breaks free of the webs."));

            if (fullTurnsTrapped > 0 && monster.IsAlive)
            {
                var suffocationChance = Math.Min(100, fullTurnsTrapped * 5);
                var suffocationRoll = _dice.Roll(100);
                events.Add(new CombatEvent($"{monster.DisplayName} makes a suffocation check at {suffocationChance}% (rolled {suffocationRoll})."));
                if (suffocationRoll <= suffocationChance)
                {
                    monster.CurrentHitPoints = 0;
                    monster.SetStatus(MonsterStatus.Webbed, 0);
                    session.ResetMonsterWebTrappedRounds(monster);
                    events.Add(new CombatEvent($"{monster.DisplayName} suffocates in the webs."));
                    return MonsterTurnFlow.EndTurn;
                }
            }

            if (remaining <= 0)
                session.ResetMonsterWebTrappedRounds(monster);

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Entangled))
        {
            var remaining = monster.TickStatus(MonsterStatus.Entangled);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is entangled ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} breaks free of entangle."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Snared))
        {
            var remaining = monster.TickStatus(MonsterStatus.Snared);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is snared ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} breaks free of the snare."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.TurnedUndead))
        {
            var remaining = monster.TickStatus(MonsterStatus.TurnedUndead);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is turned and cannot attack ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} is no longer turned."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Paralyzed))
        {
            var remaining = monster.TickStatus(MonsterStatus.Paralyzed);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is paralyzed ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} breaks free."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Stunned))
        {
            var remaining = monster.TickStatus(MonsterStatus.Stunned);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is stunned ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} is no longer stunned."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Disarmed))
        {
            var remaining = monster.TickStatus(MonsterStatus.Disarmed);
            events.Add(new CombatEvent(remaining > 0
                ? $"{monster.DisplayName} is running after its weapon ({remaining} round(s) remaining)."
                : $"{monster.DisplayName} retrieves its weapon."));

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Charmed))
        {
            ResolveCharmedMonsterTurn(session, monster, events);
            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.WalledOff))
        {
            if (WallOfStoneRules.IsBurrower(monster))
            {
                if (monster.TickStatus(MonsterStatus.WalledOff) > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} tunnels through the wall of stone."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} bursts through the wall of stone!"));
            }
            else if (WallOfStoneRules.IsBreacher(monster) && _dice.Roll(3) == 1)
            {
                var how = _dice.Roll(4) switch
                {
                    1 => "disintegrates the wall of stone",
                    2 => "teleports through the wall of stone",
                    3 => "passes around the wall of stone",
                    _ => "finally batters down the wall of stone"
                };
                monster.SetStatus(MonsterStatus.WalledOff, 0);
                events.Add(new CombatEvent($"{monster.DisplayName} {how}!"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is held back by the wall of stone."));
            }

            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Mazed))
        {
            var remaining = monster.TickStatus(MonsterStatus.Mazed);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is trapped in the maze ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} finds the exit and returns from the maze."));

            return MonsterTurnFlow.EndTurn;
        }

        if (TryResolveConfusedMonsterTurn(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (monster.HasStatus(MonsterStatus.Panicked))
        {
            var remaining = monster.TickStatus(MonsterStatus.Panicked);
            var fleeRoll = _dice.Roll(100);
            if (fleeRoll <= 50)
            {
                monster.HasFled = true;
                monster.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{monster.DisplayName} flees in panic!"));
                return MonsterTurnFlow.EndTurn;
            }

            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} panics and cannot act ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} regains its nerve."));

            return MonsterTurnFlow.EndTurn;
        }

        var isSlowed = monster.HasStatus(MonsterStatus.Slowed);
        if (isSlowed)
        {
            var remaining = monster.TickStatus(MonsterStatus.Slowed);
            if (remaining > 0)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is slowed ({remaining} round(s) remaining)."));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is no longer slowed."));
                isSlowed = false;
            }
        }

        if (isSlowed && session.RoundNumber % 2 != 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} is too slowed to attack this round."));
            return MonsterTurnFlow.EndTurn;
        }

        if (monster.HasStatus(MonsterStatus.Unconscious))
        {
            var remaining = monster.TickStatus(MonsterStatus.Unconscious);
            if (remaining > 0)
                events.Add(new CombatEvent($"{monster.DisplayName} is unconscious ({remaining} round(s) remaining)."));
            else
                events.Add(new CombatEvent($"{monster.DisplayName} regains consciousness."));

            return MonsterTurnFlow.EndTurn;
        }

        if (IsShrieker(monster))
        {
            events.Add(new CombatEvent($"{monster.DisplayName} shrieks and does not make physical attacks."));
            return MonsterTurnFlow.EndTurn;
        }

        var isPiercer = IsPiercer(monster);
        if (isPiercer)
        {
            var climbRounds = session.GetPiercerClimbRounds(monster);
            if (climbRounds > 0)
            {
                var remainingClimb = session.TickPiercerClimbRounds(monster);
                if (remainingClimb > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} climbs up the wall ({remainingClimb} round(s) remaining) and cannot attack."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} finishes climbing and can attack next round."));

                return MonsterTurnFlow.EndTurn;
            }
        }

        return MonsterTurnFlow.Proceed;
    }

    private MonsterTurnFlow ResolveMonsterLatePowers(RoundState state, MonsterInstance monster)
    {
        var session = state.Session;
        var events = state.Events;

        if (TryResolvePyroHydraBreath(monster, session, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterLayOnHands(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterBodyHeal(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterColdRadiation(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        if (TryResolveMonsterAssassination(session, monster, events))
            return MonsterTurnFlow.EndTurn;

        return MonsterTurnFlow.Proceed;
    }

    private bool ResolveMonsterMeleeAttacks(RoundState state, MonsterInstance monster)
    {
        var session = state.Session;
        var events = state.Events;
        var parrying = state.Parrying;
        var isPiercer = IsPiercer(monster);

        var monsterBackstabInfo = TryGetMonsterThiefBackstabInfo(session, monster);

        var attacks = monster.Template.Attacks.Count > 0 ? monster.Template.Attacks : new List<Adnd.Core.Monsters.MonsterAttack> { new() { NumberOfAttacks = 1, Damage = "1d4", Name = "Claw" } };

        foreach (var attack in attacks)
        {
            if (IsPurpleWorm(monster) && string.Equals(attack.Name, "Sting", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanPurpleWormSting(session, monster))
                {
                    LogPurpleWormRule(events, $"{monster.DisplayName} cannot use its tail sting: it needs rear defense or a spacious area with at least four exposed opponents.");
                    continue;
                }
                LogPurpleWormRule(events, $"{monster.DisplayName} uses its tail sting in {(GetPurpleWormRearAttackers(session, monster).Any() ? "rear defense" : "a spacious area against numerous opponents")}: 2d4 damage, save vs. poison or die.");
            }
            int attackCount = Math.Max(1, attack.NumberOfAttacks);
            if (_golemHasteRounds.GetValueOrDefault(monster) > 0)
                attackCount *= 2;
            for (int i = 0; i < attackCount; i++)
            {
                var swingResult = ResolveMonsterAttackSwing(state, monster, attack, monsterBackstabInfo);
                if (swingResult == AttackSwingResult.EndRound)
                    return true;
                if (swingResult == AttackSwingResult.StopAttacking)
                    break;
            }
        }

        session.RearAttackers.Remove(monster);
        if (isPiercer)
        {
            session.SetPiercerClimbRounds(monster, 5);
            events.Add(new CombatEvent($"{monster.DisplayName} starts climbing up the wall and cannot attack for 5 rounds."));
        }

        return false;
    }

    private enum AttackSwingResult
    {
        Next,
        StopAttacking,
        EndRound,
    }

    private AttackSwingResult ResolveMonsterAttackSwing(
        RoundState state,
        MonsterInstance monster,
        Adnd.Core.Monsters.MonsterAttack attack,
        (bool Enabled, int Multiplier) monsterBackstabInfo)
    {
        var session = state.Session;
        var events = state.Events;
        var parrying = state.Parrying;

        if (TryResolveBlinkDogPackRetreat(session, monster, events))
            return AttackSwingResult.Next;

        var rearAttackers = IsPurpleWorm(monster) && string.Equals(attack.Name, "Sting", StringComparison.OrdinalIgnoreCase)
            ? GetPurpleWormRearAttackers(session, monster).ToList()
            : new List<Character>();
        var target = rearAttackers.Count > 0 ? rearAttackers[_dice.Roll(rearAttackers.Count) - 1] : SelectMonsterTarget(session);
        if (target is null)
        {
            if (!session.AliveParty.Any())
            {
                session.Outcome = CombatOutcome.Defeat;
                events.Add(new CombatEvent("The party is defeated."));
                return AttackSwingResult.EndRound;
            }
            return AttackSwingResult.StopAttacking;
        }

        if (!CanMonsterLocateTarget(monster, target, events))
            return AttackSwingResult.Next;

        var invulnerabilityAcBonus = target.HasActivePotionInvulnerability
            ? target.PotionInvulnerabilityArmorClassBonus
            : 0;
        var targetAc = (target.ArmorClass - invulnerabilityAcBonus) + (parrying.Contains(target.Name) ? 2 : 0);
        var defenderAcBonus = GetDefenderArmorClassBonus(target, attack);
        if (defenderAcBonus > 0)
            targetAc += defenderAcBonus;

        var blinkBehindAttack = TryResolveBlinkDogTeleportBehind(monster, target, events);
        if (blinkBehindAttack)
        {
            var dexAcBonus = Math.Min(0, AbilitiesTables.DexterityACModifier(target.Abilities.Dexterity));
            if (dexAcBonus < 0)
            {
                targetAc -= dexAcBonus;
                events.Add(new CombatEvent($"{target.Name} loses Dexterity AC bonus against {monster.DisplayName}'s rear blink attack."));
            }
        }

        var thac0 = GetMonsterThac0(monster);
        if (state.ChantActiveAtRoundStart || state.PrayerActiveAtRoundStart)
            thac0 += 1;
        if (monsterBackstabInfo.Enabled)
            thac0 -= 4;
        if (blinkBehindAttack)
        {
            thac0 -= 2;
            events.Add(new CombatEvent($"{monster.DisplayName} gains +2 to hit from partial invisibility while blinking."));
        }
        var incapacitatedCharacterReason = GetIncapacitatedTargetReason(target, session);
        if (!string.IsNullOrWhiteSpace(incapacitatedCharacterReason))
        {
            thac0 -= 4;
            RuleApplicationInfo.PublishLinked(
                "PHB",
                "38WeaponTypeToHitAdjustment",
                $"Weapon type to-hit adjustment: {monster.DisplayName} attacks {target.Name}. Weapon type: natural attack. Adjustment: +4 because target is {incapacitatedCharacterReason}.");
        }
        if (monster.HasStatus(MonsterStatus.Blinded)
            && !target.HasStatus(CharacterStatus.Invisible))
        {
            thac0 += 4;
        }
        int needed = Math.Clamp(thac0 - targetAc, 1, 20);
        var cloakDisplacementAutoMiss = ShouldApplyCloakOfDisplacementAutoMiss(session, target);
        int roll = cloakDisplacementAutoMiss ? 0 : _dice.Roll(20);
        var hasDoubleDamageOnNaturalTwenty = HasAnySpecialAbility(monster, "Double Damage on Natural 20");

        if (GameRulesProvider.Current.ShowToHitRoll)
        {
            var toHitMessage = $"TO-HIT: {monster.DisplayName} THAC0 {thac0}, {target.Name} AC {targetAc}, needs {needed} on 1d20, rolled {roll}.";
            events.Add(new CombatEvent(toHitMessage));
            RuleApplicationInfo.PublishLinked("DMG", "74ToHitTablesFightersAndMonsters", toHitMessage);
        }

        if (cloakDisplacementAutoMiss)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} attacks where {target.Name} appears to be, but the cloak's displacement causes the strike to miss!"));
            return AttackSwingResult.Next;
        }

        if (roll >= needed && session.IsBlinking(target.Name) && _dice.Roll(100) <= 50)
        {
            events.Add(new CombatEvent($"{target.Name} blinks out as {monster.DisplayName} strikes; the attack misses."));
            return AttackSwingResult.Next;
        }

        if (roll >= needed)
        {
            var imageCount = session.GetMirrorImageCount(target.Name);
            if (imageCount > 0)
            {
                var imageHitRoll = _dice.Roll(imageCount + 1);
                if (imageHitRoll > 1)
                {
                    var remainingImages = session.RemoveOneMirrorImage(target.Name);
                    events.Add(new CombatEvent($"{monster.DisplayName} hits a mirror image of {target.Name}!"));
                    events.Add(new CombatEvent($"{target.Name} has {remainingImages} mirror image(s) remaining."));
                    if (remainingImages <= 0)
                        events.Add(new CombatEvent($"{target.Name} has no mirror images left."));
                    return AttackSwingResult.Next;
                }
            }

            if (target.HasActivePotionInvulnerability
                && !IsMonsterAttackConsideredMagical(monster, attack)
                && (monster.Template.HitDice < 4 || !MonsterHasMagicalProperties(monster)))
            {
                events.Add(new CombatEvent($"{monster.DisplayName}'s attack cannot harm {target.Name} due to Potion of Invulnerability."));
                return AttackSwingResult.Next;
            }

            int damage = RollDamage(attack.Damage);
            var enemyDamagePenaltyApplied = state.ChantActiveAtRoundStart || state.PrayerActiveAtRoundStart;
            if (enemyDamagePenaltyApplied)
                damage = Math.Max(1, damage - 1);
            if (monsterBackstabInfo.Enabled)
            {
                var baseDamage = damage;
                damage *= monsterBackstabInfo.Multiplier;
                events.Add(new CombatEvent($"{monster.DisplayName} backstabs! +4 to hit, damage x{monsterBackstabInfo.Multiplier} ({baseDamage}->{damage})."));
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "27Backstab",
                    $"{monster.DisplayName} uses backstab from surprise/behind: +4 to hit, damage multiplier x{monsterBackstabInfo.Multiplier} ({baseDamage}->{damage}).");
            }

            if (hasDoubleDamageOnNaturalTwenty && roll == 20)
            {
                var beforeDouble = damage;
                damage *= 2;
                events.Add(new CombatEvent($"{monster.DisplayName} rolled a natural 20: Double Damage on Natural 20 ({beforeDouble}->{damage})."));
            }

            if (target.HasStatus(CharacterStatus.LycanthropeTransformed)
                && !CanMonsterHarmLycanthropeForm(monster))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} hits {target.Name} with {attack.Name}, but lycanthrope form ignores non-silver, non-magical attacks."));
                return AttackSwingResult.Next;
            }

            target.CurrentHitPoints -= damage;
            var enemyDamageModText = enemyDamagePenaltyApplied ? " (chant/prayer -1 damage)" : string.Empty;
            events.Add(new CombatEvent($"{monster.DisplayName} hits {target.Name} with {attack.Name} for {damage}.{enemyDamageModText}"));
            WakeCharacterIfAsleepAfterDamage(target, damage, events);

            if (damage > 0
                && target.CanBecomeLycanthropeNow()
                && target.TriggerLycanthropeTransformationByDamage())
            {
                events.Add(new CombatEvent($"{target.Name} transforms into lycanthrope form after taking damage!"));
            }

            TryApplyDestroyMetalOnMonsterHit(monster, target, events);
            TryApplyDissolveWoodOnMonsterHit(monster, target, events);

            if (state.ChantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, target.Name, StringComparison.OrdinalIgnoreCase) && damage > 0)
            {
                session.BreakChant();
                state.ChantActiveAtRoundStart = false;
                events.Add(new CombatEvent($"{target.Name}'s chant is interrupted and ends."));
            }

            if (target.CurrentHitPoints <= 0)
            {
                target.CurrentHitPoints = 0;
                target.AddStatus(CharacterStatus.Dead);
                TryApplyMummyRotOnDeath(monster, target, events);
                events.Add(new CombatEvent($"{target.Name} is slain!"));
            }
            else
            {
                ApplyMonsterOnHitEffects(state, monster, target, attack, damage, roll, needed);
            }
        }
        else
        {
            events.Add(new CombatEvent($"{monster.DisplayName} misses {target.Name}."));
        }

        return AttackSwingResult.Next;
    }

    private void ApplyMonsterOnHitEffects(
        RoundState state,
        MonsterInstance monster,
        Character target,
        Adnd.Core.Monsters.MonsterAttack attack,
        int damage,
        int roll,
        int needed)
    {
        var session = state.Session;
        var events = state.Events;

        ApplyPurpleWormHit(session, monster, target, attack, roll, needed, events);
        if (!IsAlive(target) || session.IsSwallowed(target.Name))
            return;
        TryApplyLycanthropy(monster, target, attack.Name, damage, events);
        TryApplyEarSeekerDisease(monster, target, events);
        TryApplyInfestation(monster, target, events);
        TryApplyTickDisease(monster, target, events);
        TryApplyEnergyDrain(monster, target, session, events);
        TryApplyLevel8OnHit(session, monster, target, attack.Name, events);
        TryApplyPitFiendTail(monster, target, attack.Name, events);
        TryApplyLevel10OnHit(monster, target, attack.Name, events);
        if (!IsAlive(target))
            return;
        TryApplyStrengthDrain(monster, target, events);
        TryApplyDrainBlood(monster, target, session, events);
        TryApplyRotGrubExposure(monster, target, events);
        TryApplyGiantRatDisease(monster, target, events);
        TryApplyOtyughDisease(monster, target, attack.Name, events);
        TryApplySalamanderHeat(monster, target, events);
        if (!IsAlive(target))
            return;
        TryApplyRottingDisease(monster, target, events);
        TryApplyPetrificationFromSpecialAttacks(monster, target, events);
        TryApplyBoneHook(monster, target, events);
        TryApplyIceDevilSpear(monster, target, attack.Name, events);
        TryApplyBarbedFear(monster, target, events);
        TryApplyHornedDevilAttack(monster, target, attack.Name, events);
        if (!IsAlive(target))
            return;
        TryApplyErinyesDagger(monster, target, session, events);
        TryApplyMedusaAspBite(monster, target, events);
        if (!IsAlive(target))
            return;
        TryApplyWebsSpecialAttack(monster, target, session, events);
        if (TryApplyGiantSnakePoison(monster, target, events, state.ChantActiveAtRoundStart, state.PrayerActiveAtRoundStart))
            return;
        if (TryApplyPoisonSpit(monster, target, events, state.ChantActiveAtRoundStart, state.PrayerActiveAtRoundStart))
            return;
        if (TryApplyPoisonSpores(monster, target, events, state.ChantActiveAtRoundStart, state.PrayerActiveAtRoundStart))
            return;

        if (HasAnySpecialAbility(monster, "Poison", "Weak Poison", "Huge Spider Poison", "Deadly Poison", "Phase Spider Poison"))
        {
            if (target.IsMonkImmuneToPoison())
            {
                events.Add(new CombatEvent($"{target.Name} is immune to poison."));
                return;
            }

            var hasDeadlyPoison = HasSpecialAbility(monster, "Deadly Poison");
            var poisonRoll = _dice.Roll(100);
            if (hasDeadlyPoison || poisonRoll <= 70)
            {
                var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                    target,
                    _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
                var isWeakPoison = HasSpecialAbility(monster, "Weak Poison");
                var isHugeSpiderPoison = HasSpecialAbility(monster, "Huge Spider Poison");
                var isPhaseSpiderPoison = HasSpecialAbility(monster, "Phase Spider Poison");
                var poisonSaveBonus = isWeakPoison ? 2 : isHugeSpiderPoison ? 1 : isPhaseSpiderPoison ? -2 : 0;
                if (poisonSaveBonus != 0)
                    saveTarget = Math.Max(1, saveTarget - poisonSaveBonus);
                if (state.ChantActiveAtRoundStart || state.PrayerActiveAtRoundStart)
                    saveTarget = Math.Max(1, saveTarget - 1);
                var saveRoll = _dice.Roll(20);
                if (saveRoll >= saveTarget)
                {
                    var poisonLabel = isWeakPoison ? "weak poison" : isPhaseSpiderPoison ? "phase spider poison" : "huge spider poison";
                    var resistMessage = hasDeadlyPoison
                        ? $"{target.Name} resists deadly poison (save {saveRoll} vs {saveTarget})."
                        : poisonSaveBonus != 0
                            ? $"{target.Name} resists {poisonLabel} (save {saveRoll} vs {saveTarget}, {poisonSaveBonus:+#;-#} save modifier)."
                            : $"{target.Name} resists poison (save {saveRoll} vs {saveTarget}).";
                    events.Add(new CombatEvent(resistMessage));
                    RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", resistMessage);
                }
                else if (hasDeadlyPoison)
                {
                    var beforeHp = target.CurrentHitPoints;
                    target.CurrentHitPoints = 0;
                    target.RemoveStatus(CharacterStatus.Poisoned);
                    target.AddStatus(CharacterStatus.Dead);
                    var failedDeadlyPoisonSaveMessage = $"{target.Name} fails save ({saveRoll} vs {saveTarget}) against deadly poison from {monster.DisplayName} and dies instantly. HP {beforeHp}->0.";
                    events.Add(new CombatEvent(failedDeadlyPoisonSaveMessage));
                    RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", failedDeadlyPoisonSaveMessage);
                }
                else if (!target.HasStatus(CharacterStatus.Poisoned))
                {
                    target.AddStatus(CharacterStatus.Poisoned);
                    var failedPoisonSaveMessage = poisonSaveBonus != 0
                        ? $"{target.Name} is poisoned by {(isWeakPoison ? "weak poison" : isPhaseSpiderPoison ? "phase spider poison" : "huge spider poison")} from {monster.DisplayName}! (save {saveRoll} vs {saveTarget}, {poisonSaveBonus:+#;-#} save modifier)"
                        : $"{target.Name} is poisoned by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})";
                    events.Add(new CombatEvent(failedPoisonSaveMessage));
                    RuleApplicationInfo.PublishLinked("DMG", "79 Savethrow", failedPoisonSaveMessage);
                }
            }

        }

        if (HasAnySpecialAbility(monster, "Paralyze", "Paralyzation", "Paralysis"))
        {
            TryApplyMonsterParalyzation(monster, target, session, events);
        }

        TryApplyConstrictionOnHit(monster, target, session, events);
    }
}
