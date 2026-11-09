# Wizardry Player's Handbook

Rules for characters when **Rules = Wizardry** in Settings. AD&D characters and rules are not affected.
Each rule names the code that implements it and the test that guards it. When a Wizardry rule is added or changed,
update this file and its test in the same change.

Code: `Adnd.Core\Wizardry\WizardryRules.cs`, `Adnd.Core\Wizardry\WizardryCharacterFactory.cs`.
Tests: `Adnd.Tests\WizardryRulesTests.cs`.

Source of the numbers: https://datadrivengamer.blogspot.com/2019/08/the-not-so-basic-mechanics-of-wizardry.html

## 1. Ruleset and party

- Settings has a `Rules` option: `ADND` (default) or `Wizardry`.
- Every character belongs to one ruleset. Characters saved before this option existed are ADND.
- Changing `Rules` removes party members of the other ruleset from the party. They stay in the roster.
- The Training Grounds and the Party menu only list characters of the active ruleset.
- In Wizardry, "Halfling" is shown as **Hobbit**.
- There is no multiclassing or dual-classing. Instead the Training Grounds offers **U) Change class** (`WizardryClassChange`): level returns to 1 and XP to 0, HP and known spells are kept, stats return to the racial base plus newly rolled bonus points (the new class must meet its stat and alignment requirements), items the new class cannot use are unequipped, and the character ages (52 * random 0..2) + 252 weeks.

## 2. Creating a character

Order: race, alignment, bonus points (with the live class list), class, name.

### Races: base stats

| Stat     | Human | Elf | Dwarf | Gnome | Hobbit |
|----------|------:|----:|------:|------:|-------:|
| Strength |     8 |   7 |    10 |     7 |      5 |
| I.Q.     |     8 |  10 |     7 |     7 |      7 |
| Piety    |     5 |  10 |    10 |    10 |      7 |
| Vitality |     8 |   6 |    10 |     8 |      6 |
| Agility  |     8 |   9 |     5 |    10 |     10 |
| Luck     |     9 |   6 |     6 |     7 |     12 |

### Bonus points

- Bonus points to assign: **95%** 5-9 points, **4.75%** 15-19 points, **0.25%** 25-29 points (`WizardryRules.RollBonusPoints`).
- No stat can exceed **18**, and no stat can go below the race base.
- All points must be spent before finishing (unless every stat is at 18).
- Screen: ↵ moves the pointer (wraps from Luck to Strength), ↑ adds a point, ↓ removes one, Esc finishes.
  The classes you can pick are shown live and update as points change.

### Class requirements

`--` means no minimum.

| Class   | STR | IQ | PIE | VIT | AGI | LUC | Alignment       |
|---------|----:|---:|----:|----:|----:|----:|-----------------|
| Priest  |  -- | -- |  11 |  -- |  -- |  -- | Good or Evil    |
| Fighter |  11 | -- |  -- |  -- |  -- |  -- | Any             |
| Lord    |  15 | 12 |  12 |  15 |  14 |  15 | Good only       |
| Mage    |  -- | 11 |  -- |  -- |  -- |  -- | Any             |
| Ninja   |  17 | 17 |  17 |  17 |  17 |  17 | Evil only       |
| Samurai |  15 | 11 |  10 |  14 |  10 |  -- | Good or Neutral |
| Thief   |  -- | -- |  -- |  -- |  11 |  -- | Neutral or Evil |
| Bishop  |  -- | 12 |  12 |  -- |  -- |  -- | Good or Evil    |

### Starting values (level 1)

- **HP and max HP: always 8**, whatever the class. The class hit dice work from level 2.
- Gold: **1d100 + 89**.
- Age: 14 to 17 years.
- AC 10. Damage 2d2 (Ninja 2d4).
- Stats are stored in the ability fields: Strength, I.Q. = Intelligence, Piety = Wisdom, Vitality = Constitution,
  Agility = Dexterity, Luck = Charisma.
- The character also keeps the nearest AD&D class (for example Priest = Cleric) so shared systems keep working.

## 3. Experience

XP needed to go from level N to the next level. Level 13 and beyond needs the same amount for every level.
`TotalXpForLevel` is the sum of the earlier steps.

