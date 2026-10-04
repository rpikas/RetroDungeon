using System;
using System.Collections.Generic;
using Adnd.Core.Characters.Progression;
using Adnd.Core.Items;
using Adnd.Core.Spells;

namespace Adnd.Core.Characters;

public sealed class DualClassService
{
    public const string NoOldClassProgressionMessage = "No old-class progression.";
    public const string OldClassFunctionBlocksXpMessage = "Using old-class functions blocks adventure XP until new class level surpasses old class level.";

    public static string GetDualClassSwitchWarningText()
        => $"{NoOldClassProgressionMessage} {OldClassFunctionBlocksXpMessage}";

    public DualClassSwitchResult TryStartDualClass(Character character, CharacterClass targetClass)
    {
        if (character == null)
        {
            return new DualClassSwitchResult
            {
                Success = false,
                Message = "Character is missing.",
                TargetClass = targetClass
            };
        }

        var originalClass = character.Classes != null && character.Classes.Count > 0
            ? character.Classes[0]
            : (CharacterClass?)null;
        var originalLevel = originalClass.HasValue ? character.GetClassLevel(originalClass.Value) : 0;

        if (!CanStartDualClass(character, targetClass, out var reason))
        {
            return new DualClassSwitchResult
            {
                Success = false,
                Message = reason,
                OriginalClass = originalClass,
                OriginalLevel = originalLevel,
                TargetClass = targetClass
            };
        }

        if (!this.StartDualClass(character, targetClass, out reason))
        {
            return new DualClassSwitchResult
            {
                Success = false,
                Message = reason,
                OriginalClass = originalClass,
                OriginalLevel = originalLevel,
                TargetClass = targetClass
            };
        }

        return new DualClassSwitchResult
        {
            Success = true,
            Message = $"Dual-class started: {character.Name} switched from {originalClass?.ToDisplayString() ?? "Unknown"} to {targetClass.ToDisplayString()}. {GetDualClassSwitchWarningText()}",
            OriginalClass = originalClass,
            OriginalLevel = originalLevel,
            TargetClass = targetClass
        };
    }

    public bool CanStartDualClass(Character character, CharacterClass targetClass, out string reason)
    {
        reason = string.Empty;

        if (character == null)
        {
            reason = "Character is missing.";
            return false;
        }

        if (character.Race != Race.Human)
        {
            reason = "Only humans can dual-class.";
            return false;
        }

        character.EnsureClassProgressions();

        if (targetClass == CharacterClass.Bard)
        {
            if (!CanStartBardTransition(character, out reason))
                return false;

            if (!IsAlignmentAllowedForClass(character.Alignment, targetClass))
            {
                reason = "Alignment does not allow the target class.";
                return false;
            }

            return true;
        }

        if (character.IsDualClassed)
        {
            reason = "Character has already dual-classed.";
            return false;
        }

        if (character.Classes.Count != 1)
        {
            reason = "Dual-class requires a single original class.";
            return false;
        }

        var originalClass = character.Classes[0];

        if (targetClass == CharacterClass.Bard)
        {
            if (originalClass != CharacterClass.Thief)
            {
                reason = "To become a bard, the character must currently be a thief (after first being a fighter).";
                return false;
            }

            var thiefLevel = character.GetClassLevel(CharacterClass.Thief);
            if (thiefLevel < 5 || thiefLevel > 8)
            {
                reason = "Bard transition requires thief level 5-8.";
                return false;
            }

            if (character.DualClassOriginalClass != CharacterClass.Fighter)
            {
                reason = "Bard progression requires the prior class to be fighter.";
                return false;
            }

            if (character.DualClassOriginalLevel < 5 || character.DualClassOriginalLevel > 7)
            {
                reason = "Bard progression requires fighter level 5-7 before switching to thief.";
                return false;
            }

            if (!MeetsBardAbilityPrerequisites(character.Abilities))
            {
                reason = "Bard requires high Strength, Dexterity, Intelligence, Wisdom, and Charisma (15+ each).";
                return false;
            }
        }

        if (originalClass == targetClass)
        {
            reason = "Target class must be different from original class.";
            return false;
        }

        if (!MeetsPrimeRequisiteThreshold(character.Abilities, originalClass, 15))
        {
            reason = "Original class prime requisite must be at least 15.";
            return false;
        }

        if (!MeetsPrimeRequisiteThreshold(character.Abilities, targetClass, 17))
        {
            reason = "Target class prime requisite must be at least 17.";
            return false;
        }

        if (!IsAlignmentAllowedForClass(character.Alignment, targetClass))
        {
            reason = "Alignment does not allow the target class.";
            return false;
        }

        return true;
    }

