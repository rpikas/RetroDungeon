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
    private void ResolvePartySpell(CombatSession session, Character caster, CombatAction action, List<CombatEvent> events)
    {
        if (caster.HasStatus(CharacterStatus.Petrified))
        {
            events.Add(new CombatEvent($"{caster.Name} is petrified and cannot cast spells."));
            return;
        }

        if (caster.HasStatus(CharacterStatus.LycanthropeTransformed))
        {
            events.Add(new CombatEvent($"{caster.Name} is transformed by lycanthropy and cannot cast spells."));
            return;
        }

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

        var selectedSpell = _spellCastingService.FindSpellById(action.SpellId);
        if (selectedSpell != null
            && _dualClassService.IsSpellFromOriginalClass(caster, selectedSpell.SpellClass))
        {
            _dualClassService.MarkOriginalClassFunctionUsed(caster);
        }

        var targets = action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();

        var result = _spellCastingService.Cast(new SpellCastRequest
        {
            Caster = caster,
            SpellId = action.SpellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party.Where(p => !session.IsSwallowed(p.Name)).ToList(),
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

    private void ResolveBardCharm(CombatSession session, Character bard, CombatAction action, List<CombatEvent> events)
    {
        if (bard.HasStatus(CharacterStatus.LycanthropeTransformed))
        {
            events.Add(new CombatEvent($"{bard.Name} is transformed by lycanthropy and cannot use bard abilities."));
            return;
        }

        if (!bard.IsBard())
        {
            events.Add(new CombatEvent($"{bard.Name} cannot use bard charm."));
            return;
        }

        var bardLevel = bard.GetBardLevel();
        var charmPercent = BardRules.GetCharmPercentage(bardLevel);
        if (charmPercent <= 0)
        {
            events.Add(new CombatEvent($"{bard.Name} cannot use bard charm yet."));
            return;
        }

        events.Add(new CombatEvent($"{bard.Name} uses Bard Charm (chance {charmPercent}%)."));
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "118BardTable",
            $"Bard charm used: {bard.Name} L{bardLevel} has {charmPercent}% base chance.");

        var candidates = string.IsNullOrWhiteSpace(action.TargetGroupId)
            ? session.AliveMonsters.ToList()
            : session.GetAliveMonstersByGroup(action.TargetGroupId).ToList();

        candidates = candidates
            .Where(m => !m.HasStatus(MonsterStatus.Charmed))
            .Where(m => m.InstanceMonsterType == MonsterType.Humanoid || m.InstanceMonsterType == MonsterType.Other)
            .ToList();

        if (candidates.Count == 0)
        {
            events.Add(new CombatEvent($"{bard.Name} uses bard charm, but there are no suitable targets."));
            return;
        }

        var target = candidates[_rng.Next(candidates.Count)];
        var roll = _dice.Roll(100);
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "118BardTable",
            $"Bard charm: {bard.Name} L{bardLevel} has {charmPercent}% chance (rolled {roll}) against {target.DisplayName}.");

        if (roll <= charmPercent)
        {
            target.SetStatus(MonsterStatus.Charmed, int.MaxValue);
            events.Add(new CombatEvent($"{bard.Name} charms {target.DisplayName} ({roll} <= {charmPercent})."));
            return;
        }

        events.Add(new CombatEvent($"{bard.Name} fails to charm {target.DisplayName} ({roll} > {charmPercent})."));
    }

    private bool TryResolveMonsterBardCharm(CombatSession session, MonsterInstance monster, List<CombatEvent> events)
    {
        if (!HasAnySpecialAbility(monster, "BardCharm", "Bardic Music"))
            return false;

        var aliveParty = session.ExposedParty
            .Where(IsAlive)
            .ToList();
        if (aliveParty.Count == 0)
            return false;

        var bardLevel = Math.Max(1, monster.Template.HitDice);
        var charmPercent = BardRules.GetCharmPercentage(bardLevel);
        if (charmPercent <= 0)
            return false;

        var candidates = aliveParty
            .Where(c => !c.HasStatus(CharacterStatus.Charmed))
            .ToList();
        if (candidates.Count == 0)
        {
            events.Add(new CombatEvent($"{monster.DisplayName} uses Bard Charm, but all party members are already charmed."));
            RuleApplicationInfo.PublishLinked(
                "PHB",
                "118BardTable",
                $"Bard charm: {monster.DisplayName} L{bardLevel} has {charmPercent}% chance, but there are no uncharmed party targets.");
            return true;
        }

        var target = candidates[_dice.Roll(candidates.Count) - 1];
        var roll = _dice.Roll(100);
        events.Add(new CombatEvent($"{monster.DisplayName} uses Bard Charm on {target.Name} (chance {charmPercent}%)."));
        RuleApplicationInfo.PublishLinked(
            "PHB",
            "118BardTable",
            $"Bard charm: {monster.DisplayName} L{bardLevel} has {charmPercent}% chance (rolled {roll}) against {target.Name}.");

        if (roll <= charmPercent)
        {
            target.AddStatus(CharacterStatus.Charmed);
            events.Add(new CombatEvent($"{target.Name} is charmed by {monster.DisplayName} ({roll} <= {charmPercent}) and will attack party allies."));
            return true;
        }

        events.Add(new CombatEvent($"{monster.DisplayName} fails to charm {target.Name} ({roll} > {charmPercent})."));
        return true;
    }

    private void ResolvePartyUseItem(CombatSession session, Character user, CombatAction action, List<CombatEvent> events)
    {
        if (user.HasStatus(CharacterStatus.Petrified))
        {
            events.Add(new CombatEvent($"{user.Name} is petrified and cannot use items."));
            return;
        }

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

        if (ItemSpecialAbilityParser.IsPotionOfDelusion(item))
        {
            var appearedAs = ItemSpecialAbilityParser.TryGetDelusionDisguiseName(item, out var disguise)
                ? disguise
                : item.Name;

            user.Inventory.RemoveAt(action.ItemInventoryIndex.Value);
            events.Add(new CombatEvent($"{user.Name} drinks {appearedAs}. It was a Potion of Delusion!"));
            return;
        }

        if (user.HasActiveProtectionFromMagicScroll)
        {
            events.Add(new CombatEvent($"{user.Name} is inside anti-magic protection and cannot activate magical items."));
            return;
        }

        var grantsRegenerationUntilDungeonExit = ItemSpecialAbilityParser.HasCastsAbility(item, "Regeneration");
        var grantsFireResistancePotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Fire Resistance");
        var grantsGiantStrengthPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Giant Strength");
        var grantsAnimalControlPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Animal Control");
        var grantsHumanControlPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Human Control");
        var grantsMammalControlRing = string.Equals(item.Name, "Ring of Mammal Control", StringComparison.OrdinalIgnoreCase);
        var grantsHeroismPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Heroism");
        var grantsSuperHeroismPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Super-Heroism");
        var grantsInvulnerabilityPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Invulnerability");
        var grantsProtectionFromMagicScroll = ItemSpecialAbilityParser.HasCastsAbility(item, "Protection from Magic")
                                             || string.Equals(item.Name, "Scroll of Protection from Magic", StringComparison.OrdinalIgnoreCase);
        var grantsLevitationPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Levitate") || string.Equals(item.Name, "Potion of Levitation", StringComparison.OrdinalIgnoreCase);
        var grantsSpeedPotion = ItemSpecialAbilityParser.HasCastsAbility(item, "Haste")
                                || string.Equals(item.Name, "Potion of Speed", StringComparison.OrdinalIgnoreCase);
        var grantsDustOfAppearance = ItemSpecialAbilityParser.HasCastsAbility(item, "Dust of Appearance")
                                     || string.Equals(item.Name, "Dust of Appearance", StringComparison.OrdinalIgnoreCase);
        var grantsTalismanOfPureGood = ItemSpecialAbilityParser.HasSpecialAbility(item, "Talisman of Pure Good");
        var grantsHornOfValhalla = HornOfValhalla.IsHornOfValhalla(item);
        var isPotionOfHealing = string.Equals(item.Name, "Potion of Healing", StringComparison.OrdinalIgnoreCase);

        bool IsDirectEffectConsumable()
            => grantsRegenerationUntilDungeonExit
               || grantsFireResistancePotion
               || grantsGiantStrengthPotion
               || grantsAnimalControlPotion
               || grantsHumanControlPotion
               || grantsMammalControlRing
               || grantsHeroismPotion
               || grantsSuperHeroismPotion
               || grantsInvulnerabilityPotion
               || grantsProtectionFromMagicScroll
               || grantsLevitationPotion
               || grantsSpeedPotion
               || grantsDustOfAppearance
               || grantsHornOfValhalla
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

            if (grantsHumanControlPotion)
            {
                var typeRoll = PotionOfHumanControl.EnsureAssignedControlRoll(item, sides => _dice.Roll(Math.Max(1, sides)));
                var category = PotionOfHumanControl.GetControlCategory(typeRoll);
                var durationRounds = 0;
                for (var i = 0; i < 5; i++)
                    durationRounds += _dice.Roll(6);

                var eligibleTargets = session.AliveMonsters
                    .Where(m => IsHumanControlCategoryMatch(m, typeRoll))
                    .OrderBy(_ => _rng.Next())
                    .ToList();

                events.Add(new CombatEvent($"{user.Name} drinks Potion of Human Control (d20 {typeRoll}: {category}, duration {durationRounds} rounds)."));

                if (eligibleTargets.Count == 0)
                {
                    events.Add(new CombatEvent("No eligible humans, humanoids, or demi-humans match this potion's control type."));
                }
                else
                {
                    var hitDiceBudget = 32;
                    var attempted = 0;
                    var controlled = 0;

                    foreach (var target in eligibleTargets)
                    {
                        if (hitDiceBudget <= 0)
                            break;

                        var hitDiceCost = Math.Max(1, GetHumanControlHitDiceCost(target));
                        if (hitDiceCost > hitDiceBudget)
                            continue;

                        attempted += 1;
                        hitDiceBudget -= hitDiceCost;

                        if (target.HasStatus(MonsterStatus.Charmed))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is already charmed."));
                            continue;
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMindAffectingImmunity(target, "Human Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} is immune to mind-affecting control."));
                            continue;
                        }

                        if (SpellDamageSaveHelper.IsNegatedByMagicResistance(target, _rng, "Human Control"))
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} negates human control with magic resistance."));
                            continue;
                        }

                        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
                        var saveRoll = _dice.Roll(20);
                        if (saveRoll >= saveTarget)
                        {
                            events.Add(new CombatEvent($"{target.DisplayName} resists human control (save {saveRoll} vs {saveTarget})."));
                            continue;
                        }

                        target.SetStatus(MonsterStatus.Charmed, durationRounds);
                        controlled += 1;
                        events.Add(new CombatEvent($"{target.DisplayName} is charmed for {durationRounds} rounds and fights for {user.Name}."));
                    }

                    events.Add(new CombatEvent($"Human control budget: started at 32 HD, attempted {attempted}, controlled {controlled}, remaining {hitDiceBudget}."));
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
                var protectionMessage = $"{user.Name} reads Scroll of Protection from Magic: anti-magic globe (5' radius) surrounds the reader for {rounds} round(s).";
                events.Add(new CombatEvent(protectionMessage));
                RuleApplicationInfo.PublishLinked("DMG", "128ScrollOfProtectionFromMagic", protectionMessage);
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

            if (grantsDustOfAppearance)
            {
                var revealedCount = 0;
                foreach (var monster in session.Monsters)
                {
                    if (!monster.IsAlive)
                        continue;

                    if (!monster.HasStatus(MonsterStatus.Invisible))
                        continue;

                    monster.SetStatus(MonsterStatus.Invisible, 0);
                    revealedCount += 1;
                }

                events.Add(revealedCount > 0
                    ? new CombatEvent($"{user.Name} uses Dust of Appearance: {revealedCount} invisible monster(s) are revealed.")
                    : new CombatEvent($"{user.Name} uses Dust of Appearance, but no invisible monsters are present."));
            }
        }

        if (string.IsNullOrWhiteSpace(spellId))
        {
            var spell = _spellCastingService.FindSpellFromItem(item);
            spellId = spell?.Id;
        }

        if (string.Equals(action.SpellId, "__wand_of_fear__", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, "Wand of Fear", StringComparison.OrdinalIgnoreCase))
        {
            ResolveWandOfFearUse(session, user, action, item, events);
            return;
        }

        if (string.Equals(action.SpellId, "__talisman_pure_good__", StringComparison.OrdinalIgnoreCase)
            || grantsTalismanOfPureGood)
        {
            ResolveTalismanOfPureGoodUse(session, user, action, item, events);
            return;
        }

        if (string.Equals(action.SpellId, "__horn_of_valhalla__", StringComparison.OrdinalIgnoreCase)
            || grantsHornOfValhalla)
        {
            ResolveHornOfValhallaUse(session, user, item, events);
            return;
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

        var targets = item.Type == ItemType.Potion
            ? new List<SpellCastTarget> { SpellCastTarget.Ally(user) }
            : action.Target != null ? new List<SpellCastTarget> { action.Target } : new List<SpellCastTarget>();
        var result = _spellCastingService.CastFromItem(new SpellCastRequest
        {
            Caster = user,
            SpellId = spellId,
            Context = SpellUseContext.Combat,
            PartyTargets = session.Party.Where(p => !session.IsSwallowed(p.Name)).ToList(),
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

    private void ResolveWandOfFearUse(CombatSession session, Character user, CombatAction action, Item wand, List<CombatEvent> events)
    {
        const string chargePrefix = "WandCharges:";
        wand.SpecialAbilities ??= new List<string>();
        var existing = wand.SpecialAbilities.FirstOrDefault(a => a.StartsWith(chargePrefix, StringComparison.OrdinalIgnoreCase));
        var charges = existing != null && int.TryParse(existing.Split(':', 2)[1], out var parsed)
            ? Math.Max(0, parsed)
            : _dice.Roll(20) + 80;

        if (charges <= 0)
        {
            events.Add(new CombatEvent($"{user.Name} points {wand.Name}, but it has no charges remaining."));
            return;
        }

        charges--;
        wand.SpecialAbilities.RemoveAll(a => a.StartsWith(chargePrefix, StringComparison.OrdinalIgnoreCase));
        wand.SpecialAbilities.Add($"{chargePrefix}{charges}");

        var targetGroup = action.Target?.TargetGroupId;
        var targets = session.Monsters.Where(m => m.IsAlive);
        if (!string.IsNullOrWhiteSpace(targetGroup) && !string.Equals(targetGroup, "default", StringComparison.OrdinalIgnoreCase))
            targets = targets.Where(m => string.Equals(m.GroupId, targetGroup, StringComparison.OrdinalIgnoreCase));

        var targetList = targets.ToList();
        events.Add(new CombatEvent($"{user.Name} points {wand.Name}: a pale amber cone of fear flashes out. Charges remaining: {charges}."));
        if (targetList.Count == 0)
        {
            events.Add(new CombatEvent("The ray touches no creature."));
            return;
        }

        var messages = new List<string>();
        FearHandler.ApplyFear(targetList, 6, wand.Name, "fear", saveVsWand: true, new Random(_dice.Roll(1_000_000)), messages);
        foreach (var message in messages)
            events.Add(new CombatEvent(message));
    }

    private void ResolveTalismanOfPureGoodUse(CombatSession session, Character user, CombatAction action, Item talisman, List<CombatEvent> events)
    {
        var charges = GetOrAssignTalismanOfPureGoodCharges(talisman);
        if (charges <= 0)
        {
            events.Add(new CombatEvent($"{user.Name} tries to use {talisman.Name}, but it has no charges remaining."));
            return;
        }

        SetTalismanOfPureGoodCharges(talisman, charges - 1);
        events.Add(new CombatEvent($"{user.Name} invokes {talisman.Name}. Charges remaining: {Math.Max(0, charges - 1)}."));

        var clericLevel = user.Classes.Contains(CharacterClass.Cleric)
            ? user.GetClassLevel(CharacterClass.Cleric)
            : 0;

        if (clericLevel < 9)
        {
            events.Add(new CombatEvent($"{talisman.Name} can only be properly used by a cleric of level 9 or higher."));
            return;
        }

        if (IsNeutralAlignment(user.Alignment))
        {
            var damage = RollNd4(7);
            ApplyDirectDamageToCharacter(user, damage, events,
                $"{talisman.Name} lashes back at {user.Name} for {{0}} damage (neutral alignment). HP {{1}}->{{2}}.");
            return;
        }

        if (IsEvilAlignment(user.Alignment))
        {
            var damage = RollNd4(12);
            ApplyDirectDamageToCharacter(user, damage, events,
                $"{talisman.Name} devastates {user.Name} for {{0}} damage (evil alignment). HP {{1}}->{{2}}.");
            return;
        }

        if (!IsGoodAlignment(user.Alignment))
        {
            events.Add(new CombatEvent($"{talisman.Name} does not respond to {user.Name}."));
            return;
        }

        var target = FindTargetedEvilCleric(session, action);
        if (target == null)
        {
            events.Add(new CombatEvent($"No evil cleric is affected by {talisman.Name}."));
            return;
        }

        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"{target.DisplayName} is sent to the center of the earth and destroyed by {talisman.Name}!"));
    }

    private static MonsterInstance? FindTargetedEvilCleric(CombatSession session, CombatAction action)
    {
        IEnumerable<MonsterInstance> candidates = session.Monsters.Where(m => m.IsAlive);

        var targetGroup = action.Target?.TargetGroupId;
        if (!string.IsNullOrWhiteSpace(targetGroup) && !string.Equals(targetGroup, "default", StringComparison.OrdinalIgnoreCase))
            candidates = candidates.Where(m => string.Equals(m.GroupId, targetGroup, StringComparison.OrdinalIgnoreCase));

        return candidates.FirstOrDefault(IsEvilClericMonster);
    }

    private static bool IsEvilClericMonster(MonsterInstance monster)
    {
        if (monster == null || !monster.IsAlive)
            return false;

        if (!IsMonsterLikelyCleric(monster))
            return false;

        return IsEvilAlignmentText(monster.Template.Alignment);
    }

    private static bool IsMonsterLikelyCleric(MonsterInstance monster)
    {
        var name = monster.Template.Name ?? string.Empty;
        if (name.Contains("Cleric", StringComparison.OrdinalIgnoreCase))
            return true;

        return monster.Template.SpecialAbilities.Any(a =>
            (a.Name ?? string.Empty).Contains("Cleric", StringComparison.OrdinalIgnoreCase)
            || (a.Description ?? string.Empty).Contains("Cleric", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGoodAlignment(Alignment alignment)
        => alignment is Alignment.LawfulGood or Alignment.NeutralGood or Alignment.ChaoticGood;

    private static bool IsNeutralAlignment(Alignment alignment)
        => alignment is Alignment.LawfulNeutral or Alignment.TrueNeutral or Alignment.ChaoticNeutral;

    private static bool IsEvilAlignment(Alignment alignment)
        => alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil;

    private static bool IsEvilAlignmentText(string? alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
            return false;

        var text = alignment.Trim();
        return text.Equals("Lawful Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Neutral Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Chaotic Evil", StringComparison.OrdinalIgnoreCase)
               || text.Equals("LE", StringComparison.OrdinalIgnoreCase)
               || text.Equals("NE", StringComparison.OrdinalIgnoreCase)
               || text.Equals("CE", StringComparison.OrdinalIgnoreCase);
    }

    private int RollNd4(int count)
    {
        var total = 0;
        for (var i = 0; i < Math.Max(0, count); i++)
            total += _dice.Roll(4);

        return total;
    }

    private static void ApplyDirectDamageToCharacter(Character target, int damage, List<CombatEvent> events, string messageTemplate)
    {
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - Math.Max(0, damage));
        events.Add(new CombatEvent(string.Format(messageTemplate, Math.Max(0, damage), before, target.CurrentHitPoints)));

        if (target.CurrentHitPoints <= 0)
        {
            target.AddStatus(CharacterStatus.Dead);
            events.Add(new CombatEvent($"{target.Name} is slain by the talisman's backlash."));
        }
    }

    private static int GetOrAssignTalismanOfPureGoodCharges(Item talisman)
    {
        talisman.SpecialAbilities ??= new List<string>();

        var existing = talisman.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("TalismanPureGoodCharges:", StringComparison.OrdinalIgnoreCase));

        if (existing != null
            && int.TryParse(existing.Split(':', 2)[1], out var parsed))
        {
            return Math.Max(0, parsed);
        }

        talisman.SpecialAbilities.Add("TalismanPureGoodCharges:7");
        return 7;
    }

    private static void SetTalismanOfPureGoodCharges(Item talisman, int charges)
    {
        talisman.SpecialAbilities ??= new List<string>();
        talisman.SpecialAbilities.RemoveAll(a => a.StartsWith("TalismanPureGoodCharges:", StringComparison.OrdinalIgnoreCase));
        talisman.SpecialAbilities.Add($"TalismanPureGoodCharges:{Math.Max(0, charges)}");
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

    private static bool IsBroochOfShielding(Item? item)
    {
        if (item == null)
            return false;

        var name = item.Name?.Trim() ?? string.Empty;
        if (name.Contains("Brooch of Shielding", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.SpecialAbilities != null
               && item.SpecialAbilities.Any(a => !string.IsNullOrWhiteSpace(a)
                                                 && a.Contains("Brooch of Shielding", StringComparison.OrdinalIgnoreCase));
    }

    private static Item? FindBroochOfShielding(Character character)
    {
        if (character == null)
            return null;

        if (character.Equipment.TryGetValue(EquipmentSlot.Neck, out var neck)
            && IsBroochOfShielding(neck))
            return neck;

        return character.Inventory.FirstOrDefault(IsBroochOfShielding);
    }

    private static int ApplyBroochOfShieldingAgainstMagicMissile(Character target, int incomingDamage, List<CombatEvent> events)
    {
        if (target == null || incomingDamage <= 0)
            return incomingDamage;

        var brooch = FindBroochOfShielding(target);
        if (brooch == null)
            return incomingDamage;

        if (target.BroochOfShieldingRemainingHitPoints <= 0)
            target.BroochOfShieldingRemainingHitPoints = 101;

        var beforeCapacity = target.BroochOfShieldingRemainingHitPoints;
        var absorbed = Math.Min(beforeCapacity, incomingDamage);
        target.BroochOfShieldingRemainingHitPoints = Math.Max(0, beforeCapacity - absorbed);

        if (absorbed > 0)
        {
            events.Add(new CombatEvent(
                $"{target.Name}'s Brooch of Shielding absorbs {absorbed} magic missile damage ({beforeCapacity}->{target.BroochOfShieldingRemainingHitPoints} capacity)."));
        }

        if (target.BroochOfShieldingRemainingHitPoints <= 0)
        {
            if (target.Equipment.TryGetValue(EquipmentSlot.Neck, out var equippedNeck)
                && ReferenceEquals(equippedNeck, brooch))
            {
                target.Equipment[EquipmentSlot.Neck] = null;
            }

            target.Inventory.RemoveAll(i => ReferenceEquals(i, brooch));
            events.Add(new CombatEvent($"{target.Name}'s Brooch of Shielding melts and becomes useless."));
        }

        return Math.Max(0, incomingDamage - absorbed);
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

    private static int GetHumanControlHitDiceCost(MonsterInstance target)
    {
        return Math.Max(1, target.Template.HitDice);
    }

    private static bool IsHumanControlCategoryMatch(MonsterInstance target, int d20)
    {
        var category = PotionOfHumanControl.GetControlCategory(d20);
        var name = target.Template.Name?.Trim() ?? string.Empty;
        var lowered = name.ToLowerInvariant();
        var isDwarf = lowered.Contains("dwarf");
        var isElf = lowered.Contains("elf");
        var isHalfElf = lowered.Contains("half-elf") || lowered.Contains("half elf");
        var isGnome = lowered.Contains("gnome");
        var isHalfling = lowered.Contains("halfling");
        var isHalfOrc = lowered.Contains("half-orc") || lowered.Contains("half orc");
        var isHuman = IsHumanControlHumanType(lowered);

        return category switch
        {
            "Dwarves" => isDwarf,
            "Elves/Half-Elves" => isElf || isHalfElf,
            "Gnomes" => isGnome,
            "Halflings" => isHalfling,
            "Half-Orcs" => isHalfOrc,
            "Humans" => isHuman,
            "Humanoids" => target.InstanceMonsterType == MonsterType.Humanoid && !isHuman && !isDwarf && !isElf && !isHalfElf && !isGnome && !isHalfling && !isHalfOrc,
            "Elves, Half-Elves, and Humans" => isElf || isHalfElf || isHuman,
            _ => false
        };
    }

    private static bool IsHumanControlHumanType(string loweredName)
    {
        if (string.IsNullOrWhiteSpace(loweredName))
            return false;

        if (loweredName.Contains("human")
            || loweredName.Contains("bandit")
            || loweredName.Contains("berserker")
            || loweredName.Contains("brigand")
            || loweredName.Contains("brigadd"))
        {
            return true;
        }

        if (loweredName.StartsWith("lvl ", StringComparison.OrdinalIgnoreCase))
        {
            return loweredName.Contains("fighter")
                   || loweredName.Contains("magic-user")
                   || loweredName.Contains("cleric")
                   || loweredName.Contains("illusionist")
                   || loweredName.Contains("thief")
                   || loweredName.Contains("assassin")
                   || loweredName.Contains("monk")
                   || loweredName.Contains("ranger")
                   || loweredName.Contains("paladin");
        }

        if (loweredName.Contains("master thief")
            || loweredName.Equals("thief", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("fighter", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("magic-user", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("cleric", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("illusionist", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("assassin", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("monk", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("ranger", StringComparison.OrdinalIgnoreCase)
            || loweredName.Equals("paladin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void ResolveLayOnHands(CombatSession session, Character paladin, CombatAction action, List<CombatEvent> events)
    {
        if (paladin.HasStatus(CharacterStatus.LycanthropeTransformed))
        {
            events.Add(new CombatEvent($"{paladin.Name} is transformed by lycanthropy and cannot use Lay on Hands."));
            return;
        }

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
        if (_dualClassService.IsTurnUndeadFromOriginalClass(actor))
            _dualClassService.MarkOriginalClassFunctionUsed(actor);

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
}
