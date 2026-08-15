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
            Size = json.Size,
            HitPoints = json.HitPoints,

            XPValue = json.XPValue,
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

            SavingThrows = new MonsterSavingThrows
            {
                ParalyzationPoisonDeath = json.SavingThrows.ParalyzationPoisonDeath,
                RodStaffWand = json.SavingThrows.RodStaffWand,
                PetrificationPolymorph = json.SavingThrows.PetrificationPolymorph,
                BreathWeapon = json.SavingThrows.BreathWeapon,
                Spell = json.SavingThrows.Spell
            },

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
}