    public bool StartDualClass(Character character, CharacterClass targetClass, out string reason)
    {
        reason = string.Empty;

        if (!CanStartDualClass(character, targetClass, out reason))
            return false;

        if (targetClass == CharacterClass.Bard && character.IsDualClassed)
            return StartBardTransition(character, out reason);

        character.EnsureClassProgressions();

        var originalClass = character.Classes[0];
        var originalLevel = character.GetClassLevel(originalClass);

        character.IsDualClassed = true;
        character.DualClassOriginalClass = originalClass;
        character.DualClassOriginalLevel = Math.Max(1, originalLevel);
        character.DualClass = targetClass;
        character.DualClassState = DualClassState.TrainingNewClass;
        character.UsedOriginalClassFunctionThisAdventure = false;
        character.DualClassStartedAtExperience = Math.Max(0, character.Experience);

        // Switch active progression to the new class at level 1 and 0 XP.
        character.Classes = new List<CharacterClass> { targetClass };
        character.ClassProgressions = new List<ClassProgression>
        {
            new()
            {
                Class = targetClass,
                Level = 1,
                Experience = 0
            }
        };

        character.Level = 1;
        character.Experience = 0;

        // Drop old-class spellcasting state and rebuild from the new class only.
        _ = new SpellProgressionService().RecalculateFromClassProgressions(character);

        // Keep HP/HD as-is per AD&D dual-class rule, but recalculate derived combat state
        // so old-class combat values (e.g., monk open-hand damage/attacks) do not leak.
        character.RecalculateArmorClassFromState();
        character.RefreshMoveFromArmorAndClass();
        character.NumberOfAttacks = 1f;
        character.Damage = GetCurrentWeaponDamageOrUnarmed(character);

        return true;
    }

    private static bool CanStartBardTransition(Character character, out string reason)
    {
        reason = string.Empty;

        if (!character.IsDualClassed)
        {
            reason = "Bard transition requires prior class changes (Fighter -> Thief).";
            return false;
        }

        if (character.Classes.Count == 0)
        {
            reason = "Character has no active class.";
            return false;
        }

        var currentClass = character.Classes[0];
        if (currentClass != CharacterClass.Thief)
        {
            reason = "To become a bard, the character must currently be a thief (after first being a fighter).";
            return false;
        }

        var thiefLevel = character.GetClassLevel(CharacterClass.Thief);
        if (thiefLevel < 5 || thiefLevel > 8)
        {
            reason = "Bard transition requires thief level 5-8.";
            return false;
        }

        if (character.DualClassOriginalClass != CharacterClass.Fighter)
        {
            reason = "Bard progression requires the prior class to be fighter.";
            return false;
        }

        if (character.DualClassOriginalLevel < 5 || character.DualClassOriginalLevel > 7)
        {
            reason = "Bard progression requires fighter level 5-7 before switching to thief.";
            return false;
        }

        if (!MeetsBardAbilityPrerequisites(character.Abilities))
        {
            reason = "Bard requires high Strength, Dexterity, Intelligence, Wisdom, and Charisma (15+ each).";
            return false;
        }

        return true;
    }

    private bool StartBardTransition(Character character, out string reason)
    {
        reason = string.Empty;

        if (!CanStartBardTransition(character, out reason))
            return false;

        character.EnsureClassProgressions();

        // Bard path is a special second class change (Fighter -> Thief -> Bard).
        // After this transition, thief becomes the "original class" for dual-class
        // lock/unlock semantics so thief features can unlock again once bard level
        // surpasses thief level.
        var thiefLevel = Math.Max(1, character.GetClassLevel(CharacterClass.Thief));
        character.DualClassOriginalClass = CharacterClass.Thief;
        character.DualClassOriginalLevel = thiefLevel;

        character.DualClass = CharacterClass.Bard;
        character.DualClassState = DualClassState.TrainingNewClass;
        character.UsedOriginalClassFunctionThisAdventure = false;
        character.DualClassStartedAtExperience = Math.Max(0, character.Experience);

        character.Classes = new List<CharacterClass> { CharacterClass.Bard };
        character.ClassProgressions = new List<ClassProgression>
        {
            new()
            {
                Class = CharacterClass.Bard,
                Level = 1,
                Experience = 0
            }
        };

        character.Level = 1;
        character.Experience = 0;

        _ = new SpellProgressionService().RecalculateFromClassProgressions(character);

        character.RecalculateArmorClassFromState();
        character.RefreshMoveFromArmorAndClass();
        character.NumberOfAttacks = 1f;
        character.Damage = GetCurrentWeaponDamageOrUnarmed(character);

        return true;
    }

