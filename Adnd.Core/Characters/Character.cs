// Characters/Character.cs
using Adnd.Core.Items;
using Adnd.Core.Spells;
using System.Text.Json.Serialization;
namespace Adnd.Core.Characters;

public class Character
{
    public string Name { get; set; } = "";
    public Race Race { get; set; }
    // Keep a computed legacy Class property for compatibility (primary class)
    [System.Text.Json.Serialization.JsonIgnore]
    public CharacterClass Class => Classes.Count > 0 ? Classes[0] : default;
    // Support multiple classes (multiclass) for non-human races
    public List<CharacterClass> Classes { get; set; } = new();
    public List<ClassProgression> ClassProgressions { get; set; } = new();

    // Persisted dungeon location (where this character currently is / was left).
    public int DungeonLevel { get; set; } = 0;
    public int DungeonCellX { get; set; } = 0;
    public int DungeonCellY { get; set; } = 0;

    // Prepared support for human dual-classing (AD&D dual-class mechanic).
    // Dual-class is a separate mechanic from multiclass: a human may later
    // change primary class to a second class while retaining progression in the
    // original class under AD&D rules. The runtime fields are provided now so
    // the dual-class action can be implemented later without schema changes.
    public bool IsDualClassed { get; set; } = false;
    // The class the character dual-classed into (if any)
    public CharacterClass? DualClass { get; set; }
    // Level in the original class at the time of dual-classing (optional)
    public int DualClassOriginalLevel { get; set; } = 0;
    public AbilityScores Abilities { get; set; } = new();
    public int? ConstitutionBeforeDisease { get; set; }
    public bool RotGrubFlamePromptPending { get; set; }
    public int RotGrubDeathRoundsRemaining { get; set; }
    public int Level { get; set; } = 1;
    public bool LayOnHandsUsedToday { get; set; }
    public int TemporaryStrengthBonus { get; set; }
    public int TemporaryStrengthRoundsRemaining { get; set; }
    public int TemporaryArmorClassBonusUntilDungeonExit { get; set; }
    public int MaxHitPoints { get; set; }
    public int CurrentHitPoints { get; set; }
    public int Experience { get; set; }

    // Coins
    public int GoldPieces { get; set; }
    public int CopperPieces { get; set; }
    public int SilverPieces { get; set; }
    public int ElectrumPieces { get; set; }
    public int PlatinumPieces { get; set; }

    public int ArmorClass { get; set; }
    public Gender Gender { get; set; }
    public Alignment Alignment { get; set; }
    public int Age { get; set; }
    public List<Item> Inventory { get; set; } = new();
    public Dictionary<EquipmentSlot, Item?> Equipment { get; set; } = new();
    public int? ExceptionalStrengthPercentile { get; set; }
    public List<SpellcastingState> Spellcasting { get; set; } = new();

    // Combat stats (AD&D)
    [JsonIgnore]
    public int Thac0 => Thac0Calculator.GetThac0(Classes.Count > 0 ? Classes[0] : default, GetClassLevel(Classes.Count > 0 ? Classes[0] : default));
    [JsonIgnore]
    public string Thac0Display
    {
        get
        {
            var baseThac0 = Thac0 - GetStrengthAttackBonus() - GetMainWeaponToHitBonus();

            if (Equipment.TryGetValue(EquipmentSlot.OffHand, out var offHandItem)
                && offHandItem != null)
            {
                var (mainHandPenalty, offHandPenalty) = GetDualWieldThac0Penalties(Abilities.Dexterity);
                var mainHandThac0 = baseThac0 + mainHandPenalty;
                var offHandThac0 = baseThac0 + offHandPenalty;
                return $"{mainHandThac0}/{offHandThac0}";
            }

            return baseThac0.ToString();
        }
    }
    [JsonIgnore]
    public string DamageDisplay => GetDamageDisplay();
   // public int NumberOfAttacks { get; set; } = 1; // Base attacks per round; can be modified by level/class/weapon
    public float NumberOfAttacks { get; set; } = 1; // Base attacks per round; can be modified by level/class/weapon
    public string Damage { get; set; } = "1d6"; // Base damage or equipped weapon damage

