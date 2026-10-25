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
    private void ResolveFigurineAnimalAllies(CombatSession session, List<CombatEvent> events)
    {
        foreach (var summoner in session.Party)
        {
            if (!IsAlive(summoner))
                continue;

            var activeFigurines = summoner.Inventory
                .Where(FigurineOfWondrousPower.IsActiveInAnimalForm)
                .ToList();

            foreach (var figurine in activeFigurines)
            {
                var kind = FigurineOfWondrousPower.GetKind(figurine);
                switch (kind)
                {
                    case FigurineKind.GoldenLions:
                        ResolveGoldenLionsAttacks(session, summoner, figurine, events);
                        break;
                    case FigurineKind.SerpentineOwl:
                        ResolveSerpentineOwlAttack(session, summoner, figurine, events);
                        break;
                    case FigurineKind.EbonyFly:
                        events.Add(new CombatEvent($"{summoner.Name}'s Ebony Fly circles overhead and can assist with travel, but makes no direct attacks."));
                        break;
                }
            }
        }
    }

    private void ResolveGoldenLionsAttacks(CombatSession session, Character summoner, Item figurine, List<CombatEvent> events)
    {
        for (int lion = 1; lion <= 2; lion++)
        {
            for (int attackIndex = 0; attackIndex < 3; attackIndex++)
            {
                var target = SelectFigurineTargetMonster(session);
                if (target == null)
                    return;

                var roll = _dice.Roll(20);
                const int needed = 11;
                if (roll < needed)
                {
                    events.Add(new CombatEvent($"{summoner.Name}'s Golden Lion #{lion} misses {target.DisplayName} (rolled {roll} vs {needed})."));
                    continue;
                }

                var damage = attackIndex == 2 ? RollDamage("1d8") : RollDamage("1d4");
                var before = target.CurrentHitPoints;
                target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
                var actual = before - target.CurrentHitPoints;
                events.Add(new CombatEvent($"{summoner.Name}'s Golden Lion #{lion} hits {target.DisplayName} for {actual} damage."));

                if (target.CurrentHitPoints <= 0)
                {
                    target.CurrentHitPoints = 0;
                    events.Add(new CombatEvent($"{target.DisplayName} is slain by {summoner.Name}'s Golden Lion #{lion}."));
                }
            }
        }
    }

    private void ResolveSerpentineOwlAttack(CombatSession session, Character summoner, Item figurine, List<CombatEvent> events)
    {
        var isGiant = FigurineOfWondrousPower.IsSerpentineOwlGiantForm(figurine);
        var target = SelectFigurineTargetMonster(session);
        if (target == null)
            return;

        var attacks = isGiant ? 2 : 2;
        var damageExpr = isGiant ? "1d6" : "1d2";
        var needed = isGiant ? 11 : 14;

        for (int i = 0; i < attacks; i++)
        {
            var roll = _dice.Roll(20);
            if (roll < needed)
            {
                events.Add(new CombatEvent($"{summoner.Name}'s {(isGiant ? "Giant " : string.Empty)}Serpentine Owl misses {target.DisplayName} (rolled {roll} vs {needed})."));
                continue;
            }

            var damage = RollDamage(damageExpr);
            var before = target.CurrentHitPoints;
            target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
            var actual = before - target.CurrentHitPoints;
            events.Add(new CombatEvent($"{summoner.Name}'s {(isGiant ? "Giant " : string.Empty)}Serpentine Owl hits {target.DisplayName} for {actual} damage."));

            if (target.CurrentHitPoints <= 0)
            {
                target.CurrentHitPoints = 0;
                events.Add(new CombatEvent($"{target.DisplayName} is slain by {summoner.Name}'s Serpentine Owl."));
                break;
            }
        }
    }

    private MonsterInstance? SelectFigurineTargetMonster(CombatSession session)
    {
        var aliveMonsters = session.AliveMonsters.ToList();
        if (aliveMonsters.Count == 0)
            return null;

        var index = _dice.Roll(aliveMonsters.Count) - 1;
        return aliveMonsters[index];
    }

    private void ResolveHornOfValhallaUse(CombatSession session, Character user, Item horn, List<CombatEvent> events)
    {
        var existingType = HornOfValhalla.GetType(horn);
        var typeRoll = _dice.Roll(20);
        var hornType = HornOfValhalla.GetOrAssignType(horn, typeRoll);
        var typeName = hornType.ToString();

        if (existingType == HornOfValhallaType.Unknown)
        {
            var aligned = _dice.Roll(100) <= 50;
            var assignedAlignment = aligned ? RollRandomAlignment() : (Alignment?)null;
            HornOfValhalla.EnsureAlignmentProfile(horn, aligned, assignedAlignment);
            if (aligned && assignedAlignment.HasValue)
                events.Add(new CombatEvent($"{horn.Name} reveals itself as a {typeName} horn aligned to {assignedAlignment.Value.ToDisplayString()}."));
            else
                events.Add(new CombatEvent($"{horn.Name} reveals itself as a {typeName} horn with no alignment binding."));
        }

        if (HornOfValhalla.IsOnCooldown(horn, session.DungeonDay))
        {
            var remaining = HornOfValhalla.GetCooldownDaysRemaining(horn, session.DungeonDay);
            events.Add(new CombatEvent($"{horn.Name} cannot be sounded yet. {remaining} day(s) remain before it can be used again."));
            return;
        }

        var fighterLevel = HornOfValhalla.GetFighterLevel(hornType);
        var berserkerCount = HornOfValhalla.RollBerserkerCount(hornType, _dice.Roll);
        var classAllowed = CanCharacterUseHornType(user, hornType);
        var hostileByClass = !classAllowed;
        var hostileByAlignment = false;
        if (HornOfValhalla.IsAligned(horn)
            && HornOfValhalla.TryGetAlignment(horn, out var hornAlignment)
            && IsRadicallyDifferentAlignment(user.Alignment, hornAlignment))
            hostileByAlignment = true;

        var hostile = hostileByClass || hostileByAlignment;

        HornOfValhalla.MarkUsed(horn, session.DungeonDay);

        var levelOrdinal = fighterLevel switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{fighterLevel}th"
        };

        RuleApplicationInfo.PublishLinked(
            "DMG",
            "Other items/DMG147HordOfValhalla",
            $"{user.Name} blows {horn.Name}: type roll {typeRoll} -> {typeName}, summons {berserkerCount} berserkers ({levelOrdinal} level).");

        if (hostile)
        {
            var hostileGroupId = SpawnHostileHornBerserkers(session, user, hornType, fighterLevel, berserkerCount);
            var reasons = new List<string>();
            if (hostileByClass)
                reasons.Add("blower class cannot use this horn type");
            if (hostileByAlignment)
                reasons.Add("radical alignment difference with horn alignment");

            events.Add(new CombatEvent(
                $"{user.Name} sounds {horn.Name}: {berserkerCount} {levelOrdinal}-level berserker(s) answer from Valhalla and turn hostile ({string.Join("; ", reasons)})."));
            events.Add(new CombatEvent(
                $"Hostile berserkers appear as a separate enemy group ({hostileGroupId}) and can be attacked by the party."));
            return;
        }

        session.ActiveHornOfValhallaSummons.Add(
            new HornOfValhallaSummon(user.Name, typeName, hostileToParty: false, fighterLevel, berserkerCount, roundsRemaining: 60));

        events.Add(new CombatEvent(
            $"{user.Name} sounds {horn.Name}: {berserkerCount} {levelOrdinal}-level berserker(s) answer from Valhalla and await commands."));
    }

    private void ResolveHornOfValhallaSummons(CombatSession session, List<CombatEvent> events)
    {
        ResolveHostileHornOfValhallaGroupExpiry(session, events);

        if (session.ActiveHornOfValhallaSummons.Count == 0)
            return;

        for (var i = session.ActiveHornOfValhallaSummons.Count - 1; i >= 0; i--)
        {
            var summon = session.ActiveHornOfValhallaSummons[i];
            if (summon.RoundsRemaining <= 0 || summon.BerserkerCount <= 0)
            {
                session.ActiveHornOfValhallaSummons.RemoveAt(i);
                continue;
            }

            for (var attacker = 0; attacker < summon.BerserkerCount; attacker++)
            {
                var target = SelectFigurineTargetMonster(session);
                if (target == null)
                    break;

                ResolveHornAttackAgainstMonster(summon, target, events);
            }

            summon.RoundsRemaining -= 1;
            if (summon.RoundsRemaining <= 0)
            {
                events.Add(new CombatEvent($"The berserkers summoned by {summon.SummonerName}'s {summon.HornTypeName} horn fade back to Valhalla."));
                session.ActiveHornOfValhallaSummons.RemoveAt(i);
            }
        }
    }

    private void ResolveHornAttackAgainstMonster(HornOfValhallaSummon summon, MonsterInstance target, List<CombatEvent> events)
    {
        var thac0 = Thac0Calculator.GetThac0(CharacterClass.Fighter, summon.FighterLevel);
        var needed = Math.Clamp(thac0 - target.ArmorClass, 2, 20);
        var roll = _dice.Roll(20);

        if (roll != 20 && (roll == 1 || roll < needed))
        {
            events.Add(new CombatEvent($"Valhalla berserker misses {target.DisplayName} (rolled {roll} vs {needed})."));
            return;
        }

        var damage = RollHornBerserkerDamage();
        var before = target.CurrentHitPoints;
        target.CurrentHitPoints = Math.Max(0, target.CurrentHitPoints - damage);
        var actual = before - target.CurrentHitPoints;

        events.Add(new CombatEvent($"Valhalla berserker hits {target.DisplayName} for {actual} damage."));
        if (target.CurrentHitPoints <= 0)
            events.Add(new CombatEvent($"{target.DisplayName} is slain by a Valhalla berserker."));
    }

    private int RollHornBerserkerDamage()
    {
        var styleRoll = _dice.Roll(100);
        var usingSwordOrAxe = styleRoll <= 50;
        return usingSwordOrAxe ? RollDamage("1d8") : RollDamage("1d6");
    }

    private string SpawnHostileHornBerserkers(CombatSession session, Character blower, HornOfValhallaType hornType, int fighterLevel, int berserkerCount)
    {
        var groupId = $"horn-valhalla-{blower.Name}-{session.RoundNumber}-{_dice.Roll(10000)}"
            .Replace(" ", string.Empty);

        var template = BuildHostileHornBerserkerTemplate(hornType, fighterLevel);
        var instances = new List<MonsterInstance>(berserkerCount);
        for (var i = 1; i <= berserkerCount; i++)
            instances.Add(new MonsterInstance(template, i, groupId));

        session.Monsters.AddRange(instances);
        session.HostileHornOfValhallaGroups[groupId] = 60;
        session.NoXpMonsterGroupIds.Add(groupId);
        return groupId;
    }

    private static Monster BuildHostileHornBerserkerTemplate(HornOfValhallaType hornType, int fighterLevel)
    {
        var styleName = Random.Shared.Next(1, 101) <= 50 ? "Sword+Spear" : "BattleAxe+Spear";
        var damage = styleName.StartsWith("BattleAxe", StringComparison.OrdinalIgnoreCase) ? "1d8" : "1d8";

        return new Monster
        {
            Name = $"Valhalla Berserker ({hornType})",
            Type = MonsterType.Humanoid,
            TypeName = "Humanoid",
            Alignment = "Any",
            NumberOfAppearancesMin = 1,
            NumberOfAppearancesMax = 1,
            ArmorClass = 4,
            HitDice = Math.Max(1, fighterLevel),
            HitDiceType = 1,
            ExtraHitPoints = Math.Max(0, fighterLevel * 5),
            THAC0 = Thac0Calculator.GetThac0(CharacterClass.Fighter, fighterLevel),
            NumberOfAttacks = 1,
            MovementRate = "12",
            Movement = new MonsterMovement { Walk = 120 },
            Morale = new MonsterMorale { Value = 12 },
            SavingThrows = new MonsterSavingThrows(),
            Attacks = new List<MonsterAttack>
            {
                new()
                {
                    Name = styleName,
                    NumberOfAttacks = 1,
                    Damage = damage
                }
            },
            BaseXPValue = 0,
            XPValuePerHitPoint = 0
        };
    }

    private void ResolveHostileHornOfValhallaGroupExpiry(CombatSession session, List<CombatEvent> events)
    {
        if (session.HostileHornOfValhallaGroups.Count == 0)
            return;

        foreach (var groupId in session.HostileHornOfValhallaGroups.Keys.ToList())
        {
            var aliveInGroup = session.GetAliveMonstersByGroup(groupId).ToList();
            if (aliveInGroup.Count == 0)
            {
                session.HostileHornOfValhallaGroups.Remove(groupId);
                continue;
            }

            var roundsRemaining = session.HostileHornOfValhallaGroups[groupId] - 1;
            if (roundsRemaining > 0)
            {
                session.HostileHornOfValhallaGroups[groupId] = roundsRemaining;
                continue;
            }

            foreach (var spirit in aliveInGroup)
                spirit.CurrentHitPoints = 0;

            events.Add(new CombatEvent($"Hostile Valhalla berserkers of group {groupId} fade back to Valhalla after 6 turns."));
            session.HostileHornOfValhallaGroups.Remove(groupId);
        }
    }

    private static bool CanCharacterUseHornType(Character user, HornOfValhallaType type)
    {
        var hasCleric = user.Classes.Contains(CharacterClass.Cleric) || user.Class == CharacterClass.Cleric;
        var hasThief = user.Classes.Contains(CharacterClass.Thief) || user.Class == CharacterClass.Thief;

        return type switch
        {
            HornOfValhallaType.Silver => true,
            HornOfValhallaType.Brass => hasCleric || user.IsFighterClassed() || hasThief,
            HornOfValhallaType.Bronze => hasCleric || user.IsFighterClassed(),
            HornOfValhallaType.Iron => user.IsFighterClassed(),
            _ => false
        };
    }

    private static bool IsRadicallyDifferentAlignment(Alignment blowerAlignment, Alignment hornAlignment)
    {
        var blowerAxis = GetMoralAxis(blowerAlignment);
        var hornAxis = GetMoralAxis(hornAlignment);
        return blowerAxis != hornAxis;
    }

    private static int GetMoralAxis(Alignment alignment)
    {
        return alignment switch
        {
            Alignment.LawfulGood or Alignment.NeutralGood or Alignment.ChaoticGood => 1,
            Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil => -1,
            _ => 0
        };
    }

    private Alignment RollRandomAlignment()
    {
        var roll = _dice.Roll(9);
        return roll switch
        {
            1 => Alignment.LawfulGood,
            2 => Alignment.NeutralGood,
            3 => Alignment.ChaoticGood,
            4 => Alignment.LawfulNeutral,
            5 => Alignment.TrueNeutral,
            6 => Alignment.ChaoticNeutral,
            7 => Alignment.LawfulEvil,
            8 => Alignment.NeutralEvil,
            _ => Alignment.ChaoticEvil
        };
    }
}