| Class   |     1 |     2 |     3 |     4 |      5 |      6 |      7 |
|---------|------:|------:|------:|------:|-------:|-------:|-------:|
| Priest  | 1,050 | 1,810 | 3,120 | 5,379 |  9,274 | 15,989 | 27,567 |
| Fighter | 1,000 | 1,724 | 2,972 | 5,124 |  8,834 | 15,231 | 26,260 |
| Lord    | 1,300 | 2,280 | 4,000 | 7,017 | 12,310 | 21,596 | 37,887 |
| Mage    | 1,100 | 1,896 | 3,268 | 5,634 |  9,713 | 16,746 | 28,872 |
| Ninja   | 1,450 | 2,543 | 4,461 | 7,826 | 13,729 | 24,085 | 42,254 |
| Samurai | 1,200 | 2,105 | 3,677 | 6,477 | 11,363 | 19,935 | 34,973 |
| Thief   |   900 | 1,551 | 2,674 | 4,610 |  7,948 | 13,703 | 23,625 |
| Bishop  | 1,200 | 2,105 | 3,677 | 6,477 | 11,363 | 19,935 | 34,973 |

| Class   |      8 |       9 |      10 |      11 |      12 | 13 and beyond (per level) |
|---------|-------:|--------:|--------:|--------:|--------:|--------------------------:|
| Priest  | 47,529 |  81,946 | 141,286 | 243,596 | 419,993 |                   304,132 |
| Fighter | 45,275 |  78,060 | 134,586 | 232,044 | 400,075 |                   289,709 |
| Lord    | 66,468 | 116,610 | 204,578 | 358,908 | 629,663 |                   475,008 |
| Mage    | 49,779 |  85,825 | 147,974 | 255,127 | 439,874 |                   318,529 |
| Ninja   | 74,129 | 130,050 | 228,157 | 400,275 | 702,236 |                   529,756 |
| Samurai | 61,356 | 107,642 | 188,845 | 331,307 | 581,240 |                   428,479 |
| Thief   | 40,732 |  70,227 | 121,081 | 208,760 | 359,931 |                   260,639 |
| Bishop  | 61,356 | 107,642 | 188,845 | 331,307 | 581,240 |                   428,479 |

**Gaining levels:** killing monsters only adds experience. A Wizardry character gains the earned level(s) (HP, spell slots) only when resting at the Adventures Inn. AD&D characters still level straight after combat.

**Attributes on level-up** (`WizardryAttributeChanges`, `WizardryPeekAge` in game-rules.json, default 25): for each level gained and each of Strength, I.Q., Piety, Vitality, Agility and Luck there is a 75% chance it changes. Roll 0 to (2 x WizardryPeekAge) - 1. If age is higher than the roll the attribute loses 1 (at 18 there is only a 1 in 6 chance of losing 1); if age is lower and the attribute is under 18 it gains 1. A Vitality below 3 kills the character. The inn shows "YOU GAINED/LOST ..." for each change.

**Learning spells on level-up:** for each level gained, every unknown spell of a circle the character has slots for is learned with chance IQ/30 (Piety/30 for priest spells). The first spell of each circle is always learned.

**Monster size:** every monster from the Wizardry source is size Medium (set in `MonsterImporter`), so weapons use their normal damage (Long Sword 1d8), never the "vs Large" damage (1d12).

**Paralyze attack:** a Wizardry monster's Paralyze special attack only applies to the character it actually hits (never on a miss). The paralysis does not expire, not even in town, and lasts until cured with Dialko or Madi (or by the temple).

**Temple fees** (`WizardryRules.TempleCost`, used by `Temple`): cure paralysis 100 gp x level, cure stoned 200 gp x level, raise dead 250 gp x level, raise from ashes 500 gp x level. Other temple services keep the AD&D fees. Poison needs no temple visit: it is cured automatically whenever the party is back in town (`MainMenu.CureWizardryPoisonInTown`).

## 4. Hit points after level 1

Max HP is re-rolled at each level up (`WizardryRules.RollLevelUpMaxHitPoints`): the whole total is rolled again for the new level; if it is higher than the current max HP that becomes the new max, otherwise the character gains only +1 HP. AD&D keeps adding one die per level.

