using Adnd.Core.Characters;
using Adnd.Core.Combat.Sessions;
using Adnd.Core.Diagnostics;
using Adnd.Core.Monsters;

namespace Adnd.Core.Spells.Casting.Handlers;

public sealed class RaiseDeadHandler : ISpellEffectHandler
{
    public bool CanHandle(string spellId) => string.Equals(spellId, "raise_dead", StringComparison.OrdinalIgnoreCase);

    public SpellCastResult Resolve(SpellCastRequest request)
    {
        var spell = request.Spell;
        if (spell == null)
            return SpellCastResult.Failure("Missing spell definition.");

        var enemyTarget = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Enemy);
        if (request.Context == SpellUseContext.Combat && enemyTarget != null)
            return ResolveMummyTargetInCombat(request, spell, enemyTarget);

        return ResolveAllyRaiseDead(request, spell);
    }

    private static SpellCastResult ResolveAllyRaiseDead(SpellCastRequest request, Spell spell)
    {
        var targetRef = request.Targets.FirstOrDefault(t => t.Type == SpellCastTargetType.Ally);
        var target = targetRef == null
            ? request.Caster
            : request.PartyTargets.FirstOrDefault(p => string.Equals(p.Name, targetRef.CharacterName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
            return SpellCastResult.Failure("No valid ally target selected.");

        if (target.DiedOfOldAge)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; death due to old age is final (DMG 15).");

        if (target.HasStatus(CharacterStatus.Lost))
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; the character is lost.");

        var isDead = target.HasStatus(CharacterStatus.Dead) || target.CurrentHitPoints <= 0;
        if (!isDead)
            return SpellCastResult.Failure($"{spell.Name} can only target a dead character.");

        if (target.HasStatus(CharacterStatus.Ashes))
            return SpellCastResult.Failure($"{spell.Name} cannot target ashes.");

        if (target.MummyRotRaiseExpired)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; mummy rot has passed the 6-turn revival window.");

        if (target.MummyRotRaiseWindowRoundsRemaining > 0 && target.MummyRotRaiseRequiresCureDisease)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; Cure Disease must be cast first (within 6 turns of mummy-inflicted death).");

        if (target.PoisonSporesRaiseExpired)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; poison spores has passed the 24-hour revival window.");

        if (target.PoisonSporesRaiseWindowDaysRemaining > 0 && target.PoisonSporesRaiseRequiresCureDisease)
            return SpellCastResult.Failure($"{spell.Name} cannot restore {target.Name}; Cure Disease must be cast first (within 24 hours of poison spores death).");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name}. {spell.EffectDescription}");

        var constitution = target.Abilities.Constitution;
        var chance = SystemShockSurvivalChance(constitution);
        var saveTarget = ChanceToD20SaveTarget(chance);
        var rng = request.Rng ?? Random.Shared;
        var roll = rng.Next(1, 21);

        result.Events.Add($"Saving throw (d20) for {target.Name}: CON {constitution} gives {chance}% survival (needs {saveTarget} or less). Rolled {roll}.");

        if (roll <= saveTarget)
        {
            var beforeHp = target.CurrentHitPoints;
            target.RemoveStatus(CharacterStatus.Dead);
            target.RemoveStatus(CharacterStatus.Ashes);
            target.RemoveStatus(CharacterStatus.Lost);
            target.CurrentHitPoints = 1;
            target.Abilities.Constitution = Math.Max(0, constitution - 1);
            target.ClearMummyRotDeathCurse();
            target.ClearPoisonSporesDeathCurse();

            result.HpChanges[target.Name] = Math.Max(0, target.CurrentHitPoints - beforeHp);
            result.Events.Add($"{target.Name} is restored to life with 1 HP.");
            result.Events.Add($"{target.Name} loses 1 Constitution (now {target.Abilities.Constitution}).");
            return result;
        }

        target.RemoveStatus(CharacterStatus.Dead);
        target.AddStatus(CharacterStatus.Ashes);
        target.CurrentHitPoints = 0;
        result.Events.Add($"Raise Dead fails. {target.Name} turns to ashes.");

        return result;
    }

    private static SpellCastResult ResolveMummyTargetInCombat(SpellCastRequest request, Spell spell, SpellCastTarget targetRef)
    {
        var target = ResolveEnemyTarget(request, targetRef);
        if (target == null)
            return SpellCastResult.Failure("No valid enemy target selected.");

        if (SpellDamageSaveHelper.IsWraith(target))
        {
            var slain = new SpellCastResult { Success = true };
            slain.Events.Add($"{request.Caster.Name} casts {spell.Name} at {target.DisplayName}!");
            var hpBefore = target.CurrentHitPoints;
            target.CurrentHitPoints = 0;
            slain.Events.Add($"{target.DisplayName} is slain by Raise Dead! HP {hpBefore}->0.");
            RuleApplicationInfo.PublishLinked("MM", "Wraith", "A raise dead spell slays a wraith (no save).");
            return slain;
        }

        if (!SpellDamageSaveHelper.IsMummy(target))
            return SpellCastResult.Failure($"{spell.Name} can only target mummies or wraiths in combat.");

        var result = new SpellCastResult { Success = true };
        result.Events.Add($"{request.Caster.Name} casts {spell.Name} at {target.DisplayName}!");

        var saveTarget = SpellDamageSaveHelper.GetMonsterMagicSaveTarget(target, 20);
        var rng = request.Rng ?? Random.Shared;
        var saveRoll = rng.Next(1, 21);

        RuleApplicationInfo.PublishLinked(
            "MM",
            "Mummy",
            $"{target.DisplayName} saving throw vs magic against Raise Dead: 1d20={saveRoll} vs {saveTarget}.");

        if (saveRoll >= saveTarget)
        {
            result.Events.Add($"{target.DisplayName} resists Raise Dead (save {saveRoll} vs {saveTarget}).");
            return result;
        }

        var transformed = CreateRaisedManInstance(target);
        var session = request.CombatSession;
        if (session == null)
            return SpellCastResult.Failure("Raise Dead transformation requires an active combat session.");

        var indexInSession = session.Monsters.IndexOf(target);
        if (indexInSession < 0)
            return SpellCastResult.Failure("Target mummy is no longer present in combat.");

        session.RearAttackers.Remove(target);
        session.PyroHydraBreathUsed.Remove(target);
        session.Monsters[indexInSession] = transformed;

        result.Events.Add($"{target.DisplayName} fails save ({saveRoll} vs {saveTarget}) and becomes {transformed.DisplayName}!");
        result.Events.Add($"{transformed.DisplayName} is now a normal human with 7th-level fighting ability.");
        return result;
    }

    private static MonsterInstance? ResolveEnemyTarget(SpellCastRequest request, SpellCastTarget targetRef)
    {
        if (targetRef.MonsterIndex is int idx)
            return request.MonsterTargets.FirstOrDefault(m => m.Index == idx && m.IsAlive);

        if (!string.IsNullOrWhiteSpace(targetRef.TargetGroupId))
            return request.MonsterTargets.FirstOrDefault(m => m.IsAlive
                                                               && string.Equals(m.GroupId, targetRef.TargetGroupId, StringComparison.OrdinalIgnoreCase)
                                                               && (SpellDamageSaveHelper.IsMummy(m) || SpellDamageSaveHelper.IsWraith(m)));

        return null;
    }

    private static MonsterInstance CreateRaisedManInstance(MonsterInstance mummy)
    {
        var raisedManTemplate = new Monster
        {
            Name = "Normal Man",
            Type = MonsterType.Humanoid,
            TypeName = "Human",
            Alignment = "Neutral",
            NumberOfAppearancesMin = 1,
            NumberOfAppearancesMax = 1,
            ArmorClass = 4,
            MovementRate = "12",
            Movement = new MonsterMovement { Walk = 12 },
            HitDice = 7,
            HitDiceType = 10,
            ExtraHitPoints = 0,
            THAC0 = 14,
            NumberOfAttacks = 1,
            SavingThrows = new MonsterSavingThrows
            {
                ParalyzationPoisonDeath = 10,
                RodStaffWand = 11,
                PetrificationPolymorph = 12,
                BreathWeapon = 13,
                Spell = 13
            },
            Morale = new MonsterMorale { Value = 12 },
            Attacks = new List<MonsterAttack>
            {
                new() { Name = "Weapon", NumberOfAttacks = 1, Damage = "1d8" }
            },
            BaseXPValue = 350,
            XPValuePerHitPoint = 10,
            TreasureType = "None",
            IndividualTreasure = "None"
        };

        var transformed = new MonsterInstance(raisedManTemplate, mummy.Index, mummy.GroupId)
        {
            IsInLair = mummy.IsInLair
        };

        return transformed;
    }

    internal static int SystemShockSurvivalChance(int constitution)
    {
        return constitution switch
        {
            <= 1 => 30,
            <= 3 => 35,
            <= 5 => 40,
            <= 7 => 45,
            <= 9 => 50,
            <= 11 => 55,
            <= 13 => 60,
            <= 15 => 65,
            16 => 70,
            17 => 75,
            18 => 80,
            19 => 85,
            20 => 90,
            21 => 95,
            22 => 97,
            23 => 98,
            24 => 99,
            _ => 100
        };
    }

    internal static int ChanceToD20SaveTarget(int chancePercent) => Math.Clamp((int)Math.Ceiling(chancePercent / 5.0), 1, 20);
}