    private static bool HasEquippedWeapon(Character c)
    {
        var mainHandWeapon = c.Equipment.TryGetValue(EquipmentSlot.MainHand, out var main)
                             && main != null
                             && main.Type == ItemType.Weapon;
        var offHandWeapon = c.Equipment.TryGetValue(EquipmentSlot.OffHand, out var off)
                            && off != null
                            && off.Type == ItemType.Weapon;
        return mainHandWeapon || offHandWeapon;
    }

    private static string GetCurrentWeaponDamageOrUnarmed(Character c)
    {
        var hasMain = c.Equipment.TryGetValue(EquipmentSlot.MainHand, out var main)
                      && main != null
                      && main.Type == ItemType.Weapon
                      && !string.IsNullOrWhiteSpace(main.Damage);

        var hasOff = c.Equipment.TryGetValue(EquipmentSlot.OffHand, out var off)
                     && off != null
                     && off.Type == ItemType.Weapon
                     && !string.IsNullOrWhiteSpace(off.Damage)
                     && !ReferenceEquals(main, off);

        if (hasMain && hasOff)
            return $"{main!.Damage}/{off!.Damage}";

        if (hasMain)
            return main!.Damage;

        if (hasOff)
            return off!.Damage;

        return "1d2";
    }

    public void MarkOriginalClassFunctionUsed(Character character)
    {
        if (character == null)
            return;

        if (!character.IsDualClassed)
            return;

        if (character.DualClassState != DualClassState.TrainingNewClass)
            return;

        character.UsedOriginalClassFunctionThisAdventure = true;
    }

    public bool IsSpellFromOriginalClass(Character character, SpellClass spellClass)
    {
        if (character == null || !character.IsDualClassed)
            return false;

        if (!character.DualClassOriginalClass.HasValue)
            return false;

        if (!SpellProgression.TryGetSpellClass(character.DualClassOriginalClass.Value, out var originalSpellClass))
            return false;

        return originalSpellClass == spellClass;
    }

    public bool IsTurnUndeadFromOriginalClass(Character character)
    {
        if (character == null || !character.IsDualClassed)
            return false;

        return character.DualClassOriginalClass is CharacterClass.Cleric or CharacterClass.Paladin;
    }

    public bool IsThiefBackstabFromOriginalClass(Character character)
    {
        if (character == null || !character.IsDualClassed)
            return false;

        return character.DualClassOriginalClass is CharacterClass.Thief or CharacterClass.Assassin;
    }

    public bool IsAssassinationFromOriginalClass(Character character)
    {
        if (character == null || !character.IsDualClassed)
            return false;

        return character.DualClassOriginalClass == CharacterClass.Assassin;
    }

    public int ApplyAdventureXpGate(Character character, int awardedXp)
    {
        if (character == null)
            return Math.Max(0, awardedXp);

        if (!character.IsDualClassed)
            return Math.Max(0, awardedXp);

        if (character.DualClassState == DualClassState.TrainingNewClass
            && character.UsedOriginalClassFunctionThisAdventure)
        {
            return 0;
        }

        return Math.Max(0, awardedXp);
    }

    public void UpdateStateAfterLevelGain(Character character)
    {
        if (character == null || !character.IsDualClassed)
            return;

        if (character.DualClass == CharacterClass.Bard)
        {
            NormalizeBardClassState(character);
            return;
        }

        if (character.DualClassState == DualClassState.SurpassedOriginal)
        {
            RestoreOriginalClassProgressionIfUnlocked(character);
            return;
        }

        var activeClass = character.Classes.Count > 0 ? character.Classes[0] : character.DualClass;
        if (!activeClass.HasValue)
            return;

        var currentLevel = character.GetClassLevel(activeClass.Value);
        if (currentLevel > Math.Max(0, character.DualClassOriginalLevel))
        {
            character.DualClassState = DualClassState.SurpassedOriginal;

            if (character.DualClassOriginalClass.HasValue)
            {
                var originalClass = character.DualClassOriginalClass.Value;
                character.EnsureClassProgressions();

                var originalProgression = character.ClassProgressions.Find(cp => cp.Class == originalClass);
                if (originalProgression == null)
                {
                    character.ClassProgressions.Add(new ClassProgression
                    {
                        Class = originalClass,
                        Level = Math.Max(1, character.DualClassOriginalLevel),
                        Experience = 0
                    });
                }
                else if (originalProgression.Level < Math.Max(1, character.DualClassOriginalLevel))
                {
                    originalProgression.Level = Math.Max(1, character.DualClassOriginalLevel);
                }

                if (!character.Classes.Contains(originalClass))
                    character.Classes.Add(originalClass);

                _ = new SpellProgressionService().RecalculateFromClassProgressions(character);
            }

            // Re-apply derived combat stats when original class features unlock.
            character.RecalculateArmorClassFromState();
            character.RefreshMoveFromArmorAndClass();
            character.RefreshMonkProgressionStats();
        }
    }