    // Status conditions (can be combined as flags)
    public CharacterStatus Status { get; set; } = CharacterStatus.None;

    public override string ToString()
    {
        EnsureClassProgressions();

        var classes = Classes.Count > 0 ? string.Join("/", Classes.Select(c => c.ToDisplayString())) : Class.ToDisplayString();
        var classLevels = Classes.Count > 1
            ? " | " + string.Join(" ", Classes.Select(c => $"{c.ToDisplayString()} L{GetClassLevel(c)} XP {GetClassExperience(c)}"))
            : string.Empty;

        var dualInfo = IsDualClassed && DualClass.HasValue ? $" (dual: {DualClass.Value.ToDisplayString()})" : string.Empty;
        var alignment = Alignment.ToDisplayString();
        var statusInfo = Status != CharacterStatus.None ? $" [{GetStatusDisplay()}]" : string.Empty;

        var levelPart = Classes.Count > 1 ? string.Empty : $"Lvl {Level}, ";

        return $"{Name} - {Race.ToDisplayString()} {alignment} {classes}{dualInfo} ({levelPart}HP {CurrentHitPoints}/{MaxHitPoints}, XP {Experience}, GP {GoldPieces}, SP {SilverPieces}, EP {ElectrumPieces}, CP {CopperPieces}, PP {PlatinumPieces}, AC {ArmorClass}, THAC0 {Thac0Display}, Attacks {NumberOfAttacks}, Dmg {DamageDisplay}){statusInfo}{classLevels}\n" +
               $"STR {GetStrengthDisplay()}, INT {Abilities.Intelligence}, WIS {Abilities.Wisdom}, DEX {Abilities.Dexterity}, CON {Abilities.Constitution}, CHA {Abilities.Charisma}";
    }

    public Character()
    {
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            Equipment[slot] = null;
    }

    public int GetClassLevel(CharacterClass cls)
    {
        EnsureClassProgressions();
        var entry = ClassProgressions.FirstOrDefault(x => x.Class == cls);
        return entry?.Level ?? Level;
    }

    public int GetClassExperience(CharacterClass cls)
    {
        EnsureClassProgressions();
        var entry = ClassProgressions.FirstOrDefault(x => x.Class == cls);
        return entry?.Experience ?? Experience;
    }

    public bool IsPaladin() => Classes.Contains(CharacterClass.Paladin);

    public int GetPaladinLevel() => IsPaladin()
        ? GetClassLevel(CharacterClass.Paladin)
        : 0;

    public void EnsureClassProgressions()
    {
        if (Classes == null || Classes.Count == 0)
            return;

        ClassProgressions ??= new List<ClassProgression>();

        if (ClassProgressions.Count == 0)
        {
            if (Classes.Count == 1)
            {
                ClassProgressions.Add(new ClassProgression
                {
                    Class = Classes[0],
                    Level = Math.Max(1, Level),
                    Experience = Math.Max(0, Experience)
                });
            }
            else
            {
                var share = Math.Max(0, Experience) / Classes.Count;
                foreach (var cls in Classes)
                {
                    ClassProgressions.Add(new ClassProgression
                    {
                        Class = cls,
                        Level = Adnd.Core.Characters.Progression.ExperienceTable.GetLevelForClass(cls, share),
                        Experience = share
                    });
                }
            }
        }

        var rebuilt = new List<ClassProgression>();
        foreach (var cls in Classes)
        {
            var existing = ClassProgressions.FirstOrDefault(x => x.Class == cls);
            rebuilt.Add(existing ?? new ClassProgression
            {
                Class = cls,
                Level = 1,
                Experience = 0
            });
        }

        ClassProgressions = rebuilt;

        if (Classes.Count == 1)
        {
            Level = Math.Max(1, ClassProgressions[0].Level);
            Experience = Math.Max(0, ClassProgressions[0].Experience);
        }
        else
        {
            Level = Math.Max(1, ClassProgressions[0].Level);
            Experience = Math.Max(0, ClassProgressions.Sum(x => x.Experience));
        }
    }

