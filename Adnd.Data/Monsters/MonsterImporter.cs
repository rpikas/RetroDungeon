//Konverterar JSON‑modeller → Core‑modeller.
using System;
using System.Linq;
using Adnd.Core.Monsters;
using Adnd.Core.Config;

namespace Adnd.Data.Monsters;

public static class MonsterImporter
{
    public static Monster Convert(MonsterJsonModel json)
    {
        return new Monster
        {
            Name = json.Name,
            Type = Enum.TryParse<MonsterType>(json.Type, out var t) ? t : MonsterType.Other,
            ClimateTerain = json.ClimateTerain,
            Frequency = json.Frequency,
            ActivityCycle = json.ActivityCycle,
            Intelligence = json.Intelligence,
            Alignment = json.Alignment,
            NumberOfAppearancesMin = json.NumberOfAppearancesMin,
            NumberOfAppearancesMax = json.NumberOfAppearancesMax,
            ArmorClass = json.ArmorClass,
            MovementRate = json.MovementRate,
            HitDice = json.HitDice,
            HitDiceType = json.HitDiceType,
            ExtraHitPoints = json.ExtraHitPoints,
            THAC0 = json.THAC0,
            NumberOfAttacks = json.NumberOfAttacks,
            MagicResistance = json.MagicResistance,
            Size = ParseSize(json.Size),
            HitPoints = json.HitPoints,

            BaseXPValue = json.BaseXPValue,
            XPValuePerHitPoint = json.XPValuePerHitPoint,
            TreasureType = string.IsNullOrWhiteSpace(json.TreasureType) ? "None" : json.TreasureType,
            TreasureChanceOverride = json.TreasureChanceOverride,
            Source = ParseSource(json),

            Movement = new MonsterMovement
            {
                Walk = json.Movement.Walk,
                Fly = json.Movement.Fly,
                Swim = json.Movement.Swim,
                Burrow = json.Movement.Burrow,
                Climb = json.Movement.Climb
            },

            SavingThrows = ResolveSavingThrows(json),

            Morale = new MonsterMorale
            {
                Value = json.Morale.Value
            },

            Attacks = json.Attacks.Select(a => new MonsterAttack
            {
                Name = a.Name,
                NumberOfAttacks = a.NumberOfAttacks,
                Damage = a.Damage
            }).ToList(),

            SpecialAbilities = json.SpecialAbilities.Select(sa => new MonsterSpecialAbility
            {
                Name = sa.Name,
                Description = sa.Description
            }).ToList()
        };
    }

    private static Sources ParseSource(MonsterJsonModel json)
    {
        if (string.IsNullOrWhiteSpace(json.Source))
            return Sources.Adnd;

        // Try to parse the source value
        if (Enum.TryParse<Sources>(json.Source, ignoreCase: true, out var result))
            return result;

        // Default to Adnd if parsing fails
        return Sources.Adnd;
    }

    private static MonsterSavingThrows ResolveSavingThrows(MonsterJsonModel json)
    {
        if (json.SavingThrows != null)
        {
            return new MonsterSavingThrows
            {
                ParalyzationPoisonDeath = json.SavingThrows.ParalyzationPoisonDeath,
                RodStaffWand = json.SavingThrows.RodStaffWand,
                PetrificationPolymorph = json.SavingThrows.PetrificationPolymorph,
                BreathWeapon = json.SavingThrows.BreathWeapon,
                Spell = json.SavingThrows.Spell
            };
        }

        var fallbackSpell = GetFallbackSpellSave(json.Class, json.HitDice);

        return new MonsterSavingThrows
        {
            ParalyzationPoisonDeath = 0,
            RodStaffWand = 0,
            PetrificationPolymorph = 0,
            BreathWeapon = 0,
            Spell = fallbackSpell
        };
    }

    private static int GetFallbackSpellSave(string? monsterClass, int hitDice)
    {
        var normalizedClass = string.IsNullOrWhiteSpace(monsterClass) ? "Fighter" : monsterClass.Trim();
        var level = Math.Clamp(hitDice, 1, 17);

        return normalizedClass.Equals("Fighter", StringComparison.OrdinalIgnoreCase)
            ? GetFighterSpellSave(level)
            : GetFighterSpellSave(level);
    }

    private static int GetFighterSpellSave(int level)
    {
        return level switch
        {
            1 => 17,
            2 => 17,
            3 => 16,
            4 => 16,
            5 => 14,
            6 => 14,
            7 => 13,
            8 => 13,
            9 => 11,
            10 => 11,
            11 => 10,
            12 => 10,
            13 => 8,
            14 => 8,
            15 => 7,
            16 => 7,
            _ => 6
        };
    }

    private static MonsterSize ParseSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return MonsterSize.Medium;

        return Enum.TryParse<MonsterSize>(raw.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : MonsterSize.Medium;
    }
}