    private static void RestoreOriginalClassProgressionIfUnlocked(Character character)
    {
        if (!character.IsDualClassed
            || character.DualClassState != DualClassState.SurpassedOriginal
            || !character.DualClassOriginalClass.HasValue)
        {
            return;
        }

        var originalClass = character.DualClassOriginalClass.Value;
        character.EnsureClassProgressions();

        if (!character.Classes.Contains(originalClass))
            character.Classes.Add(originalClass);

        var originalProgression = character.ClassProgressions.Find(cp => cp.Class == originalClass);
        if (originalProgression == null)
        {
            character.ClassProgressions.Add(new ClassProgression
            {
                Class = originalClass,
                Level = Math.Max(1, character.DualClassOriginalLevel),
                Experience = 0
            });
        }
        else if (originalProgression.Level < Math.Max(1, character.DualClassOriginalLevel))
        {
            originalProgression.Level = Math.Max(1, character.DualClassOriginalLevel);
        }

        _ = new SpellProgressionService().RecalculateFromClassProgressions(character);
    }

    public void ResetAdventureFlag(Character character)
    {
        if (character == null)
            return;

        character.UsedOriginalClassFunctionThisAdventure = false;
    }

    private static bool MeetsPrimeRequisiteThreshold(AbilityScores abilities, CharacterClass cls, int threshold)
    {
        foreach (var stat in GetPrimeRequisites(cls))
        {
            if (GetAbilityScore(abilities, stat) < threshold)
                return false;
        }

        return true;
    }

    private static IEnumerable<string> GetPrimeRequisites(CharacterClass cls)
    {
        return cls switch
        {
            CharacterClass.Fighter => new[] { "Strength" },
            CharacterClass.Paladin => new[] { "Strength", "Wisdom", "Charisma" },
            CharacterClass.Ranger => new[] { "Strength", "Intelligence", "Wisdom" },
            CharacterClass.Cleric => new[] { "Wisdom" },
            CharacterClass.Druid => new[] { "Wisdom", "Charisma" },
            CharacterClass.MagicUser => new[] { "Intelligence" },
            CharacterClass.Illusionist => new[] { "Intelligence", "Dexterity" },
            CharacterClass.Thief => new[] { "Dexterity" },
            CharacterClass.Assassin => new[] { "Strength", "Dexterity" },
            CharacterClass.Monk => new[] { "Strength", "Dexterity", "Wisdom", "Constitution" },
            CharacterClass.Bard => new[] { "Dexterity", "Charisma" },
            _ => Array.Empty<string>()
        };
    }

    private static int GetAbilityScore(AbilityScores abilities, string ability)
    {
        return ability switch
        {
            "Strength" => abilities.Strength,
            "Intelligence" => abilities.Intelligence,
            "Wisdom" => abilities.Wisdom,
            "Dexterity" => abilities.Dexterity,
            "Constitution" => abilities.Constitution,
            "Charisma" => abilities.Charisma,
            _ => 0
        };
    }

    private static bool IsAlignmentAllowedForClass(Alignment alignment, CharacterClass cls)
    {
        return cls switch
        {
            CharacterClass.Paladin => alignment == Alignment.LawfulGood,
            CharacterClass.Druid => alignment == Alignment.TrueNeutral,
            CharacterClass.Monk => alignment is Alignment.LawfulGood or Alignment.LawfulNeutral or Alignment.LawfulEvil,
            CharacterClass.Assassin => alignment is Alignment.LawfulEvil or Alignment.NeutralEvil or Alignment.ChaoticEvil,
            _ => true
        };
    }

    private static void NormalizeBardClassState(Character character)
    {
        character.Classes = new List<CharacterClass> { CharacterClass.Bard };

        character.ClassProgressions ??= new List<ClassProgression>();
        var bardProgression = character.ClassProgressions.FirstOrDefault(cp => cp.Class == CharacterClass.Bard)
                            ?? new ClassProgression { Class = CharacterClass.Bard, Level = 1, Experience = 0 };

        character.ClassProgressions = new List<ClassProgression> { bardProgression };
        character.Level = Math.Max(1, bardProgression.Level);
        character.Experience = Math.Max(0, bardProgression.Experience);
    }

    private static bool MeetsBardAbilityPrerequisites(AbilityScores abilities)
    {
        return abilities.Strength >= 15
               && abilities.Dexterity >= 15
               && abilities.Intelligence >= 15
               && abilities.Wisdom >= 15
               && abilities.Charisma >= 15;
    }
}

public sealed class DualClassSwitchResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public CharacterClass? OriginalClass { get; set; }
    public int OriginalLevel { get; set; }
    public CharacterClass TargetClass { get; set; }
}
