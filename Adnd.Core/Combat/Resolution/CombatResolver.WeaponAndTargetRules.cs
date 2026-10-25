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
    private static bool IsSilverWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        return text.Contains("silver", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIronWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        if (text.Contains("iron", StringComparison.OrdinalIgnoreCase) || text.Contains("steel", StringComparison.OrdinalIgnoreCase))
            return true;

        // Ordinary weapons are iron/steel unless made of wood or only a blunt improvised item.
        return !IsWoodenItem(weapon) && !IsBluntWeapon(weapon);
    }

    private static bool IsColdIronWeapon(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var text = $"{weapon.Name} {weapon.Description}";
        return text.Contains("cold iron", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetColdIronVulnerabilityDamageMultiplier(Item? weapon, MonsterInstance target)
    {
        if (target?.Template?.SpecialDefenses == null)
            return 1;

        var hasVulnerability = target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "Cold Iron Vulnerability", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Description?.Trim(), "Cold Iron Vulnerability", StringComparison.OrdinalIgnoreCase)
            || ((d.Name ?? string.Empty) + " " + (d.Description ?? string.Empty))
                .Contains("cold iron", StringComparison.OrdinalIgnoreCase));

        if (!hasVulnerability)
            return 1;

        return IsColdIronWeapon(weapon) ? 2 : 1;
    }

    private static int GetWeaponVsArmorClassAdjustment(Item? weapon, int targetArmorClass, bool isRanged)
    {
        if (targetArmorClass < 2 || targetArmorClass > 10)
            return 0;

        var key = ResolveWeaponVsArmorTableKey(weapon, isRanged);
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        var table = isRanged ? RangedWeaponVsArmorAdjustments : MeleeWeaponVsArmorAdjustments;
        if (!table.TryGetValue(key, out var row) || row.Length < 9)
            return 0;

        return row[targetArmorClass - 2];
    }

    private static string? GetIncapacitatedTargetReason(MonsterInstance target)
    {
        if (target.HasStatus(MonsterStatus.Paralyzed))
            return "paralyzed";
        if (target.HasStatus(MonsterStatus.Stunned))
            return "stunned";
        if (target.HasStatus(MonsterStatus.Asleep))
            return "asleep";
        if (target.HasStatus(MonsterStatus.Unconscious))
            return "unable to move (unconscious)";
        if (target.HasStatus(MonsterStatus.Petrified))
            return "unable to move (petrified)";

        return null;
    }

    private static string? GetIncapacitatedTargetReason(Character target, CombatSession session)
    {
        if (target.HasStatus(CharacterStatus.Paralyzed))
            return "paralyzed";

        if (target.HasStatus(CharacterStatus.Asleep) || session.GetPartyAsleepRounds(target.Name) > 0)
            return "asleep";

        if (target.HasStatus(CharacterStatus.Petrified))
            return "unable to move (petrified)";

        return null;
    }

    private static string? ResolveWeaponVsArmorTableKey(Item? weapon, bool isRanged)
    {
        if (weapon == null)
            return isRanged ? null : "fist or open hand";

        var exactTable = isRanged ? ExactRangedWeaponNameToTableKey : ExactMeleeWeaponNameToTableKey;
        if (exactTable.TryGetValue(weapon.Name.Trim(), out var exactKey))
            return exactKey;

        var normalized = NormalizeWeaponName(weapon.Name);
        if (string.IsNullOrWhiteSpace(normalized))
            return isRanged ? null : "fist or open hand";

        if (isRanged)
        {
            if (normalized.Contains("composite") && normalized.Contains("long") && normalized.Contains("bow")) return "bow composite long";
            if (normalized.Contains("composite") && normalized.Contains("short") && normalized.Contains("bow")) return "bow composite short";
            if (normalized.Contains("long") && normalized.Contains("bow")) return "bow long";
            if (normalized.Contains("short") && normalized.Contains("bow")) return "bow short";
            if (normalized.Contains("crossbow") && normalized.Contains("heavy")) return "crossbow heavy";
            if (normalized.Contains("crossbow") && normalized.Contains("light")) return "crossbow light";
            if (normalized.Contains("sling") && normalized.Contains("bullet")) return "sling bullet";
            if (normalized.Contains("sling") && normalized.Contains("stone")) return "sling stone";
            if (normalized.Contains("sling")) return "sling stone";
            if (normalized.Contains("javelin")) return "javelin";
            if (normalized.Contains("dart")) return "dart";
            if (normalized.Contains("hand") && normalized.Contains("axe")) return "axe hand";
            if (normalized.Contains("dagger")) return "dagger";
            if (normalized.Contains("club")) return "club";
            if (normalized.Contains("hammer")) return "hammer";
            if (normalized.Contains("spear")) return "spear";
            return null;
        }

        if (normalized.Contains("battle") && normalized.Contains("axe")) return "axe battle";
        if (normalized.Contains("hand") && normalized.Contains("axe")) return "axe hand";
        if (normalized.Contains("bardiche")) return "bardiche";
        if (normalized.Contains("boc") && normalized.Contains("corbin")) return "boc de corbin";
        if (normalized.Contains("bill") && normalized.Contains("guisarme")) return "bill guisarme";
        if (normalized.Contains("bo stick")) return "bo stick";
        if (normalized.Contains("club")) return "club";
        if (normalized.Contains("dagger")) return "dagger";
        if (normalized.Contains("fauchard") && normalized.Contains("fork")) return "fauchard fork";
        if (normalized.Contains("fauchard")) return "fauchard";
        if (normalized.Contains("flail") && normalized.Contains("footman")) return "flail footman";
        if (normalized.Contains("flail") && normalized.Contains("horseman")) return "flail horseman";
        if (normalized.Contains("flail") && normalized.Contains("military")) return "flail military";
        if (normalized.Contains("glaive") && normalized.Contains("guisarme")) return "glaive guisarme";
        if (normalized.Equals("glaive")) return "glaive";
        if (normalized.Contains("guisarme") && normalized.Contains("voulge")) return "guisarme voulge";
        if (normalized.Equals("guisarme")) return "guisarme";
        if (normalized.Contains("halberd")) return "halberd";
        if (normalized.Contains("lucern") && normalized.Contains("hammer")) return "hammer lucern";
        if (normalized.Equals("hammer") || normalized.Contains("war hammer")) return "hammer";
        if (normalized.Contains("jo stick")) return "jo stick";
        if (normalized.Contains("lance") && normalized.Contains("heavy")) return "lance heavy horse";
        if (normalized.Contains("lance") && normalized.Contains("light")) return "lance light horse";
        if (normalized.Contains("lance") && normalized.Contains("medium")) return "lance medium horse";
        if (normalized.Contains("mace") && normalized.Contains("footman")) return "mace footman";
        if (normalized.Contains("mace") && normalized.Contains("horseman")) return "mace horseman";
        if (normalized.Contains("morning") && normalized.Contains("star")) return "morning star";
        if (normalized.Contains("partisan")) return "partisan";
        if (normalized.Contains("pick") && normalized.Contains("military") && normalized.Contains("footman")) return "pick military footman";
        if (normalized.Contains("pick") && normalized.Contains("military") && normalized.Contains("horseman")) return "pick military horseman";
        if (normalized.Contains("pike") && normalized.Contains("awl")) return "pike awl";
        if (normalized.Contains("ranseur")) return "ranseur";
        if (normalized.Contains("scimitar")) return "scimitar";
        if (normalized.Equals("spear")) return "spear";
        if (normalized.Contains("spetum")) return "spetum";
        if (normalized.Contains("quarter") && normalized.Contains("staff")) return "staff quarter";
        if (normalized.Contains("bastard") && normalized.Contains("sword")) return "sword bastard";
        if (normalized.Contains("broad") && normalized.Contains("sword")) return "sword broad";
        if (normalized.Contains("long") && normalized.Contains("sword")) return "sword long";
        if (normalized.Contains("short") && normalized.Contains("sword")) return "sword short";
        if (normalized.Contains("two") && normalized.Contains("handed") && normalized.Contains("sword")) return "sword two handed";
        if (normalized.Contains("trident")) return "trident";
        if (normalized.Contains("voulge")) return "voulge";

        return null;
    }

    private static string NormalizeWeaponName(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return string.Empty;

        var text = weaponName.Trim().ToLowerInvariant();
        text = Regex.Replace(text, @"\+\d+", string.Empty);
        text = text.Replace("(", " ").Replace(")", " ").Replace(",", " ").Replace("-", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static int GetEffectiveWeaponBonusForHitRequirement(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return 0;

        var baseBonus = Math.Max(0, Math.Max(weapon.MagicBonus, weapon.ToHitBonus));
        if (IsSwordOfSharpness(weapon))
            return Math.Max(baseBonus, 3);
        if (IsSwordVorpalWeapon(weapon))
            return Math.Max(baseBonus, 3);

        return baseBonus;
    }

    private static int GetRequiredWeaponBonusToHit(MonsterInstance target)
    {
        if (target?.Template == null)
            return 0;

        var required = 0;
        foreach (var defense in target.Template.SpecialDefenses)
        {
            var text = defense?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var match = Regex.Match(text, @"\+(\d+)\s*or\s*better\s*weapons?\s*to\s*hit", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                required = Math.Max(required, Math.Max(0, parsed));
        }

        return required;
    }

    private void TryResolveSwordOfLifeStealingEffect(Character wielder, MonsterInstance target, List<CombatEvent> events)
    {
        if (wielder == null || target?.Template == null || !target.IsAlive)
            return;

        var hitDieType = Math.Max(2, target.Template.HitDiceType <= 0 ? 8 : target.Template.HitDiceType);
        var drained = _dice.Roll(hitDieType);

        var beforeTargetHp = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - drained);
        var actualDrained = beforeTargetHp - target.CurrentHitPoints;
        if (actualDrained <= 0)
            return;

        // Approximation of one level/hit-die drain: reduce monster offense slightly.
        target.AdjustThac0(1);

        var beforeWielderHp = wielder.CurrentHitPoints;
        var missing = Math.Max(0, wielder.MaxHitPoints - wielder.CurrentHitPoints);
        var gained = Math.Min(missing, actualDrained);
        if (gained > 0)
            wielder.CurrentHitPoints = Math.Min(wielder.MaxHitPoints, wielder.CurrentHitPoints + gained);

        events.Add(new CombatEvent(
            gained > 0
                ? $"Sword of Life Stealing drains {actualDrained} life from {target.DisplayName} (natural 20). {wielder.Name} gains {gained} HP. HP {beforeWielderHp}->{wielder.CurrentHitPoints}."
                : $"Sword of Life Stealing drains {actualDrained} life from {target.DisplayName} (natural 20), but {wielder.Name} is already at full HP."));
    }

    private static bool IsSwordPlusThreeFrostBrand(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("Frost Brand", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Frost Brand", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryResolveNineLivesStealerEffect(Item? weapon, MonsterInstance target, List<CombatEvent> events)
    {
        if (weapon == null || target?.Template == null)
            return false;

        var charges = GetOrAssignNineLivesStealerCharges(weapon);
        if (charges <= 0)
            return false;

        var saveTarget = target.Template.SavingThrows?.Spell ?? 20;
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent($"{target.DisplayName} resists Nine Lives Stealer (save vs spell {saveRoll} vs {saveTarget})."));
            return false;
        }

        SetNineLivesStealerCharges(weapon, charges - 1);
        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent($"Nine Lives Stealer draws the life force from {target.DisplayName}! Charges remaining: {Math.Max(0, charges - 1)}."));
        return true;
    }

    private static int GetOrAssignNineLivesStealerCharges(Item weapon)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var existing = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("NineLivesCharges:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing)
            && int.TryParse(existing[(existing.IndexOf(':') + 1)..].Trim(), out var parsed))
        {
            return Math.Max(0, parsed);
        }

        weapon.SpecialAbilities.Add("NineLivesCharges:9");
        return 9;
    }

    private static void SetNineLivesStealerCharges(Item weapon, int charges)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var idx = weapon.SpecialAbilities.FindIndex(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.StartsWith("NineLivesCharges:", StringComparison.OrdinalIgnoreCase));

        var value = $"NineLivesCharges:{Math.Max(0, charges)}";
        if (idx >= 0)
            weapon.SpecialAbilities[idx] = value;
        else
            weapon.SpecialAbilities.Add(value);
    }

    private void TryResolveDaggerOfVenomEffect(Item? weapon, MonsterInstance target, List<CombatEvent> events)
    {
        if (weapon == null || target?.Template == null || !target.IsAlive)
            return;

        if (SpellDamageSaveHelper.HasPoisonOrParalysisImmunity(target))
        {
            events.Add(new CombatEvent($"{target.DisplayName} is immune to poison from {weapon.Name}."));
            return;
        }

        var doses = GetOrAssignDaggerOfVenomDoses(weapon);
        if (doses <= 0)
        {
            events.Add(new CombatEvent($"{weapon.Name} has no venom dose remaining."));
            return;
        }

        var remaining = Math.Max(0, doses - 1);
        SetDaggerOfVenomDoses(weapon, remaining);

        var saveTarget = SpellDamageSaveHelper.GetMonsterPoisonSaveTarget(target, fallbackTarget: 20);
        var saveRoll = _dice.Roll(20);
        if (saveRoll >= saveTarget)
        {
            events.Add(new CombatEvent(
                $"{target.DisplayName} resists {weapon.Name} poison (save {saveRoll} vs {saveTarget}). Doses remaining: {remaining}."));
            return;
        }

        target.CurrentHitPoints = 0;
        events.Add(new CombatEvent(
            $"{weapon.Name} injects fatal poison into {target.DisplayName} on a natural 20! (save {saveRoll} vs {saveTarget}, doses remaining: {remaining})."));
    }

    private static bool IsDaggerOfVenom(Item? weapon)
    {
        if (weapon == null || weapon.Type != ItemType.Weapon)
            return false;

        var name = weapon.Name?.Trim() ?? string.Empty;
        if (name.Contains("Dagger of Venom", StringComparison.OrdinalIgnoreCase))
            return true;

        return weapon.SpecialAbilities != null
               && weapon.SpecialAbilities.Any(a =>
                   !string.IsNullOrWhiteSpace(a)
                   && a.Contains("Dagger of Venom", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetOrAssignDaggerOfVenomDoses(Item weapon)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var existing = weapon.SpecialAbilities
            .FirstOrDefault(a => a.StartsWith("VenomDoses:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing)
            && int.TryParse(existing[(existing.IndexOf(':') + 1)..].Trim(), out var parsed))
        {
            return Math.Max(0, parsed);
        }

        weapon.SpecialAbilities.Add("VenomDoses:6");
        return 6;
    }

    private static void SetDaggerOfVenomDoses(Item weapon, int doses)
    {
        weapon.SpecialAbilities ??= new List<string>();

        var idx = weapon.SpecialAbilities.FindIndex(a =>
            !string.IsNullOrWhiteSpace(a)
            && a.StartsWith("VenomDoses:", StringComparison.OrdinalIgnoreCase));

        var value = $"VenomDoses:{Math.Max(0, doses)}";
        if (idx >= 0)
            weapon.SpecialAbilities[idx] = value;
        else
            weapon.SpecialAbilities.Add(value);
    }

    private static int GetSituationalSwordBonusAgainstTarget(Item? weapon, MonsterInstance target)
    {
        if (IsSwordPlusOneFlameTongue(weapon))
        {
            // Weapon is +1 normally, but:
            // +2 vs regenerating, +3 vs cold-using/inflammable/avian, +4 vs undead.
            if (target?.InstanceMonsterType == MonsterType.Undead)
                return 3;

            if (IsTargetColdUsingCreature(target)
                || IsTargetInflammableCreature(target)
                || IsTargetAvianCreature(target))
            {
                return 2;
            }

            if (IsTargetRegeneratingCreature(target))
                return 1;

            return 0;
        }

        if (IsSwordPlusTwoGiantSlayer(weapon)
            && IsTargetGiantKinForGiantSlayer(target))
        {
            // Weapon is +2 normally, but +3 vs giant-kind targets.
            return 1;
        }

        if (IsSwordPlusTwoDragonSlayer(weapon)
            && IsTargetEligibleForDragonSlayerBonus(target))
        {
            // Weapon is +2 normally, but +4 vs true dragons.
            return 2;
        }

        if (IsSwordPlusThreeFrostBrand(weapon)
            && IsTargetFireUsingOrDwellingCreature(target))
        {
            // Weapon is +3 normally, but +6 vs fire-using/dwelling creatures.
            return 3;
        }

        if (IsSwordPlusOnePlusTwoVsMagicUsingAndEnchantedCreatures(weapon)
            && IsTargetEligibleForSwordPlusTwoBonus(target))
        {
            // Weapon is +1 normally, but +2 vs magic-using/enchanted/conjured-created-gated-summoned creatures.
            return 1;
        }

        if (IsSwordPlusOnePlusThreeVsLycanthropesAndShapeChangers(weapon)
            && IsTargetLycanthropeOrShapeChanger(target))
        {
            // Weapon is +1 normally, but +3 vs lycanthropes and shape changers.
            return 2;
        }

        if (IsSwordPlusOnePlusThreeVsRegeneratingCreatures(weapon)
            && IsTargetRegeneratingCreature(target))
        {
            // Weapon is +1 normally, but +3 vs regenerating creatures.
            return 2;
        }

        if (IsSwordPlusOnePlusFourVsReptiles(weapon)
            && IsTargetReptile(target))
        {
            // Weapon is +1 normally, but +4 vs reptiles.
            return 3;
        }

        return 0;
    }

    private static bool IsTargetEligibleForSwordPlusTwoBonus(MonsterInstance target)
    {
        return IsMagicUsingMonster(target)
               || IsConjuredCreatedGatedOrSummoned(target)
               || IsEnchantedCreature(target);
    }

    private static bool IsMagicUsingMonster(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var lowerName = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (lowerName.Contains("magic-user")
            || lowerName.Contains("mage")
            || lowerName.Contains("wizard")
            || lowerName.Contains("illusionist")
            || lowerName.Contains("sorcerer")
            || lowerName.Contains("warlock"))
        {
            return true;
        }

        foreach (var ability in GetMonsterAbilityNames(target))
        {
            var lower = ability.ToLowerInvariant();
            if (lower.Contains("casts ")
                || lower.Contains("spellcasting")
                || lower.Contains("spell casting")
                || lower.Contains("spell use")
                || lower.Contains("magic use")
                || Regex.IsMatch(lower, @"\b(mage|magic-user|illusionist|cleric|priest|druid)\b.*\bspells?\b"))
            {
                return true;
            }
        }

        return target.Template.Attacks.Any(attack =>
            string.Equals(attack.Name?.Trim(), "Spell", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConjuredCreatedGatedOrSummoned(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("conjur")
               || composite.Contains("summon")
               || composite.Contains("gated")
               || composite.Contains("gate")
               || composite.Contains("created");
    }

    private static bool IsEnchantedCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("enchant");
    }

    private static bool IsTargetLycanthropeOrShapeChanger(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var lowerName = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (lowerName.Contains("lycanthrope")
            || lowerName.Contains("were")
            || lowerName.Contains("doppelganger")
            || lowerName.Contains("wolfwer")
            || lowerName.Contains("shape changer")
            || lowerName.Contains("shapechanger"))
        {
            return true;
        }

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("lycanthrope")
               || composite.Contains("shape changer")
               || composite.Contains("shapechanger")
               || composite.Contains("shapechange")
               || composite.Contains("shape change")
               || composite.Contains("shape-shift")
               || composite.Contains("shapeshift")
               || composite.Contains("polymorph");
    }

    private static bool IsTargetRegeneratingCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (HasMonsterRegeneration(target))
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("regenerat");
    }

    private static bool IsTargetReptile(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Dragon)
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return Regex.IsMatch(composite,
            @"\b(reptil\w*|lizard\w*|snake\w*|serpent\w*|dragons?|drakes?|wyverns?|crocodil\w*|alligators?|turtles?|tortoises?|dinosaurs?|basilisks?|hydras?|tuataras?|cobras?|vipers?|pythons?)\b");
    }

    private static bool IsTargetGiantKinForGiantSlayer(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Giant)
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("ettin")
               || composite.Contains("ogre mage")
               || composite.Contains("ogre-mage")
               || composite.Contains("titan");
    }

    private static bool IsTargetEligibleForDragonSlayerBonus(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType != MonsterType.Dragon)
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();
        if (name.Contains("bahamut") || name.Contains("tiamat"))
            return false;

        return name.Contains("dragon");
    }

    private static bool IsTargetFireUsingOrDwellingCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("fire")
               || composite.Contains("flame")
               || composite.Contains("burn")
               || composite.Contains("heat")
               || composite.Contains("lava")
               || composite.Contains("magma")
               || composite.Contains("pyro");
    }

    private static void ApplySwordOfWoundingHealingRestriction(CombatSession session, MonsterInstance target)
    {
        if (session == null || target == null)
            return;

        var blockedHealing = session.GetMonsterWoundingUnhealableDamage(target);
        if (blockedHealing <= 0)
            return;

        var cappedHp = Math.Max(0, target.MaxHitPoints - blockedHealing);
        if (target.CurrentHitPoints > cappedHp)
            target.CurrentHitPoints = cappedHp;
    }

    private static bool IsTargetTrueGiantForGiantSlayer(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var name = (target.Template.Name ?? string.Empty).ToLowerInvariant();

        return name.Contains("hill giant")
               || name.Contains("stone giant")
               || name.Contains("frost giant")
               || name.Contains("fire giant")
               || name.Contains("cloud giant")
               || name.Contains("storm giant");
    }

    private static bool IsTargetColdUsingCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("cold")
               || composite.Contains("frost")
               || composite.Contains("ice")
               || composite.Contains("winter");
    }

    private static bool IsTargetInflammableCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        if (target.InstanceMonsterType == MonsterType.Plant)
            return true;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("inflammable")
               || composite.Contains("flammable")
               || composite.Contains("combustible")
               || composite.Contains("burns easily");
    }

    private static bool IsTargetAvianCreature(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var composite = string.Join(" ",
            new[] { target.Template.Name, target.Template.TypeName }
                .Concat(GetMonsterAbilityNames(target)))
            .ToLowerInvariant();

        return composite.Contains("avian")
               || composite.Contains("bird")
               || composite.Contains("eagle")
               || composite.Contains("hawk")
               || composite.Contains("owl")
               || composite.Contains("vulture")
               || composite.Contains("raven")
               || composite.Contains("falcon")
               || composite.Contains("roc");
    }

    private static bool RequiresPlusOneWeaponToHit(MonsterInstance target)
    {
        return target.Template.SpecialDefenses.Any(d =>
            string.Equals(d.Name?.Trim(), "+1 or better weapons to hit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWillOWisp(MonsterInstance target)
    {
        if (target?.Template == null)
            return false;

        var normalized = (target.Template.Name ?? string.Empty)
            .ToLowerInvariant()
            .Replace("'", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        return normalized.Contains("willowisp", StringComparison.Ordinal);
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