- Hit die: Fighter and Lord d10; Priest and Samurai d8; Thief, Bishop and Ninja d6; Mage d4.
- Number of dice: the character's level. A Samurai rolls one extra.
- Vitality modifier per die: 3 = -2, 4-5 = -1, 6-15 = 0, 16 = +1, 17 = +2, 18 = +3.

## 5. Spells

Spells are tagged Wizardry and kept apart from AD&D spells. Characters and monsters only use spells of their own ruleset: Wizardry characters and monsters with source Wizardry use these spells, AD&D ones use the AD&D spells.

- Mage spells (22) are cast by Mage-school characters (Mage, Bishop); Priest spells (29) by Priest-school characters. Circles 1-7.
- Spell slots per day still come from the AD&D memorize-and-slots system for now (Mage = Magic-User slots, Priest = Cleric slots).
- Starting spells: Mage and Bishop know Halito and Katino; Priest knows Dios and Badios.
- The first spell of each circle is always learned once the character has slots for that circle.
- Monsters with "Level N Mage/Priest spells" can cast every Wizardry spell of that school up to circle N, always (100%).
- Implemented effects: damage (single target, group, all enemies; a successful save halves it; sleeping or paralyzed targets take double), healing (Dios, Dial, Dialma, Madi), sleep (Katino), paralyze (Manifo), silence (Montino), fear (Morlis, Mamorlis), Makanito (kills enemies below 35 HP), Badi and Lakanito (instant kill), Zilwan (kills one undead), Mabadi (reduces to 1d8 HP).
- Monsters cast damage, healing, Katino and Badi (save vs spell avoids sleep and death).
- Other spells (armor class buffs, light, detection, resurrection, teleport, divine intervention, cures) can be learned but report "not implemented" when cast.

## 5b. Attacks per round

- Fighter, Samurai, Lord and Ninja get 1 attack plus 1 more every 4 levels. Priest, Bishop, Mage and Thief stay at 1.
- Many weapons have a swing count (Sword +1 = 2, Sword +2 = 3, Blade Cusinart = 4, Dagger of Speed = 7). It is a minimum: attacks = the higher of the class attacks and the weapon swings, never the sum.

## 5c. Unidentified items

- Items found in the Proving Grounds start unidentified and show a generic name (Sword, Staff, Armour, Shield, Helm, Potion...) marked with "?". Swings and other stats still apply.
- A Bishop can identify an item from the Identify action. Chance = Bishop level x 5% + 10% (max 100%). A failure leaves the item unidentified and the Bishop may try again.
- Each attempt first risks a cursed item being equipped by accident: 35% - 3% per level (0% from level 12). It only matters if the item is cursed; the Bishop then wears it.
- The shop identifies any item for half its value (A)ppraise Items).

## 5d. Who can use which item

- Class access follows WizardryItems.json ("allowed classes": f=Fighter, m=Mage, p=Priest, t=Thief, b=Bishop, s=Samurai, l=Lord, n=Ninja), so Samurai, Bishop and Ninja are told apart. Example: Long Sword is fsln (no Thief), Short Sword ftsln.
- The table is `WizardryItemClasses` in Adnd.Core; items it does not list fall back to their AD&D class list.

## 5e. Carrying and weapon skill

- A character carries at most 8 items in the inventory. Weight does not matter, and carry weight is not shown.
- Weapon proficiencies do not exist: they are not shown, and there is no to-hit penalty for an unfamiliar weapon.
- There is no Character Sheet action; the inspect screen is all there is.
- Monsters put to sleep stay asleep when damaged (AD&D wakes them); they wake only when the sleep runs out. Hits and spells on a sleeping monster still get their sleeping bonuses. The same goes for sleeping Wizardry party members: damage does not wake them.
- Turn Undead never destroys the undead in Wizardry: a result that would disintegrate it (D) turns it instead, shown as Turned.
- A Dead, Ashes or Lost character can have no other status (poisoned, paralyzed, ...); `Character.Status` drops them. This holds for AD&D too.

## 6. Not implemented yet

Level-up, learning spells by chance on level-up, spell points per circle, hit and damage calculation, armor class, initiative, aging, statuses and temple costs.