    // Helper methods for status management
    public bool HasStatus(CharacterStatus status) => (Status & status) == status;
    public void AddStatus(CharacterStatus status) => Status |= status;
    public void RemoveStatus(CharacterStatus status) => Status &= ~status;
    public void ClearStatus() => Status = CharacterStatus.None;

    public void ApplyDisease()
    {
        if (!HasStatus(CharacterStatus.Diseased))
        {
            ConstitutionBeforeDisease ??= Abilities.Constitution;
            AddStatus(CharacterStatus.Diseased);
        }
    }

    public void CureDiseaseAndRestoreConstitution()
    {
        RemoveStatus(CharacterStatus.Diseased);
        RotGrubFlamePromptPending = false;
        RotGrubDeathRoundsRemaining = 0;

        if (ConstitutionBeforeDisease.HasValue)
        {
            Abilities.Constitution = Math.Max(0, ConstitutionBeforeDisease.Value);
            ConstitutionBeforeDisease = null;
        }
    }

    public void MarkRotGrubExposurePendingFlame()
    {
        if (RotGrubDeathRoundsRemaining > 0)
            return;

        RotGrubFlamePromptPending = true;
    }

    public void ApplyRotGrubInfestation(int deathInRounds)
    {
        ApplyDisease();
        RotGrubFlamePromptPending = false;
        RotGrubDeathRoundsRemaining = Math.Max(1, deathInRounds);
    }

    private string GetStatusDisplay()
    {
        var statuses = new List<string>();
        if (HasStatus(CharacterStatus.Dead)) statuses.Add("Dead");
        if (HasStatus(CharacterStatus.Poisoned)) statuses.Add("Poisoned");
        if (HasStatus(CharacterStatus.Paralyzed)) statuses.Add("Paralyzed");
        if (HasStatus(CharacterStatus.Petrified)) statuses.Add("Petrified");
        if (HasStatus(CharacterStatus.Asleep)) statuses.Add("Asleep");
        if (HasStatus(CharacterStatus.Ashes)) statuses.Add("Ashes");
        if (HasStatus(CharacterStatus.Lost)) statuses.Add("Lost");
        if (HasStatus(CharacterStatus.Invisible)) statuses.Add("Invisible");
        return string.Join(", ", statuses);
    }

    private static (int MainHandPenalty, int OffHandPenalty) GetDualWieldThac0Penalties(int dexterity)
    {
        return dexterity switch
        {
            <= 8 => (3, 6),
            <= 12 => (2, 4),
            <= 15 => (1, 2),
            <= 17 => (0, 1),
            _ => (0, 0)
        };
    }

    private string GetStrengthDisplay()
    {
        if (Abilities.Strength == 18 && ExceptionalStrengthPercentile.HasValue)
        {
            var pct = ExceptionalStrengthPercentile.Value;
            if (pct >= 1 && pct <= 100)
            {
                var suffix = pct == 100 ? "00" : pct.ToString("00");
                return $"18/{suffix}";
            }
        }

        return Abilities.Strength.ToString();
    }

    private int GetStrengthAttackBonus()
    {
        var str = Abilities.Strength;
        if (str <= 7) return -2;
        if (str <= 9) return -1;
        if (str <= 12) return 0;
        if (str <= 15) return 1;
        if (str <= 18)
        {
            if (str == 18 && ExceptionalStrengthPercentile.HasValue)
            {
                var pct = ExceptionalStrengthPercentile.Value;
                if (pct <= 75) return 3;
                if (pct <= 90) return 4;
                if (pct <= 99) return 5;
                return 6;
            }

            return 2;
        }

        return str switch
        {
            19 => 3,
            20 => 3,
            21 => 4,
            22 => 4,
            23 => 5,
            24 => 6,
            _ => 7
        };
    }

