using Adnd.Core.Assassination;
using Adnd.Core.Characters;
using Adnd.Core.Combat.Actions;
using Adnd.Core.Combat.Events;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Dices;
using Adnd.Core.Items;
using Adnd.Core.Monsters;
using Adnd.Core.Spells;
using Adnd.Core.Spells.Casting;
using Adnd.Core.Spells.Casting.Handlers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adnd.Core.Combat.Resolution;

public sealed class CombatResolver
{
    private readonly Random _rng = new();//for assassination rolls 

    private readonly IDice _dice;
    private readonly SpellCastingService? _spellCastingService;
    private readonly CharacterSavingThrowService _savingThrowService = new();

    public CombatResolver(IDice? dice = null, SpellCastingService? spellCastingService = null)
    {
        _dice = dice ?? new SystemDice();
        _spellCastingService = spellCastingService;
    }

    public List<CombatEvent> ResolveRound(CombatSession session, IReadOnlyDictionary<string, CombatActionType> partyActions)
    {
        var converted = new Dictionary<string, CombatAction>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type) in partyActions)
        {
            if (type is not (CombatActionType.Spell or CombatActionType.CastSpell))
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            var caster = session.Party.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (caster == null || _spellCastingService == null)
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            var spell = _spellCastingService.FindFirstCastableSpell(caster, SpellUseContext.Combat);
            if (spell == null)
            {
                converted[name] = CombatAction.OfType(type);
                continue;
            }

            SpellCastTarget? target = null;
            if (spell.RangeType == SpellRangeType.Enemy)
            {
                var enemy = session.AliveMonsters.FirstOrDefault();
                if (enemy != null)
                    target = SpellCastTarget.Enemy(enemy.Index);
            }
            else if (spell.RangeType == SpellRangeType.Self)
            {
                target = SpellCastTarget.Ally(caster);
            }
            else
            {
                var ally = session.AliveParty.OrderBy(a => a.CurrentHitPoints).FirstOrDefault() ?? caster;
                target = SpellCastTarget.Ally(ally);
            }

            converted[name] = new CombatAction
            {
                Type = CombatActionType.CastSpell,
                SpellId = spell.Id,
                Target = target
            };
        }

        return ResolveRound(session, converted);
    }

    public List<CombatEvent> ResolveRound(CombatSession session, IReadOnlyDictionary<string, CombatAction> partyActions)
    {
        ApplyRingProtectionAuras(session);
        ApplyRingWizardryEffects(session);

        var events = new List<CombatEvent>
        {
            new($"-- Round {session.RoundNumber} --")
        };

        if (session.Outcome != CombatOutcome.InProgress)
            return events;

        var parrying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool partyAttemptedRun = false;

        var chantActiveAtRoundStart = session.IsChantActive;
        var prayerActiveAtRoundStart = session.IsPrayerActive;

        if (chantActiveAtRoundStart)
        {
            var chantCaster = session.Party.FirstOrDefault(p => string.Equals(p.Name, session.ActiveChantCasterName, StringComparison.OrdinalIgnoreCase));
            if (chantCaster == null || !IsAlive(chantCaster))
            {
                session.BreakChant();
                chantActiveAtRoundStart = false;
                events.Add(new CombatEvent("Chant ends as the chanter can no longer continue."));
            }
        }

        if (prayerActiveAtRoundStart)
        {
            var remainingPrayerRounds = session.ActivePrayerRounds;
            var prayerCaster = string.IsNullOrWhiteSpace(session.ActivePrayerCasterName)
                ? "A cleric"
                : session.ActivePrayerCasterName;
            events.Add(new CombatEvent($"{prayerCaster}'s Prayer remains in effect ({remainingPrayerRounds} round(s) remaining)."));
        }

        foreach (var member in session.Party)
        {
            if (!IsAlive(member))
                continue;

            if (session.RoundNumber == 1 && session.PartySurprisedRound1)
            {
                events.Add(new CombatEvent($"{member.Name} is surprised and cannot act in round 1."));
                continue;
            }

            var drainRemaining = session.GetPartyDrainBloodRemaining(member.Name);
            if (drainRemaining > 0)
            {
                var drainRoll = _dice.Roll(4);
                var drainAmount = session.ConsumePartyDrainBlood(member.Name, drainRoll);
                if (drainAmount > 0)
                {
                    var beforeHp = member.CurrentHitPoints;
                    member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - drainAmount);
                    var actualDrain = beforeHp - member.CurrentHitPoints;
                    WakeCharacterIfAsleepAfterDamage(member, actualDrain, events);

                    var remainingAfter = session.GetPartyDrainBloodRemaining(member.Name);
                    events.Add(new CombatEvent($"{member.Name} suffers Drain Blood for {actualDrain} (rolled {drainRoll}). Remaining drain: {remainingAfter}. HP {beforeHp}->{member.CurrentHitPoints}."));

                    if (member.CurrentHitPoints <= 0)
                    {
                        member.AddStatus(CharacterStatus.Dead);
                        events.Add(new CombatEvent($"{member.Name} dies from blood loss!"));
                        continue;
                    }
                }
            }

            var veryHotFireExposureRounds = session.GetPartyVeryHotFireExposureRounds(member.Name);
            if (veryHotFireExposureRounds > 0)
            {
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

                    if (member.CurrentHitPoints <= 0)
                    {
                        member.AddStatus(CharacterStatus.Dead);
                        events.Add(new CombatEvent($"{member.Name} is consumed by the conflagration!"));
                        continue;
                    }
                }

                var exposureRemaining = session.TickPartyVeryHotFireExposure(member.Name);
                if (exposureRemaining > 0)
                    events.Add(new CombatEvent($"{member.Name} remains within very hot fire ({exposureRemaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{member.Name} is no longer directly within very hot fire."));
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

                continue;
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
            if (mirrorRounds > 0)
            {
                var imageCount = session.GetMirrorImageCount(member.Name);
                var remaining = session.TickMirrorImage(member.Name);
                if (remaining <= 0 && imageCount > 0)
                    events.Add(new CombatEvent($"{member.Name}'s mirror image fades."));
            }

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

                continue;
            }

            if (member.HasStatus(CharacterStatus.Paralyzed))
            {
                var remainingParalysis = member.TickParalysisRound();
                events.Add(new CombatEvent(remainingParalysis > 0
                    ? $"{member.Name} is paralyzed and cannot act ({remainingParalysis} round(s) remaining)."
                    : $"{member.Name} is no longer paralyzed."));
                continue;
            }

            if (member.RotGrubDeathRoundsRemaining > 0)
            {
                member.RotGrubDeathRoundsRemaining = Math.Max(0, member.RotGrubDeathRoundsRemaining - 1);
                if (member.RotGrubDeathRoundsRemaining <= 0)
                {
                    member.CurrentHitPoints = 0;
                    member.AddStatus(CharacterStatus.Dead);
                    events.Add(new CombatEvent($"{member.Name} dies as rot grubs reach the heart."));
                    continue;
                }
            }

            if (!partyActions.TryGetValue(member.Name, out var action))
                action = CombatAction.OfType(CombatActionType.Parry);

            switch (action.Type)
            {
                case CombatActionType.Fight:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolvePartyAttack(session, member, action, events);
                    break;
                case CombatActionType.Parry:
                    parrying.Add(member.Name);
                    events.Add(new CombatEvent($"{member.Name} parries."));
                    break;
                case CombatActionType.UseItem:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolvePartyUseItem(session, member, action, events);
                    break;
                case CombatActionType.DispellUndead:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveDispellUndead(session, member, action, events);
                    break;
                case CombatActionType.LayOnHands:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveLayOnHands(session, member, action, events);
                    break;
                case CombatActionType.MonkBodyHeal:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolveMonkBodyHeal(session, member, events);
                    break;
                case CombatActionType.Spell:
                case CombatActionType.CastSpell:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    ResolvePartySpell(session, member, action, events);
                    break;
                case CombatActionType.Run:
                    if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        session.BreakChant();
                        chantActiveAtRoundStart = false;
                        events.Add(new CombatEvent($"{member.Name} stops chanting. Chant ends."));
                    }
                    partyAttemptedRun = true;
                    events.Add(new CombatEvent($"{member.Name} tries to run!"));
                    break;
            }

            if (!session.AliveMonsters.Any())
            {
                session.Outcome = CombatOutcome.Victory;
                events.Add(new CombatEvent("All monsters are defeated!"));
                return FinalizeRound(session, events);
            }
        }

        if (partyAttemptedRun)
        {
            var allEncounterGroupsSnared = session.GetDistinctGroupIds().All(groupId =>
            {
                var livingInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
                return livingInGroup.Count == 0 || livingInGroup.All(m => m.HasStatus(MonsterStatus.Snared));
            });

            if (allEncounterGroupsSnared)
            {
                session.Outcome = CombatOutcome.Escaped;
                events.Add(new CombatEvent("All enemy groups are snared. The party escapes automatically!"));
                return FinalizeRound(session, events);
            }

            var runRoll = _dice.Roll(100);
            if (runRoll <= 50)
            {
                session.Outcome = CombatOutcome.Escaped;
                events.Add(new CombatEvent("The party escapes!"));
                return FinalizeRound(session, events);
            }

            events.Add(new CombatEvent("The party fails to escape!"));
        }

        foreach (var monster in session.AliveMonsters.ToList())
        {
            if (!monster.IsAlive || !session.Monsters.Contains(monster))
                continue;

            ApplyMonsterRegeneration(monster, events);

            if (session.RoundNumber == 1 && session.MonstersSurprisedRound1)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is surprised and cannot act in round 1."));
                continue;
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
                if (HasAnySpecialAbility(monster,
                        "Level 1 Mage spells",
                        "Level 1 Magic-User spells",
                        "Level 5 Mage spells",
                        "Level 5 Magic-User spells",
                        "Level 5 Illusionist spells",
                        "Level 4 Mage spells",
                        "Level 4 Magic-User spells",
                        "Level 4 Illusionist spells",
                        "Level 3 Mage spells",
                        "Level 3 Magic-User spells",
                        "Level 3 Illusionist spells",
                        "Level 2 Illusionist spells",
                        "Level 3 Priest spells",
                        "Level 3 Cleric spells",
                        "Level 5 Priest spells",
                        "Level 5 Cleric spells",
                        "Level 1 Priest spells",
                        "Level 1 Cleric spells",
                        "Level 1 Druid spells",
                        "Level 5 Druid spells",
                        "Level 1 Illusionist spells"))
                {
                    events.Add(new CombatEvent(remainingSilence > 0
                        ? $"{monster.DisplayName} is silenced and cannot cast spells ({remainingSilence} round(s) remaining)."
                        : $"{monster.DisplayName} is no longer silenced."));
                }
            }

            if (!isSilenced && isFeebleminded
                && HasAnySpecialAbility(monster,
                    "Level 1 Mage spells",
                    "Level 1 Magic-User spells",
                    "Level 5 Mage spells",
                    "Level 5 Magic-User spells",
                    "Level 5 Illusionist spells",
                    "Level 4 Mage spells",
                    "Level 4 Magic-User spells",
                    "Level 4 Illusionist spells",
                    "Level 3 Mage spells",
                    "Level 3 Magic-User spells",
                    "Level 3 Illusionist spells",
                    "Level 2 Mage spells",
                    "Level 2 Magic-User spells",
                    "Level 2 Illusionist spells",
                    "Level 3 Priest spells",
                    "Level 3 Cleric spells",
                    "Level 5 Priest spells",
                    "Level 5 Cleric spells",
                    "Level 1 Priest spells",
                    "Level 1 Cleric spells",
                    "Level 1 Druid spells",
                    "Level 2 Druid spells",
                    "Level 3 Druid spells",
                    "Level 4 Druid spells",
                    "Level 5 Druid spells",
                    "Level 1 Illusionist spells"))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is feebleminded and cannot cast spells."));
            }

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 5 Illusionist spells")
                && ResolveLevel5IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 5 Mage spells", "Level 5 Magic-User spells")
                && ResolveLevel5MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 5 Druid spells")
                && ResolveLevel5DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 5 Priest spells", "Level 5 Cleric spells")
                && ResolveLevel5ClericSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 4 Illusionist spells")
                && ResolveLevel4IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 4 Mage spells", "Level 4 Magic-User spells")
                && ResolveLevel4MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 3 Mage spells", "Level 3 Magic-User spells")
                && ShouldTryMonsterLevel1SpellCast()
                && ResolveLevel3MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 3 Illusionist spells")
                && ResolveLevel3IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 3 Priest spells", "Level 3 Cleric spells")
                && ResolveLevel3ClericSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 2 Illusionist spells")
                && ShouldTryMonsterLevel1SpellCast()
                && ResolveLevel2IllusionistSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 2 Mage spells", "Level 2 Magic-User spells")
                && ShouldTryMonsterLevel1SpellCast()
                && ResolveLevel2MagicUserSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 1 Mage spells", "Level 1 Magic-User spells")
                && ShouldTryMonsterLevel1SpellCast())
            {
                ResolveLevel1MageSpells(monster, session, events);
                continue;
            }

            if (!isSilenced
                && !isFeebleminded
                && HasAnySpecialAbility(monster, "Level 1 Priest spells", "Level 1 Cleric spells")
                && ShouldTryMonsterLevel1SpellCast()
                && ResolveLevel1PriestSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 4 Druid spells")
                && ResolveLevel4DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 3 Druid spells")
                && ResolveLevel3DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 2 Druid spells")
                && ResolveLevel2DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 1 Druid spells")
                && ResolveLevel1DruidSpell(monster, session, events))
                continue;

            if (!isSilenced
                && !isFeebleminded
                && HasSpecialAbility(monster, "Level 1 Illusionist spells")
                && ShouldTryMonsterLevel1SpellCast()
                && ResolveLevel1IllusionistSpell(monster, session, events))
                continue;

            if (HasSpecialAbility(monster, "Dragon breath")
                && ResolveDragonBreathCurrentHp(monster, session, events))
                continue;

            if (TryGetHpDamageBreathDamage(monster, out var breathDamage)
                && ResolveHpDamageBreath(monster, breathDamage, session, events))
                continue;

            if (TryResolveMonsterPickPockets(session, monster, events))
                continue;

            if (monster.HasStatus(MonsterStatus.IncendiaryCloud))
            {
                var rolledDamage = _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6) + _dice.Roll(6);
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - rolledDamage);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
                var remaining = monster.TickStatus(MonsterStatus.IncendiaryCloud);

                events.Add(new CombatEvent($"Incendiary cloud burns {monster.DisplayName} for {actualDamage} (rolled {rolledDamage}). HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains inside the incendiary cloud ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The incendiary cloud around {monster.DisplayName} dissipates."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is consumed by flames."));
                    continue;
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
                    continue;
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
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.WallOfFire))
            {
                var beforeHp = monster.CurrentHitPoints;
                monster.CurrentHitPoints = Math.Max(0, monster.CurrentHitPoints - 10);
                var actualDamage = beforeHp - monster.CurrentHitPoints;
                WakeMonsterIfAsleepAfterDamage(monster, actualDamage, events);
                var remaining = monster.TickStatus(MonsterStatus.WallOfFire);

                events.Add(new CombatEvent($"Wall of fire burns {monster.DisplayName} for {actualDamage}. HP {beforeHp}->{monster.CurrentHitPoints}."));
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} remains within wall of fire ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"The wall of fire around {monster.DisplayName} is no longer affecting it."));

                if (!monster.IsAlive)
                {
                    events.Add(new CombatEvent($"{monster.DisplayName} is consumed by the wall of fire."));
                    continue;
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
                    continue;
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
                    continue;
                }
            }

            if (monster.HasStatus(MonsterStatus.Asleep))
            {
                var remaining = monster.TickStatus(MonsterStatus.Asleep);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is asleep ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} wakes up."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Entangled))
            {
                var remaining = monster.TickStatus(MonsterStatus.Entangled);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is entangled ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free of entangle."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Snared))
            {
                var remaining = monster.TickStatus(MonsterStatus.Snared);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is snared ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free of the snare."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.TurnedUndead))
            {
                var remaining = monster.TickStatus(MonsterStatus.TurnedUndead);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is turned and cannot attack ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} is no longer turned."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Paralyzed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Paralyzed);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is paralyzed ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} breaks free."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Stunned))
            {
                var remaining = monster.TickStatus(MonsterStatus.Stunned);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is stunned ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} is no longer stunned."));

                continue;
            }

            if (monster.HasStatus(MonsterStatus.Charmed))
            {
                ResolveCharmedMonsterTurn(session, monster, events);
                continue;
            }

            if (monster.HasStatus(MonsterStatus.Mazed))
            {
                var remaining = monster.TickStatus(MonsterStatus.Mazed);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is trapped in the maze ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} finds the exit and returns from the maze."));

                continue;
            }

            if (TryResolveConfusedMonsterTurn(session, monster, events))
                continue;

            if (monster.HasStatus(MonsterStatus.Panicked))
            {
                var remaining = monster.TickStatus(MonsterStatus.Panicked);
                var fleeRoll = _dice.Roll(100);
                if (fleeRoll <= 50)
                {
                    monster.CurrentHitPoints = 0;
                    events.Add(new CombatEvent($"{monster.DisplayName} flees in panic!"));
                    continue;
                }

                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} panics and cannot act ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} regains its nerve."));

                continue;
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
                continue;
            }

            if (monster.HasStatus(MonsterStatus.Unconscious))
            {
                var remaining = monster.TickStatus(MonsterStatus.Unconscious);
                if (remaining > 0)
                    events.Add(new CombatEvent($"{monster.DisplayName} is unconscious ({remaining} round(s) remaining)."));
                else
                    events.Add(new CombatEvent($"{monster.DisplayName} regains consciousness."));

                continue;
            }

            if (IsShrieker(monster))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} shrieks and does not make physical attacks."));
                continue;
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

                    continue;
                }
            }

            if (TryResolveMonsterLayOnHands(session, monster, events))
                continue;

            if (TryResolveMonsterAssassination(session, monster, events))
                continue;

            var monsterBackstabInfo = TryGetMonsterThiefBackstabInfo(session, monster);

            var attacks = monster.Template.Attacks.Count > 0 ? monster.Template.Attacks : new List<Adnd.Core.Monsters.MonsterAttack> { new() { NumberOfAttacks = 1, Damage = "1d4", Name = "Claw" } };

            foreach (var attack in attacks)
            {
                int attackCount = Math.Max(1, attack.NumberOfAttacks);
                for (int i = 0; i < attackCount; i++)
                {
                    var target = SelectMonsterTarget(session);
                    if (target is null)
                    {
                        session.Outcome = CombatOutcome.Defeat;
                        events.Add(new CombatEvent("The party is defeated."));
                        return FinalizeRound(session, events);
                    }

                    var invulnerabilityAcBonus = target.HasActivePotionInvulnerability
                        ? target.PotionInvulnerabilityArmorClassBonus
                        : 0;
                    var targetAc = (target.ArmorClass - invulnerabilityAcBonus) + (parrying.Contains(target.Name) ? 2 : 0);
                    var thac0 = GetMonsterThac0(monster);
                    if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
                        thac0 += 1;
                    if (monsterBackstabInfo.Enabled)
                        thac0 -= 4;
                    if (monster.HasStatus(MonsterStatus.Blinded)
                        && !target.HasStatus(CharacterStatus.Invisible))
                    {
                        thac0 += 4;
                    }
                    int needed = thac0 - targetAc;
                    int roll = _dice.Roll(20);

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
                                continue;
                            }
                        }

                        if (target.HasActivePotionInvulnerability
                            && !IsMonsterAttackConsideredMagical(monster, attack)
                            && (monster.Template.HitDice < 4 || !MonsterHasMagicalProperties(monster)))
                        {
                            events.Add(new CombatEvent($"{monster.DisplayName}'s attack cannot harm {target.Name} due to Potion of Invulnerability."));
                            continue;
                        }

                        int damage = RollDamage(attack.Damage);
                        var enemyDamagePenaltyApplied = chantActiveAtRoundStart || prayerActiveAtRoundStart;
                        if (enemyDamagePenaltyApplied)
                            damage = Math.Max(1, damage - 1);
                        if (monsterBackstabInfo.Enabled)
                        {
                            var baseDamage = damage;
                            damage *= monsterBackstabInfo.Multiplier;
                            events.Add(new CombatEvent($"{monster.DisplayName} backstabs! +4 to hit, damage x{monsterBackstabInfo.Multiplier} ({baseDamage}->{damage})."));
                        }
                        target.CurrentHitPoints -= damage;
                        var enemyDamageModText = enemyDamagePenaltyApplied ? " (chant/prayer -1 damage)" : string.Empty;
                        events.Add(new CombatEvent($"{monster.DisplayName} hits {target.Name} with {attack.Name} for {damage}.{enemyDamageModText}"));
                        WakeCharacterIfAsleepAfterDamage(target, damage, events);

                        if (chantActiveAtRoundStart && string.Equals(session.ActiveChantCasterName, target.Name, StringComparison.OrdinalIgnoreCase) && damage > 0)
                        {
                            session.BreakChant();
                            chantActiveAtRoundStart = false;
                            events.Add(new CombatEvent($"{target.Name}'s chant is interrupted and ends."));
                        }

                        if (target.CurrentHitPoints <= 0)
                        {
                            target.CurrentHitPoints = 0;
                            target.AddStatus(CharacterStatus.Dead);
                            events.Add(new CombatEvent($"{target.Name} is slain!"));
                        }
                        else
                        {
                            TryApplyEarSeekerDisease(monster, target, events);
                            TryApplyInfestation(monster, target, events);
                            TryApplyDrainBlood(monster, target, session, events);
                            TryApplyRotGrubExposure(monster, target, events);
                            TryApplyGiantRatDisease(monster, target, events);

                            if (HasSpecialAbility(monster, "Poison"))
                            {
                                if (target.IsMonkImmuneToPoison())
                                {
                                    events.Add(new CombatEvent($"{target.Name} is immune to poison."));
                                    continue;
                                }

                                var poisonRoll = _dice.Roll(100);
                                if (poisonRoll <= 70)
                                {
                                    var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                                        target,
                                        _savingThrowService.GetSaveTarget(target, SaveThrowType.ParalyzationPoisonDeath));
                                    if (chantActiveAtRoundStart || prayerActiveAtRoundStart)
                                        saveTarget = Math.Max(1, saveTarget - 1);
                                    var saveRoll = _dice.Roll(20);
                                    if (saveRoll >= saveTarget)
                                    {
                                        events.Add(new CombatEvent($"{target.Name} resists poison (save {saveRoll} vs {saveTarget})."));
                                    }
                                    else if (!target.HasStatus(CharacterStatus.Poisoned))
                                    {
                                        target.AddStatus(CharacterStatus.Poisoned);
                                        events.Add(new CombatEvent($"{target.Name} is poisoned by {monster.DisplayName}! (save {saveRoll} vs {saveTarget})"));
                                    }
                                }

            if (isPiercer)
            {
                session.SetPiercerClimbRounds(monster, 4);
                events.Add(new CombatEvent($"{monster.DisplayName} starts climbing up the wall and cannot attack for 4 rounds."));
            }
                            }

                            if (HasAnySpecialAbility(monster, "Paralyze", "Paralyzation", "Paralysis"))
                            {
                                TryApplyMonsterParalyzation(monster, target, session, events);
                            }
                        }
                    }
                    else
                    {
                        events.Add(new CombatEvent($"{monster.DisplayName} misses {target.Name}."));
                    }
                }
            }
        }

        ApplyPoisonDamageDuringCombat(session, events);

        if (!session.AliveParty.Any())
        {
            session.Outcome = CombatOutcome.Defeat;
            events.Add(new CombatEvent("The party is defeated."));
        }
        else if (!session.AliveMonsters.Any())
        {
            session.Outcome = CombatOutcome.Victory;
            events.Add(new CombatEvent("All monsters are defeated!"));
        }

        if (prayerActiveAtRoundStart)
        {
            var afterTickPrayerRounds = session.TickPrayer();
            if (afterTickPrayerRounds <= 0)
                events.Add(new CombatEvent("Prayer fades."));
        }

        return FinalizeRound(session, events);
    }

    private void ResolvePartySpell(CombatSession session, Character caster, CombatAction action, List<CombatEvent> events)
    {
        if (caster.BreakRingInaudibilityForSpeaking())
            events.Add(new CombatEvent($"{caster.Name} speaks and loses ring inaudibility."));

        if (_spellCastingService == null)
        {
            events.Add(new CombatEvent($"{caster.Name} cannot cast spells right now."));
            return;
        }

        if (string.IsNullOrWhiteSpace(action.SpellId))
        {
            events.Add(new CombatEvent($"{caster.Name} has no spell selected."));
            return;
        }

        var targets = action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();

        var result = _spellCastingService.Cast(new SpellCastRequest
        {
            Caster = caster,
            SpellId = action.SpellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party,
            MonsterTargets = session.Monsters,
            Targets = targets,
            RoundNumber = session.RoundNumber,
            CombatSession = session
        });

        if (!result.Success)
        {
            events.Add(new CombatEvent($"{caster.Name} fails to cast: {result.Error}"));
            return;
        }

        foreach (var message in result.Events)
            events.Add(new CombatEvent(message));
    }

    private void ResolvePartyUseItem(CombatSession session, Character user, CombatAction action, List<CombatEvent> events)
    {
        if (user.BreakRingInaudibilityForSpeaking())
            events.Add(new CombatEvent($"{user.Name} speaks and loses ring inaudibility."));

        if (_spellCastingService == null)
        {
            events.Add(new CombatEvent($"{user.Name} cannot use magical items right now."));
            return;
        }

        if (string.Equals(action.SpellId, "__ring_mammal_control__", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveRingOfMammalControlUse(session, user, events))
                events.Add(new CombatEvent($"{user.Name} cannot invoke Ring of Mammal Control right now."));

            return;
        }

        if (!action.ItemInventoryIndex.HasValue || action.ItemInventoryIndex.Value < 0 || action.ItemInventoryIndex.Value >= user.Inventory.Count)
        {
            events.Add(new CombatEvent($"{user.Name} has no valid item selected."));
            return;
        }

        var item = user.Inventory[action.ItemInventoryIndex.Value];
        var spellId = action.SpellId;

        if (user.HasActiveProtectionFromMagicScroll)
        {
            events.Add(new CombatEvent($"{user.Name} is inside anti-magic protection and cannot activate magical items."));
            return;
        }

        var grantsRegenerationUntilDungeonExit = ItemSpecialAbilityParser.HasCastsAbility(item, "Regeneration");
        var grantsFireResistancePotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Fire Resistance");
        var grantsGiantStrengthPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Giant Strength");
        var grantsAnimalControlPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Animal Control");
        var grantsMammalControlRing = string.Equals(item.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase);
        var grantsHeroismPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Heroism");
        var grantsSuperHeroismPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Super-Heroism");
        var grantsInvulnerabilityPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Invulnerability");
        var grantsProtectionFromMagicScroll = ItemSpecialAbilityParser.HasCastsAbility(item, "Protection from Magic")
                                             || string.Equals(item.Name, "Scroll of Protection from Magic", StringComparison.OrdinalIgnoreCase);
        var grantsLevitationPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Levitate") || string.Equals(item.Name, "Potion of Levitation", StringComparison.OrdinalIgnoreCase);
        var grantsSpeedPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Haste")
                                || string.Equals(item.Name, "Potion of Speed", StringComparison.OrdinalIgnoreCase);
        var isPotionOfHealing = string.Equals(item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase);

        bool IsDirectEffectConsumable()
            => grantsRegenerationUntilDungeonExit
               || grantsFireResistancePotion
               || grantsGiantStrengthPotion
               || grantsAnimalControlPotion
               || grantsMammalControlRing
               || grantsHeroismPotion
               || grantsSuperHeroismPotion
               || grantsInvulnerabilityPotion
               || grantsProtectionFromMagicScroll
               || grantsLevitationPotion
               || grantsSpeedPotion
               || isPotionOfHealing;

        bool FailsFighterRestriction()
        {
            if (grantsGiantStrengthPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Giant Strength can only be used by fighters."));
                return true;
            }

            if (grantsHeroismPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Heroism can only be used by fighters."));
                return true;
            }

            if (grantsSuperHeroismPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Super-Heroism can only be used by fighters."));
                return true;
            }

            if (grantsInvulnerabilityPotion && !user.IsFighterClassed())
            {
                events.Add(new CombatEvent("Potion of Invulnerability can only be used by fighters."));
                return true;
            }

            return false;
        }

        void ApplyDirectConsumableEffects()
        {
            if (grantsRegenerationUntilDungeonExit
                && !user.Inventory.Any(inv => ItemSpecialAbilityParser.HasSpecialAbility(inv, "Regeneration (Potion)")))
            {
                user.Inventory.Add(new Item
                {
                    Name = "Regeneration (Potion Effect)",
                    Type = ItemType.MagicItem,
                    IsShopBuyable = false,
                    SpecialAbilities = new List<string> { "Regeneration (Potion)" }
                });

                events.Add(new CombatEvent($"{user.Name} begins regenerating until leaving the dungeon."));
            }

            if (grantsFireResistancePotion)
            {
                user.SetPotionFireResistanceFullDose();
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Fire Resistance (full dose): normal fire immunity, +4 saves vs fire, -2 per fire die for 10 rounds."));
            }

            if (grantsGiantStrengthPotion)
            {
                var roll = _dice.Roll(20);
                var profile = roll switch
                {
                    <= 6 => (Type: "Hill Giant", Carry: 4500, Damage: 7, RockRange: 8, RockDamage: "1-6", BendBars: 50),
                    <= 10 => (Type: "Stone Giant", Carry: 5000, Damage: 8, RockRange: 9, RockDamage: "1-8", BendBars: 60),
                    <= 14 => (Type: "Frost Giant", Carry: 6000, Damage: 9, RockRange: 10, RockDamage: "1-8", BendBars: 70),
                    <= 17 => (Type: "Fire Giant", Carry: 7500, Damage: 10, RockRange: 12, RockDamage: "1-10", BendBars: 80),
                    <= 19 => (Type: "Cloud Giant", Carry: 9000, Damage: 11, RockRange: 14, RockDamage: "1-12", BendBars: 90),
                    _ => (Type: "Storm Giant", Carry: 12000, Damage: 12, RockRange: 16, RockDamage: "1-12", BendBars: 100)
                };

                user.SetPotionGiantStrength(
                    giantType: profile.Type,
                    carryWeightBonus: profile.Carry,
                    damageBonus: profile.Damage,
                    rockRangeInches: profile.RockRange,
                    rockDamage: profile.RockDamage,
                    bendBarsLiftGatesPercent: profile.BendBars);

                events.Add(new CombatEvent($"{user.Name} drinks Potion of Giant Strength (roll {roll}): {profile.Type} strength until dungeon exit (+{profile.Carry} carry, +{profile.Damage} damage). Rock hurling stored: range {profile.RockRange}\" damage {profile.RockDamage}, bend bars/lift gates {profile.BendBars}%."));
            }

            if (isPotionOfHealing)
            {
                var heal = _dice.Roll(4) + _dice.Roll(4) + 2;
                var before = user.CurrentHitPoints;
                user.CurrentHitPoints = Math.Min(user.MaxHitPoints, user.CurrentHitPoints + heal);
                var actual = Math.Max(0, user.CurrentHitPoints - before);
                events.Add(actual > 0
                    ? new CombatEvent($"{user.Name} drinks Potion of Healing and recovers {actual} HP (rolled {heal} on 2d4+2).")
                    : new CombatEvent($"{user.Name} drinks Potion of Healing (rolled {heal} on 2d4+2), but is already at full health."));
            }

            if (grantsHeroismPotion)
            {
                var level = Math.Max(0, user.Level);
                var profile = level switch
                {
                    <= 0 => (LevelBonus: 4, Dice: 4, Bonus: 0),
                    <= 3 => (LevelBonus: 3, Dice: 3, Bonus: 1),
                    <= 6 => (LevelBonus: 2, Dice: 2, Bonus: 2),
                    <= 9 => (LevelBonus: 1, Dice: 1, Bonus: 3),
                    _ => (LevelBonus: 0, Dice: 0, Bonus: 0)
                };

                if (profile.LevelBonus <= 0)
                {
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Heroism, but gains no extra life energy at level {level}."));
                }
                else
                {
                    var rolled = 0;
                    for (var i = 0; i < profile.Dice; i++)
                        rolled += _dice.Roll(10);

                    var bonusHp = rolled + profile.Bonus;
                    user.SetPotionHeroism(profile.LevelBonus, bonusHp);
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Heroism: +{profile.LevelBonus} effective level(s), +{bonusHp} temporary HP ({profile.Dice}d10+{profile.Bonus}) until dungeon exit."));
                }
            }

            if (grantsAnimalControlPotion)
            {
                var categoryRoll = _dice.Roll(20);
                var category = ResolveAnimalControlCategory(categoryRoll);
                var eligibleAnimals = session.AliveMonsters
                    .Where(m => m.InstanceMonsterType == MonsterType.Animal)
                    .Where(m => IsAnimalControlCategoryMatch(m, category))
                    .ToList();

                events.Add(new CombatEvent($"{user.Name} drinks Potion of Animal Control (d20 {categoryRoll}: {category})."));

                if (eligibleAnimals.Count == 0)
                {
                    events.Add(new CombatEvent("No controllable animals of the potion's type are present."));
                }
                else
                {
                    var targetSize = PickBestAnimalControlSize(eligibleAnimals);
                    var sizeTargets = eligibleAnimals
                        .Where(m => m.Template.Size == targetSize)
                        .OrderBy(_ => _rng.Next())
                        .ToList();

                    var capacity = RollAnimalControlCapacity(targetSize);
                    var attempted = 0;
                    var controlled = 0;

                    foreach (var target in sizeTargets)
                    {
                        if (attempted >= capacity)
                            break;

                        attempted += 1;

                        if (target.HasStatus(MonsterStatus.Charmed))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is already charmed."));
                            continue;
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, "Animal Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is immune to mind-affecting control."));
                            continue;
                        }

                        MonsterIntelligenceResolver.RegisterMonsterIntelligence(target.Template.Name, target.Template.Intelligence);
                        var intelligence = MonsterIntelligenceResolver.GetIntelligenceForMonster(target.Template.Name);

                        if (intelligence >= 5)
                        {
                            var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
                            var saveRoll = _dice.Roll(20);
                            if (saveRoll >= saveTarget)
                            {
                                events.Add(new CombatEvent($"{target.DisplayName} resists animal control (save {saveRoll} vs {saveTarget})."));
                                continue;
                            }
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, _rng, "Animal Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} negates animal control with magic resistance."));
                            continue;
                        }

                        target.SetStatus(MonsterStatus.Charmed, int.MaxValue);
                        controlled += 1;
                        events.Add(new CombatEvent($"{target.DisplayName} is brought under animal control and fights for {user.Name}."));
                    }

                    events.Add(new CombatEvent($"Animal control affects {SizeLabel(targetSize)} animals: attempted {attempted} of capacity {capacity}, controlled {controlled}."));
                }
            }

            if (grantsMammalControlRing)
            {
                if (!TryResolveRingOfMammalControlUse(session, user, events))
                    events.Add(new CombatEvent($"{user.Name} cannot invoke Ring of Mammal Control right now."));
            }

            if (grantsProtectionFromMagicScroll)
            {
                var rounds = 0;
                for (var i = 0; i < 5; i++)
                    rounds += _dice.Roll(6);

                user.SetProtectionFromMagicScroll(rounds);
                events.Add(new CombatEvent($"{user.Name} reads Scroll of Protection from Magic: anti-magic globe (5' radius) surrounds the reader for {rounds} round(s)."));
                ResolveProtectionFromMagicItemContact(user, item, events);
            }

            if (grantsSuperHeroismPotion)
            {
                var level = Math.Max(0, user.Level);
                var profile = level switch
                {
                    <= 0 => (LevelBonus: 6, Dice: 5, Bonus: 0),
                    <= 3 => (LevelBonus: 5, Dice: 4, Bonus: 1),
                    <= 6 => (LevelBonus: 4, Dice: 3, Bonus: 2),
                    <= 9 => (LevelBonus: 3, Dice: 2, Bonus: 3),
                    <= 12 => (LevelBonus: 2, Dice: 1, Bonus: 4),
                    _ => (LevelBonus: 0, Dice: 0, Bonus: 0)
                };

                if (profile.LevelBonus <= 0)
                {
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Super-Heroism, but gains no extra life energy at level {level}."));
                }
                else
                {
                    var rolled = 0;
                    for (var i = 0; i < profile.Dice; i++)
                        rolled += _dice.Roll(10);

                    var bonusHp = rolled + profile.Bonus;
                    user.SetPotionHeroism(profile.LevelBonus, bonusHp);
                    events.Add(new CombatEvent($"{user.Name} drinks Potion of Super-Heroism: +{profile.LevelBonus} effective level(s), +{bonusHp} temporary HP ({profile.Dice}d10+{profile.Bonus}) until dungeon exit."));
                }
            }

            if (grantsInvulnerabilityPotion)
            {
                var rounds = _dice.Roll(16) + 4; // 5-20 rounds
                user.SetPotionInvulnerability(rounds);
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Invulnerability: +2 AC and +2 saves, and immunity to non-magical attacks from creatures with no magical properties or with fewer than 4 Hit Dice, for {rounds} rounds."));
            }

            if (grantsLevitationPotion)
            {
                user.SetPotionLevitation();
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Levitation and can levitate like the Levitate spell. Carry capacity becomes 6,000 gp equivalent until leaving the dungeon."));
            }

            if (grantsSpeedPotion)
            {
                var rounds = _dice.Roll(16) + 4; // 5-20 rounds
                if (!session.IsHasted(user.Name))
                {
                    session.SetHaste(user.Name, rounds, user.Move);
                    user.Move *= 2;
                }
                else
                {
                    session.SetHaste(user.Name, rounds, session.GetHasteOriginalMove(user.Name));
                }

                user.Age = Math.Max(0, user.Age + 1);
                events.Add(new CombatEvent($"{user.Name} drinks Potion of Speed: move/attacks doubled for {rounds} rounds; ages 1 year permanently."));
            }
        }

        if (string.IsNullOrWhiteSpace(spellId))
        {
            var spell = _spellCastingService.FindSpellFromItem(item);
            spellId = spell?.Id;
        }

        if (string.IsNullOrWhiteSpace(spellId))
        {
            if (!IsDirectEffectConsumable())
            {
                events.Add(new CombatEvent($"{item.Name} has no usable spell effect."));
                return;
            }

            if (FailsFighterRestriction())
                return;

            if (item.Type is ItemType.Potion or ItemType.Scroll)
                user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

            ApplyDirectConsumableEffects();
            events.Add(new CombatEvent($"{user.Name} uses {item.Name}."));
            return;
        }

        var targets = action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();
        var result = _spellCastingService.CastFromItem(new SpellCastRequest
        {
            Caster = user,
            SpellId = spellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party,
            MonsterTargets = session.Monsters,
            Targets = targets,
            RoundNumber = session.RoundNumber,
            CombatSession = session,
            IsScrollSpell = item.Type == ItemType.Scroll,
            SourceItemName = item.Name
        });

        if (item.Type == ItemType.Scroll)
            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

        if (!result.Success)
        {
            if (result.Events != null && result.Events.Count > 0)
            {
                foreach (var entry in result.Events)
                    events.Add(new CombatEvent(entry));
            }

            events.Add(new CombatEvent($"{user.Name} fails to use {item.Name}: {result.Error}"));
            return;
        }

        if (FailsFighterRestriction())
            return;

        if (item.Type == ItemType.Potion)
            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);

        ApplyDirectConsumableEffects();

        events.Add(new CombatEvent($"{user.Name} uses {item.Name}."));
        foreach (var message in result.Events)
            events.Add(new CombatEvent(message));
    }

    private bool TryResolveRingOfMammalControlUse(CombatSession session, Character user, List<CombatEvent> events)
    {
        if (!HasEquippedRingOfMammalControl(user) && !user.Inventory.Any(i => string.Equals(i.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase)))
            return false;

        events.Add(new CombatEvent($"{user.Name} concentrates for 3 segments and invokes Ring of Mammal Control."));

        var candidates = session.AliveMonsters
            .Where(IsEligibleMammalForRingControl)
            .OrderBy(m => Math.Max(1, m.Template.HitDice))
            .ThenBy(m => m.DisplayName)
            .ToList();

        if (candidates.Count == 0)
        {
            events.Add(new CombatEvent("No eligible mammal is affected."));
            return true;
        }

        const int maxHitDiceControlled = 30;
        var controlledCount = 0;
        var controlledHitDice = 0;

        foreach (var mammal in candidates)
        {
            var hd = Math.Max(1, mammal.Template.HitDice);
            if (controlledHitDice + hd > maxHitDiceControlled)
                continue;

            mammal.SetStatus(MonsterStatus.Charmed, int.MaxValue);
            controlledCount += 1;
            controlledHitDice += hd;
            events.Add(new CombatEvent($"{mammal.DisplayName} is controlled by {user.Name}'s ring."));
        }

        events.Add(new CombatEvent($"Ring of Mammal Control affects {controlledCount} mammal(s), total {controlledHitDice} HD (max 30 HD)."));
        return true;
    }

    private static bool HasEquippedRingOfMammalControl(Character user)
    {
        return (user.Equipment.TryGetValue(EquipmentSlot.Ring1, out var ring1)
                && ring1 != null
                && string.Equals(ring1.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase))
               ||
               (user.Equipment.TryGetValue(EquipmentSlot.Ring2, out var ring2)
                && ring2 != null
                && string.Equals(ring2.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEligibleMammalForRingControl(MonsterInstance monster)
    {
        if (monster == null || !monster.IsAlive)
            return false;

        if (monster.InstanceMonsterType != MonsterType.Animal)
            return false;

        if (!IsLikelyMammal(monster))
            return false;

        if (!HasRingMammalControlEligibleIntelligence(monster.Template.Intelligence))
            return false;

        var name = (monster.Template.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("lammasu") || name.Contains("shedu") || name.Contains("manes") || name.Contains("core"))
            return false;

        return true;
    }

    private static bool IsLikelyMammal(MonsterInstance monster)
    {
        var text = $"{monster.Template.Name} {monster.Template.TypeName}".ToLowerInvariant();

        if (text.Contains("bat") || text.Contains("bird") || text.Contains("avian")
            || text.Contains("snake") || text.Contains("lizard") || text.Contains("rept")
            || text.Contains("amphib") || text.Contains("frog") || text.Contains("toad")
            || text.Contains("fish") || text.Contains("shark") || text.Contains("eel")
            || text.Contains("insect") || text.Contains("spider") || text.Contains("scorpion"))
            return false;

        return text.Contains("rat")
               || text.Contains("weasel")
               || text.Contains("badger")
               || text.Contains("wolf")
               || text.Contains("dog")
               || text.Contains("fox")
               || text.Contains("cat")
               || text.Contains("lion")
               || text.Contains("tiger")
               || text.Contains("bear")
               || text.Contains("boar")
               || text.Contains("pig")
               || text.Contains("horse")
               || text.Contains("pony")
               || text.Contains("camel")
               || text.Contains("cow")
               || text.Contains("bull")
               || text.Contains("ox")
               || text.Contains("deer")
               || text.Contains("stag")
               || text.Contains("goat")
               || text.Contains("sheep")
               || text.Contains("ram")
               || text.Contains("dolphin")
               || text.Contains("whale")
               || text.Contains("ape")
               || text.Contains("baboon")
               || text.Contains("monkey")
               || text.Contains("mammal");
    }

    private static bool HasRingMammalControlEligibleIntelligence(string? intelligenceText)
    {
        if (string.IsNullOrWhiteSpace(intelligenceText))
            return false;

        var lower = intelligenceText.Trim().ToLowerInvariant();
        if (lower.StartsWith("non") || lower.StartsWith("animal") || lower.StartsWith("semi"))
            return true;

        var matches = Regex.Matches(lower, @"\d+")
            .Select(m => int.TryParse(m.Value, out var n) ? n : -1)
            .Where(n => n >= 0)
            .ToList();

        if (matches.Count == 0)
            return false;

        return matches.Min() <= 4;
    }

    private void ResolveProtectionFromMagicItemContact(Character user, Item sourceItem, List<CombatEvent> events)
    {
        var candidates = user.Inventory
            .Where(i => !ReferenceEquals(i, sourceItem))
            .Concat(user.Equipment.Values.Where(i => i != null).Select(i => i!))
            .Where(IsMagicalItemForProtectionFromMagic)
            .Distinct()
            .OrderByDescending(GetProtectionFromMagicItemPower)
            .ToList();

        foreach (var magicItem in candidates)
        {
            var saveRoll = _dice.Roll(20);
            if (saveRoll >= 11)
            {
                events.Add(new CombatEvent($"{magicItem.Name} resists anti-magic contact (save {saveRoll} vs 11)."));
                continue;
            }

            DrainItemMagic(magicItem);
            user.ClearProtectionFromMagicScroll();
            events.Add(new CombatEvent($"{magicItem.Name} fails anti-magic contact save ({saveRoll} vs 11), is drained of magic, and the anti-magic globe collapses."));
            return;
        }
    }

    private static bool IsMagicalItemForProtectionFromMagic(Item item)
    {
        if (item == null)
            return false;

        return item.Type is ItemType.Potion or ItemType.Scroll or ItemType.MagicItem
               || item.MagicBonus != 0
               || item.ToHitBonus != 0
               || item.ArmorClassBonus != 0
               || (item.SpecialAbilities != null && item.SpecialAbilities.Count > 0)
               || item.IsCursed;
    }

    private static int GetProtectionFromMagicItemPower(Item item)
    {
        if (item == null)
            return 0;

        var specialCount = item.SpecialAbilities?.Count ?? 0;
        return Math.Abs(item.MagicBonus) * 100
               + Math.Abs(item.ToHitBonus) * 80
               + Math.Abs(item.ArmorClassBonus) * 80
               + specialCount * 25
               + Math.Max(0, item.Cost / 100);
    }

    private static void DrainItemMagic(Item item)
    {
        item.MagicBonus = 0;
        item.ToHitBonus = 0;
        item.ArmorClassBonus = 0;
        item.IsCursed = false;

        if (item.SpecialAbilities != null)
            item.SpecialAbilities.Clear();
    }

    private int RollAnimalControlCapacity(MonsterSize size)
    {
        return size switch
        {
            MonsterSize.Small => _dice.Roll(16) + 4,
            MonsterSize.Medium => _dice.Roll(10) + 2,
            _ => _dice.Roll(4)
        };
    }

    private static MonsterSize PickBestAnimalControlSize(IReadOnlyCollection<MonsterInstance> animals)
    {
        return animals
            .GroupBy(a => a.Template.Size)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key == MonsterSize.Small ? 0 : g.Key == MonsterSize.Medium ? 1 : 2)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static string SizeLabel(MonsterSize size)
    {
        return size switch
        {
            MonsterSize.Small => "small",
            MonsterSize.Medium => "man-sized",
            _ => "large"
        };
    }

    private static string ResolveAnimalControlCategory(int d20)
    {
        return d20 switch
        {
            <= 4 => "mammal/marsupial",
            <= 8 => "avian",
            <= 12 => "reptile/amphibian",
            <= 15 => "fish",
            <= 17 => "mammal/marsupial/avian",
            <= 19 => "reptile/amphibian/fish",
            _ => "all animal types"
        };
    }

    private static bool IsAnimalControlCategoryMatch(MonsterInstance monster, string category)
    {
        if (string.Equals(category, "all animal types", StringComparison.OrdinalIgnoreCase))
            return true;

        var kind = ResolveAnimalKind(monster);
        return category.Split('/').Any(part => string.Equals(part.Trim(), kind, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveAnimalKind(MonsterInstance monster)
    {
        var name = monster.Template.Name?.ToLowerInvariant() ?? string.Empty;
        var typeName = monster.Template.TypeName?.ToLowerInvariant() ?? string.Empty;
        var merged = $"{name} {typeName}";

        if (merged.Contains("bird") || merged.Contains("avian") || merged.Contains("eagle") || merged.Contains("hawk") || merged.Contains("owl") || merged.Contains("vulture") || merged.Contains("raven"))
            return "avian";

        if (merged.Contains("snake") || merged.Contains("lizard") || merged.Contains("reptile") || merged.Contains("amphib") || merged.Contains("frog") || merged.Contains("toad") || merged.Contains("turtle") || merged.Contains("crocod"))
            return "reptile";

        if (merged.Contains("fish") || merged.Contains("shark") || merged.Contains("eel") || merged.Contains("piranha") || merged.Contains("trout") || merged.Contains("salmon"))
            return "fish";

        return "mammal";
    }

    private static void ResolveLayOnHands(CombatSession session, Character paladin, CombatAction action, List<CombatEvent> events)
    {
        if (!paladin.IsPaladin())
        {
            events.Add(new CombatEvent($"{paladin.Name} cannot use Lay on Hands."));
            return;
        }

        if (paladin.LayOnHandsUsedToday)
        {
            events.Add(new CombatEvent($"{paladin.Name} has already used Lay on Hands today."));
            return;
        }

        var targetName = action.Target?.CharacterName;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            events.Add(new CombatEvent($"{paladin.Name} has no Lay on Hands target."));
            return;
        }

        var target = session.Party.FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            events.Add(new CombatEvent($"{paladin.Name}'s Lay on Hands target is no longer present."));
            return;
        }

        if (target.HasStatus(CharacterStatus.Dead) || target.HasStatus(CharacterStatus.Ashes) || target.HasStatus(CharacterStatus.Lost))
        {
            events.Add(new CombatEvent($"{paladin.Name} cannot heal {target.Name} with Lay on Hands."));
            return;
        }

        var healAmount = Math.Max(0, paladin.GetPaladinLevel()) * 2;
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healAmount);
        var healed = target.CurrentHitPoints - before;

        paladin.LayOnHandsUsedToday = true;

        events.Add(new CombatEvent(healed > 0
            ? $"{paladin.Name} lays on hands and heals {target.Name} for {healed} hit point(s)."
            : $"{paladin.Name} lays on hands on {target.Name}, but no healing is needed."));
    }

    private void ResolveMonkBodyHeal(CombatSession session, Character monk, List<CombatEvent> events)
    {
        if (!monk.IsMonk() || monk.GetMonkLevel() < 7)
        {
            events.Add(new CombatEvent($"{monk.Name} cannot use Body Heal."));
            return;
        }

        if (monk.MonkBodyHealUsedToday)
        {
            events.Add(new CombatEvent($"{monk.Name} has already used Body Heal today."));
            return;
        }

        if (monk.HasStatus(CharacterStatus.Dead) || monk.HasStatus(CharacterStatus.Ashes) || monk.HasStatus(CharacterStatus.Lost))
        {
            events.Add(new CombatEvent($"{monk.Name} cannot use Body Heal right now."));
            return;
        }

        var healAmount = monk.RollMonkBodyHealAmount();
        var before = monk.CurrentHitPoints;
        monk.CurrentHitPoints = Math.Min(monk.MaxHitPoints, monk.CurrentHitPoints + healAmount);
        var healed = monk.CurrentHitPoints - before;

        monk.MonkBodyHealUsedToday = true;

        events.Add(new CombatEvent(healed > 0
            ? $"{monk.Name} heals own wounds for {healed} hit point(s)."
            : $"{monk.Name} focuses inner discipline, but no healing is needed."));
    }

    private void ResolveDispellUndead(CombatSession session, Character actor, CombatAction action, List<CombatEvent> events)
    {
        var effectiveClericLevel = GetEffectiveTurnUndeadLevel(actor);
        if (effectiveClericLevel < 1)
        {
            events.Add(new CombatEvent($"{actor.Name} cannot dispell undead."));
            return;
        }

        var targetMonsters = string.IsNullOrWhiteSpace(action.TargetGroupId)
            ? session.AliveMonsters.ToList()
            : session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();

        var undead = targetMonsters.Where(m => m.InstanceMonsterType == Adnd.Core.Monsters.MonsterType.Undead).ToList();
        if (undead.Count == 0)
        {
            events.Add(new CombatEvent($"{actor.Name} presents a holy symbol, but no undead are affected."));
            return;
        }

        events.Add(new CombatEvent($"{actor.Name} uses Dispell Undead!"));

        var table = LoadTurnUndeadTable();
        var row = table.FirstOrDefault(r => effectiveClericLevel >= r.MinLevel && effectiveClericLevel <= r.MaxLevel)
                  ?? table.OrderByDescending(r => r.MaxLevel).First();

        foreach (var monster in undead)
        {
            var key = ResolveTurnUndeadKey(monster.Name);
            if (!row.Results.TryGetValue(key, out var token) || string.IsNullOrWhiteSpace(token))
                token = row.Results.TryGetValue("Skeleton", out var fallback) ? fallback : "-";

            token = token.Trim();

            if (token == "-")
            {
                events.Add(new CombatEvent($"{monster.DisplayName} resists the turning attempt."));
                continue;
            }

            if (string.Equals(token, "D", StringComparison.OrdinalIgnoreCase))
            {
                monster.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{monster.DisplayName} is disintegrated by holy power!"));
                continue;
            }

            if (string.Equals(token, "T", StringComparison.OrdinalIgnoreCase))
            {
                var rounds = _dice.Roll(10) + 2;
                monster.SetStatus(MonsterStatus.TurnedUndead, rounds);
                events.Add(new CombatEvent($"{monster.DisplayName} is turned for {rounds} round(s)!"));
                continue;
            }

            if (!int.TryParse(token, out var required))
            {
                events.Add(new CombatEvent($"{monster.DisplayName} is unaffected."));
                continue;
            }

            var roll = _dice.Roll(20);
            if (roll >= required)
            {
                var rounds = _dice.Roll(10) + 2;
                monster.SetStatus(MonsterStatus.TurnedUndead, rounds);
                events.Add(new CombatEvent($"{monster.DisplayName} is turned ({roll} vs {required}) for {rounds} round(s)!"));
            }
            else
            {
                events.Add(new CombatEvent($"{monster.DisplayName} resists turning ({roll} vs {required})."));
            }
        }
    }

    private static int GetEffectiveTurnUndeadLevel(Character actor)
    {
        var clericLevel = actor.Classes.Contains(CharacterClass.Cleric)
            ? actor.GetClassLevel(CharacterClass.Cleric)
            : 0;

        var paladinLevel = actor.Classes.Contains(CharacterClass.Paladin)
            ? Math.Max(0, actor.GetClassLevel(CharacterClass.Paladin) - 2)
            : 0;

        if (!actor.Classes.Contains(CharacterClass.Cleric) && paladinLevel < 1)
            return 0;

        if (actor.Classes.Contains(CharacterClass.Paladin) && actor.GetClassLevel(CharacterClass.Paladin) < 3)
            paladinLevel = 0;

        return Math.Max(clericLevel, paladinLevel);
    }

    private static string ResolveTurnUndeadKey(string monsterName)
    {
        if (string.IsNullOrWhiteSpace(monsterName))
            return "Skeleton";

        var n = monsterName.Trim().ToLowerInvariant();
        if (n.Contains("skeleton")) return "Skeleton";
        if (n.Contains("zombie")) return "Zombie";
        if (n.Contains("ghoul")) return "Ghoul";
        if (n.Contains("shadow")) return "Shadow";
        if (n.Contains("wight")) return "Wight";
        if (n.Contains("wraith")) return "Wraith";
        if (n.Contains("mummy")) return "Mummy";
        if (n.Contains("spectre") || n.Contains("specter")) return "Spectre";
        if (n.Contains("vampire")) return "Vampire";
        if (n.Contains("ghost")) return "Ghost";
        if (n.Contains("lich")) return "Lich";
        return "Skeleton";
    }

    private static List<TurnUndeadRow> LoadTurnUndeadTable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Characters", "Progression", "ClericTurnUndead.json");
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Adnd.Core", "Characters", "Progression", "ClericTurnUndead.json"));

        if (!File.Exists(path))
            return new List<TurnUndeadRow>
            {
                new() { MinLevel = 1, MaxLevel = 2, Results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Skeleton"] = "10" } }
            };

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("TurnUndeadTable", out var tableEl) || tableEl.ValueKind != JsonValueKind.Array)
            return new List<TurnUndeadRow>();

        var rows = new List<TurnUndeadRow>();
        foreach (var rowEl in tableEl.EnumerateArray())
        {
            var range = rowEl.GetProperty("LevelRange").GetString() ?? "1-1";
            var parts = range.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var min = 1;
            var max = 1;
            if (parts.Length >= 1) int.TryParse(parts[0], out min);
            if (parts.Length >= 2) int.TryParse(parts[1], out max);
            else max = min;

            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rowEl.TryGetProperty("Results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in resultsEl.EnumerateObject())
                    results[p.Name] = p.Value.GetString() ?? "-";
            }

            rows.Add(new TurnUndeadRow { MinLevel = min, MaxLevel = max, Results = results });
        }

        return rows;
    }

    private sealed class TurnUndeadRow
    {
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public Dictionary<string, string> Results { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private List<CombatEvent> FinalizeRound(CombatSession session, List<CombatEvent> events)
    {
        if (session.Outcome == CombatOutcome.InProgress)
            session.RoundNumber++;

        return events;
    }

    private int GetAttacksThisRound(float attacksPerRound, int roundNumber)
    {
        // 1 attack per round
        if (attacksPerRound <= 1f)
            return 1;

        // 1.25 attacks per round (5/4)
        // Every 4th round: 2 attacks, otherwise 1.
        if (Math.Abs(attacksPerRound - 1.25f) < 0.01f)
            return (roundNumber % 4 == 0) ? 2 : 1;

        // 2 attacks per round
        if (attacksPerRound >= 2f)
            return 2;

        // 1.5 attacks per round (3/2)
        // Odd rounds: 1 attack
        // Even rounds: 2 attacks
        if (Math.Abs(attacksPerRound - 1.5f) < 0.01f)
            return (roundNumber % 2 == 0) ? 2 : 1;

        // fallback
        return 1;
    }

    private void ResolvePartyAttack(CombatSession session, Character member, CombatAction action, List<CombatEvent> events)
    {
        var chantOrPrayerBonus = (session.IsChantActive || session.IsPrayerActive) ? 1 : 0;

        if (member.BreakRingInvisibilityOnHostileAction())
            events.Add(new CombatEvent($"{member.Name}'s ring invisibility breaks on attack."));

        var rangedWeapon = member.Equipment.TryGetValue(EquipmentSlot.Range, out var rw) ? rw : null;
        var ammo = member.Equipment.TryGetValue(EquipmentSlot.Ammo, out var am) ? am : null;
        var useRanged = rangedWeapon != null
                        && (!string.IsNullOrWhiteSpace(rangedWeapon.Range)
                            || !string.IsNullOrWhiteSpace(rangedWeapon.FireRate)
                            || rangedWeapon.RequiresAmmo
                            || !string.IsNullOrWhiteSpace(rangedWeapon.AmmoType));

        if (useRanged && rangedWeapon != null)
        {
            if (string.Equals((rangedWeapon.FireRate ?? string.Empty).Trim(), "1/2", StringComparison.OrdinalIgnoreCase)
                && session.RoundNumber % 2 != 0)
            {
                events.Add(new CombatEvent($"{member.Name} reloads {rangedWeapon.Name} and cannot fire this round."));
                return;
            }

            if (rangedWeapon.RequiresAmmo)
            {
                if (ammo == null || ammo.Quantity <= 0)
                {
                    events.Add(new CombatEvent($"{member.Name} has no ammo for {rangedWeapon.Name}."));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(rangedWeapon.AmmoType)
                    && !string.Equals(rangedWeapon.AmmoType, ammo.AmmoType, StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new CombatEvent($"{member.Name} has incompatible ammo for {rangedWeapon.Name}."));
                    return;
                }
            }
        }

        var hasImprovedInvisibility = session.GetImprovedInvisibilityRounds(member.Name) > 0;
        if (!hasImprovedInvisibility
            && member.HasStatus(CharacterStatus.Invisible)
            && session.InvisiblyBuffedPartyMembers.Contains(member.Name))
        {
            member.RemoveStatus(CharacterStatus.Invisible);
            member.ArmorClass += 4;
            session.InvisiblyBuffedPartyMembers.Remove(member.Name);
            events.Add(new CombatEvent($"{member.Name} attacks and becomes visible."));
        }

        MonsterInstance? target = null;

        if (!string.IsNullOrEmpty(action.TargetMonsterId))
        {
            var named = session.FindMonster(action.TargetMonsterId);
            if (named != null && named.IsAlive) target = named;
        }

        if (target is null && action.SpreadTargets)
        {
            var spreadable = (string.IsNullOrEmpty(action.TargetGroupId)
                    ? session.AliveMonsters
                    : session.GetAliveMonstersByGroup(action.TargetGroupId))
                .ToList();

            if (spreadable.Count > 0)
            {
                var at = ((session.SpreadCursor % spreadable.Count) + spreadable.Count) % spreadable.Count;
                target = spreadable[at];
                session.SpreadCursor = at + 1;
            }
        }

        if (target is null && !string.IsNullOrEmpty(action.TargetGroupId))
        {
            var groupMonsters = session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();
            if (groupMonsters.Count > 0)
                target = groupMonsters.First();
        }

        target ??= session.AliveMonsters.FirstOrDefault();
        if (target is null)
            return;

        int attacks = useRanged && rangedWeapon != null
            ? GetRangedAttacksPerRound(rangedWeapon.FireRate, session.RoundNumber)
            : GetAttacksThisRound(member.NumberOfAttacks, session.RoundNumber);
        if (session.IsHasted(member.Name))
            attacks *= 2;
        if (session.IsPartySlowed(member.Name))
            attacks = Math.Max(1, attacks / 2);

        var isBackstab = IsThiefBackstabAttack(member, session);
        var backstabMultiplier = isBackstab ? GetThiefBackstabMultiplierByLevel(member.GetClassLevel(CharacterClass.Thief)) : 1;

        if (member.Class == CharacterClass.Assassin
            && session.RoundNumber == 1
            && session.MonstersSurprisedRound1
            && target.IsAlive)
        {
            var assassination = new AssassinationService("Data/Assassination");
            int monsterLevel = target.Template.HitDice;
            bool success = assassination.TryAssassinate(monsterLevel, _rng);

            if (success)
            {
                RuleApplicationInfo.Publish(
                    "HomeBrewAI",
                    "NA",
                    "assassination",
                    "assassination success",
                    "1",
                    "6",
                    "0",
                    "Monster dies?");
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{member.Name} assassinates {target.DisplayName} instantly!"));
                return;
            }

            RuleApplicationInfo.Publish(
                "HomeBrewAI",
                "NA",
                "assassination",
                "assassination fail",
                "1",
                "6",
                "0",
                "Monster survives?");
        }

        for (int i = 0; i < attacks; i++)
        {
            var thac0Modifier = session.IsBlessed(member.Name) ? 1 : 0;
            thac0Modifier += chantOrPrayerBonus;
            if (isBackstab)
                thac0Modifier += 4;

            Item? mainHand = null;
            if (member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) && equipped != null)
            {
                mainHand = equipped;
                thac0Modifier += Math.Max(0, mainHand.ToHitBonus);
            }

            if (useRanged && rangedWeapon != null)
            {
                mainHand = rangedWeapon;
                thac0Modifier += Math.Max(0, rangedWeapon.ToHitBonus);
            }

            int needed = (member.Thac0 - thac0Modifier) - target.ArmorClass;
            int roll = _dice.Roll(20);

            if (roll < needed)
            {
                events.Add(new CombatEvent($"{member.Name} misses {target.DisplayName}."));
                continue;
            }

            var mirrorImages = session.GetMonsterMirrorImageCount(target);
            if (mirrorImages > 0)
            {
                var imageHitRoll = _dice.Roll(mirrorImages + 1);
                if (imageHitRoll > 1)
                {
                    var remainingImages = session.RemoveOneMonsterMirrorImage(target);
                    events.Add(new CombatEvent($"{member.Name} hits a mirror image of {target.DisplayName}!"));
                    events.Add(new CombatEvent($"{target.DisplayName} has {remainingImages} mirror image(s) remaining."));
                    if (remainingImages <= 0)
                        events.Add(new CombatEvent($"{target.DisplayName} has no mirror images left."));
                    continue;
                }
            }

            if (RequiresPlusOneWeaponToHit(target) && !IsMagicalWeapon(mainHand))
            {
                var blockedWeaponName = mainHand != null ? mainHand.Name : "bare hands";
                events.Add(new CombatEvent(
                    $"{member.Name} hits {target.DisplayName} with {blockedWeaponName}, but the attack cannot harm it (+1 or better weapon required)."));

                RuleApplicationInfo.Publish(
                    $"AD&D special defense: {target.DisplayName} requires '+1 or better weapons to hit'. " +
                    $"{member.Name}'s attack with {blockedWeaponName} is non-magical (no '+' in name and no special abilities), so it deals no damage.");
                continue;
            }

            var damageExpression = ResolveWeaponDamageExpression(member, mainHand, target);
            var strengthDamageBonus = mainHand != null && mainHand.Type == ItemType.Weapon
                ? AbilitiesTables.StrengthDamageModifier(member.Abilities.Strength)
                : 0;

            int damage = RollDamage(damageExpression) + strengthDamageBonus;
            var partyDamageBonusApplied = chantOrPrayerBonus > 0;
            if (partyDamageBonusApplied)
                damage += 1;

            if (isBackstab)
            {
                var beforeBackstab = damage;
                damage *= backstabMultiplier;
                events.Add(new CombatEvent($"{member.Name} backstabs! +4 to hit, damage x{backstabMultiplier} ({beforeBackstab}->{damage})."));
            }

            if (IsHalfDamageFromSharpWeapons(target, mainHand))
            {
                var originalDamage = damage;
                damage = Math.Max(1, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName} has Half Damage from Sharp Weapons. Damage reduced from {originalDamage} to {damage}."));
            }

            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actualDamageToTarget = before - target.CurrentHitPoints;
            WakeMonsterIfAsleepAfterDamage(target, actualDamageToTarget, events);

            if (useRanged && rangedWeapon != null && rangedWeapon.RequiresAmmo && ammo != null && ammo.Quantity > 0)
            {
                ammo.Quantity = Math.Max(0, ammo.Quantity - 1);
                events.Add(new CombatEvent($"{member.Name} uses 1 {ammo.Name}. {ammo.Quantity} remaining."));
            }

            var weaponName = mainHand != null ? mainHand.Name : "bare hands";
            var damageFormula = strengthDamageBonus == 0
                ? damageExpression
                : $"{damageExpression}+{strengthDamageBonus}";

            var partyDamageModText = partyDamageBonusApplied ? " +1 chant/prayer" : string.Empty;
            events.Add(new CombatEvent(
                $"{member.Name} hits {target.DisplayName} with {weaponName} ({damageFormula}{partyDamageModText}) for {damage}  damage. HP {before}->{target.CurrentHitPoints}."));

            if (actualDamageToTarget > 0 && TryResolveMonsterExplosionOnHit(target, session, events))
                break;

            if (target.CurrentHitPoints <= 0)
            {
                events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));
                break;
            }
        }
    }

    private static int GetRangedAttacksPerRound(string? fireRate, int roundNumber)
    {
        var rate = (fireRate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rate))
            return 1;

        if (string.Equals(rate, "1/2", StringComparison.OrdinalIgnoreCase))
            return roundNumber % 2 == 0 ? 1 : 0;

        if (int.TryParse(rate, out var parsed) && parsed > 0)
            return parsed;

        return 1;
    }

    private static bool IsThiefBackstabAttack(Character member, CombatSession session)
    {
        var isThief = member.Classes.Contains(CharacterClass.Thief)
                      || member.Class == CharacterClass.Thief;

        return isThief
               && session.RoundNumber == 1
               && session.MonstersSurprisedRound1;
    }

    private static int GetThiefBackstabMultiplierByLevel(int thiefLevel)
    {
        if (thiefLevel <= 4) return 2;
        if (thiefLevel <= 8) return 3;
        if (thiefLevel <= 12) return 4;
        return 5;
    }

    private bool ResolveLevel2MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.AliveParty.ToList();
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

        var target = aliveParty[_dice.Roll(aliveParty.Count) - 1];
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
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var damagedAllies = session.AliveMonsters
                .Where(m => !ReferenceEquals(m, monster) && m.CurrentHitPoints < m.MaxHitPoints)
                .ToList();

            if (damagedAllies.Count == 0)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} tries to cast Cure Serious Wounds but no ally needs healing."));
                return true;
            }

            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Serious Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var damagedAllies = session.AliveMonsters
                .Where(m => !ReferenceEquals(m, monster) && m.CurrentHitPoints < m.MaxHitPoints)
                .ToList();

            if (damagedAllies.Count == 0)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} tries to cast Cure Critical Wounds but no ally needs healing."));
                return true;
            }

            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + _dice.Roll(8) + 3;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Critical Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var partyTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var rolledDamage = 0;
        for (int i = 0; i < 6; i++)
            rolledDamage += _dice.Roll(8);

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
        var damagedAllies = session.AliveMonsters
            .Where(m => m.CurrentHitPoints < m.MaxHitPoints)
            .ToList();

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
        var actual = cureTarget.CurrentHitPoints - before;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {cureTarget.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{cureTarget.CurrentHitPoints}."));
        return true;
    }

    private bool ResolveLevel3DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            var damagedAllies = session.AliveMonsters
                .Where(m => !ReferenceEquals(m, monster) && m.CurrentHitPoints < m.MaxHitPoints)
                .ToList();

            if (damagedAllies.Count == 0)
            {
                events.Add(new CombatEvent($"{monster.DisplayName} tries to cast Cure Serious Wounds but no ally needs healing."));
                return true;
            }

            var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
            var healRoll = _dice.Roll(8) + _dice.Roll(8) + 1;
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
            var actual = target.CurrentHitPoints - before;

            events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Serious Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
            return true;
        }

        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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

        // Insect Plague: panic one party member for 2-6 rounds.
        var insectTarget = aliveParty[_dice.Roll(aliveParty.Count) - 1];
        var panicRounds = _dice.Roll(5) + 1;
        insectTarget.AddStatus(CharacterStatus.Feeblemind);
        events.Add(new CombatEvent($"{monster.DisplayName} casts Insect Plague! {insectTarget.Name} panics and loses control for {panicRounds} round(s)."));
        return true;
    }

    private bool ResolveLevel5MagicUserSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

        var spellRoll = _dice.Roll(100);
        if (spellRoll <= 50)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} casts Confusion!"));

            var casterLevel = Math.Max(1, monster.Template.HitDice);
            var affectedCount = Math.Min(aliveParty.Count, Math.Max(1, casterLevel));
            var rounds = casterLevel;

            var targets = aliveParty
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
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var aliveParty = session.AliveParty.ToList();
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

    private int GetMonsterThac0(MonsterInstance monster)
    {
        return Math.Max(1, Math.Max(10, 20 - Math.Max(0, monster.Template.HitDice - 1)) + monster.Thac0Modifier);
    }

    private Character? SelectMonsterTarget(CombatSession session)
    {
        var frontline = session.Party.Take(3).Where(IsAlive).ToList();
        if (frontline.Count == 0)
            frontline = session.AliveParty.ToList();

        if (frontline.Count == 0)
            return null;

        var index = _dice.Roll(frontline.Count) - 1;
        return frontline[index];
    }

    private (bool Enabled, int Multiplier) TryGetMonsterThiefBackstabInfo(CombatSession session, MonsterInstance monster)
    {
        if (session.RoundNumber != 1 || !session.PartySurprisedRound1)
            return (false, 1);

        var isThiefByName = monster.Template.Name?.IndexOf("thief", StringComparison.OrdinalIgnoreCase) >= 0;
        var isThiefByAbility = HasAnySpecialAbility(monster, "Thief", "Backstab", "Thief Backstab");
        if (!isThiefByName && !isThiefByAbility)
            return (false, 1);

        var thiefLevel = Math.Max(1, monster.Template.HitDice);
        var multiplier = GetThiefBackstabMultiplierByLevel(thiefLevel);
        return (true, multiplier);
    }

    private int RollDamage(string damageExpression)
    {
        var normalized = damageExpression.Trim().ToLowerInvariant();

        if (normalized.Contains('/'))
            normalized = normalized.Split('/')[0].Trim();

        var m = Regex.Match(normalized, @"^(?<count>\d+)d(?<sides>\d+)(?<mod>[+-]\d+)?$");
        if (!m.Success)
            return 1;

        int count = int.Parse(m.Groups["count"].Value);
        int sides = int.Parse(m.Groups["sides"].Value);
        int mod = m.Groups["mod"].Success ? int.Parse(m.Groups["mod"].Value) : 0;

        int value = _dice.RollMany(sides, Math.Max(1, count)) + mod;
        return Math.Max(1, value);
    }

    private void ResolveLevel1MageSpells(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.AliveParty.ToList();
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
                events.Add(new CombatEvent($"{target.Name} resists Sleep (save {saveRoll} vs {saveTarget})."));
                continue;
            }

            var rounds = _dice.Roll(3) + 1; // 2-4 rounds
            target.AddStatus(CharacterStatus.Asleep);
            session.SetPartyAsleep(target.Name, rounds);
            events.Add(new CombatEvent($"{target.Name} fails save ({saveRoll} vs {saveTarget}) and falls asleep for {rounds} round(s)."));
        }
    }

    private bool ResolveLevel1PriestSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        if (session.Level1PriestSpellCastsUsed >= 3)
            return false;

        var damagedAllies = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, monster) && m.CurrentHitPoints < m.MaxHitPoints)
            .ToList();

        if (damagedAllies.Count == 0)
            return false;

        var target = damagedAllies[_dice.Roll(damagedAllies.Count) - 1];
        var healRoll = _dice.Roll(8);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Min(target.MaxHitPoints, target.CurrentHitPoints + healRoll);
        var actual = target.CurrentHitPoints - before;

        session.Level1PriestSpellCastsUsed += 1;
        events.Add(new CombatEvent($"{monster.DisplayName} casts Cure Light Wounds on {target.DisplayName}, healing {actual} HP (rolled {healRoll}). HP {before}->{target.CurrentHitPoints}."));
        return true;
    }

    private bool ShouldTryMonsterLevel1SpellCast()
    {
        return _dice.Roll(100) <= 50;
    }

    private bool ResolveLevel1DruidSpell(MonsterInstance monster, CombatSession session, List<CombatEvent> events)
    {
        var aliveParty = session.AliveParty.ToList();
        if (aliveParty.Count == 0)
            return false;

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
        var aliveParty = session.AliveParty.ToList();
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
        var aliveParty = session.AliveParty.ToList();
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

    private bool ResolveHpDamageBreath(MonsterInstance monster, int maxDamage, CombatSession session, List<CombatEvent> events)
    {
        if (maxDamage <= 0)
            return false;

        var aliveParty = session.AliveParty.ToList();
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

        var aliveParty = session.AliveParty.ToList();
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

    private static bool HasSpecialAbility(MonsterInstance monster, string abilityName)
    {
        return GetMonsterAbilityNames(monster)
            .Any(name => string.Equals(name, abilityName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetMonsterAbilityNames(MonsterInstance monster)
    {
        if (monster?.Template == null)
            yield break;

        foreach (var name in monster.Template.SpecialAbilities.Select(a => a.Name))
        {
            if (!string.IsNullOrWhiteSpace(name))
                yield return name.Trim();
        }

        foreach (var name in monster.Template.SpecialAttacks.Select(a => a.Name))
        {
            if (!string.IsNullOrWhiteSpace(name))
                yield return name.Trim();
        }

        foreach (var name in monster.Template.SpecialDefenses.Select(a => a.Name))
        {
            if (!string.IsNullOrWhiteSpace(name))
                yield return name.Trim();
        }
    }

    private bool TryResolveMonsterPickPockets(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Pick pockets"))
            return false;

        var aliveParty = session.Party.Where(IsAlive).ToList();
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

    private static bool HasAnySpecialAbility(MonsterInstance monster, params string[] abilityNames)
    {
        return GetMonsterAbilityNames(monster)
            .Any(name => abilityNames.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ApplyMonsterRegeneration(MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasSpecialAbility(monster, "Regeneration"))
            return;

        if (!monster.IsAlive || monster.CurrentHitPoints >= monster.MaxHitPoints)
            return;

        var before = monster.CurrentHitPoints;
        monster.CurrentHitPoints = Math.Min(monster.MaxHitPoints, monster.CurrentHitPoints + 1);
        var healed = monster.CurrentHitPoints - before;

        if (healed > 0)
            events.Add(new CombatEvent($"{monster.DisplayName} regenerates {healed} HP. HP {before}->{monster.CurrentHitPoints}."));
    }

    private bool TryResolveConfusedMonsterTurn(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var roundsRemaining = monster.GetStatusRounds(MonsterStatus.Confused);
        if (roundsRemaining <= 0)
            return false;

        // Save vs spell each round at -2.
        var saveTarget = monster.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = _dice.Roll(20);
        var effectiveRoll = Math.Max(1, saveRoll - 2);

        if (saveTarget > 0 && effectiveRoll >= saveTarget)
        {
            monster.SetStatus(MonsterStatus.Confused, 0);
            events.Add(new CombatEvent($"{monster.DisplayName} shakes off confusion (save {saveRoll}-2={effectiveRoll} vs {saveTarget})."));
            return false;
        }

        var actionRoll = _dice.Roll(100);
        if (actionRoll <= 10)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} wanders away in confusion and cannot act."));
            TickConfusedDuration(monster, events);
            return true;
        }

        if (actionRoll <= 60)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} stands confused and does nothing."));
            TickConfusedDuration(monster, events);
            return true;
        }

        if (actionRoll <= 80)
        {
            ResolveConfusedAttackNearestCreature(session, monster, events);
            TickConfusedDuration(monster, events);
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} turns on the druid's side in confusion!"));
        TickConfusedDuration(monster, events);
        return false;
    }

    private void TickConfusedDuration(MonsterInstance monster, List<CombatEvent> events)
    {
        var remaining = monster.TickStatus(MonsterStatus.Confused);
        if (remaining <= 0)
            events.Add(new CombatEvent($"{monster.DisplayName} is no longer confused."));
    }

    private void ResolveConfusedAttackNearestCreature(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        var targetMonster = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, monster))
            .OrderBy(m => string.Equals(m.GroupId, monster.GroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.CurrentHitPoints)
            .FirstOrDefault();

        if (targetMonster == null)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} lashes out wildly but finds no nearby creature."));
            return;
        }

        ResolveMonsterAttackAgainstMonster(
            attacker: monster,
            target: targetMonster,
            events: events,
            missText: $"{monster.DisplayName} attacks nearest creature {targetMonster.DisplayName} in confusion but misses.",
            hitText: $"{monster.DisplayName} attacks nearest creature {targetMonster.DisplayName} in confusion",
            slainText: $"{targetMonster.DisplayName} is slain by the confused attack!");
    }

    private void ResolveCharmedMonsterTurn(CombatSession session, MonsterInstance charmedMonster, List<CombatEvent> events)
    {
        var hostileMonsters = session.AliveMonsters
            .Where(m => !ReferenceEquals(m, charmedMonster))
            .Where(m => !m.HasStatus(MonsterStatus.Charmed))
            .OrderBy(m => string.Equals(m.GroupId, charmedMonster.GroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.CurrentHitPoints)
            .ToList();

        if (hostileMonsters.Count == 0)
        {
            events.Add(new CombatEvent($"{charmedMonster.DisplayName} is charmed and stands by the druid."));
            return;
        }

        var target = hostileMonsters[0];
        ResolveMonsterAttackAgainstMonster(
            attacker: charmedMonster,
            target: target,
            events: events,
            missText: $"{charmedMonster.DisplayName} fights for the druid and misses {target.DisplayName}.",
            hitText: $"{charmedMonster.DisplayName} fights for the druid and hits {target.DisplayName}",
            slainText: $"{target.DisplayName} is slain by the charmed ally!");
    }

    private void ResolveMonsterAttackAgainstMonster(
        MonsterInstance attacker,
        MonsterInstance target,
        List<CombatEvent> events,
        string missText,
        string hitText,
        string slainText)
    {
        var attack = attacker.Template.Attacks.FirstOrDefault()
                     ?? new MonsterAttack { Name = "Claw", NumberOfAttacks = 1, Damage = "1d4" };

        var thac0 = GetMonsterThac0(attacker);
        var needed = thac0 - target.ArmorClass;
        var roll = _dice.Roll(20);

        if (roll < needed)
        {
            events.Add(new CombatEvent(missText));
            return;
        }

        var damage = RollDamage(string.IsNullOrWhiteSpace(attack.Damage) ? "1d4" : attack.Damage);
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;
        WakeMonsterIfAsleepAfterDamage(target, actual, events);

        events.Add(new CombatEvent($"{hitText} for {actual} damage. HP {before}->{target.CurrentHitPoints}."));
        if (!target.IsAlive)
            events.Add(new CombatEvent(slainText));
    }

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
        var victimLevel = Math.Max(1, target.Level);
        var success = assassination.TryAssassinate(victimLevel, _rng);

        if (!success)
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

    private void TryApplyGiantRatDisease(MonsterInstance monster, Character target, List<CombatEvent> events)
    {
        var isGiantRat = string.Equals(monster.Template.Name, "Giant Rat", StringComparison.OrdinalIgnoreCase);
        if (!isGiantRat && !HasAnySpecialAbility(monster, "Giant Rat Disease", "Disease"))
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
        events.Add(new CombatEvent($"{target.Name} is diseased by {monster.DisplayName}! They will die upon next dungeon entry unless cured."));
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

        if (target.EarSeekerDeathOnNextDungeonEntry)
            return;

        target.ApplyInfestationDisease();
        events.Add(new CombatEvent($"{target.Name} suffers infestation by {monster.DisplayName}: rhizomes penetrate flesh. Cure Disease is required within 24 hours or death will occur."));
    }

    private static void TryApplyDrainBlood(MonsterInstance monster, Character target, CombatSession session, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "Drain Blood"))
            return;

        if (!IsAlive(target) || target.CurrentHitPoints <= 0)
            return;

        var remaining = session.GetPartyDrainBloodRemaining(target.Name);
        if (remaining <= 0)
        {
            session.SetPartyDrainBlood(target.Name, 12);
            events.Add(new CombatEvent($"{target.Name} is afflicted by Drain Blood from {monster.DisplayName}! 12 HP will be drained over subsequent rounds (1-4 per round)."));
            return;
        }

        events.Add(new CombatEvent($"{target.Name} is hit again by {monster.DisplayName}, but Drain Blood is already active ({remaining} HP remaining to drain)."));
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

        var rolledDamage = 0;
        for (int i = 0; i < 6; i++)
            rolledDamage += _dice.Roll(6);

        monster.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{monster.DisplayName} explodes violently!"));

        foreach (var member in session.AliveParty.ToList())
        {
            var saveTarget = ApplyUniversalPotionInvulnerabilitySaveBonus(
                member,
                _savingThrowService.GetSaveTarget(member, SaveThrowType.Spell));
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

            var applied = saveRoll >= saveTarget ? Math.Max(1, preSaveDamage / 2) : preSaveDamage;

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

    private static bool HasRingOfFireResistance(Character target)
    {
        if (target.Equipment == null)
            return false;

        static bool IsRingOfFireResistance(Item? item)
            => item != null
               && string.Equals(item.Name, "Ring of Fire Resistance", StringComparison.OrdinalIgnoreCase);

        target.Equipment.TryGetValue(EquipmentSlot.Ring1, out var ring1);
        target.Equipment.TryGetValue(EquipmentSlot.Ring2, out var ring2);
        return IsRingOfFireResistance(ring1) || IsRingOfFireResistance(ring2);
    }

    private static int GetFireResistanceSaveBonus(Character target)
    {
        var saveBonus = Math.Max(target.FireProtectionSaveBonusVsFire, target.PotionFireResistanceSaveBonusVsFire);
        if (HasRingOfFireResistance(target))
            saveBonus = Math.Max(saveBonus, 4);

        return saveBonus;
    }

    private static int ApplyFireProtectionDamageReduction(Character target, int incomingDamage, int damageDiceCount, bool isMagicalFire, bool isNormalFire)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        var hasSpellProtection = target.HasActiveProtectionFromFire;
        var hasPotionProtection = target.HasActivePotionFireResistance;
        var hasRingProtection = HasRingOfFireResistance(target);
        if (!hasSpellProtection && !hasPotionProtection && !hasRingProtection)
            return damage;

        var normalFireImmunity = (hasSpellProtection && target.FireProtectionNormalFireImmunity)
            || (hasPotionProtection && target.PotionFireResistanceNormalFireImmunity)
            || hasRingProtection;
        if (isNormalFire && normalFireImmunity)
            return 0;

        if (isMagicalFire && hasSpellProtection && target.FireProtectionHalfDamageFromMagicalFire)
            damage = (int)Math.Ceiling(damage * 0.5);

        var fireReductionPerDie = 0;
        if (isMagicalFire && hasPotionProtection && target.PotionFireResistanceDamageReductionPerDie > 0)
            fireReductionPerDie = Math.Max(fireReductionPerDie, target.PotionFireResistanceDamageReductionPerDie);
        if (isMagicalFire && hasRingProtection)
            fireReductionPerDie = Math.Max(fireReductionPerDie, 2);

        if (isMagicalFire && fireReductionPerDie > 0 && damageDiceCount > 0)
        {
            var reduction = fireReductionPerDie * damageDiceCount;
            damage = Math.Max(damageDiceCount, damage - reduction);
        }

        if (isMagicalFire && hasSpellProtection && target.FireProtectionAbsorptionRemaining > 0)
        {
            var absorbed = Math.Min(target.FireProtectionAbsorptionRemaining, damage);
            target.FireProtectionAbsorptionRemaining -= absorbed;
            damage -= absorbed;
        }

        return Math.Max(0, damage);
    }

    private static int ApplyColdResistanceDamageReduction(Character target, int incomingDamage, bool isMagicalCold, bool isNormalCold)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        if (!target.HasActiveResistCold)
            return damage;

        // Resist Cold inures normal cold and halves magical cold before saving throw.
        if (isNormalCold)
            return 0;

        if (isMagicalCold)
            damage = Math.Max(1, damage / 2);

        return Math.Max(0, damage);
    }

    private static int ApplyLightningProtectionDamageReduction(Character target, int incomingDamage, bool isMagicalLightning, bool isNormalLightning)
    {
        var damage = Math.Max(0, incomingDamage);
        if (damage <= 0)
            return 0;

        if (!target.HasActiveProtectionFromLightning)
            return damage;

        if (isNormalLightning && target.LightningProtectionNormalLightningImmunity)
            return 0;

        if (isMagicalLightning && target.LightningProtectionHalfDamageFromMagicalLightning)
            damage = (int)Math.Ceiling(damage * 0.5);

        if (isMagicalLightning && target.LightningProtectionAbsorptionRemaining > 0)
        {
            var absorbed = Math.Min(target.LightningProtectionAbsorptionRemaining, damage);
            target.LightningProtectionAbsorptionRemaining -= absorbed;
            damage -= absorbed;
        }

        return Math.Max(0, damage);
    }

    private static int ApplyUniversalPotionInvulnerabilitySaveBonus(Character target, int saveTarget)
    {
        var adjusted = saveTarget;

        if (target.HasActivePotionInvulnerability && target.PotionInvulnerabilitySaveBonus > 0)
            adjusted = Math.Max(1, adjusted - target.PotionInvulnerabilitySaveBonus);

        var ringProtectionSaveBonus = Math.Max(target.RingProtectionSelfSaveBonus, target.RingProtectionReceivedAuraSaveBonus);
        if (ringProtectionSaveBonus > 0)
            adjusted = Math.Max(1, adjusted - ringProtectionSaveBonus);

        return adjusted;
    }

    private static void ApplyRingProtectionAuras(CombatSession session)
    {
        if (session?.Party == null)
            return;

        foreach (var member in session.Party)
        {
            member.RefreshRingProtectionEffects();
            member.SetRingProtectionReceivedAuraSaveBonus(0);
        }

        var strongestAura = session.Party
            .Where(m => m.CurrentHitPoints > 0 && !m.HasStatus(CharacterStatus.Dead))
            .Select(m => Math.Max(0, m.RingProtectionAuraSaveBonus))
            .DefaultIfEmpty(0)
            .Max();

        if (strongestAura <= 0)
            return;

        foreach (var member in session.Party)
            member.SetRingProtectionReceivedAuraSaveBonus(strongestAura);
    }

    private static void ApplyRingWizardryEffects(CombatSession session)
    {
        if (session?.Party == null)
            return;

        foreach (var member in session.Party)
            member.RefreshRingWizardryEffects();
    }

    private void ApplyPoisonDamageDuringCombat(CombatSession session, List<CombatEvent> events)
    {
        foreach (var member in session.Party)
        {
            if (member.IsMonkImmuneToPoison())
            {
                if (member.HasStatus(CharacterStatus.Poisoned))
                {
                    member.RemoveStatus(CharacterStatus.Poisoned);
                    events.Add(new CombatEvent($"{member.Name}'s poison is negated by monk immunity."));
                }
                continue;
            }

            if (!IsAlive(member) || !member.HasStatus(CharacterStatus.Poisoned))
                continue;

            var poisonTickRoll = _dice.Roll(100);
            if (poisonTickRoll > 50)
                continue;

            var damage = _dice.Roll(3);
            var before = member.CurrentHitPoints;
            member.CurrentHitPoints = Math.Max(0, member.CurrentHitPoints - damage);
            var actual = before - member.CurrentHitPoints;
            WakeCharacterIfAsleepAfterDamage(member, actual, events);

            events.Add(new CombatEvent($"Poison harms {member.Name} for {actual} (rolled {damage}). HP {before}->{member.CurrentHitPoints}."));

            if (member.CurrentHitPoints <= 0)
            {
                member.AddStatus(CharacterStatus.Dead);
                events.Add(new CombatEvent($"{member.Name} dies from poison!"));
            }
        }
    }

    private static void WakeMonsterIfAsleepAfterDamage(MonsterInstance monster, int actualDamage, List<CombatEvent> events)
    {
        if (actualDamage <= 0)
            return;

        if (!monster.IsAlive)
            return;

        var wokeFromAsleep = monster.HasStatus(MonsterStatus.Asleep);
        var wokeFromUnconscious = monster.HasStatus(MonsterStatus.Unconscious);

        if (!wokeFromAsleep && !wokeFromUnconscious)
            return;

        if (wokeFromAsleep)
            monster.SetStatus(MonsterStatus.Asleep, 0);

        if (wokeFromUnconscious)
            monster.SetStatus(MonsterStatus.Unconscious, 0);

        events.Add(new CombatEvent($"{monster.DisplayName} wakes up from taking damage!"));
    }

    private static void WakeCharacterIfAsleepAfterDamage(Character member, int actualDamage, List<CombatEvent> events)
    {
        if (actualDamage <= 0)
            return;

        if (member.CurrentHitPoints <= 0)
            return;

        if (!member.HasStatus(CharacterStatus.Asleep))
            return;

        member.RemoveStatus(CharacterStatus.Asleep);
        events.Add(new CombatEvent($"{member.Name} wakes up from taking damage!"));
    }

    private static int GetEquippedRegeneration(Character member)
    {
        var hasRegeneration = member.Equipment.Values
            .Where(item => item != null)
            .Any(item => item!.SpecialAbilities.Any(a =>
                string.Equals(a?.Trim(), "Regeneration (1)", StringComparison.OrdinalIgnoreCase)));

        if (hasRegeneration)
            return 1;

        var hasRegenerationFromConsumable = member.Inventory
            .Any(item => ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (1)")
                         || ItemSpecialAbilityParser.HasSpecialAbility(item, "Regeneration (Potion)"));

        return hasRegenerationFromConsumable ? 1 : 0;
    }

    private static bool IsAlive(Character c) => c.CurrentHitPoints > 0 && !c.HasStatus(CharacterStatus.Dead);

    private static string ResolveWeaponDamageExpression(Character member, Item? mainHand, MonsterInstance target)
    {
        if (mainHand != null && mainHand.Type == ItemType.Weapon)
        {
            if (target.Template.Size == MonsterSize.Large && !string.IsNullOrWhiteSpace(mainHand.DamageVsLarge))
                return mainHand.DamageVsLarge;

            if (!string.IsNullOrWhiteSpace(mainHand.Damage))
                return mainHand.Damage;
        }

        return string.IsNullOrWhiteSpace(member.Damage) ? "1d2" : member.Damage;
    }

    private static bool IsHalfDamageFromSharpWeapons(MonsterInstance target, Item? mainHand)
    {
        if (mainHand == null || mainHand.Type != ItemType.Weapon)
            return false;

        var damageType = mainHand.DamageType?.Trim();
        if (string.IsNullOrWhiteSpace(damageType))
            return false;

        var isSharp = damageType.Equals("Piercing", StringComparison.OrdinalIgnoreCase)
            || damageType.Equals("Slashing", StringComparison.OrdinalIgnoreCase);

        if (!isSharp)
            return false;

        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "Half Damage from Sharp Weapons", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresPlusOneWeaponToHit(MonsterInstance target)
    {
        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "+1 or better weapons to hit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMagicalWeapon(Item? mainHand)
    {
        if (mainHand == null || mainHand.Type != ItemType.Weapon)
            return false;

        var hasPlusInName = !string.IsNullOrWhiteSpace(mainHand.Name)
            && mainHand.Name.Contains('+', StringComparison.Ordinal);
        var hasSpecialAbilities = mainHand.SpecialAbilities != null
            && mainHand.SpecialAbilities.Any(a => !string.IsNullOrWhiteSpace(a));

        return hasPlusInName || hasSpecialAbilities;
    }

    private static bool IsMonsterAttackConsideredMagical(MonsterInstance monster, Adnd.Core.Monsters.MonsterAttack attack)
    {
        static bool HasMagicalKeyword(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("magic", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("enchant", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("spell", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("elemental", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("demon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("devil", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("dragon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (HasMagicalKeyword(attack.Name))
            return true;

        if (monster.Template.SpecialAttacks.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        if (monster.Template.SpecialAbilities.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        return false;
    }

    private static bool MonsterHasMagicalProperties(MonsterInstance monster)
    {
        static bool HasMagicalKeyword(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("magic", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("enchant", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("spell", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("summon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("conjur", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("elemental", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("demon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("devil", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("dragon", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("undead", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (monster.InstanceMonsterType == Adnd.Core.Monsters.MonsterType.Undead)
            return true;

        if (HasMagicalKeyword(monster.Template.Name))
            return true;

        if (monster.Template.SpecialAbilities.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        if (monster.Template.SpecialAttacks.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        if (monster.Template.SpecialDefenses.Any(a => HasMagicalKeyword(a.Name)))
            return true;

        return false;
    }
}
