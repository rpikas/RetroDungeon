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
    private bool TryResolveLycanthropeCharacterAttack(CombatSession session, Character member, CombatAction action, List<CombatEvent> events)
    {
        if (!member.HasStatus(CharacterStatus.LycanthropeTransformed))
            return false;

        MonsterInstance? target = null;

        if (!string.IsNullOrEmpty(action.TargetMonsterId))
        {
            var named = session.FindMonster(action.TargetMonsterId);
            if (named != null && named.IsAlive)
                target = named;
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
            return true;

        if (IsLycanthropeMonster(target))
        {
            events.Add(new CombatEvent($"{member.Name} is a lycanthrope and refuses to attack {target.DisplayName}."));
            return true;
        }

        var thac0Modifier = (session.IsBlessed(member.Name) ? 1 : 0) + ((session.IsChantActive || session.IsPrayerActive) ? 1 : 0);
        var attacks = 3;

        for (var attackIndex = 0; attackIndex < attacks; attackIndex++)
        {
            if (!target.IsAlive)
                break;

            var roll = _dice.Roll(20);
            var needed = (member.Thac0 - thac0Modifier) - target.ArmorClass;
            if (roll == 1 || (roll != 20 && roll < needed))
            {
                var attackNameMiss = attackIndex < 2 ? "claw" : "bite";
                events.Add(new CombatEvent($"{member.Name} misses {target.DisplayName} with {attackNameMiss}."));
                continue;
            }

            var damageExpression = attackIndex < 2 ? "1d4" : "2d5";
            var before = target.CurrentHitPoints;
            var damage = RollDamage(damageExpression);
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            var attackName = attackIndex < 2 ? "claw" : "bite";
            events.Add(new CombatEvent($"{member.Name} hits {target.DisplayName} with {attackName} ({damageExpression}) for {damage} damage. HP {before}->{target.CurrentHitPoints}."));
            WakeMonsterIfAsleepAfterDamage(target, actual, events);

            if (target.CurrentHitPoints <= 0)
            {
                events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));
                break;
            }
        }

        return true;
    }

    private static bool IsLycanthropeMonster(MonsterInstance monster)
    {
        if (monster?.Template == null)
            return false;

        var name = monster.Template.Name ?? string.Empty;
        var typeName = monster.Template.TypeName ?? string.Empty;
        return name.Contains("Lycanthrope", StringComparison.OrdinalIgnoreCase)
               || typeName.Contains("Lycanthrope", StringComparison.OrdinalIgnoreCase)
               || HasAnySpecialAbility(monster, "Lycanthropy");
    }

    private void ResolvePartyAttack(CombatSession session, Character member, CombatAction action, List<CombatEvent> events)
    {
        session.SwallowedCharacters.TryGetValue(member.Name, out var swallowed);
        if (swallowed != null)
        {
            if (!member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var cuttingWeapon)
                || cuttingWeapon?.Type != ItemType.Weapon
                || !(string.Equals(cuttingWeapon.DamageType, "Slashing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cuttingWeapon.DamageType, "Piercing", StringComparison.OrdinalIgnoreCase)))
            {
                LogPurpleWormRule(events, $"{member.Name} is swallowed and needs an equipped slashing or piercing weapon to cut the worm from inside.");
                return;
            }
            LogPurpleWormRule(events, $"{member.Name} cuts at {swallowed.Worm.DisplayName} from inside: AC 9; -{swallowed.RoundsInside(session.RoundNumber)} damage on each attack this round.");
        }
        var chantOrPrayerBonus = (session.IsChantActive || session.IsPrayerActive) ? 1 : 0;

        if (member.BreakRingInvisibilityOnHostileAction())
            events.Add(new CombatEvent($"{member.Name}'s ring invisibility breaks on attack."));

        if (swallowed == null && member.HasStatus(CharacterStatus.LycanthropeTransformed) && TryResolveLycanthropeCharacterAttack(session, member, action, events))
            return;

        var rangedWeapon = member.Equipment.TryGetValue(EquipmentSlot.Range, out var rw) ? rw : null;
        var ammo = member.Equipment.TryGetValue(EquipmentSlot.Ammo, out var am) ? am : null;
        var fightingRank = CombatFormation.GetFightingRank(session.Party, member, session);
        var isFrontRankPosition = fightingRank is >= 1 and <= 3;
        var useRanged = swallowed == null && rangedWeapon != null
                        && !isFrontRankPosition
                        && (!string.IsNullOrWhiteSpace(rangedWeapon.Range)
                            || !string.IsNullOrWhiteSpace(rangedWeapon.FireRate)
                            || rangedWeapon.RequiresAmmo
                            || !string.IsNullOrWhiteSpace(rangedWeapon.AmmoType));

        if (useRanged && rangedWeapon != null)
        {
            if (IsJavelinOfLightning(rangedWeapon) && rangedWeapon.Quantity <= 0)
            {
                if (member.Equipment.TryGetValue(EquipmentSlot.Range, out var equippedRange)
                    && ReferenceEquals(equippedRange, rangedWeapon))
                {
                    member.Equipment[EquipmentSlot.Range] = null;
                }

                member.Inventory.RemoveAll(item => IsJavelinOfLightning(item) && item.Quantity <= 0);
                events.Add(new CombatEvent($"{member.Name}'s Javelin of Lightning is depleted."));
                return;
            }

            if (IsHammerOfThunderbolts(rangedWeapon) && session.RoundNumber % 2 != 0)
            {
                events.Add(new CombatEvent($"{member.Name} steadies {rangedWeapon.Name} and cannot hurl it this round."));
                return;
            }

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

        MonsterInstance? target = swallowed?.Worm;

        if (swallowed == null && !string.IsNullOrEmpty(action.TargetMonsterId))
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

        if (session.RoundNumber == 1
            && session.PartySurprisedRound1
            && useRanged
            && IsCrossbowOfSpeed(rangedWeapon))
        {
            attacks = Math.Min(attacks, 1);
        }

        if (session.IsHasted(member.Name))
            attacks *= 2;
        if (session.IsPartySlowed(member.Name))
            attacks = Math.Max(1, attacks / 2);

        var targetArmorClass = swallowed == null ? target.ArmorClass : 9;
        var isBackstab = swallowed == null && IsThiefBackstabAttack(member, session);
        if (isBackstab && IsPurpleWorm(target))
        {
            if (!session.RearAttackers.TryGetValue(target, out var rearAttackers))
                session.RearAttackers[target] = rearAttackers = new HashSet<Character>();
            rearAttackers.Add(member);
        }
        var backstabMultiplier = isBackstab ? GetThiefBackstabMultiplierByLevel(member.GetClassLevel(CharacterClass.Thief)) : 1;

        if (isBackstab && _dualClassService.IsThiefBackstabFromOriginalClass(member))
            _dualClassService.MarkOriginalClassFunctionUsed(member);

        if (member.Class == CharacterClass.Assassin
            && session.RoundNumber == 1
            && session.MonstersSurprisedRound1
            && target.IsAlive)
        {
            if (_dualClassService.IsAssassinationFromOriginalClass(member))
                _dualClassService.MarkOriginalClassFunctionUsed(member);

            var assassination = new AssassinationService("Data/Assassination");
            var assassinLevel = Math.Max(1, member.GetClassLevel(CharacterClass.Assassin));
            int monsterLevel = target.Template.HitDice;
            var assassinationRoll = assassination.RollAssassination(assassinLevel, monsterLevel, _rng);

            RuleApplicationInfo.Publish(
                "DMG",
                "75Assassin",
                $"{member.Name} assassination attempt vs {target.DisplayName}",
                "Use assassin level vs victim level/hit dice table; roll 1d100 and succeed if roll is less than or equal to chance.",
                "1",
                "100",
                assassinationRoll.Roll.ToString(),
                $"Assassin level {assassinLevel}, target HD {monsterLevel}, chance {assassinationRoll.ChancePercent}% => {(assassinationRoll.Success ? "success" : "failure")}.");

            if (assassinationRoll.Success)
            {
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{member.Name} assassinates {target.DisplayName} instantly!"));
                return;
            }
        }

        for (int i = 0; i < attacks; i++)
        {
            if (useRanged
                && (!member.Equipment.TryGetValue(EquipmentSlot.Range, out var currentRangedWeapon)
                    || !ReferenceEquals(currentRangedWeapon, rangedWeapon)))
            {
                useRanged = false;
                rangedWeapon = null;
            }

            var thac0Modifier = session.IsBlessed(member.Name) ? 1 : 0;
            thac0Modifier += chantOrPrayerBonus;
            if (isBackstab)
                thac0Modifier += 4;

            var weaponMagicToHitBonus = 0;

            Item? mainHand = null;
            if (member.Equipment.TryGetValue(EquipmentSlot.MainHand, out var equipped) && equipped != null)
            {
                mainHand = equipped;
                weaponMagicToHitBonus = Math.Max(0, mainHand.ToHitBonus);
                thac0Modifier += weaponMagicToHitBonus;

                var dancingSwordToHitBonus = GetSwordOfDancingCurrentBonus(mainHand, session);
                if (dancingSwordToHitBonus > 0)
                    thac0Modifier += dancingSwordToHitBonus - Math.Max(0, mainHand.ToHitBonus);

                if (IsSwordPlusFourDefender(mainHand))
                {
                    var defenderAttackBonus = GetDefenderAttackBonusAllocation(mainHand);
                    var redirectedToDefense = Math.Max(0, 4 - defenderAttackBonus);
                    if (redirectedToDefense > 0)
                        thac0Modifier -= redirectedToDefense;
                }

                if (IsSwordPlusFiveHolyAvenger(mainHand) && !member.IsPaladin())
                    thac0Modifier -= 3;
            }

            if (useRanged && rangedWeapon != null)
            {
                mainHand = rangedWeapon;
                weaponMagicToHitBonus = Math.Max(0, rangedWeapon.ToHitBonus);
                thac0Modifier += weaponMagicToHitBonus;

                if (CanUseDwarvenThrowerFullPower(member, rangedWeapon))
                    thac0Modifier += 1;

                thac0Modifier += GetHammerOfThunderboltsToHitBonus(member, rangedWeapon);
            }

            var strengthToHitBonus = useRanged ? 0 : member.Thac0StrengthModifier;
            thac0Modifier += strengthToHitBonus;

            var nauseaPenaltyApplied = 0;
            if (HasAnySpecialAbility(target, "Nausea") && session.HasPartyNauseaAgainstGroup(member.Name, target.GroupId))
            {
                nauseaPenaltyApplied = 2;
                thac0Modifier -= nauseaPenaltyApplied;
                events.Add(new CombatEvent($"{member.Name} suffers -2 to hit {target.DisplayName} due to carrion stench nausea (group {target.GroupId}); the penalty lasts until battle ends."));
            }

            var dexPenaltyApplied = 0;
            if (useRanged
                && member.Abilities.Dexterity < 13
                && HasSpecialDefense(target, "Dexterity Penalty"))
            {
                dexPenaltyApplied = 3;
                thac0Modifier -= dexPenaltyApplied;
                RuleApplicationInfo.PublishLinked(
                    "MM",
                    "Giant Bat",
                    $"Dexterity Penalty: {member.Name} has Dexterity {member.Abilities.Dexterity} (<13), so ranged attacks vs {target.DisplayName} are at -3 to hit.");
            }

            var nonProficiencyPenalty = WeaponProficiencyRules.GetNonProficiencyPenalty(member, mainHand, useRanged);
            if (nonProficiencyPenalty > 0)
            {
                var neededBeforeNonProficiencyPenalty = (member.Thac0 - thac0Modifier) - target.ArmorClass;
                thac0Modifier -= nonProficiencyPenalty;
                var neededAfterNonProficiencyPenalty = (member.Thac0 - thac0Modifier) - target.ArmorClass;
                var profWeaponType = WeaponProficiencyRules.ResolveWeaponProficiencyKey(mainHand, useRanged, member.IsMonk());
                var profWeaponName = mainHand?.Name ?? (member.IsMonk() ? "Fist or Open Hand" : "Unarmed");
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "37WeaponProficiency",
                    $"Weapon proficiency: {member.Name} attacks with {profWeaponName} ({profWeaponType}). Not proficient => to-hit penalty {nonProficiencyPenalty}. Needed roll changes {neededBeforeNonProficiencyPenalty} -> {neededAfterNonProficiencyPenalty}.");
            }

            var swordSituationalBonus = GetSituationalSwordBonusAgainstTarget(mainHand, target);
            if (swordSituationalBonus > 0 && mainHand != null)
            {
                thac0Modifier += swordSituationalBonus;
                var totalSwordBonus = weaponMagicToHitBonus + swordSituationalBonus;
                var bonusMessage = $"{member.Name} attacks {target.DisplayName} with {mainHand.Name}: +{totalSwordBonus} to hit and +{totalSwordBonus} damage against this creature (replaces normal +{weaponMagicToHitBonus}).";
                events.Add(new CombatEvent(bonusMessage));
                RuleApplicationInfo.Publish(bonusMessage);
            }

            var weaponVsAcKey = ResolveWeaponVsArmorTableKey(mainHand, useRanged);
            var weaponVsAcAdjustment = swallowed == null ? GetWeaponVsArmorClassAdjustment(mainHand, targetArmorClass, useRanged) : 0;
            if (!useRanged && mainHand == null && member.IsMonk())
            {
                weaponVsAcKey = null;
                weaponVsAcAdjustment = 0;
            }

            // Table values are expressed as weapon-vs-armor adjustments.
            // For needed-roll clarity: a negative table value increases needed roll.
            var neededAdjustmentFromWeaponVsAc = -weaponVsAcAdjustment;
            thac0Modifier -= neededAdjustmentFromWeaponVsAc;

            var incapacitatedMonsterReason = GetIncapacitatedTargetReason(target);
            if (!string.IsNullOrWhiteSpace(incapacitatedMonsterReason))
            {
                var incapacitatedToHitAdjustment = string.Equals(incapacitatedMonsterReason, "asleep", StringComparison.OrdinalIgnoreCase)
                    ? 3
                    : 4;
                thac0Modifier += incapacitatedToHitAdjustment;
                var weaponType = !string.IsNullOrWhiteSpace(weaponVsAcKey) ? weaponVsAcKey : (mainHand?.Name ?? "fist or open hand");
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "38WeaponTypeToHitAdjustment",
                    $"Weapon type to-hit adjustment: {member.Name} attacks {target.DisplayName}. Weapon type: {weaponType}. Adjustment: +{incapacitatedToHitAdjustment} because target is {incapacitatedMonsterReason}.");

                if (string.Equals(incapacitatedMonsterReason, "asleep", StringComparison.OrdinalIgnoreCase))
                    events.Add(new CombatEvent($"{member.Name} gains +3 to hit because {target.DisplayName} is asleep."));
            }

            if (target.ArmorClass >= 2 && target.ArmorClass <= 10)
            {
                var mode = useRanged ? "ranged" : "melee";
                var weaponNameForAdjustment = mainHand?.Name ?? "Fist or Open Hand";
                if (!string.IsNullOrWhiteSpace(weaponVsAcKey))
                {
                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "38WeaponTypeToHitAdjustment",
                        $"Weapon vs AC adjustment ({mode}): {member.Name} uses {weaponNameForAdjustment}. Weapon type: {weaponVsAcKey}. Needed-roll adjustment vs AC {target.ArmorClass}: {neededAdjustmentFromWeaponVsAc:+#;-#;0}.");
                }
                else
                {
                    if (!useRanged && mainHand == null && member.IsMonk())
                    {
                        RuleApplicationInfo.PublishLinked(
                            "PHB",
                            "38WeaponTypeToHitAdjustment",
                            "Weapon vs AC adjustment is not applicable for kombinationen monks och Fist or Open Hand.");
                    }
                    else
                    {
                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "38WeaponTypeToHitAdjustment",
                        $"Weapon vs AC adjustment ({mode}): {member.Name} uses {weaponNameForAdjustment}. Weapon type: unknown (no table row matched). Adjustment vs AC {target.ArmorClass}: 0.");
                    }
                }
            }

            if (session.IsBlinking(member.Name) && _dice.Roll(12) >= 7)
            {
                var directionRoll = _dice.Roll(8);
                var direction = new[] { "right ahead of", "to the right of", "right behind", "behind", "left behind", "to the left of", "left ahead of", "ahead of" }[directionRoll - 1];
                events.Add(new CombatEvent($"{member.Name} blinks and reappears {direction} {target.DisplayName} (direction roll {directionRoll})."));
                if (directionRoll is >= 3 and <= 5)
                {
                    thac0Modifier += 2;
                    events.Add(new CombatEvent($"{member.Name} strikes from behind and gains +2 to hit."));
                }
            }

            int needed = (member.Thac0 - thac0Modifier) - targetArmorClass;
            var neededFromThac0AndAcOnly = member.Thac0 - targetArmorClass;
            int roll = _dice.Roll(20);
            var wasNaturalTwenty = roll == 20;

            if (TryResolveBlinkDefenseAgainstAttack(target, events))
            {
                events.Add(new CombatEvent($"{member.Name}'s attack misses {target.DisplayName} due to blink displacement."));
                continue;
            }

            if (GameRulesProvider.Current.ShowToHitRoll)
            {
                var showReferences = GameRulesProvider.Current.ShowReferencesInRuleAndDiceInfo;
                var weaponNameForToHit = mainHand?.Name ?? (useRanged ? "Ranged attack" : "Fist or Open Hand");
                var weaponAdjText = neededAdjustmentFromWeaponVsAc == 0
                    ? string.Empty
                    : $" Weapon vs AC adj ({weaponNameForToHit}): {neededAdjustmentFromWeaponVsAc}.";
                var dexPenaltyText = dexPenaltyApplied > 0
                    ? $" Dex penalty ({target.DisplayName}): -{dexPenaltyApplied}."
                    : string.Empty;
                var nauseaPenaltyText = nauseaPenaltyApplied > 0
                    ? $" Nausea penalty ({target.DisplayName}): -2."
                    : string.Empty;
                var nonProfText = nonProficiencyPenalty > 0
                    ? $" Non-proficiency penalty: {nonProficiencyPenalty}."
                    : string.Empty;
                var strengthNeededAdjustment = -strengthToHitBonus;
                var weaponMagicNeededAdjustment = -weaponMagicToHitBonus;
                var swordSituationalNeededAdjustment = -swordSituationalBonus;
                var baseBreakdown = showReferences
                    ? $"{neededFromThac0AndAcOnly}(base)"
                    : $"{neededFromThac0AndAcOnly}";
                var neededBreakdownText =
                    $" {baseBreakdown}" +
                    $"{(neededAdjustmentFromWeaponVsAc >= 0 ? "+" : string.Empty)}{neededAdjustmentFromWeaponVsAc}(ac adj)" +
                    $"{(strengthNeededAdjustment >= 0 ? "+" : string.Empty)}{strengthNeededAdjustment}(str adj)" +
                    $"{(weaponMagicNeededAdjustment >= 0 ? "+" : string.Empty)}{weaponMagicNeededAdjustment}(magic weapon)" +
                    $"{(swordSituationalNeededAdjustment >= 0 ? "+" : string.Empty)}{swordSituationalNeededAdjustment}(special weapon)" +
                    $"{(nonProficiencyPenalty >= 0 ? "+" : string.Empty)}{nonProficiencyPenalty}(non-prof)" +
                    $"{(nauseaPenaltyApplied > 0 ? $"+{nauseaPenaltyApplied}(nausea)" : string.Empty)}={needed}";

                var toHitMessage = $"TO-HIT: {member.Name} THAC0 {member.Thac0},{weaponAdjText}{nonProfText}{nauseaPenaltyText} {target.DisplayName} AC {targetArmorClass}, needs {needed}{neededBreakdownText} on 1d20, rolled {roll}.";
                events.Add(new CombatEvent(toHitMessage));
                RuleApplicationInfo.Publish(toHitMessage);

                if (showReferences)
                {
                    RuleApplicationInfo.PublishLinked(
                        "DMG",
                        "74ToHitTables",
                        $"{member.Name} THAC0 {member.Thac0} from class:{member.Class.ToDisplayString()}/level:{member.GetClassLevel(member.Class)}");

                    RuleApplicationInfo.PublishLinked(
                        "MM",
                        target.Template.Name ?? target.DisplayName,
                        $"{target.DisplayName} AC:{targetArmorClass}{(swallowed != null ? " (stomach interior)" : string.Empty)}.");

                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "38WeaponTypeToHitAdjustment",
                        $"Weapon vs AC adjustment: {weaponNameForToHit} vs AC {targetArmorClass} => {neededAdjustmentFromWeaponVsAc:+#;-#;0}.");

                    var strengthLabel = member.Abilities.Strength == 18 && member.ExceptionalStrengthPercentile.HasValue
                        ? $"18/{member.ExceptionalStrengthPercentile.Value:00}"
                        : member.Abilities.Strength.ToString();
                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "9StrengthModifyers",
                        $"Strength {strengthLabel} gives {strengthToHitBonus:+#;-#;0} to hit bonus according to PHB 8, Ability Score Modifiers.");

                    RuleApplicationInfo.PublishLinked(
                        "DMG",
                        "165MagicWeapons",
                        $"Magic weapon bonus from {weaponNameForToHit}: {weaponMagicToHitBonus:+#;-#;0} to hit.");

                    RuleApplicationInfo.PublishLinked(
                        "PHB",
                        "37WeaponProficiency",
                        $"Weapon proficiency adjustment for {weaponNameForToHit}: {nonProficiencyPenalty:+#;-#;0} (penalty)." );
                }
            }

            if (roll < needed)
            {
                events.Add(new CombatEvent($"{member.Name} misses {target.DisplayName}."));
                continue;
            }

            if (HasSpecialDefense(target, "Phasing") && !_phaseSpidersInPhase.Remove(target))
            {
                events.Add(new CombatEvent($"{member.Name}'s attack passes through {target.DisplayName}, which is out of phase."));
                continue;
            }

            if (HasOnlyAffectedByFireBasedAttacksDefense(target) && !IsFireBasedWeaponAttack(mainHand))
            {
                var blockedWeaponName = mainHand?.Name ?? "bare hands";
                events.Add(new CombatEvent($"{member.Name} hits {target.DisplayName} with {blockedWeaponName}, but it is affected only by fire-based attacks."));
                RuleApplicationInfo.Publish(
                    "AD&D",
                    "Affected only by fire based attacks",
                    $"{target.DisplayName} ignores non-fire attack",
                    "Creatures with this defense can only be harmed by fire-based attacks.",
                    null,
                    null,
                    null,
                    $"{member.Name}'s attack with {blockedWeaponName} deals no damage.");
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

            if (wasNaturalTwenty
                && IsSwordPlusTwoNineLivesStealer(mainHand)
                && TryResolveNineLivesStealerEffect(mainHand, target, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;

                continue;
            }

            if (wasNaturalTwenty
                && IsSwordOfLifeStealing(mainHand))
            {
                TryResolveSwordOfLifeStealingEffect(member, target, events);
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (wasNaturalTwenty
                && IsDaggerOfVenom(mainHand))
            {
                TryResolveDaggerOfVenomEffect(mainHand, target, events);
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (TryResolveMaceOfDisruptionEffect(member, mainHand, target, useRanged, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (TryResolveSwordOfSharpnessSeverEffect(mainHand, target, roll, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            if (TryResolveSwordVorpalSeverEffect(mainHand, target, roll, events))
            {
                if (target.CurrentHitPoints <= 0)
                    break;
            }

            TryApplyHitOnMonsterDestroysMetal(member, target, mainHand, events);

            if (!CanWeaponHarmTargetByMagicRequirement(mainHand, target))
            {
                var blockedWeaponName = mainHand != null ? mainHand.Name : "bare hands";
                events.Add(new CombatEvent(
                    $"{member.Name} hits {target.DisplayName} with {blockedWeaponName}, but the attack cannot harm it (insufficient magical weapon bonus)."));

                RuleApplicationInfo.Publish(
                    $"AD&D special defense: {target.DisplayName} requires a magical weapon bonus to hit. " +
                    $"{member.Name}'s attack with {blockedWeaponName} does not meet the required bonus, so it deals no damage.");
                continue;
            }

            if (TryResolveMummyImprovisedWeaponDamage(session, member, target, mainHand, events))
                break;

            var damageExpression = ResolveWeaponDamageExpression(member, mainHand, target);
            var strengthDamageBonus = mainHand != null && mainHand.Type == ItemType.Weapon
                ? AbilitiesTables.StrengthDamageModifier(member.Abilities.Strength, member.ExceptionalStrengthPercentile)
                : 0;

            int damage = RollDamage(damageExpression) + strengthDamageBonus;
            if (IsSwordPlusFiveHolyAvenger(mainHand) && !member.IsPaladin())
                damage = Math.Max(1, damage - 3);

            var dancingSwordBonus = GetSwordOfDancingCurrentBonus(mainHand, session);
            if (dancingSwordBonus > 1)
                damage += dancingSwordBonus - 1;

            if (IsSwordPlusFiveHolyAvenger(mainHand)
                && member.IsPaladin()
                && IsTargetChaoticEvil(target))
            {
                damage += 10;
            }

            if (swordSituationalBonus > 0)
                damage += swordSituationalBonus;
            var partyDamageBonusApplied = chantOrPrayerBonus > 0;
            if (partyDamageBonusApplied)
                damage += 1;

            if (IsSwordPlusTwoGiantSlayer(mainHand) && IsTargetTrueGiantForGiantSlayer(target))
                damage *= 2;

            var dragonSlayerDamageMultiplier = GetDragonSlayerDamageMultiplier(mainHand, target);
            if (dragonSlayerDamageMultiplier > 1)
                damage *= dragonSlayerDamageMultiplier;

            var maceOfDisruptionMultiplier = GetMaceOfDisruptionDamageMultiplier(mainHand, target);
            if (maceOfDisruptionMultiplier > 1)
                damage *= maceOfDisruptionMultiplier;

            var coldIronVulnerabilityMultiplier = GetColdIronVulnerabilityDamageMultiplier(mainHand, target);
            if (coldIronVulnerabilityMultiplier > 1)
            {
                var beforeColdIron = damage;
                damage *= coldIronVulnerabilityMultiplier;
                var coldIronWeaponName = mainHand?.Name ?? "attack";
                events.Add(new CombatEvent($"{target.DisplayName} is vulnerable to cold iron: {coldIronWeaponName} damage doubled ({beforeColdIron}->{damage})."));
            }

            if (useRanged && CanUseDwarvenThrowerFullPower(member, mainHand))
                damage += 1;

            var hammerOfThunderboltsPowerBonus = GetHammerOfThunderboltsDamageBonus(member, mainHand, useRanged);
            if (hammerOfThunderboltsPowerBonus > 0)
                damage += hammerOfThunderboltsPowerBonus;

            var dwarvenThrowerDamageMultiplier = GetDwarvenThrowerDamageMultiplier(member, mainHand, target, useRanged);
            if (dwarvenThrowerDamageMultiplier > 1)
                damage *= dwarvenThrowerDamageMultiplier;

            var thunderboltsDamageDiceMultiplier = GetHammerOfThunderboltsDamageDiceMultiplier(member, mainHand);
            if (thunderboltsDamageDiceMultiplier > 1)
                damage *= thunderboltsDamageDiceMultiplier;

            var monkWeaponDamageBonus = 0;
            if (member.IsMonk() && mainHand?.Type == ItemType.Weapon)
                monkWeaponDamageBonus = Math.Max(0, member.GetMonkLevel() / 2);

            if (CanWieldHammerOfThunderboltsAsPlusFive(member, mainHand)
                && IsTargetGiantOrKinForHammerOfThunderbolts(target))
            {
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{member.Name}'s Hammer of Thunderbolts slays {target.DisplayName} outright!"));
                continue;
            }

            if (isBackstab)
            {
                var beforeBackstab = damage;
                damage *= backstabMultiplier;
                events.Add(new CombatEvent($"{member.Name} backstabs! +4 to hit, damage x{backstabMultiplier} ({beforeBackstab}->{damage})."));
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "27Backstab",
                    $"{member.Name} uses backstab from surprise/behind: +4 to hit, damage multiplier x{backstabMultiplier} ({beforeBackstab}->{damage}).");
            }

            if (monkWeaponDamageBonus > 0)
            {
                damage += monkWeaponDamageBonus;
                var monkWeaponName = mainHand?.Name ?? "weapon";
                RuleApplicationInfo.PublishLinked(
                    "PHB",
                    "30Monk",
                    $"Monk weapon damage bonus: {member.Name} L{member.GetMonkLevel()} uses {monkWeaponName}. Bonus +{monkWeaponDamageBonus} damage (level/2, rounded down)."
                );
            }

            if (IsHalfDamageFromSharpWeapons(target, mainHand))
            {
                var originalDamage = damage;
                damage = Math.Max(1, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName} has Half Damage from Sharp Weapons. Damage reduced from {originalDamage} to {damage}."));
            }

            if (HasSpecialDefense(target, "Rakshasa Weapon Defense")
                && GetEffectiveWeaponBonusForHitRequirement(mainHand) < 3)
            {
                var originalDamage = damage;
                damage = Math.Max(1, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName} takes half damage from magical weapons below +3 ({originalDamage}->{damage})."));
            }

            if (HasSpecialDefense(target, "Silver Weapons Half Damage")
                && IsSilverWeapon(mainHand)
                && GetEffectiveWeaponBonusForHitRequirement(mainHand) < 1)
            {
                var originalDamage = damage;
                damage = Math.Max(1, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName} takes only half damage from non-magical silver weapons ({originalDamage}->{damage})."));
            }

            if (HasMagicWeaponsOnlyHalfDamageDefense(target))
            {
                var originalDamage = damage;
                damage = Math.Max(0, damage / 2);
                events.Add(new CombatEvent($"{target.DisplayName}'s mummy defenses halve magical weapon damage ({originalDamage}->{damage}, fractions dropped)."));
            }

            if (swallowed != null)
            {
                var penalty = swallowed.RoundsInside(session.RoundNumber);
                var beforePenalty = damage;
                damage = Math.Max(0, damage - penalty);
                LogPurpleWormRule(events, $"{member.Name}'s stomach attack damage: {beforePenalty} - {penalty} (round {penalty} inside) = {damage}.");
            }
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actualDamageToTarget = before - target.CurrentHitPoints;

            TryRevealWillOWispLairTreasureOnLowHp(target, events);

            if (actualDamageToTarget > 0 && IsFireBasedWeaponAttack(mainHand) && HasMonsterRegeneration(target))
                session.AddMonsterWoundingUnhealableDamage(target, actualDamageToTarget);

            if (actualDamageToTarget > 0 && IsSwordOfWounding(mainHand))
            {
                session.AddMonsterWoundingWound(target, rounds: 10);
                session.AddMonsterWoundingUnhealableDamage(target, actualDamageToTarget);
                events.Add(new CombatEvent($"Sword of Wounding opens a lingering wound on {target.DisplayName} (10 rounds)."));
            }

            WakeMonsterIfAsleepAfterDamage(target, actualDamageToTarget, events);

            if (useRanged && mainHand != null)
                ResolveJavelinOfLightningStrike(member, mainHand, target, session, events);

            if (useRanged && rangedWeapon != null && rangedWeapon.RequiresAmmo && ammo != null && ammo.Quantity > 0)
            {
                ammo.Quantity = Math.Max(0, ammo.Quantity - 1);
                events.Add(new CombatEvent($"{member.Name} uses 1 {ammo.Name}. {ammo.Quantity} remaining."));
            }

            if (useRanged && IsHammerOfThunderbolts(mainHand))
            {
                var restTriggered = session.RegisterPartyThunderboltsThrow(member.Name, session.RoundNumber);
                if (restTriggered)
                    events.Add(new CombatEvent($"{member.Name} has overtaxed the Hammer of Thunderbolts and must rest for 10 rounds."));

                events.Add(new CombatEvent($"The hurled Hammer of Thunderbolts crashes like thunder! Nearby creatures are stunned for 1 round."));
                foreach (var monster in session.AliveMonsters.Where(m => !ReferenceEquals(m, target)).ToList())
                    monster.SetStatus(MonsterStatus.Stunned, 1);
            }

            var weaponName = mainHand != null ? mainHand.Name : "bare hands";
            var damageFormula = strengthDamageBonus == 0
                ? damageExpression
                : $"{damageExpression}+{strengthDamageBonus}";
            if (monkWeaponDamageBonus > 0)
                damageFormula += $"+{monkWeaponDamageBonus}(monk)";

            var partyDamageModText = partyDamageBonusApplied ? " +1 chant/prayer" : string.Empty;
            events.Add(new CombatEvent(
                $"{member.Name} hits {target.DisplayName} with {weaponName} ({damageFormula}{partyDamageModText}) for {damage}  damage. HP {before}->{target.CurrentHitPoints}."));

            var didSplitFromSlashing = actualDamageToTarget > 0
                && IsSlashingAttack(mainHand)
                && MonsterSplitResolver.TryResolveSplit(
                    session,
                    target,
                    actualDamageToTarget,
                    JellySplitTrigger.Slashing,
                    message => events.Add(new CombatEvent(message)));

            if (didSplitFromSlashing)
                break;

            if (actualDamageToTarget > 0 && TryResolveMonsterExplosionOnHit(target, session, events))
                break;

            if (target.CurrentHitPoints <= 0)
            {
                events.Add(new CombatEvent($"{target.DisplayName} is destroyed."));
                break;
            }
        }
    }

    private static void TryRevealWillOWispLairTreasureOnLowHp(MonsterInstance target, List<CombatEvent> events)
    {
        if (target?.Template == null)
            return;

        if (!IsWillOWisp(target))
            return;

        if (!target.IsAlive)
            return;

        if (target.CurrentHitPoints > 5)
            return;

        if (target.HasRevealedLairTreasure)
            return;

        target.HasRevealedLairTreasure = true;
        target.IsInLair = true;

        events.Add(new CombatEvent($"{target.DisplayName} falters at {target.CurrentHitPoints} HP, reveals its lair, and gives over its treasure."));
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

    private static bool CanActDuringCompleteSurpriseWithCrossbowOfSpeed(Character member, CombatAction? action)
    {
        if (member?.Equipment == null)
            return false;

        if (action?.Type != CombatActionType.Fight)
            return false;

        if (!member.Equipment.TryGetValue(EquipmentSlot.Range, out var rangedWeapon))
            return false;

        return IsCrossbowOfSpeed(rangedWeapon);
    }

    private static bool IsCrossbowOfSpeed(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        return string.Equals(weapon.Name, "Crossbow of Speed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(weapon.Name, "Crossbow of Speed (Heavy Crossbow)", StringComparison.OrdinalIgnoreCase);
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
}