    private int GetStrengthDamageBonus()
    {
        var str = Abilities.Strength;
        if (str <= 5) return -2;
        if (str <= 7) return -1;
        if (str <= 12) return 0;
        if (str <= 15) return 1;
        if (str == 16) return 2;
        if (str <= 18)
        {
            if (str == 18 && ExceptionalStrengthPercentile.HasValue)
            {
                var pct = ExceptionalStrengthPercentile.Value;
                if (pct <= 75) return 3;
                if (pct <= 90) return 4;
                if (pct <= 99) return 5;
                return 6;
            }

            return 3;
        }

        return str switch
        {
            19 => 7,
            20 => 8,
            21 => 9,
            22 => 10,
            23 => 11,
            24 => 12,
            _ => 14
        };
    }

    private string GetDamageDisplay()
    {
        var bonus = GetStrengthDamageBonus();
        if (bonus == 0)
            return Damage;

        if (string.IsNullOrWhiteSpace(Damage))
            return bonus > 0 ? $"+{bonus}" : bonus.ToString();

        var mainHandIsWeapon = Equipment.TryGetValue(EquipmentSlot.MainHand, out var mainHand)
            && mainHand != null
            && mainHand.Type == ItemType.Weapon;
        var offHandIsWeapon = Equipment.TryGetValue(EquipmentSlot.OffHand, out var offHand)
            && offHand != null
            && offHand.Type == ItemType.Weapon;

        var parts = Damage.Split('/');
        if (parts.Length >= 1 && mainHandIsWeapon)
        {
            parts[0] = ApplyDamageModifier(parts[0].Trim(), bonus);
        }

        if (parts.Length >= 2 && offHandIsWeapon)
        {
            parts[1] = ApplyDamageModifier(parts[1].Trim(), bonus);
        }

        return string.Join("/", parts);
    }

    private static string ApplyDamageModifier(string expression, int bonus)
    {
        var plusIdx = expression.LastIndexOf('+');
        var minusIdx = expression.LastIndexOf('-');
        var idx = Math.Max(plusIdx, minusIdx);

        if (idx > 0 && int.TryParse(expression[(idx + 1)..], out var existing))
        {
            var signedExisting = expression[idx] == '-' ? -existing : existing;
            var total = signedExisting + bonus;
            var core = expression[..idx];
            if (total == 0)
                return core;
            return total > 0 ? $"{core}+{total}" : $"{core}{total}";
        }

        return bonus > 0 ? $"{expression}+{bonus}" : $"{expression}{bonus}";
    }

    [JsonIgnore]
    public int MaxCarryWeight => GetMaxCarryWeight();

    [JsonIgnore]
    public int CurrentCarryWeight => GetCurrentCarryWeight();

    public bool CanCarry(Item item)
    {
        var itemWeight = item == null ? 0 : Math.Max(0, item.Weight);
        return CurrentCarryWeight + itemWeight <= MaxCarryWeight;
    }

    public bool TryReceiveItem(Item item)
    {
        if (item == null)
            return false;

        if (!CanCarry(item))
            return false;

        Inventory.Add(item);
        return true;
    }

    private int GetMaxCarryWeight()
    {
        var str = Abilities.Strength;
        return str switch
        {
            <= 3 => 35,
            <= 5 => 45,
            <= 7 => 55,
            <= 9 => 70,
            <= 11 => 85,
            <= 13 => 100,
            <= 15 => 120,
            16 => 140,
            17 => 160,
            18 => 180,
            19 => 220,
            20 => 260,
            21 => 300,
            22 => 350,
            23 => 400,
            _ => 500
        };
    }

    private int GetCurrentCarryWeight()
    {
        var inventoryWeight = Inventory.Sum(i => Math.Max(0, i?.Weight ?? 0));
        var equippedWeight = Equipment.Values.Where(v => v != null).Sum(v => Math.Max(0, v!.Weight));
        return inventoryWeight + equippedWeight;
    }

    private int GetMainWeaponToHitBonus()
    {
        if (!Equipment.TryGetValue(EquipmentSlot.MainHand, out var main) || main == null)
            return 0;

        return Math.Max(0, main.ToHitBonus);
    }
}

